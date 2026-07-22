# Procedural Rules Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Load the active procedural rules from `shared/ruleset/lab.yml` into the Python procedural-memory service, expose them through the existing gRPC surface, and support a minimal `ACTR_RULESET_PATH` override.

**Architecture:** Add a small `procedural_rules.py` loader that owns path resolution, YAML parsing, validation, and YAML-to-proto mapping. Keep `ProceduralMemory` focused on runtime behavior by importing `Rule` and `load_rules`, auto-loading rules when none are injected, and reusing the existing startup path in `main.py` without extra orchestration changes.

**Tech Stack:** Python 3.13, PyYAML, betterproto/grpclib, pytest, pixi

---

## File Structure

- `python/src/actr_harness/services/procedural_rules.py`: new `Rule` dataclass plus ruleset path resolution, YAML loading, validation, and mapping helpers
- `python/src/actr_harness/services/procedural_memory.py`: service constructor updated to accept injected rules or auto-load defaults
- `python/tests/test_procedural_rules.py`: loader mapping, override, and fail-fast coverage
- `python/tests/test_procedural_memory.py`: service startup, `get_all_conditions()`, and `learn_utility()` coverage
- `python/src/actr_harness/main.py`: no code change expected; the existing `ProceduralMemory(...)` startup call becomes the integration point once the service auto-loads rules

### Task 1: Add Loader Mapping Coverage

**Files:**

- Create: `python/tests/test_procedural_rules.py`
- Create: `python/src/actr_harness/services/procedural_rules.py`

- [ ] **Step 1: Write the failing loader mapping tests**

```python
from actr_harness.services.procedural_rules import (
    DEFAULT_RULESET_PATH,
    INITIAL_RULE_UTILITY,
    load_rules,
)


def test_load_rules_maps_three_active_rules_from_default_ruleset() -> None:
    rules = load_rules(DEFAULT_RULESET_PATH)

    assert set(rules) == {"rule-init", "rule-memory-hit", "rule-memory-miss"}
    assert all(rule.utility == INITIAL_RULE_UTILITY for rule in rules.values())


def test_rule_init_maps_condition_and_commands() -> None:
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-init"]

    assert rule.condition.rule_id == "rule-init"
    assert rule.condition.condition.to_dict() == {
        "type": "equals",
        "slot": "intention.current_goal.slots.status",
        "value": "start",
    }
    assert rule.condition.semantics.to_dict() == {}

    retrieve_memory = rule.action.commands["retrieveMemory"]
    update_status = rule.action.commands["updateStatus"]

    assert retrieve_memory.target_module_id == "declarative_memory"
    assert retrieve_memory.command == "retrieve_chunk"
    assert retrieve_memory.params.to_dict() == {}

    assert update_status.target_module_id == "intention"
    assert update_status.command == "modify_slot"
    assert update_status.params.to_dict() == {
        "slot": "status",
        "slot_value": "memory_queried",
    }


def test_rule_memory_hit_preserves_commands_and_semantics() -> None:
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-memory-hit"]

    assert set(rule.action.commands) == {"setTags", "openFileIfPath", "updateStatus"}

    open_file = rule.action.commands["openFileIfPath"]
    assert open_file.target_module_id == "code_viewport"
    assert open_file.command == "open_file"
    assert open_file.params.to_dict() == {}

    assert rule.action.semantics["command:setTags"].to_dict() == {
        "sources": ["declarative_memory.retrieved_chunk.slots.keywords"],
        "params": {
            "tags": "Split and normalize the retrieved keywords into lowercase tags. If the source `keywords` is missing, set an empty list",
        },
    }
    assert rule.action.semantics["meta:skip_if_missing"].to_dict() == {
        "command": "openFileIfPath",
        "instruction": "ONLY apply this skip rule to the command `openFileIfPath`. If the source `file_path` is missing or None, you MUST NOT generate any operation for this command",
    }


def test_rule_memory_miss_maps_push_subgoal_params() -> None:
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-memory-miss"]

    push_subgoal = rule.action.commands["pushSubgoal"]

    assert push_subgoal.target_module_id == "intention"
    assert push_subgoal.command == "push_subgoal"
    assert push_subgoal.params.to_dict() == {
        "id": "ExploreFileSystem",
        "slots": {
            "parent_goal_id": "${intention.current_goal.slots.id}",
            "query": "${intention.current_goal.slots.query}",
            "status": "exploring",
        },
    }
```

- [ ] **Step 2: Run the loader mapping tests to verify they fail**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_rules.py -v
```

Expected:

```text
FAIL with ModuleNotFoundError for actr_harness.services.procedural_rules
```

- [ ] **Step 3: Write the minimal loader implementation**

```python
from dataclasses import dataclass
from pathlib import Path

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
INITIAL_RULE_UTILITY = 0.0


