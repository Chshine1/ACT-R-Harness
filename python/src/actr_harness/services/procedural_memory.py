import logging
import math
import random
from dataclasses import dataclass

from betterproto.lib.google.protobuf import Empty

from actr_harness.generated.grpc.actr import NeuroAction, ProceduralCondition
from actr_harness.generated.grpc.actr.services import (
    GetAllConditionsResponse,
    LearnUtilityRequest,
    ProceduralMemoryBase,
    SelectRuleRequest,
)

from .rules_loader import load_ruleset

logger = logging.getLogger(__name__)


@dataclass
class Rule:
    id: str
    condition: ProceduralCondition
    action: NeuroAction
    utility: float


class ProceduralMemory(ProceduralMemoryBase):
    def __init__(
            self,
            temperature: float = 0.5,
            learning_rate: float = 0.1,
            rules_path: str | None = None,
            default_utility: float = 0.0,
            random_seed: int | None = None,
    ):
        loaded_rules = load_ruleset(
            rules_path=rules_path,
            default_utility=default_utility,
        )
        self.rules: dict[str, Rule] = {
            rule_id: Rule(
                id=loaded.id,
                condition=loaded.condition,
                action=loaded.action,
                utility=loaded.utility,
            )
            for rule_id, loaded in loaded_rules.items()
        }
        self.temperature = temperature
        self.lr = learning_rate
        self._rng = random.Random(random_seed)
        logger.info(
            "ProceduralMemory initialized: rules=%d temperature=%s learning_rate=%s random_seed=%s",
            len(self.rules),
            self.temperature,
            self.lr,
            random_seed,
        )

    async def get_all_conditions(
            self,
            betterproto_lib_google_protobuf_empty,
    ) -> GetAllConditionsResponse:
        _ = betterproto_lib_google_protobuf_empty
        logger.info("get_all_conditions request: rules=%d", len(self.rules))

        return GetAllConditionsResponse(conditions=[r.condition for r in self.rules.values()])

    async def select_rule(self, select_rule_request: SelectRuleRequest) -> NeuroAction:
        applicable = [
            rule
            for rule in self.rules.values()
            if rule.id in select_rule_request.satisfied_rule_ids
        ]
        if not applicable:
            logger.error(
                "select_rule failed: no applicable rule found for satisfied_rule_ids=%s",
                list(select_rule_request.satisfied_rule_ids),
            )
            raise ValueError(
                "No applicable rule found for satisfied_rule_ids="
                f"{list(select_rule_request.satisfied_rule_ids)}."
            )

        if self.temperature <= 0:
            selected_rule = max(applicable, key=lambda rule: (rule.utility, rule.id))
            logger.info(
                "select_rule deterministic response: selected=%s candidates=%d",
                selected_rule.id,
                len(applicable),
            )
            return selected_rule.action

        utilities = [r.utility for r in applicable]
        max_u = max(utilities)
        exp_utils = [math.exp((u - max_u) / self.temperature) for u in utilities]
        sum_exp = sum(exp_utils)

        probs = [e / sum_exp for e in exp_utils]
        selected_rule = self._rng.choices(applicable, weights=probs, k=1)[0]

        logger.info(
            "select_rule stochastic response: selected=%s candidates=%d",
            selected_rule.id,
            len(applicable),
        )
        logger.debug(
            "select_rule candidate_utilities=%s candidate_probabilities=%s",
            {rule.id: rule.utility for rule in applicable},
            {rule.id: prob for rule, prob in zip(applicable, probs, strict=False)},
        )

        return selected_rule.action

    async def learn_utility(self, learn_utility_request: LearnUtilityRequest) -> Empty:
        rule_id = learn_utility_request.rule_id

        if rule_id in self.rules:
            rule = self.rules[rule_id]
            old_utility = rule.utility
            rule.utility += self.lr * (learn_utility_request.reward - rule.utility)
            logger.info(
                "learn_utility updated rule=%s reward=%s old_utility=%s new_utility=%s",
                rule_id,
                learn_utility_request.reward,
                old_utility,
                rule.utility,
            )
        else:
            logger.warning("learn_utility skipped missing rule=%s", rule_id)

        return Empty()
