from actr_harness.generated.grpc.actr import (
    BufferState,
    ModuleSchema,
    NeuroAction,
    BufferOperation,
)
from actr_harness.generated.grpc.actr.services import (
    DecodeActionResponse,
)

from actr_harness.utils import dict_to_struct
from dataclasses import dataclass, field, asdict
from typing import Any
from .buffers_view import BuffersView
from ..llm_client import LLMClient


@dataclass
class SemanticEntry:
    target_module_id: str
    command: str
    existing_params: dict[str, Any]
    semantic: dict[str, Any]
    meta: dict[str, Any]
    command_schema: str
    semantic_sources: dict[str, Any] = field(default_factory=dict)
    semantic_param_leaves: dict[str, str] = field(default_factory=dict)


class ActionResolver:
    def __init__(self, llm_client: LLMClient):
        self._llm_client = llm_client

    async def decode_action(
            self,
            action_intent: NeuroAction,
            current_states: list[BufferState],
            schemas: list[ModuleSchema],
    ) -> DecodeActionResponse:
        view = BuffersView(current_states)
        keyed_schemas: dict[str, dict[str, str]] = {s.module_id: s.command_schemas for s in schemas}

        command_semantics: dict[str, dict[str, Any]] = {}
        neuro_intents: list[dict[str, Any]] = []
        meta_instructions: dict[str, dict[str, Any]] = {}

        for sem_key, sem_struct in action_intent.semantics.items():
            sem_dict = sem_struct.to_dict()
            prefix, _, sem_name = sem_key.partition(":")
            if prefix == "meta":
                meta_instructions[sem_name] = sem_dict
            elif prefix == "neuro":
                neuro_intents.append(sem_dict)
            elif prefix == "command":
                command_semantics[sem_name] = sem_dict

        determined_ops: list[BufferOperation] = []
        semantic_entries: list[SemanticEntry] = []

        for alias, base_op in action_intent.commands.items():
            base_params = self._resolve_placeholder_params(
                base_op.params.to_dict(), view
            )
            semantic = command_semantics.get(alias)

            if semantic is not None or len(meta_instructions) > 0:
                schema = keyed_schemas.get(base_op.target_module_id, {}).get(
                    base_op.command
                )
                if schema is None:
                    raise ValueError

                sources_list: dict[str, Any] = {}
                param_leaves: dict[str, str] = {}
                if semantic is not None:
                    sources_raw = semantic.get("sources", [])
                    sources_list = {
                        src: view.get(src)
                        for src in sources_raw
                    }

                    param_leaves = self._flatten_semantic_params(
                        semantic.get("params", {})
                    )

                entry = SemanticEntry(
                    target_module_id=base_op.target_module_id,
                    command=base_op.command,
                    existing_params=base_params,
                    semantic=semantic if semantic is not None else {},
                    meta=meta_instructions,
                    command_schema=schema,
                    semantic_sources=sources_list,
                    semantic_param_leaves=param_leaves,
                )
                semantic_entries.append(entry)
            else:
                determined_ops.append(
                    BufferOperation(
                        target_module_id=base_op.target_module_id,
                        command=base_op.command,
                        params=dict_to_struct(base_params),
                    )
                )

        if len(semantic_entries) > 0:
            semantic_ops = await self._llm_resolve_semantic_commands(
                determined_ops, semantic_entries, keyed_schemas
            )
            determined_ops.extend(semantic_ops)

        if len(neuro_intents) > 0:
            command_supplements = [
                {
                    "target_module_id": op.target_module_id,
                    "command": op.command,
                    "existing_params": op.params.to_dict(),
                }
                for op in determined_ops
            ]
            llm_ops = await self._llm_decode_fuzzy(
                command_supplements,
                neuro_intents,
                [
                    {"module_id": bs.module_id, "data": bs.data.to_dict()}
                    for bs in current_states
                ],
                keyed_schemas,
            )
            determined_ops.extend(llm_ops)

        return DecodeActionResponse(operations=determined_ops)

    async def _llm_resolve_semantic_commands(
            self,
            determined_ops: list[BufferOperation],
            semantic_entries: list[SemanticEntry],
            keyed_schemas: dict[str, dict[str, str]],
    ) -> list[BufferOperation]:
        prompt_data = {
            "determined_ops": [
                {
                    "target_module_id": op.target_module_id,
                    "command": op.command,
                    "params": op.params.to_dict(),
                }
                for op in determined_ops
            ],
            "semantic_commands": [asdict(c) for c in semantic_entries],
            "module_schemas": keyed_schemas,
        }

        system_prompt = (
            "You are given already determined operations (do NOT include them in your output) "
            "and a set of incomplete semantic commands with parameters described in natural language. "
            "For each semantic command, resolve it into zero or more concrete operations according to "
            "its semantic description and any meta policies (e.g., skip if required sources are missing). "
            "Return ONLY the operations derived from the semantic commands (the already determined ones "
            "will be kept automatically). "
            "Output a strict JSON array of objects with keys: target_module_id, command, params. "
            "No extra text."
        )

        response = await self._llm_client.chat_json(prompt_data, system_prompt)
        ops: list[BufferOperation] = []
        if not isinstance(response, list):
            return ops
        for item in response:
            if not isinstance(item, dict):
                continue
            try:
                ops.append(
                    BufferOperation(
                        target_module_id=item["target_module_id"],
                        command=item["command"],
                        params=dict_to_struct(item.get("params", {})),
                    )
                )
            except (KeyError, TypeError):
                continue
        return ops

    def _resolve_placeholder_params(self, value: Any, view: BuffersView) -> Any:
        if isinstance(value, str):
            if value.startswith("${") and value.endswith("}"):
                resolved = view.get(value[2:-1])
                return resolved if resolved is not None else value
            return value
        if isinstance(value, dict):
            return {k: self._resolve_placeholder_params(v, view) for k, v in value.items()}
        if isinstance(value, list):
            return [self._resolve_placeholder_params(v, view) for v in value]
        return value

    def _flatten_semantic_params(self, value: Any, prefix: str = "") -> dict[str, str]:
        if isinstance(value, dict):
            leaves: dict[str, str] = {}
            for key, child in value.items():
                path = f"{prefix}.{key}" if len(prefix) > 0 else key
                for k, v in (self._flatten_semantic_params(child, path).items()): leaves[k] = v
            return leaves
        if isinstance(value, str):
            return {prefix: value}
        raise ValueError

    async def _llm_decode_fuzzy(
            self,
            command_supplements: list[dict],
            neuro_intents: list[dict],
            buffers: list[dict],
            schemas: dict[str, Any],
    ) -> list[BufferOperation]:
        prompt_data = {
            "buffers": buffers,
            "module_schemas": schemas,
            "partial_commands": command_supplements,
            "neural_intents": neuro_intents,
        }
        system_prompt = (
            "Translate partial commands and neural intents into concrete operations. "
            "Each operation must use a valid module_id from schemas, a command defined there, "
            "and parameters with correct types. "
            "Output a strict JSON array of objects with keys: target_module_id, command, params. "
            "No commentary."
        )
        ops_raw = await self._llm_client.chat_json(prompt_data, system_prompt)
        ops = []
        if not isinstance(ops_raw, list):
            return ops
        for item in ops_raw:
            try:
                ops.append(
                    BufferOperation(
                        target_module_id=item["target_module_id"],
                        command=item["command"],
                        params=dict_to_struct(item.get("params", {})),
                    )
                )
            except (KeyError, TypeError):
                continue
        return ops
