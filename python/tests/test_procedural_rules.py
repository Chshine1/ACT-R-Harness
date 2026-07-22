from pathlib import Path

import pytest

from actr_harness.services.procedural_rules import (
    DEFAULT_RULESET_PATH,
    ENV_RULESET_PATH,
    INITIAL_RULE_UTILITY,
    load_rules,
    resolve_ruleset_path,
)


def test_load_rules_maps_three_active_rules_from_default_ruleset():
    rules = load_rules(DEFAULT_RULESET_PATH)

    assert set(rules) == {"rule-init", "rule-memory-hit", "rule-memory-miss"}
    assert all(rule.utility == INITIAL_RULE_UTILITY for rule in rules.values())


def test_rule_init_maps_condition_and_commands():
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-init"]

    assert rule.condition.rule_id == "rule-init"
    assert rule.condition.condition.to_dict() == {
        "type": "equals",
        "slot": "intention.current_goal.slots.status",
        "value": "start",
    }
    assert rule.condition.semantics.to_dict() == {}
    assert rule.action.semantics["command:retrieveMemory"].to_dict() == {
        "sources": [
            "intention.current_goal.slots.query",
            "declarative_memory.available_slot_keys",
        ],
        "params": {
            "cue": (
                "Construct a cue object. Choose keys from the available slot keys "
                "of declarative memory (if present in the buffer state) that best "
                "match the current goal query.\n"
            )
        },
    }
    assert set(rule.action.commands) == {"retrieveMemory", "updateStatus"}
    assert (
        rule.action.commands["retrieveMemory"].target_module_id
        == "declarative_memory"
    )
    assert rule.action.commands["retrieveMemory"].command == "retrieve_chunk"
    assert rule.action.commands["retrieveMemory"].params.to_dict() == {}
    assert rule.action.commands["updateStatus"].target_module_id == "intention"
    assert rule.action.commands["updateStatus"].command == "modify_slot"
    assert rule.action.commands["updateStatus"].params.to_dict() == {
        "slot": "status",
        "slot_value": "memory_queried",
    }


def test_rule_memory_hit_preserves_commands_and_semantics():
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-memory-hit"]

    assert rule.condition.condition.to_dict() == {
        "type": "and",
        "conditions": [
            {
                "type": "equals",
                "slot": "intention.current_goal.slots.status",
                "value": "memory_queried",
            },
            {
                "type": "exist",
                "slot": "declarative_memory.retrieved_chunk",
            },
        ],
    }
    assert set(rule.action.commands) == {"setTags", "openFileIfPath", "updateStatus"}
    assert rule.action.commands["setTags"].target_module_id == "file_explorer"
    assert rule.action.commands["setTags"].command == "set_attention_tags"
    assert rule.action.commands["openFileIfPath"].command == "open_file"
    assert rule.action.commands["openFileIfPath"].target_module_id == "code_viewport"
    assert rule.action.commands["openFileIfPath"].params.to_dict() == {}
    assert rule.action.commands["updateStatus"].params.to_dict() == {
        "slot": "status",
        "slot_value": "file_opened",
    }
    assert rule.action.semantics["command:setTags"].to_dict() == {
        "sources": ["declarative_memory.retrieved_chunk.slots.keywords"],
        "params": {
            "tags": (
                "Split and normalize the retrieved keywords into lowercase tags. "
                "If the source `keywords` is missing, set an empty list"
            )
        },
    }
    assert rule.action.semantics["command:openFileIfPath"].to_dict() == {
        "sources": ["declarative_memory.retrieved_chunk.slots.file_path"],
        "params": {
            "file_path": "Use the retrieved file path when one is available."
        },
    }
    assert rule.action.semantics["meta:skip_if_missing"].to_dict() == {
        "command": "openFileIfPath",
        "instruction": (
            "ONLY apply this skip rule to the command `openFileIfPath`. "
            "If the source `file_path` is missing or None, you MUST NOT generate "
            "any operation for this command"
        ),
    }


