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
