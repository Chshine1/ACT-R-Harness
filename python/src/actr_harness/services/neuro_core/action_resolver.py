import logging
from dataclasses import asdict, dataclass, field
from typing import Any

from actr_harness.generated.grpc.actr import (
    BufferOperation,
    BufferState,
    ModuleSchema,
    NeuroAction,
)
from actr_harness.generated.grpc.actr.services import DecodeActionResponse
from actr_harness.observability import log_event, observe_boundary
from actr_harness.utils import dict_to_struct

from .buffers_view import BuffersView
from ..llm_client import LLMClient

logger = logging.getLogger(__name__)


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

    @observe_boundary("action_resolver.decode_action")
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
            base_params = self._resolve_placeholder_params(base_op.params.to_dict(), view)
            semantic = command_semantics.get(alias)

            if semantic is not None or len(meta_instructions) > 0:
                schema = keyed_schemas.get(base_op.target_module_id, {}).get(base_op.command)
                if schema is None:
                    raise ValueError(
                        "Missing schema for target_module_id="
                        f"'{base_op.target_module_id}', command='{base_op.command}', alias='{alias}'."
                    )

                sources_list: dict[str, Any] = {}
                param_leaves: dict[str, str] = {}
                if semantic is not None:
                    sources_raw = semantic.get("sources", [])
                    sources_list = {src: view.get(src) for src in sources_raw}
                    param_leaves = self._flatten_semantic_params(semantic.get("params", {}))

                semantic_entries.append(
                    SemanticEntry(
                        target_module_id=base_op.target_module_id,
                        command=base_op.command,
                        existing_params=base_params,
                        semantic=semantic if semantic is not None else {},
                        meta=meta_instructions,
                        command_schema=schema,
                        semantic_sources=sources_list,
                        semantic_param_leaves=param_leaves,
                    )
                )
            else:
                determined_ops.append(
                    BufferOperation(
                        target_module_id=base_op.target_module_id,
                        command=base_op.command,
                        params=dict_to_struct(base_params),
                    )
                )

        log_event(
            logger,
            logging.DEBUG,
            "action_decode.plan_built",
            "Built action decode plan.",
            rule_id=action_intent.rule_id,
            command_aliases=list(action_intent.commands.keys()),
            semantic_keys=list(action_intent.semantics.keys()),
            determined_operations=[_operation_summary(op) for op in determined_ops],
            semantic_entries=[_semantic_entry_summary(entry) for entry in semantic_entries],
            neuro_intent_count=len(neuro_intents),
        )

        if len(semantic_entries) > 0:
            semantic_ops = await self._llm_resolve_semantic_commands(
                determined_ops,
                semantic_entries,
                keyed_schemas,
            )
            determined_ops.extend(semantic_ops)
            log_event(
                logger,
                logging.INFO,
                "action_decode.semantic_resolution",
                "Resolved semantic command supplements.",
                semantic_entry_count=len(semantic_entries),
                produced_operations=[_operation_summary(op) for op in semantic_ops],
            )

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
            log_event(
                logger,
                logging.INFO,
                "action_decode.neuro_resolution",
                "Resolved neuro intents into buffer operations.",
                neuro_intent_count=len(neuro_intents),
                produced_operations=[_operation_summary(op) for op in llm_ops],
            )

        log_event(
            logger,
            logging.INFO,
            "action_decode.completed",
            "Completed action decode.",
            rule_id=action_intent.rule_id,
            operation_count=len(determined_ops),
            operations=[_operation_summary(op) for op in determined_ops],
        )
        return DecodeActionResponse(operations=determined_ops)

    @observe_boundary("action_resolver.llm_resolve_semantic_commands")
    async def _llm_resolve_semantic_commands(
            self,
            determined_ops: list[BufferOperation],
            semantic_entries: list[SemanticEntry],
            keyed_schemas: dict[str, dict[str, str]],
    ) -> list[BufferOperation]:
        prompt_data = {
            "determined_ops": [_operation_summary(op) for op in determined_ops],
            "semantic_commands": [asdict(entry) for entry in semantic_entries],
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
            log_event(
                logger,
                logging.WARNING,
                "action_decode.semantic_resolution_invalid_payload",
                "Semantic command resolution returned a non-list payload.",
                payload_type=type(response).__name__,
            )
            return ops

        for item in response:
            if not isinstance(item, dict):
                log_event(
                    logger,
                    logging.DEBUG,
                    "action_decode.semantic_resolution_item_skipped",
                    "Skipped semantic command item with invalid type.",
                    payload_type=type(item).__name__,
                )
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
                log_event(
                    logger,
                    logging.DEBUG,
                    "action_decode.semantic_resolution_item_skipped",
                    "Skipped semantic command item with invalid shape.",
                    item=item,
                )
        return ops

    def _resolve_placeholder_params(self, value: Any, view: BuffersView) -> Any:
        if isinstance(value, str):
            if value.startswith("${") and value.endswith("}"):
                resolved = view.get(value[2:-1])
                return resolved if resolved is not None else value
            return value
        if isinstance(value, dict):
            return {key: self._resolve_placeholder_params(inner, view) for key, inner in value.items()}
        if isinstance(value, list):
            return [self._resolve_placeholder_params(inner, view) for inner in value]
        return value

    def _flatten_semantic_params(self, value: Any, prefix: str = "") -> dict[str, str]:
        if isinstance(value, dict):
            leaves: dict[str, str] = {}
            for key, child in value.items():
                path = f"{prefix}.{key}" if len(prefix) > 0 else key
                for leaf_key, leaf_value in self._flatten_semantic_params(child, path).items():
                    leaves[leaf_key] = leaf_value
            return leaves
        if isinstance(value, str):
            return {prefix: value}
        raise ValueError(
            f"Semantic param leaf at '{prefix or '<root>'}' must be a string, "
            f"got {type(value).__name__}."
        )

    @observe_boundary("action_resolver.llm_decode_fuzzy")
    async def _llm_decode_fuzzy(
            self,
            command_supplements: list[dict[str, Any]],
            neuro_intents: list[dict[str, Any]],
            buffers: list[dict[str, Any]],
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
        ops: list[BufferOperation] = []
        if not isinstance(ops_raw, list):
            log_event(
                logger,
                logging.WARNING,
                "action_decode.neuro_resolution_invalid_payload",
                "Fuzzy decode returned a non-list payload.",
                payload_type=type(ops_raw).__name__,
            )
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
                log_event(
                    logger,
                    logging.DEBUG,
                    "action_decode.neuro_resolution_item_skipped",
                    "Skipped fuzzy decode item with invalid shape.",
                    item=item,
                )
        return ops


def _operation_summary(operation: BufferOperation) -> dict[str, Any]:
    return {
        "target_module_id": operation.target_module_id,
        "command": operation.command,
        "params": operation.params.to_dict(),
    }


def _semantic_entry_summary(entry: SemanticEntry) -> dict[str, Any]:
    return {
        "target_module_id": entry.target_module_id,
        "command": entry.command,
        "semantic_sources": entry.semantic_sources,
        "semantic_param_leaves": entry.semantic_param_leaves,
        "meta": entry.meta,
    }
