import pytest
from src.procedural import ProceduralSystem
from src.models import Rule, BufferSet


def dummy_condition(_):
    return True


def dummy_action(_, __):
    return []


@pytest.fixture
def procedural():
    ps = ProceduralSystem(temperature=0.1, learning_rate=0.5)
    r1 = Rule("r1", "always", "do_a", utility=0.0)
    r1.condition_fn = dummy_condition
    r1.action_fn = dummy_action
    r2 = Rule("r2", "always", "do_b", utility=0.0)
    r2.condition_fn = dummy_condition
    r2.action_fn = dummy_action
    ps.add_rule(r1)
    ps.add_rule(r2)
    return ps


def test_utility_learning_increases_preferred_rule(procedural):
    buffers = BufferSet()
    procedural.learn_utility("r1", reward=10.0)
    procedural.learn_utility("r2", reward=-5.0)
    counts = {"r1": 0, "r2": 0}
    for _ in range(1000):
        chosen = procedural.select_rule(buffers)
        counts[chosen.id] += 1
    assert counts["r1"] > counts["r2"] * 2
