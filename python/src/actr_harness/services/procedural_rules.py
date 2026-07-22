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

DEFAULT_RULESET_PATH = Path(__file__).resolve().parents[4] / "shared" / "ruleset" / "lab.yml"
INITIAL_RULE_UTILITY = 0.0


@dataclass(slots=True)
class Rule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float


def load_rules(path: Path) -> dict[str, Rule]:
    with path.open(encoding="utf-8") as rules_file:
        ruleset: dict[str, Any] = yaml.safe_load(rules_file)

    rules: dict[str, Rule] = {}
    for rule_data in ruleset["rules"]:
        rule_id = rule_data["id"]
        condition_data = rule_data["condition"]
        action_data = rule_data["action"]

        condition = ProceduralCondition(
            rule_id=rule_id,
            condition=Struct.from_dict(condition_data["symbolic"]),
            semantics=Struct.from_dict(condition_data.get("semantics", {})),
        )
        commands = {
            name: BufferOperation(
                target_module_id=command_data["target_module_id"],
                command=command_data["command"],
                params=Struct.from_dict(command_data.get("params", {})),
            )
            for name, command_data in action_data.get("commands", {}).items()
        }
        semantics = {
            name: Struct.from_dict(semantic_data)
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
