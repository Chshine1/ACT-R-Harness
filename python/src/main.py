from .agent import ACTRAgent
from .declarative import DeclarativeMemory
from .models import Rule, Op
from .neuro_core_mock import MockNeuroCore
from .perception import FrostPunkPerception
from .procedural import ProceduralSystem
from .world.frostpunk_sim import MiniFrostPunk

config = {
    "procedural": {"temperature": 0.5, "learning_rate": 0.1},
    "declarative": {"decay": 0.5, "noise_sd": 0.25},
}

procedural = ProceduralSystem(**config["procedural"])
memory = DeclarativeMemory(**config["declarative"])
neuro = MockNeuroCore()
world = MiniFrostPunk()
perception = FrostPunkPerception(world)

rule1 = Rule(
    id="crisis_response",
    condition_desc="facing resource crisis",
    action_desc="identify most urgent problem and solve it",
    utility=0.0
)
rule1.condition_fn = lambda buff: neuro.condition_holds(rule1.condition_desc, buff)
rule1.action_fn = lambda buff, n: n.translate_action(rule1.action_desc, buff)
procedural.add_rule(rule1)

rule_default = Rule(id="maintain", condition_desc="no crisis", action_desc="proceed with current plan", utility=0.0)
rule_default.condition_fn = lambda buff: True
rule_default.action_fn = lambda buff, n: [Op("manual", "noop")]
procedural.add_rule(rule_default)

agent = ACTRAgent(perception=perception, memory=memory, procedural=procedural, neuro=neuro)

num_episodes = 100
for ep in range(num_episodes):
    state = world.reset()
    agent.reset(initial_goal={"focus": "survive"})
    total_reward = 0
    done = False

    while not done:
        result = agent.step(reward=total_reward)
        if result:
            state = result["state"]
            reward = result["reward"]
            done = result["done"]
            total_reward += reward

    print(
        f"Episode {ep}: total reward = {total_reward:.2f}, rule utilities: {[(r.id, round(r.utility, 3)) for r in procedural.rules.values()]}")
