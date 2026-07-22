import os
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


def _struct_from_dict(data: dict[str, object]) -> Struct:
    return Struct.from_dict(data)  # type: ignore[misc, no-any-return]


def _validate_rule_id(value: object) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError("Rule is missing 'id'")
    return value


def resolve_ruleset_path() -> Path:
    configured_path = os.environ.get(ENV_RULESET_PATH)
    if configured_path:
        return Path(configured_path).resolve()
    return DEFAULT_RULESET_PATH


def load_rules(path: Path | None = None) -> dict[str, Rule]:
    ruleset_path = path if path is not None else resolve_ruleset_path()
    if not ruleset_path.is_file():
        raise FileNotFoundError(f"Ruleset file does not exist: {ruleset_path}")

    with ruleset_path.open(encoding="utf-8") as rules_file:
        payload = cast("object", yaml.safe_load(rules_file))  # type: ignore[misc]

    if not isinstance(payload, dict):
        raise ValueError("Ruleset top-level payload must be a mapping")
    payload_data = cast("dict[str, object]", payload)
    rules_value = payload_data.get("rules")
    if not isinstance(rules_value, list):
        raise ValueError("Ruleset top-level 'rules' must be a list")

    rules: dict[str, Rule] = {}
    for raw_rule_data in cast("list[object]", rules_value):
        if not isinstance(raw_rule_data, dict):
            raise ValueError("Each rule must be a mapping")
        rule_data = cast("dict[str, object]", raw_rule_data)
        if "id" not in rule_data:
            raise ValueError("Rule is missing 'id'")
        condition_value = rule_data.get("condition")
        if not isinstance(condition_value, dict):
            raise ValueError("Rule is missing 'condition'")
        condition_data = cast("ConditionData", condition_value)
        if not isinstance(condition_data.get("symbolic"), dict):
            raise ValueError("Rule is missing 'condition.symbolic'")
        action_value = rule_data.get("action")
        if not isinstance(action_value, dict):
            raise ValueError("Rule is missing 'action'")
        action_data = cast("ActionData", action_value)
        if not isinstance(action_data.get("commands"), dict):
            raise ValueError("Rule is missing 'action.commands'")

        validated_rule_data = cast("RuleData", rule_data)
        rule_id = _validate_rule_id(validated_rule_data["id"])

        condition = ProceduralCondition(
            rule_id=rule_id,
            condition=_struct_from_dict(condition_data["symbolic"]),
            semantics=_struct_from_dict(condition_data.get("semantics", {})),
        )
        commands = {
            name: BufferOperation(
                target_module_id=command_data["target_module_id"],
                command=command_data["command"],
                params=_struct_from_dict(command_data.get("params", {})),
            )
            for name, command_data in action_data.get("commands", {}).items()
        }
        semantics = {
            name: _struct_from_dict(semantic_data)
            for name, semantic_data in action_data.get("semantics", {}).items()
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
