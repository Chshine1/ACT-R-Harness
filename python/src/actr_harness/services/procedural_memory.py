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
from actr_harness.observability import log_event, observe_boundary

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
        log_event(
            logger,
            logging.INFO,
            "service.initialized",
            "ProceduralMemory initialized.",
            rule_count=len(self.rules),
            temperature=self.temperature,
            learning_rate=self.lr,
            random_seed=random_seed,
        )

    @observe_boundary("procedural_memory.get_all_conditions")
    async def get_all_conditions(
            self,
            betterproto_lib_google_protobuf_empty,
    ) -> GetAllConditionsResponse:
        _ = betterproto_lib_google_protobuf_empty
        log_event(
            logger,
            logging.DEBUG,
            "procedural_memory.conditions_served",
            "Serving procedural conditions.",
            rule_count=len(self.rules),
            rule_ids=list(self.rules),
        )

        return GetAllConditionsResponse(conditions=[r.condition for r in self.rules.values()])

    @observe_boundary("procedural_memory.select_rule")
    async def select_rule(self, select_rule_request: SelectRuleRequest) -> NeuroAction:
        applicable = [
            rule
            for rule in self.rules.values()
            if rule.id in select_rule_request.satisfied_rule_ids
        ]
        if not applicable:
            log_event(
                logger,
                logging.ERROR,
                "rule_selection.failed",
                "No applicable rule found for satisfied rule IDs.",
                satisfied_rule_ids=list(select_rule_request.satisfied_rule_ids),
            )
            raise ValueError(
                "No applicable rule found for satisfied_rule_ids="
                f"{list(select_rule_request.satisfied_rule_ids)}."
            )

        if self.temperature <= 0:
            selected_rule = max(applicable, key=lambda rule: (rule.utility, rule.id))
            log_event(
                logger,
                logging.INFO,
                "rule_selection.completed",
                "Selected rule deterministically.",
                selection_mode="deterministic",
                selected_rule_id=selected_rule.id,
                candidate_count=len(applicable),
                candidate_utilities={rule.id: rule.utility for rule in applicable},
            )
            return selected_rule.action

        utilities = [r.utility for r in applicable]
        max_u = max(utilities)
        exp_utils = [math.exp((u - max_u) / self.temperature) for u in utilities]
        sum_exp = sum(exp_utils)

        probs = [e / sum_exp for e in exp_utils]
        selected_rule = self._rng.choices(applicable, weights=probs, k=1)[0]

        log_event(
            logger,
            logging.INFO,
            "rule_selection.completed",
            "Selected rule stochastically.",
            selection_mode="stochastic",
            selected_rule_id=selected_rule.id,
            candidate_count=len(applicable),
            candidate_utilities={rule.id: rule.utility for rule in applicable},
            candidate_probabilities={rule.id: prob for rule, prob in zip(applicable, probs, strict=False)},
        )

        return selected_rule.action

    @observe_boundary("procedural_memory.learn_utility")
    async def learn_utility(self, learn_utility_request: LearnUtilityRequest) -> Empty:
        rule_id = learn_utility_request.rule_id

        if rule_id in self.rules:
            rule = self.rules[rule_id]
            old_utility = rule.utility
            rule.utility += self.lr * (learn_utility_request.reward - rule.utility)
            log_event(
                logger,
                logging.INFO,
                "rule_utility.updated",
                "Updated procedural rule utility.",
                rule_id=rule_id,
                reward=learn_utility_request.reward,
                old_utility=old_utility,
                new_utility=rule.utility,
            )
        else:
            log_event(
                logger,
                logging.WARNING,
                "rule_utility.skipped",
                "Skipped utility update for missing rule.",
                rule_id=rule_id,
            )

        return Empty()