@dataclass(slots=True)
class Rule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float = INITIAL_RULE_UTILITY


def load_rules(path: Path) -> dict[str, Rule]:
    with path.open("r", encoding="utf-8") as handle:
        payload = yaml.safe_load(handle)

    rules: dict[str, Rule] = {}
    for rule_data in payload["rules"]:
        rule_id = rule_data["id"]
        condition_data = rule_data["condition"]
        action_data = rule_data["action"]

        condition = ProceduralCondition(
            rule_id=rule_id,
            condition=Struct.from_dict(condition_data["symbolic"]),
            semantics=Struct.from_dict(condition_data.get("semantics", {})),
        )
        action = NeuroAction(
            rule_id=rule_id,
            commands={
                name: BufferOperation(
                    target_module_id=command_data["target_module_id"],
                    command=command_data["command"],
                    params=Struct.from_dict(command_data.get("params", {})),
                )
                for name, command_data in action_data["commands"].items()
            },
            semantics={
                name: Struct.from_dict(semantics_data)
                for name, semantics_data in action_data.get("semantics", {}).items()
            },
        )
        rules[rule_id] = Rule(id=rule_id, condition=condition, action=action)

    return rules
```

- [ ] **Step 4: Run the loader mapping tests to verify they pass**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_rules.py -v
```

Expected:

```text
PASS 4 tests in tests/test_procedural_rules.py
```

- [ ] **Step 5: Commit the loader mapping slice**

```bash
git add python/tests/test_procedural_rules.py python/src/actr_harness/services/procedural_rules.py
git commit -m "feat: add procedural rules loader"
```

### Task 2: Add Path Override and Fail-Fast Validation

**Files:**

- Modify: `python/tests/test_procedural_rules.py`
- Modify: `python/src/actr_harness/services/procedural_rules.py`

- [ ] **Step 1: Extend the loader tests with override and validation coverage**

```python
import pytest

from actr_harness.services.procedural_rules import (
    DEFAULT_RULESET_PATH,
    ENV_RULESET_PATH,
    INITIAL_RULE_UTILITY,
    load_rules,
    resolve_ruleset_path,
)


def test_resolve_ruleset_path_prefers_env_override(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
) -> None:
    override_path = tmp_path / "override.yml"
    override_path.write_text("rules: []\n", encoding="utf-8")

    monkeypatch.setenv(ENV_RULESET_PATH, str(override_path))

    assert resolve_ruleset_path() == override_path.resolve()


def test_load_rules_uses_env_override(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
) -> None:
    override_path = tmp_path / "override.yml"
    override_path.write_text(
        """rules:
  - id: "override-rule"
    condition:
      symbolic:
        type: equals
        slot: "intention.current_goal.slots.status"
        value: "start"
    action:
      commands:
        updateStatus:
          target_module_id: "intention"
          command: "modify_slot"
          params:
            slot: "status"
            slot_value: "override"
""",
        encoding="utf-8",
    )

    monkeypatch.setenv(ENV_RULESET_PATH, str(override_path))

    rules = load_rules()

    assert set(rules) == {"override-rule"}


def test_load_rules_raises_for_missing_ruleset_file(tmp_path) -> None:
    with pytest.raises(FileNotFoundError, match="Ruleset file not found"):
        load_rules(tmp_path / "missing.yml")


def test_load_rules_raises_for_missing_rules_list(tmp_path) -> None:
    invalid_path = tmp_path / "invalid.yml"
    invalid_path.write_text("not_rules: []\n", encoding="utf-8")

    with pytest.raises(ValueError, match="rules"):
        load_rules(invalid_path)


def test_load_rules_raises_for_missing_action_commands(tmp_path) -> None:
    invalid_path = tmp_path / "invalid-rule.yml"
    invalid_path.write_text(
        """rules:
  - id: "broken-rule"
    condition:
      symbolic:
        type: equals
        slot: "intention.current_goal.slots.status"
        value: "start"
    action: {}
""",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="action.commands"):
        load_rules(invalid_path)
```

- [ ] **Step 2: Run the targeted override and validation tests to verify they fail**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_rules.py -k "override or raises or resolve_ruleset_path" -v
```

Expected:

```text
FAIL because resolve_ruleset_path and ENV_RULESET_PATH do not exist, and load_rules() still requires an explicit path
```

- [ ] **Step 3: Upgrade the loader to support env overrides and validation**

```python
from __future__ import annotations

import os
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path

import yaml
from betterproto.lib.google.protobuf import Struct

