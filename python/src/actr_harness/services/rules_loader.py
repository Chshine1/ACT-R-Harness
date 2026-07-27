from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml
from betterproto.lib.google.protobuf import Struct

from actr_harness.generated.grpc.actr import (
    BufferOperation,
    NeuroAction,
    ProceduralCondition,
)

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
            condition=_to_struct(raw_condition.get("symbolic", {})),
            semantics=_to_struct(raw_condition.get("semantic")),
        )
        action = NeuroAction(
            rule_id=rule_id,
            commands={
                alias: BufferOperation.from_dict(command)
                for alias, command in raw_action.get("commands", {}).items()
            },
            semantics={
                alias: _to_struct(semantic)
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


def _to_struct(payload: Any) -> Struct:
    if payload is None:
        return Struct()
    if isinstance(payload, dict):
        return Struct().from_dict(payload)
    return Struct().from_dict({"value": payload})
