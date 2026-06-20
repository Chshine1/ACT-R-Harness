from decoy import Decoy, matchers
from src.agent import ACTRAgent
from src.interfaces import AbstractPerceptionMotor, AbstractDeclarativeMemory, AbstractProceduralSystem, \
    AbstractNeuroCore
from src.models import BufferSet
from src.models import Op, Rule


def test_agent_step_calls_neuro_and_learns(decoy: Decoy):
    mock_perception = decoy.mock(cls=AbstractPerceptionMotor)
    decoy.when(
        mock_perception.execute_manual(matchers.IsA(str), matchers.Anything())
    ).then_return(
        {"state": {}, "reward": 1.0, "done": False}
    )

    mock_memory = decoy.mock(cls=AbstractDeclarativeMemory)

    mock_procedural = decoy.mock(cls=AbstractProceduralSystem)
    decoy.when(
        mock_procedural.select_rule(matchers.IsA(BufferSet))
    ).then_return(
        Rule("r_test", "c", "a", utility=1.0)
    )

    mock_neuro = decoy.mock(cls=AbstractNeuroCore)
    decoy.when(
        mock_neuro.translate_action(matchers.IsA(str), matchers.Anything())
    ).then_return(
        [
            Op(target="manual", command="produce_food", params={})
        ]
    )

    agent = ACTRAgent(mock_perception, mock_memory, mock_procedural, mock_neuro)
    agent.reset(initial_goal={})

    result = agent.step(reward=None)
    assert result == {"state": {}, "reward": 1.0, "done": False}

    _ = agent.step(reward=1.0)
    decoy.verify(
        mock_procedural.learn_utility("r_test", 1.0),
        times=1
    )
