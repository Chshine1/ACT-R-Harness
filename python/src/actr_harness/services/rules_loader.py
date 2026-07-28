from __future__ import annotations

from actr_harness.generated.grpc.actr import (
    BufferOperation,
    NeuroAction,
    ProceduralCondition,
)

import yaml
from actr_harness.utils import dict_to_struct
from dataclasses import dataclass
from pathlib import Path

DEFAULT_RULESET_PATH = (
        Path(__file__).resolve().parents[4] / "shared" / "ruleset" / "lab.yml"
)


@dataclass(frozen=True)
class LoadedRule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float


def load_ruleset(
        rules_path: str | Path | None = None,
        default_utility: float = 0.0,
) -> dict[str, LoadedRule]:
    path = Path(rules_path) if rules_path is not None else DEFAULT_RULESET_PATH
    with path.open("r", encoding="utf-8") as handle:
        payload = yaml.safe_load(handle) or {}

    raw_rules = payload.get("rules", [])
    loaded: dict[str, LoadedRule] = {}
    for raw_rule in raw_rules:
        rule_id = raw_rule["id"]
        raw_condition = raw_rule.get("condition", {})
        raw_action = raw_rule.get("action", {})

        condition = ProceduralCondition(
            rule_id=rule_id,
            condition=dict_to_struct(raw_condition.get("symbolic", {})),
            semantics=dict_to_struct(raw_condition.get("semantic", {})),
        )
        action = NeuroAction(
            rule_id=rule_id,
            commands={
                alias: BufferOperation(
                    target_module_id=command["target_module_id"],
                    command=command["command"],
                    params=dict_to_struct(command.get("params", {})),
                )
                for alias, command in raw_action.get("commands", {}).items()
            },
            semantics={
                alias: dict_to_struct(semantic)
                for alias, semantic in raw_action.get("semantics", {}).items()
            },
        )
        loaded[rule_id] = LoadedRule(
            id=rule_id,
            condition=condition,
            action=action,
            utility=float(raw_rule.get("utility", default_utility)),
        )
    return loaded
