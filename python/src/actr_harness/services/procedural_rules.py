import os
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import NotRequired, TypedDict, cast

import yaml  # type: ignore[import-untyped]
from betterproto.lib.google.protobuf import Struct

from actr_harness.generated.grpc.actr import (
    BufferOperation,
    NeuroAction,
    ProceduralCondition,
)

DEFAULT_RULESET_PATH = (
    Path(__file__).resolve().parents[4] / "shared" / "ruleset" / "lab.yml"
)
ENV_RULESET_PATH = "ACTR_RULESET_PATH"
INITIAL_RULE_UTILITY = 0.0


class CommandData(TypedDict):
    target_module_id: str
    command: str
    params: NotRequired[dict[str, object]]


class ConditionData(TypedDict):
    symbolic: dict[str, object]
    semantics: NotRequired[dict[str, object]]


class ActionData(TypedDict):
    commands: NotRequired[dict[str, CommandData]]
    semantics: NotRequired[dict[str, dict[str, object]]]


class RuleData(TypedDict):
    id: str
    condition: ConditionData
    action: ActionData


class Ruleset(TypedDict):
    rules: list[RuleData]


@dataclass(slots=True)
class Rule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float = INITIAL_RULE_UTILITY


def _struct_from_dict(data: Mapping[str, object]) -> Struct:
    return Struct.from_dict(data)  # type: ignore[misc, no-any-return]


def _validate_rule_id(value: object) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError("Rule is missing 'id'")
    return value


def _require_mapping(value: object, message: str) -> dict[str, object]:
    if not isinstance(value, Mapping):
        raise ValueError(message)
    return cast("dict[str, object]", value)


def _validate_command(
    name: object, value: object
) -> tuple[str, str, str, dict[str, object]]:
    if not isinstance(name, str):
        raise ValueError("Command name must be a string")
    if not name.strip():
        raise ValueError("Command name must not be blank")
    command_data = _require_mapping(value, f"Command '{name}' must be a mapping")

    target_module_id = command_data.get("target_module_id")
    if not isinstance(target_module_id, str) or not target_module_id.strip():
        raise ValueError(
            f"Command '{name}' has invalid 'target_module_id'"
            if target_module_id is not None
            else f"Command '{name}' is missing 'target_module_id'"
        )
    command = command_data.get("command")
    if not isinstance(command, str) or not command.strip():
        raise ValueError(
            f"Command '{name}' has invalid 'command'"
            if command is not None
            else f"Command '{name}' is missing 'command'"
        )
    params = command_data.get("params", {})
    params_data = _require_mapping(
        params, f"Command '{name}' has invalid 'params'"
    )
    return name, target_module_id, command, params_data


def _validate_semantics(
    value: object, field_name: str
) -> dict[str, dict[str, object]]:
    semantics_data = _require_mapping(
        value, f"{field_name} must be a mapping"
    )
    validated: dict[str, dict[str, object]] = {}
    for name, semantic_value in semantics_data.items():
        if not isinstance(name, str):
            raise ValueError(f"{field_name} entry name must be a string")
        if not name.strip():
            raise ValueError(f"{field_name} entry name must not be blank")
        validated[name] = _require_mapping(
            semantic_value, f"Semantic entry '{name}' must be a mapping"
        )
    return validated


def _validate_rule(
    raw_rule_data: object,
) -> tuple[
    str,
    dict[str, object],
    dict[str, dict[str, object]],
    dict[str, object],
    dict[str, dict[str, object]],
]:
    if not isinstance(raw_rule_data, dict):
        raise ValueError("Each rule must be a mapping")
    rule_data = cast("dict[str, object]", raw_rule_data)
    rule_id = _validate_rule_id(rule_data.get("id"))

    condition_value = rule_data.get("condition")
    if not isinstance(condition_value, Mapping):
        raise ValueError("Rule is missing 'condition'")
    condition_data = _require_mapping(condition_value, "Invalid condition")
    if not isinstance(condition_data.get("symbolic"), Mapping):
        raise ValueError("Rule is missing 'condition.symbolic'")
    condition_semantics = _validate_semantics(
        condition_data.get("semantics", {}), "condition.semantics"
    )

    action_value = rule_data.get("action")
    if not isinstance(action_value, Mapping):
        raise ValueError("Rule is missing 'action'")
    action_data = _require_mapping(action_value, "Invalid action")
    commands_value = action_data.get("commands")
    if not isinstance(commands_value, Mapping):
        raise ValueError("Rule is missing 'action.commands'")
    commands_data = cast("dict[str, object]", commands_value)
    action_semantics = _validate_semantics(
        action_data.get("semantics", {}), "action.semantics"
    )

    return (
        rule_id,
        condition_data,
        condition_semantics,
        commands_data,
        action_semantics,
    )


def resolve_ruleset_path() -> Path:
    configured_path = os.environ.get(ENV_RULESET_PATH)
    if configured_path:
        return Path(configured_path).expanduser().resolve()
    return DEFAULT_RULESET_PATH


def load_rules(path: Path | None = None) -> dict[str, Rule]:
    ruleset_path = path if path is not None else resolve_ruleset_path()
    if not ruleset_path.is_file():
        raise FileNotFoundError(f"Ruleset file does not exist: {ruleset_path}")

    with ruleset_path.open(encoding="utf-8") as rules_file:
        try:
            payload = cast("object", yaml.safe_load(rules_file))  # type: ignore[misc]
        except Exception as error:
            error_message = str(error)
            raise ValueError(
                f"Failed to parse ruleset {ruleset_path}: {error_message}"
            ) from error

    if not isinstance(payload, dict):
        raise ValueError("Ruleset top-level payload must be a mapping")
    payload_data = cast("dict[str, object]", payload)
    rules_value = payload_data.get("rules")
    if not isinstance(rules_value, list):
        raise ValueError("Ruleset top-level 'rules' must be a list")

    rules: dict[str, Rule] = {}
    for raw_rule_data in cast("list[object]", rules_value):
        (
            rule_id,
            condition_data,
            condition_semantics,
            commands_data,
            action_semantics,
        ) = _validate_rule(raw_rule_data)
        if rule_id in rules:
            raise ValueError(f"Duplicate rule id: {rule_id}")

        condition = ProceduralCondition(
            rule_id=rule_id,
            condition=_struct_from_dict(
                cast("Mapping[str, object]", condition_data["symbolic"])
            ),
            semantics=_struct_from_dict(condition_semantics),
        )
        commands: dict[str, BufferOperation] = {}
        for name, command_value in commands_data.items():
            command_name, target_module_id, command, params = _validate_command(
                name, command_value
            )
            commands[command_name] = BufferOperation(
                target_module_id=target_module_id,
                command=command,
                params=_struct_from_dict(params),
            )
        semantics = {
            name: _struct_from_dict(semantic_data)
            for name, semantic_data in action_semantics.items()
        }
        rules[rule_id] = Rule(
            id=rule_id,
            condition=condition,
            action=NeuroAction(
                rule_id=rule_id,
                commands=commands,
                semantics=semantics,
            ),
            utility=INITIAL_RULE_UTILITY,
        )

    return rules
