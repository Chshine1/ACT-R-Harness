from actr_harness.generated.grpc.actr import BufferOperation, NeuroAction
from actr_harness.generated.grpc.actr.services import DecodeActionRequest

import json
import pytest
from actr_harness.services.neuro_core.neuro_core import NeuroCore
from betterproto.lib.google.protobuf import Struct
from deepdiff import DeepDiff
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


def assert_operation_match(
        actual_op,
        expected: dict[str, Any],
        ignore_extra_keys: bool = True,
        **deepdiff_kwargs,
) -> None:
    actual = {
        "target_module_id": actual_op.target_module_id,
        "command": actual_op.command,
        "params": actual_op.params.to_dict(),
    }
    if ignore_extra_keys:
        actual = {k: v for k, v in actual.items() if k in expected}
        if "params" in expected:
            expected_params = expected["params"]
            actual["params"] = {k: actual["params"].get(k) for k in expected_params}

    diff = DeepDiff(expected, actual, ignore_order=True, **deepdiff_kwargs)
    if diff:
        raise AssertionError(
            f"Operation mismatch:\n{diff.pretty()}\nFull actual: {actual}"
        )


def assert_operations_contain_commands(ops, required_commands: set[str]) -> None:
    existing = {op.command for op in ops}
    missing = required_commands - existing
    if missing:
        raise AssertionError(f"Missing commands: {missing}. Existing: {existing}")


def report_decoded_actions(
        test_name: str,
        operations,
        deterministic_commands: list[dict],
        capsys,
) -> None:
    with capsys.disabled():
        print(f"\n{'=' * 60}")
        print(f"  Test: {test_name}")
        print(f"  Total operations: {len(operations)}")
        print(f"{'=' * 60}")
        for idx, op in enumerate(operations):
            op_dict = {
                "target_module_id": op.target_module_id,
                "command": op.command,
                "params": op.params.to_dict(),
            }
            matched = any(
                d["target_module_id"] == op.target_module_id and d["command"] == op.command
                for d in deterministic_commands
            )
            origin = "Rule" if matched else "LLM"
            print(f"\nOp {idx + 1} [{origin}]")
            print(f"  module : {op.target_module_id}")
            print(f"  command: {op.command}")
            print(f"  params : {json.dumps(op_dict['params'], indent=4)}")
        print(f"\n{'=' * 60}\n")


@pytest.mark.vcr
class TestActions:

    async def test_init_retrieve_memory(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
            capsys,
    ):
        rule = rule_by_id("rule-init")
        action = rule["action"]
        current_states, schemas = runtime_snapshot_factory(intention_status="start")
        action_intent = make_action(
            "rule-init",
            action["commands"],
            action.get("semantics"),
        )

        req = DecodeActionRequest(
            action_intent=action_intent,
            current_states=current_states,
            schemas=schemas,
        )
        resp = await real_neuro_core.DecodeAction(req)

        report_decoded_actions(
            "rule-init",
            resp.operations,
            deterministic_commands=[
                {"target_module_id": "declarative_memory", "command": "retrieve_chunk"},
                {"target_module_id": "intention", "command": "modify_slot"},
            ],
            capsys=capsys,
        )

        assert len(resp.operations) == 2

        retrieve_op = next(
            (op for op in resp.operations if op.command == "retrieve_chunk"), None
        )
        modify_op = next(
            (op for op in resp.operations if op.command == "modify_slot"), None
        )
        assert retrieve_op is not None, "Missing retrieve_chunk operation"
        assert modify_op is not None, "Missing modify_slot operation"

        assert_operation_match(retrieve_op, {
            "target_module_id": "declarative_memory",
            "command": "retrieve_chunk",
        })
        cue = retrieve_op.params.to_dict().get("cue")
        assert isinstance(cue, dict), f"cue must be dict, got {type(cue)}"
        assert all(isinstance(v, str) for v in cue.values()), \
            f"cue values must be strings, got {cue}"
        combined = " ".join(cue.values()).lower()
        assert "auth" in combined or "module" in combined, \
            f"cue values should mention auth/module, got {cue}"

        assert_operation_match(modify_op, {
            "target_module_id": "intention",
            "command": "modify_slot",
            "params": {"slot": "status", "slot_value": "memory_queried"}
        }, ignore_extra_keys=False)

    async def test_memory_hit_skip_open_file(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
            capsys,
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

        report_decoded_actions(
            "rule-memory-hit (skip open_file)",
            resp.operations,
            deterministic_commands=[
                {"target_module_id": "file_explorer", "command": "set_attention_tags"},
                {"target_module_id": "intention", "command": "modify_slot"},
            ],
            capsys=capsys,
        )

        assert len(resp.operations) == 2
        assert "open_file" not in {op.command for op in resp.operations}
        assert_operations_contain_commands(
            resp.operations,
            {"set_attention_tags", "modify_slot"}
        )

        tags_op = next(op for op in resp.operations if op.command == "set_attention_tags")
        tags = tags_op.params.to_dict().get("tags")
        assert isinstance(tags, list) and all(isinstance(t, str) for t in tags), \
            f"tags should be list of strings, got {tags}"

    async def test_memory_hit_include_open_file(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
            capsys,
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

        report_decoded_actions(
            "rule-memory-hit (include open_file)",
            resp.operations,
            deterministic_commands=[
                {"target_module_id": "file_explorer", "command": "set_attention_tags"},
                {"target_module_id": "code_viewport", "command": "open_file"},
                {"target_module_id": "intention", "command": "modify_slot"},
            ],
            capsys=capsys,
        )

        assert len(resp.operations) == 3
        assert_operations_contain_commands(
            resp.operations,
            {"set_attention_tags", "open_file", "modify_slot"}
        )

        open_file_op = next(op for op in resp.operations if op.command == "open_file")
        assert_operation_match(open_file_op, {
            "target_module_id": "code_viewport",
            "command": "open_file",
            "params": {"file_path": expected_file_path}
        }, ignore_extra_keys=False)

        tags_op = next(op for op in resp.operations if op.command == "set_attention_tags")
        tags = tags_op.params.to_dict().get("tags")
        assert isinstance(tags, list) and all(isinstance(t, str) for t in tags), \
            f"tags should be list of strings, got {tags}"

    async def test_memory_miss_push_subgoal(
            self,
            rule_by_id: Callable[[str], dict],
            real_neuro_core: NeuroCore,
            runtime_snapshot_factory,
            capsys,
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

        report_decoded_actions(
            "rule-memory-miss",
            resp.operations,
            deterministic_commands=[
                {"target_module_id": "intention", "command": "push_subgoal"},
            ],
            capsys=capsys,
        )

        assert len(resp.operations) == 1
        assert_operation_match(resp.operations[0], {
            "target_module_id": "intention",
            "command": "push_subgoal",
            "params": {
                "id": "ExploreFileSystem",
                "slots": {
                    "parent_goal_id": "goal-1",
                    "query": "explore auth",
                    "status": "exploring"
                }
            }
        }, ignore_extra_keys=False)
