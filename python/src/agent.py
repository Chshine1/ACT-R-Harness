from typing import Optional

from .interfaces import AbstractPerceptionMotor, AbstractDeclarativeMemory, \
    AbstractProceduralSystem, AbstractNeuroCore
from .models import BufferSet


class ACTRAgent:
    def __init__(self, perception: AbstractPerceptionMotor,
                 memory: AbstractDeclarativeMemory,
                 procedural: AbstractProceduralSystem,
                 neuro: AbstractNeuroCore):
        self.perception = perception
        self.memory = memory
        self.procedural = procedural
        self.neuro = neuro
        self.buffers = BufferSet()
        self.last_rule_id: Optional[str] = None

    def reset(self, initial_goal: dict):
        self.buffers = BufferSet(goal=initial_goal)

    def step(self, reward: Optional[float] = None):
        if reward is not None and self.last_rule_id is not None:
            self.procedural.learn_utility(self.last_rule_id, reward)

        rule = self.procedural.select_rule(self.buffers)
        self.last_rule_id = rule.id

        context = self._buffer_context()
        ops = self.neuro.translate_action(rule.action_desc, context)

        env_action = None
        for op in ops:
            if op.target == "visual":
                info = self.perception.query_visual(op.params["what"])
                self.buffers.visual = info
            elif op.target == "goal":
                self.buffers.goal = {**(self.buffers.goal if self.buffers.goal is not None else {}), **op.params}
            elif op.target == "retrieval":
                cue = op.params.get("cue", {})
                chunk = self.memory.retrieve(cue)
                self.buffers.retrieval = chunk
            elif op.target == "manual":
                env_action = self.perception.execute_manual(op.command, op.params)
                self.buffers.manual = op.command

        return env_action

    def _buffer_context(self) -> dict:
        return {
            "goal": self.buffers.goal,
            "visual": self.buffers.visual,
            "retrieval": self.buffers.retrieval and self.buffers.retrieval.slots,
            "manual": self.buffers.manual
        }
