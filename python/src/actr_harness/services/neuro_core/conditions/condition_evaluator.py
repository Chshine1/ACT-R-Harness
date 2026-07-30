import logging

from actr_harness.generated.grpc.actr import BufferState
from actr_harness.generated.grpc.actr import ProceduralCondition
from actr_harness.generated.grpc.actr.services import EvaluateConditionsResponse
from actr_harness.observability import log_event, observe_boundary

from actr_harness.services.llm_client import LLMClient

from ..buffers_view import BuffersView
from .fuzzy_condition_evaluator import FuzzyConditionEvaluator
from .symbolic_matcher import SymbolicMatcher

logger = logging.getLogger(__name__)


class ConditionEvaluator:
    def __init__(self, llm_client: LLMClient):
        self._symbolic = SymbolicMatcher()
        self._fuzzy = FuzzyConditionEvaluator(llm_client)

    @observe_boundary("condition_evaluator.evaluate")
    async def evaluate(
            self,
            conditions: list[ProceduralCondition],
            buffer_states: list[BufferState]
    ) -> EvaluateConditionsResponse:
        view = BuffersView(buffer_states)
        satisfied_ids: list[str] = []
        fuzzy_candidates: list[ProceduralCondition] = []
        symbolic_hits = 0
        outcomes: list[dict[str, object]] = []

        for cond in conditions:
            symbolic_match = self._symbolic.evaluate(cond.condition.to_dict(), view)
            semantic_candidate = bool(cond.semantics.to_dict())
            if symbolic_match:
                satisfied_ids.append(cond.rule_id)
                symbolic_hits += 1
            elif semantic_candidate:
                fuzzy_candidates.append(cond)

            outcomes.append(
                {
                    "rule_id": cond.rule_id,
                    "symbolic_match": symbolic_match,
                    "semantic_candidate": semantic_candidate,
                    "matched": symbolic_match,
                }
            )

        if fuzzy_candidates:
            fuzzy_ids = await self._fuzzy.evaluate(fuzzy_candidates, view)
            satisfied_ids.extend(fuzzy_ids)
        else:
            fuzzy_ids = []

        fuzzy_hit_ids = set(fuzzy_ids)
        for outcome in outcomes:
            if outcome["rule_id"] in fuzzy_hit_ids:
                outcome["matched"] = True
                outcome["semantic_match"] = True

        log_event(
            logger,
            logging.INFO,
            "rule_evaluation.completed",
            "Completed condition evaluation.",
            symbolic_hits=symbolic_hits,
            fuzzy_candidate_rule_ids=[cond.rule_id for cond in fuzzy_candidates],
            fuzzy_hit_rule_ids=fuzzy_ids,
            satisfied_rule_ids=satisfied_ids,
            condition_outcomes=outcomes,
            buffer_modules=list(view.to_dict().keys()),
        )

        return EvaluateConditionsResponse(satisfied_rule_ids=satisfied_ids)
