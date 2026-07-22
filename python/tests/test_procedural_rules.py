from actr_harness.services.procedural_rules import (
    DEFAULT_RULESET_PATH,
    INITIAL_RULE_UTILITY,
    load_rules,
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
    assert set(rule.action.commands) == {"retrieveMemory", "updateStatus"}
    assert rule.action.commands["retrieveMemory"].target_module_id == "declarative_memory"
    assert rule.action.commands["retrieveMemory"].command == "retrieve_chunk"
    assert rule.action.commands["updateStatus"].target_module_id == "intention"
    assert rule.action.commands["updateStatus"].command == "modify_slot"
    assert rule.action.commands["updateStatus"].params.to_dict() == {
        "slot": "status",
        "slot_value": "memory_queried",
    }


def test_rule_memory_hit_preserves_commands_and_semantics():
    rule = load_rules(DEFAULT_RULESET_PATH)["rule-memory-hit"]

    assert set(rule.action.commands) == {"setTags", "openFileIfPath", "updateStatus"}
    assert rule.action.commands["setTags"].command == "set_attention_tags"
    assert rule.action.commands["openFileIfPath"].command == "open_file"
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

    assert rule.action.commands["pushSubgoal"].params.to_dict() == {
        "id": "ExploreFileSystem",
        "slots": {
            "parent_goal_id": "${intention.current_goal.slots.id}",
            "query": "${intention.current_goal.slots.query}",
            "status": "exploring",
        },
    }
