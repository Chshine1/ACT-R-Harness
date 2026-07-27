import pytest
from betterproto.lib.google.protobuf import Empty

from actr_harness.generated.grpc.actr.services import SelectRuleRequest
from actr_harness.services.procedural_memory import ProceduralMemory


@pytest.mark.asyncio
async def test_procedural_memory_loads_active_rules() -> None:
    service = ProceduralMemory(temperature=0.0)

    response = await service.get_all_conditions(Empty())

    assert [condition.rule_id for condition in response.conditions] == [
        "rule-init",
        "rule-memory-hit",
        "rule-memory-miss",
    ]


@pytest.mark.asyncio
async def test_procedural_memory_selects_rule_deterministically_when_temperature_zero(
) -> None:
    service = ProceduralMemory(temperature=0.0)

    action = await service.select_rule(
        SelectRuleRequest(satisfied_rule_ids=["rule-init", "rule-memory-hit"])
    )

    assert action.rule_id == "rule-memory-hit"
