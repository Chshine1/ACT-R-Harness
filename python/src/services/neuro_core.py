import httpx
import hashlib
import json
import os
from betterproto.lib.std.google.protobuf import Struct
from typing import List, Dict, Any
from ..generated.grpc.actr.services import (
    NeuroCoreBase,
    EvaluateConditionsRequest,
    EvaluateConditionsResponse,
    DecodeActionRequest,
    DecodeActionResponse,
)
from ..generated.grpc.actr import (
    ProceduralCondition,
    BufferOperation,
)


class NeuroCore(NeuroCoreBase):
    def __init__(self):
        self._llm_model = os.getenv("NEURO_LLM_MODEL", "gpt-4o-mini")
        self._llm_api_key = os.getenv("OPENAI_API_KEY")
        self._llm_base_url = os.getenv("OPENAI_BASE_URL", "https://api.openai.com/v1")
        self._http_client = httpx.AsyncClient(timeout=30.0)
        self._cache: Dict[str, Any] = {}

    async def evaluate_conditions(
            self, evaluate_conditions_request: EvaluateConditionsRequest
    ) -> EvaluateConditionsResponse:
        buffer_list = [
            {"module_id": bs.module_id, "data": bs.data.to_dict()}
            for bs in evaluate_conditions_request.buffer_states
        ]

        satisfied_ids = set()
        fuzzy_conditions: List[ProceduralCondition] = []

        for cond in evaluate_conditions_request.conditions:
            cond_dict = cond.condition.to_dict()
            semantics_dict = cond.semantics.to_dict() if cond.semantics else {}

            if self._symbolic_match(cond_dict, buffer_list):
                satisfied_ids.add(cond.rule_id)
                continue

            if semantics_dict:
                fuzzy_conditions.append(cond)

        if fuzzy_conditions:
            fuzzy_ids = await self._llm_evaluate_fuzzy(fuzzy_conditions, buffer_list)
            satisfied_ids.update(fuzzy_ids)

        return EvaluateConditionsResponse(satisfied_rule_ids=list(satisfied_ids))

    def _symbolic_match(self, condition: Any, buffers: List[dict]) -> bool:
        if not isinstance(condition, dict):
            return False

        cond_type = condition.get("type", "")
        if cond_type == "and":
            return all(self._symbolic_match(c, buffers) for c in condition.get("conditions", []))
        elif cond_type == "or":
            return any(self._symbolic_match(c, buffers) for c in condition.get("conditions", []))
        elif cond_type == "not":
            return not self._symbolic_match(condition.get("condition"), buffers)
        elif cond_type == "exist":
            slot = condition.get("slot")
            return any(slot in buf["data"] for buf in buffers)
        elif cond_type == "=":
            slot = condition.get("slot")
            value = condition.get("value")
            return any(buf["data"].get(slot) == value for buf in buffers)
        return False

    async def decode_action(
            self, decode_action_request: DecodeActionRequest
    ) -> DecodeActionResponse:
        action = decode_action_request.action_intent
        buffer_list = [
            {"module_id": bs.module_id, "data": bs.data.to_dict()}
            for bs in decode_action_request.current_states
        ]
        schemas = {
            s.module_id: s.command_schemas for s in decode_action_request.schemas
        }

        concrete_ops: List[BufferOperation] = list(action.commands.values())

        command_supplements: List[Dict[str, Any]] = []
        neuro_intents: List[Dict[str, Any]] = []

        for sem_key, sem_struct in action.semantics.items():
            sem_dict = sem_struct.to_dict()
            if sem_key.startswith("command:"):
                cmd_name = sem_key.split(":", 1)[1]
                matching_cmd = None
                for op in concrete_ops:
                    if op.command == cmd_name:
                        matching_cmd = op
                        break
                if matching_cmd is not None:
                    command_supplements.append({
                        "target_module_id": matching_cmd.target_module_id,
                        "command": cmd_name,
                        "existing_params": matching_cmd.params.to_dict(),
                        "semantic_params": sem_dict,
                    })
                else:
                    command_supplements.append({
                        "target_module_id": "",
                        "command": cmd_name,
                        "existing_params": {},
                        "semantic_params": sem_dict,
                    })
            elif sem_key.startswith("neuro:"):
                neuro_intents.append(sem_dict)

        if not command_supplements and not neuro_intents:
            return DecodeActionResponse(operations=concrete_ops)

        llm_ops = await self._llm_decode_fuzzy(
            command_supplements, neuro_intents, buffer_list, schemas
        )

        all_ops = concrete_ops + llm_ops
        return DecodeActionResponse(operations=all_ops)

    async def _llm_evaluate_fuzzy(
            self, conditions: List[ProceduralCondition], buffers: List[dict]
    ) -> List[str]:
        prompt_data = {
            "buffers": buffers,
            "conditions": [
                {
                    "rule_id": c.rule_id,
                    "symbolic": c.condition.to_dict(),
                    "semantic_hint": c.semantics.to_dict() if c.semantics else {}
                }
                for c in conditions
            ]
        }
        system_prompt = (
            "You are given buffers (world state) and conditions with optional semantic hints. "
            "Determine which conditions are satisfied. "
            "Return ONLY a JSON array of the satisfied rule_id strings. "
            "No extra text, no explanation."
        )
        response = await self._chat_json(prompt_data, system_prompt)
        return response if isinstance(response, list) else []

    async def _llm_decode_fuzzy(
            self,
            command_supplements: List[dict],
            neuro_intents: List[dict],
            buffers: List[dict],
            schemas: Dict[str, Any],
    ) -> List[BufferOperation]:
        prompt_data = {
            "buffers": buffers,
            "module_schemas": schemas,
            "partial_commands": command_supplements,
            "neural_intents": neuro_intents,
        }
        system_prompt = (
            "Translate partial commands and neural intents into a concrete operation sequence. "
            "Each operation must use a valid module_id from schemas, a command defined there, "
            "and parameters with correct types (replace natural-language descriptions with actual values inferred from buffers or intents). "
            "Output a strict JSON array of objects, each with keys: target_module_id, command, params (object with concrete values). "
            "Do not include any commentary."
        )
        ops_raw = await self._chat_json(prompt_data, system_prompt)
        ops = []
        for item in ops_raw:
            try:
                ops.append(BufferOperation(
                    target_module_id=item["target_module_id"],
                    command=item["command"],
                    params=Struct(item.get("params", {})),
                ))
            except (KeyError, TypeError):
                continue
        return ops

    async def _chat_json(self, data: Any, system_prompt: str) -> Any:
        cache_key = _hash(system_prompt + json.dumps(data, sort_keys=True))
        if cache_key in self._cache:
            return self._cache[cache_key]

        messages = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": json.dumps(data, ensure_ascii=False)},
        ]
        payload = {
            "model": self._llm_model,
            "messages": messages,
            "response_format": {"type": "json_object"},
            "temperature": 0.0,
        }
        headers = {
            "Authorization": f"Bearer {self._llm_api_key}",
            "Content-Type": "application/json",
        }
        resp = await self._http_client.post(
            f"{self._llm_base_url}/chat/completions", json=payload, headers=headers
        )
        resp.raise_for_status()
        result = resp.json()
        content = result["choices"][0]["message"]["content"]
        parsed = json.loads(content)
        self._cache[cache_key] = parsed
        return parsed


def _hash(s: str) -> str:
    return hashlib.sha256(s.encode()).hexdigest()
