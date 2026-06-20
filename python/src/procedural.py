import math
import random

from .interfaces import AbstractProceduralSystem
from .models import BufferSet, Rule


class ProceduralSystem(AbstractProceduralSystem):
    def __init__(self, temperature: float = 0.5, learning_rate: float = 0.1):
        self.rules: dict[str, Rule] = {}
        self.temperature = temperature
        self.lr = learning_rate

    def add_rule(self, rule: Rule):
        self.rules[rule.id] = rule

    def select_rule(self, buffers: BufferSet) -> Rule:
        applicable = [r for r in self.rules.values()
                      if r.condition_fn is not None and r.condition_fn(buffers)]
        if not applicable:
            raise ValueError("No applicable rule found – need fallback rule")
        utilities = [r.utility for r in applicable]
        max_u = max(utilities)
        exp_utils = [math.exp((u - max_u) / self.temperature) for u in utilities]
        sum_exp = sum(exp_utils)
        probs = [e / sum_exp for e in exp_utils]
        chosen = random.choices(applicable, weights=probs, k=1)[0]
        return chosen

    def learn_utility(self, rule_id: str, reward: float):
        if rule_id in self.rules:
            rule = self.rules[rule_id]
            rule.utility += self.lr * (reward - rule.utility)
