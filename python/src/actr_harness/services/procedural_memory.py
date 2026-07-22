import math
import random

from betterproto.lib.google.protobuf import Empty

from actr_harness.generated.grpc.actr import NeuroAction
from actr_harness.generated.grpc.actr.services import (
    GetAllConditionsResponse,
    LearnUtilityRequest,
    ProceduralMemoryBase,
    SelectRuleRequest,
)
from actr_harness.services.procedural_rules import Rule, load_rules


class ProceduralMemory(ProceduralMemoryBase):
    def __init__(
        self,
        *,
        rules: dict[str, Rule] | None = None,
        temperature: float = 0.5,
        learning_rate: float = 0.1,
    ) -> None:
        self.rules = dict(rules) if rules is not None else load_rules()
        self.temperature = temperature
        self.lr = learning_rate

    async def get_all_conditions(
        self,
        betterproto_lib_google_protobuf_empty: Empty,
    ) -> GetAllConditionsResponse:
        _ = betterproto_lib_google_protobuf_empty

        return GetAllConditionsResponse(
            conditions=[rule.condition for rule in self.rules.values()]
        )

    async def select_rule(self, select_rule_request: SelectRuleRequest) -> NeuroAction:
        _ = select_rule_request

        applicable = [
            rule
            for rule in self.rules.values()
            if rule.id in select_rule_request.satisfied_rule_ids
        ]
        if not applicable:
            raise ValueError("No applicable rule found.")

        utilities = [r.utility for r in applicable]
        max_u = max(utilities)
        exp_utils = [math.exp((u - max_u) / self.temperature) for u in utilities]
        sum_exp = sum(exp_utils)

        probs = [e / sum_exp for e in exp_utils]
        rule = random.choices(applicable, weights=probs, k=1)[0]

        return rule.action

    async def learn_utility(self, learn_utility_request: LearnUtilityRequest) -> Empty:
        rule_id = learn_utility_request.rule_id

        if rule_id in self.rules:
            rule = self.rules[rule_id]
            rule.utility += self.lr * (learn_utility_request.reward - rule.utility)

        return Empty()