def test_rule_memory_miss_maps_push_subgoal_params():
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-memory-miss"]

    assert rule.condition.condition.to_dict() == {
        "type": "and",
        "conditions": [
            {
                "type": "equals",
                "slot": "intention.current_goal.slots.status",
                "value": "memory_queried",
            },
            {
                "type": "not",
                "condition": {
                    "type": "exist",
                    "slot": "declarative_memory.retrieved_chunk",
                },
            },
        ],
    }
    assert rule.action.commands["pushSubgoal"].target_module_id == "intention"
    assert rule.action.commands["pushSubgoal"].command == "push_subgoal"
    assert rule.action.commands["pushSubgoal"].params.to_dict() == {
        "id": "ExploreFileSystem",
        "slots": {
            "parent_goal_id": "${intention.current_goal.slots.id}",
            "query": "${intention.current_goal.slots.query}",
            "status": "exploring",
        },
    }


def test_resolve_ruleset_path_prefers_environment_override(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
):
    override_path = tmp_path / "override.yml"
    monkeypatch.setenv(ENV_RULESET_PATH, str(override_path))

    assert resolve_ruleset_path() == override_path.resolve()


def test_load_rules_uses_environment_override_without_explicit_path(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
):
    override_path = tmp_path / "override.yml"
    override_path.write_text(
        "rules:\n"
        "  - id: override-rule\n"
        "    condition:\n"
        "      symbolic: {type: equals}\n"
        "    action:\n"
        "      commands: {}\n",
        encoding="utf-8",
    )
    monkeypatch.setenv(ENV_RULESET_PATH, str(override_path))

    assert set(load_rules()) == {"override-rule"}


def test_load_rules_raises_file_not_found_for_missing_ruleset(tmp_path: Path):
    missing_path = tmp_path / "missing.yml"

    with pytest.raises(FileNotFoundError, match="Ruleset file does not exist"):
        load_rules(missing_path)


def test_load_rules_raises_value_error_when_rules_list_is_missing(tmp_path: Path):
    ruleset_path = tmp_path / "invalid.yml"
    ruleset_path.write_text("name: invalid\n", encoding="utf-8")

    with pytest.raises(ValueError, match="top-level 'rules' must be a list"):
        load_rules(ruleset_path)


