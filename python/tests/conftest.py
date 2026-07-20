from collections.abc import Callable

from pathlib import Path
from typing import Any

from betterproto.lib.google.protobuf import Struct
import pytest
from vcr.config import RecordMode  # type: ignore
import yaml

from actr_harness.generated.grpc.actr import BufferState, ModuleSchema
from actr_harness.services.neuro_core.neuro_core import NeuroCore

RULES_PATH = Path(__file__).parent.parent.parent / "shared/ruleset/lab.yml"


def load_rules() -> list[dict]:
    with open(RULES_PATH) as f:
        data = yaml.safe_load(f)
    return data["rules"]


@pytest.fixture(scope="session")
def rules() -> list[dict]:
    return load_rules()


@pytest.fixture
def rule_by_id(rules: list[dict]) -> Callable[[str], dict]:
    def _get(rule_id):
        for r in rules:
            if r["id"] == rule_id:
                return r
        raise ValueError(f"Rule {rule_id} not found")

    return _get


@pytest.fixture(scope="module")
def real_neuro_core() -> NeuroCore:
    return NeuroCore()


@pytest.fixture(scope="session")
def vcr_config() -> dict[str, Any]:
    return {
        "cassette_library_dir": str(Path(__file__).parent / "cassettes"),
        "record_mode": RecordMode.ONCE,
        "match_on": ["method", "scheme", "host", "port", "path", "query"],
        "filter_headers": ["authorization"],
        "ignore_localhost": True,
    }


@pytest.fixture(scope="session")
def runtime_snapshot_factory() -> Callable[..., tuple[list[BufferState], list[ModuleSchema]]]:
    repo_root = Path(__file__).resolve().parents[2]

    def _factory(
        *,
        intention_status: str,
        intention_query: str = "Find authentication module",
        intention_goal_id: str = "goal-1",
        retrieved_chunk: dict[str, object] | None = None,
    ) -> tuple[list[BufferState], list[ModuleSchema]]:
        goal_buffer: dict[str, Any] = {
            "current_goal": {
                "id": intention_goal_id,
                "creation_time": 1.0,
                "slots": {
                    "id": intention_goal_id,
                    "status": intention_status,
                    "query": intention_query,
                },
            },
            "stack_depth": 1,
            "goal_just_changed": True,
            "last_goal_id": None,
            "max_capacity": 7,
        }
        memory_buffer: dict[str, Any] = {
            "retrieved_chunk": retrieved_chunk,
        }
        file_buffer: dict[str, Any] = {
            "current_path": str(repo_root),
            "entries": None,
            "attention_tags": None,
            "can_go_back": False,
            "can_go_forward": False,
            "parent_path": str(repo_root.parent),
        }
        code_buffer: dict[str, Any] = {
            "file_path": None,
            "file_name": None,
            "total_lines": 0,
            "viewport_start_line": None,
            "viewport_end_line": None,
            "viewport_size": 20,
            "visible_lines": None,
            "search_query": None,
            "search_match_line": None,
            "search_result": "inactive",
            "status": "no_file_open",
            "selection_start": None,
            "selection_end": None,
        }
        buffer_states = [
            BufferState(module_id="intention", data=Struct.from_dict(goal_buffer)),
            BufferState(module_id="declarative_memory", data=Struct.from_dict(memory_buffer)),
            BufferState(module_id="file_explorer", data=Struct.from_dict(file_buffer)),
            BufferState(module_id="code_viewport", data=Struct.from_dict(code_buffer)),
        ]

        goal_schema = {
            "set_goal": '{"id": "string", "slots": { "type": "object" }}',
            "push_subgoal": '{"id": "string", "slots": { "type": "object" }}',
            "modify_slot": '{"slot": "string", "slot_value": "object"}',
            "pop_goal": "{}",
            "clear_goals": "{}",
        }
        memory_schema = {
            "retrieve_chunk": (
                '{\n'
                '    "type": "object",\n'
                '    "additionalProperties": { "type": "string" }\n'
                '}'
            ),
            "add_chunk": (
                '{"id": { "type": "string" }, '
                '"slots": { "type": "object", '
                '"additionalProperties": { "type": "string" } }}'
            ),
        }
        file_schema = {
            "goto_directory": '{"path": { "type": "string" }}',
            "enter_subdirectory": '{"name": { "type": "string" }}',
            "set_attention_tags": (
                '{"tags": { "type": "array", '
                '"items": { "type": "string" } }}'
            ),
            "go_to_parent": "{}",
            "navigate_back": "{}",
            "navigate_forward": "{}",
        }
        code_schema = {
            "open_file": '{"file_path": { "type": "string" }}',
            "close_file": "{}",
            "scroll_down": '{"lines": { "type": "number" }}',
            "scroll_up": '{"lines": { "type": "number" }}',
            "go_to_line": '{"line": { "type": "number" }}',
            "find": '{"query": { "type": "string" }}',
            "select_lines": (
                '{"start_line": { "type": "number" }, '
                '"end_line": { "type": "number" }}'
            ),
        }
        schemas = [
            ModuleSchema(module_id="intention", command_schemas=goal_schema),
            ModuleSchema(module_id="declarative_memory", command_schemas=memory_schema),
            ModuleSchema(module_id="file_explorer", command_schemas=file_schema),
            ModuleSchema(module_id="code_viewport", command_schemas=code_schema),
        ]

        return buffer_states, schemas

    return _factory