from actr_harness.generated.grpc.actr import (
    BufferOperation,
    NeuroAction,
    ProceduralCondition,
)

ENV_RULESET_PATH = "ACTR_RULESET_PATH"
DEFAULT_RULESET_PATH = (
    Path(__file__).resolve().parents[4] / "shared" / "ruleset" / "lab.yml"
)
INITIAL_RULE_UTILITY = 0.0


@dataclass(slots=True)
class Rule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float = INITIAL_RULE_UTILITY


def resolve_ruleset_path() -> Path:
    override = os.getenv(ENV_RULESET_PATH)
    if override:
        return Path(override).expanduser().resolve()
    return DEFAULT_RULESET_PATH


def load_rules(path: Path | None = None) -> dict[str, Rule]:
    ruleset_path = path or resolve_ruleset_path()
    if not ruleset_path.exists():
        raise FileNotFoundError(f"Ruleset file not found: {ruleset_path}")

    with ruleset_path.open("r", encoding="utf-8") as handle:
        payload = yaml.safe_load(handle)

    root = _require_mapping(payload, "ruleset")
    rules_data = root.get("rules")
    if not isinstance(rules_data, list):
        raise ValueError(f"ruleset must contain a 'rules' list: {ruleset_path}")

    return {
        rule.id: rule
        for rule in (_build_rule(rule_data) for rule_data in rules_data)
    }


def _build_rule(rule_data: object) -> Rule:
    rule = _require_mapping(rule_data, "rule")
    rule_id = _require_non_empty_string(rule.get("id"), "rule.id")

    condition_block = _require_mapping(rule.get("condition"), "condition")
    symbolic_condition = _require_mapping(
        condition_block.get("symbolic"),
        "condition.symbolic",
    )
    condition_semantics = _require_optional_mapping(
        condition_block.get("semantics"),
        "condition.semantics",
    )

    action_block = _require_mapping(rule.get("action"), "action")
    commands_block = _require_mapping(action_block.get("commands"), "action.commands")
    semantics_block = _require_optional_mapping(
        action_block.get("semantics"),
        "action.semantics",
    )

    commands: dict[str, BufferOperation] = {}
    for command_name, command_payload in commands_block.items():
        key = _require_non_empty_string(command_name, "action.commands key")
        commands[key] = _build_buffer_operation(key, command_payload)

    semantics: dict[str, Struct] = {}
    for semantics_name, semantics_payload in semantics_block.items():
        key = _require_non_empty_string(semantics_name, "action.semantics key")
        semantics[key] = Struct.from_dict(
            dict(_require_mapping(semantics_payload, f"action.semantics.{key}"))
        )

    return Rule(
        id=rule_id,
        condition=ProceduralCondition(
            rule_id=rule_id,
            condition=Struct.from_dict(dict(symbolic_condition)),
            semantics=Struct.from_dict(dict(condition_semantics)),
        ),
        action=NeuroAction(
            rule_id=rule_id,
            commands=commands,
            semantics=semantics,
        ),
    )


def _build_buffer_operation(
    command_name: str,
    command_payload: object,
) -> BufferOperation:
    payload = _require_mapping(command_payload, f"action.commands.{command_name}")
    target_module_id = _require_non_empty_string(
        payload.get("target_module_id"),
        f"action.commands.{command_name}.target_module_id",
    )
    command = _require_non_empty_string(
        payload.get("command"),
        f"action.commands.{command_name}.command",
    )
    params = _require_optional_mapping(
        payload.get("params"),
        f"action.commands.{command_name}.params",
    )

    return BufferOperation(
        target_module_id=target_module_id,
        command=command,
        params=Struct.from_dict(dict(params)),
    )


def _require_mapping(value: object, label: str) -> Mapping[str, object]:
    if not isinstance(value, Mapping):
        raise ValueError(f"{label} must be a mapping.")
    return value


def _require_optional_mapping(value: object, label: str) -> Mapping[str, object]:
    if value is None:
        return {}
    return _require_mapping(value, label)


def _require_non_empty_string(value: object, label: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} must be a non-empty string.")
    return value
```

- [ ] **Step 4: Run the full loader test file to verify all scenarios pass**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_rules.py -v
```

Expected:

```text
PASS 9 tests in tests/test_procedural_rules.py
```

- [ ] **Step 5: Commit the override and validation slice**

```bash
git add python/tests/test_procedural_rules.py python/src/actr_harness/services/procedural_rules.py
git commit -m "feat: validate procedural ruleset loading"
```

### Task 3: Integrate the Loader into ProceduralMemory

**Files:**

- Create: `python/tests/test_procedural_memory.py`
- Modify: `python/src/actr_harness/services/procedural_memory.py`

