import logging

from actr_harness.generated.grpc.actr import BufferState
from actr_harness.generated.grpc.actr import ProceduralCondition
from actr_harness.generated.grpc.actr.services import EvaluateConditionsResponse

from actr_harness.services.llm_client import LLMClient

from ..buffers_view import BuffersView
from .fuzzy_condition_evaluator import FuzzyConditionEvaluator
from .symbolic_matcher import SymbolicMatcher

logger = logging.getLogger(__name__)


class ConditionEvaluator:
    def __init__(self, llm_client: LLMClient):
        self._symbolic = SymbolicMatcher()
        self._fuzzy = FuzzyConditionEvaluator(llm_client)

    async def evaluate(
            self,
            conditions: list[ProceduralCondition],
            buffer_states: list[BufferState]
    ) -> EvaluateConditionsResponse:
        view = BuffersView(buffer_states)
        satisfied_ids: list[str] = []
        fuzzy_candidates: list[ProceduralCondition] = []
        symbolic_hits = 0

        for cond in conditions:
            if self._symbolic.evaluate(cond.condition.to_dict(), view):
                satisfied_ids.append(cond.rule_id)
                symbolic_hits += 1
            elif cond.semantics:
                fuzzy_candidates.append(cond)

        if fuzzy_candidates:
            fuzzy_ids = await self._fuzzy.evaluate(fuzzy_candidates, view)
            satisfied_ids.extend(fuzzy_ids)
        else:
            fuzzy_ids = []

        logger.debug(
            "ConditionEvaluator result: symbolic_hits=%d fuzzy_candidates=%d fuzzy_hits=%d total_satisfied=%d",
            symbolic_hits,
            len(fuzzy_candidates),
            len(fuzzy_ids),
            len(satisfied_ids),
        )

        return EvaluateConditionsResponse(satisfied_rule_ids=satisfied_ids)