def test_load_rules_raises_value_error_when_action_commands_are_missing(
    tmp_path: Path,
):
    ruleset_path = tmp_path / "invalid.yml"
    ruleset_path.write_text(
        "rules:\n"
        "  - id: invalid-rule\n"
        "    condition:\n"
        "      symbolic: {type: equals}\n"
        "    action: {}\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match=r"missing 'action\.commands'"):
        load_rules(ruleset_path)


def test_load_rules_raises_value_error_for_invalid_required_fields(tmp_path: Path):
    invalid_cases = [
        ("- not-a-mapping\n", "top-level payload must be a mapping"),
        (
            "rules:\n"
            "  - condition:\n"
            "      symbolic: {type: equals}\n"
            "    action:\n"
            "      commands: {}\n",
            "Rule is missing 'id'",
        ),
        (
            "rules:\n"
            "  - id: ''\n"
            "    condition:\n"
            "      symbolic: {type: equals}\n"
            "    action:\n"
            "      commands: {}\n",
            "Rule is missing 'id'",
        ),
        (
            "rules:\n"
            "  - id: '   '\n"
            "    condition:\n"
            "      symbolic: {type: equals}\n"
            "    action:\n"
            "      commands: {}\n",
            "Rule is missing 'id'",
        ),
        (
            "rules:\n"
            "  - id: invalid-rule\n"
            "    condition: {}\n"
            "    action:\n"
            "      commands: {}\n",
            "Rule is missing 'condition.symbolic'",
        ),
    ]

    for index, (payload, message) in enumerate(invalid_cases):
        ruleset_path = tmp_path / f"invalid-{index}.yml"
        ruleset_path.write_text(payload, encoding="utf-8")

        with pytest.raises(ValueError, match=message):
            load_rules(ruleset_path)


def test_load_rules_rejects_malformed_yaml_with_ruleset_path(tmp_path: Path):
    ruleset_path = tmp_path / "malformed.yml"
    ruleset_path.write_text("rules: [\n", encoding="utf-8")

    with pytest.raises(ValueError, match="Failed to parse ruleset") as error:
        load_rules(ruleset_path)

    assert str(ruleset_path) in str(error.value)


def test_load_rules_rejects_duplicate_rule_ids(tmp_path: Path):
    ruleset_path = tmp_path / "duplicate.yml"
    ruleset_path.write_text(
        "rules:\n"
        "  - id: duplicate-rule\n"
        "    condition:\n"
        "      symbolic: {}\n"
        "    action:\n"
        "      commands: {}\n"
        "  - id: duplicate-rule\n"
        "    condition:\n"
        "      symbolic: {}\n"
        "    action:\n"
        "      commands: {}\n",
        encoding="utf-8",
    )

    with pytest.raises(ValueError, match="Duplicate rule id: duplicate-rule"):
        load_rules(ruleset_path)


def test_load_rules_rejects_malformed_nested_command_payloads(tmp_path: Path):
    invalid_cases = [
        (
            "commands:\n"
            "        retrieve: invalid\n",
            "Command 'retrieve' must be a mapping",
        ),
        (
            "commands:\n"
            "        retrieve:\n"
            "          command: retrieve_chunk\n",
            "Command 'retrieve' is missing 'target_module_id'",
        ),
        (
            "commands:\n"
            "        retrieve:\n"
            "          target_module_id: memory\n",
            "Command 'retrieve' is missing 'command'",
        ),
        (
            "commands:\n"
            "        retrieve:\n"
            "          target_module_id: 1\n"
            "          command: retrieve_chunk\n",
            "Command 'retrieve' has invalid 'target_module_id'",
        ),
        (
            "commands:\n"
            "        retrieve:\n"
            "          target_module_id: memory\n"
            "          command: 1\n",
            "Command 'retrieve' has invalid 'command'",
        ),
        (
            "commands:\n"
            "        retrieve:\n"
            "          target_module_id: memory\n"
            "          command: retrieve_chunk\n"
            "          params: []\n",
            "Command 'retrieve' has invalid 'params'",
        ),
    ]

    for index, (commands, message) in enumerate(invalid_cases):
        ruleset_path = tmp_path / f"invalid-command-{index}.yml"
        ruleset_path.write_text(
            "rules:\n"
            "  - id: command-rule\n"
            "    condition:\n"
            "      symbolic: {}\n"
            "    action:\n"
            f"      {commands}",
            encoding="utf-8",
        )

        with pytest.raises(ValueError, match=message):
            load_rules(ruleset_path)


def test_load_rules_rejects_malformed_semantic_payloads(tmp_path: Path):
    invalid_cases = [
        (
            "      semantics: []\n"
            "    action:\n"
            "      commands: {}\n",
            "condition.semantics must be a mapping",
        ),
        (
            "    action:\n"
            "      commands: {}\n"
            "      semantics: []\n",
            "action.semantics must be a mapping",
        ),
        (
            "    action:\n"
            "      commands: {}\n"
            "      semantics:\n"
            "        meta: []\n",
            "Semantic entry 'meta' must be a mapping",
        ),
    ]

    for index, (semantic_payload, message) in enumerate(invalid_cases):
        ruleset_path = tmp_path / f"invalid-semantics-{index}.yml"
        ruleset_path.write_text(
            "rules:\n"
            "  - id: semantic-rule\n"
            "    condition:\n"
            "      symbolic: {}\n"
            f"{semantic_payload}",
            encoding="utf-8",
        )

        with pytest.raises(ValueError, match=message):
            load_rules(ruleset_path)


def test_load_rules_explicit_path_takes_precedence_over_environment(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
):
    explicit_path = tmp_path / "explicit.yml"
    override_path = tmp_path / "override.yml"
    explicit_path.write_text(
        "rules:\n"
        "  - id: explicit-rule\n"
        "    condition:\n"
        "      symbolic: {}\n"
        "    action:\n"
        "      commands: {}\n",
        encoding="utf-8",
    )
    override_path.write_text("rules: []\n", encoding="utf-8")
    monkeypatch.setenv(ENV_RULESET_PATH, str(override_path))

    assert set(load_rules(explicit_path)) == {"explicit-rule"}


def test_resolve_ruleset_path_falls_back_to_default_when_environment_unset(
    monkeypatch: pytest.MonkeyPatch,
):
    monkeypatch.delenv(ENV_RULESET_PATH, raising=False)

    assert resolve_ruleset_path() == DEFAULT_RULESET_PATH