- [ ] **Step 1: Write the failing ProceduralMemory integration tests**

```python
import pytest
from betterproto.lib.google.protobuf import Empty

from actr_harness.generated.grpc.actr.services import LearnUtilityRequest
from actr_harness.services.procedural_memory import ProceduralMemory
from actr_harness.services.procedural_rules import (
    DEFAULT_RULESET_PATH,
    ENV_RULESET_PATH,
    INITIAL_RULE_UTILITY,
    load_rules,
)


async def test_procedural_memory_loads_rules_by_default(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv(ENV_RULESET_PATH, raising=False)

    memory = ProceduralMemory()

    assert set(memory.rules) == {"rule-init", "rule-memory-hit", "rule-memory-miss"}


async def test_get_all_conditions_returns_loaded_rule_ids(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv(ENV_RULESET_PATH, raising=False)

    memory = ProceduralMemory()
    response = await memory.get_all_conditions(Empty())

    assert {condition.rule_id for condition in response.conditions} == {
        "rule-init",
        "rule-memory-hit",
        "rule-memory-miss",
    }


async def test_learn_utility_updates_loaded_rule() -> None:
    memory = ProceduralMemory(
        rules=load_rules(DEFAULT_RULESET_PATH),
        learning_rate=0.25,
    )

    assert memory.rules["rule-init"].utility == INITIAL_RULE_UTILITY

    await memory.learn_utility(LearnUtilityRequest(rule_id="rule-init", reward=1.0))

    assert memory.rules["rule-init"].utility == pytest.approx(0.25)
```

- [ ] **Step 2: Run the ProceduralMemory tests to verify they fail**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_memory.py -v
```

Expected:

```text
FAIL because ProceduralMemory starts with an empty rules dict and does not accept injected rules
```

- [ ] **Step 3: Update ProceduralMemory to import and auto-load rules**

```python
import math
import random

from betterproto.lib.google.protobuf import Empty

from actr_harness.generated.grpc.actr import NeuroAction
from actr_harness.generated.grpc.actr.services import (
    GetAllConditionsResponse,
    LearnUtilityRequest,
    ProceduralMemoryBase,
    SelectRuleRequest,
)
from actr_harness.services.procedural_rules import Rule, load_rules


class ProceduralMemory(ProceduralMemoryBase):
    def __init__(
        self,
        *,
        rules: dict[str, Rule] | None = None,
        temperature: float = 0.5,
        learning_rate: float = 0.1,
    ) -> None:
        self.rules = dict(rules) if rules is not None else load_rules()
        self.temperature = temperature
        self.lr = learning_rate

    async def get_all_conditions(self, request: Empty) -> GetAllConditionsResponse:
        _ = request
        return GetAllConditionsResponse(
            conditions=[rule.condition for rule in self.rules.values()]
        )

    async def select_rule(self, select_rule_request: SelectRuleRequest) -> NeuroAction:
        applicable = [
            rule
            for rule in self.rules.values()
            if rule.id in select_rule_request.satisfied_rule_ids
        ]
        if not applicable:
            raise ValueError("No applicable rule found.")

        utilities = [rule.utility for rule in applicable]
        max_utility = max(utilities)
        exp_utilities = [
            math.exp((utility - max_utility) / self.temperature)
            for utility in utilities
        ]
        total_weight = sum(exp_utilities)

        probabilities = [weight / total_weight for weight in exp_utilities]
        selected_rule = random.choices(applicable, weights=probabilities, k=1)[0]

        return selected_rule.action

    async def learn_utility(self, learn_utility_request: LearnUtilityRequest) -> Empty:
        rule_id = learn_utility_request.rule_id

        if rule_id in self.rules:
            rule = self.rules[rule_id]
            rule.utility += self.lr * (learn_utility_request.reward - rule.utility)

        return Empty()
```

`python/src/actr_harness/main.py` should remain unchanged in this task. Its existing `ProceduralMemory(temperature=temperature, learning_rate=lr)` construction now exercises the default loader path, which satisfies the startup integration requirement without adding extra wiring.

- [ ] **Step 4: Run the focused and full Python test suite to verify green**

Run:

```bash
pixi run -e dev pytest tests/test_procedural_memory.py tests/test_procedural_rules.py tests/test_neuro_core_condition.py tests/test_neuro_core_decode.py -v
```

Expected:

```text
PASS all tests, including the new loader/service coverage and the existing neuro core suites
```

- [ ] **Step 5: Commit the ProceduralMemory integration slice**

```bash
git add python/tests/test_procedural_memory.py python/src/actr_harness/services/procedural_memory.py
git commit -m "feat: auto-load procedural rules in service"
```
