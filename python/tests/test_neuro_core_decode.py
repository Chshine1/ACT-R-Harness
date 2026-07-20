from actr_harness.generated.grpc.actr import BufferOperation, NeuroAction
from actr_harness.generated.grpc.actr.services import DecodeActionRequest

import pytest
from actr_harness.services.neuro_core.neuro_core import NeuroCore
from betterproto.lib.google.protobuf import Struct
from pathlib import Path
from typing import Any, Callable


def make_action(
        rule_id: str,
        commands: dict[str, dict[str, Any]] | None,
        semantics: dict[str, dict[str, Any]] | None = None,
) -> NeuroAction:
    return NeuroAction(
        rule_id=rule_id,
        commands={
            name: BufferOperation.from_dict(value)
            for name, value in (commands or {}).items()
        },
        semantics={
            name: Struct.from_dict(value)
            for name, value in (semantics or {}).items()
        },
    )


def assert_any_value_contains(params: dict[str, Any], tokens: list[str]) -> None:
    values = [value.lower() for value in params.values() if isinstance(value, str)]
    assert values
    assert any(any(token in value for token in tokens) for value in values)


@pytest.mark.vcr
class TestActions:

    # ---- rule-init: retrieveMemory needs deriving parameters ----
    async def test_init_retrieve_memory(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
    ):
        rule = rule_by_id("rule-init")
        action = rule["action"]
        current_states, schemas = runtime_snapshot_factory(intention_status="start")
        action_intent = make_action(
            "rule-init",
            action.get("commands"),
            action.get("semantics"),
        )

        req = DecodeActionRequest(
            action_intent=action_intent,
            current_states=current_states,
            schemas=schemas,
        )

        resp = await real_neuro_core.DecodeAction(req)

        assert len(resp.operations) == 2
        cmd0 = resp.operations[0]
        assert cmd0.target_module_id == "declarative_memory"
        assert cmd0.command == "retrieve_chunk"
        params = cmd0.params.to_dict()
        assert params
        assert all(isinstance(value, str) for value in params.values())
        assert_any_value_contains(params, ["authentication", "auth"])
        assert_any_value_contains(params, ["module"])

        cmd1 = resp.operations[1]
        assert cmd1.command == "modify_slot"
        assert cmd1.params.to_dict()["slot_value"] == "memory_queried"

    # ---- rule-memory-hit: skips when openFileIfPath misses file_path ----
    async def test_memory_hit_skip_open_file(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
    ):
        rule = rule_by_id("rule-memory-hit")
        action = rule["action"]
        current_states, schemas = runtime_snapshot_factory(
            intention_status="memory_queried",
            retrieved_chunk={
                "id": "chunk-1",
                "creation_time": 1.0,
                "slots": {},
            },
        )
        action_intent = make_action(
            "rule-memory-hit",
            action["commands"],
            action.get("semantics"),
        )

        req = DecodeActionRequest(
            action_intent=action_intent,
            current_states=current_states,
            schemas=schemas,
        )

        resp = await real_neuro_core.DecodeAction(req)

        commands = [c.command for c in resp.operations]
        assert "open_file" not in commands
        assert len(resp.operations) == 2

    # ---- rule-memory-hit: preserves open_file when file_path exists ----
    async def test_memory_hit_include_open_file(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
    ):
        rule = rule_by_id("rule-memory-hit")
        action = rule["action"]
        expected_file_path = str(Path(__file__).resolve().parents[2] / "README.md")
        current_states, schemas = runtime_snapshot_factory(
            intention_status="memory_queried",
            retrieved_chunk={
                "id": "chunk-1",
                "creation_time": 1.0,
                "slots": {
                    "file_path": expected_file_path,
                    "keywords": "README documentation",
                },
            },
        )
        action_intent = make_action(
            "rule-memory-hit",
            action["commands"],
            action.get("semantics"),
        )

        req = DecodeActionRequest(
            action_intent=action_intent,
            current_states=current_states,
            schemas=schemas,
        )

        resp = await real_neuro_core.DecodeAction(req)
        commands = [c.command for c in resp.operations]
        assert "open_file" in commands
        assert len(resp.operations) == 3
        set_tags = next(c for c in resp.operations if c.command == "set_attention_tags")
        tags = set_tags.params.to_dict()["tags"]
        assert isinstance(tags, list)
        assert all(isinstance(tag, str) for tag in tags)
        assert any(tag.startswith("read") for tag in tags)
        assert "documentation" in tags
        open_file = next(c for c in resp.operations if c.command == "open_file")
        assert open_file.params.to_dict()["file_path"] == expected_file_path

    # ---- rule-memory-miss: pushSubgoal derives slots ----
    async def test_memory_miss_push_subgoal(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
    ):
        rule = rule_by_id("rule-memory-miss")
        action = rule["action"]
        current_states, schemas = runtime_snapshot_factory(
            intention_status="memory_queried",
            intention_query="explore auth",
        )
        action_intent = make_action(
            "rule-memory-miss",
            action["commands"],
            action.get("semantics"),
        )

        req = DecodeActionRequest(
            action_intent=action_intent,
            current_states=current_states,
            schemas=schemas,
        )

        resp = await real_neuro_core.DecodeAction(req)
        assert len(resp.operations) == 1
        cmd = resp.operations[0]
        assert cmd.command == "push_subgoal"
        assert cmd.target_module_id == "intention"
        params = cmd.params.to_dict()
        assert params["slots"]["parent_goal_id"] == "goal-1"
        assert params["slots"]["query"] == "explore auth"
        assert params["slots"]["status"] == "exploring"
