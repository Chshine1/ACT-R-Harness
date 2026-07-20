from actr_harness.generated.grpc.actr import ModuleSchema
from actr_harness.generated.grpc.actr import BufferState
from typing import Callable

from betterproto.lib.google.protobuf import Struct

from actr_harness.generated.grpc.actr import ProceduralCondition
from actr_harness.generated.grpc.actr.services import EvaluateConditionsRequest

from actr_harness.services.neuro_core.neuro_core import NeuroCore

RuntimeSnapshotFactory = Callable[..., tuple[list[BufferState], list[ModuleSchema]]]


def make_condition(rule_id: str, symbolic: dict) -> ProceduralCondition:
    return ProceduralCondition(
        rule_id=rule_id,
        condition=Struct().from_dict(symbolic),
    )


class TestConditions:
    # --- rule-init: status == "start" ---
    async def test_init_condition_true(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory: RuntimeSnapshotFactory,
    ):
        rule = rule_by_id("rule-init")
        buffer_states, _ = runtime_snapshot_factory(intention_status="start")
        condition = make_condition("rule-init", rule["condition"]["symbolic"])

        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-init" in resp.satisfied_rule_ids

    async def test_init_condition_false(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory: RuntimeSnapshotFactory,
    ):
        rule = rule_by_id("rule-init")
        buffer_states, _ = runtime_snapshot_factory(intention_status="idle")
        condition = make_condition("rule-init", rule["condition"]["symbolic"])
        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-init" not in resp.satisfied_rule_ids

    # --- rule-memory-hit: status=="memory_queried" AND exist(retrieved_chunk) ---
    async def test_memory_hit_condition_true(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory: RuntimeSnapshotFactory,
    ):
        rule = rule_by_id("rule-memory-hit")
        buffer_states, _ = runtime_snapshot_factory(
            intention_status="memory_queried",
            retrieved_chunk={
                "id": "chunk-1",
                "creation_time": 1.0,
                "slots": {
                    "file_path": "/src/auth.py",
                    "keywords": "authentication module",
                },
            },
        )
        condition = make_condition("rule-memory-hit", rule["condition"]["symbolic"])
        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-memory-hit" in resp.satisfied_rule_ids

    async def test_memory_hit_condition_false_missing_chunk(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory: RuntimeSnapshotFactory,
    ):
        rule = rule_by_id("rule-memory-hit")
        buffer_states, _ = runtime_snapshot_factory(intention_status="memory_queried")
        condition = make_condition("rule-memory-hit", rule["condition"]["symbolic"])
        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-memory-hit" not in resp.satisfied_rule_ids

    # --- rule-memory-miss: status=="memory_queried" AND NOT exist(retrieved_chunk) ---
    async def test_memory_miss_condition_true(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory: RuntimeSnapshotFactory,
    ):
        rule = rule_by_id("rule-memory-miss")
        buffer_states, _ = runtime_snapshot_factory(intention_status="memory_queried")
        condition = make_condition("rule-memory-miss", rule["condition"]["symbolic"])
        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-memory-miss" in resp.satisfied_rule_ids

    async def test_memory_miss_condition_false_chunk_exists(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
    ):
        rule = rule_by_id("rule-memory-miss")
        buffer_states, _ = runtime_snapshot_factory(
            intention_status="memory_queried",
            retrieved_chunk={
                "id": "chunk-1",
                "creation_time": 1.0,
                "slots": {},
            },
        )
        condition = make_condition("rule-memory-miss", rule["condition"]["symbolic"])
        req = EvaluateConditionsRequest(
            conditions=[condition],
            buffer_states=buffer_states,
        )
        resp = await real_neuro_core.EvaluateConditions(req)
        assert "rule-memory-miss" not in resp.satisfied_rule_ids
