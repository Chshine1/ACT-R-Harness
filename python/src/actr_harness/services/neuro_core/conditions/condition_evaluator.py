from actr_harness.generated.grpc.actr import BufferState
from actr_harness.generated.grpc.actr import ProceduralCondition
from actr_harness.generated.grpc.actr.services import EvaluateConditionsResponse

from ..buffers_view import BuffersView
from .fuzzy_condition_evaluator import FuzzyConditionEvaluator
from .symbolic_matcher import SymbolicMatcher
from actr_harness.services.llm_client import LLMClient


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

        for cond in conditions:
            if self._symbolic.evaluate(cond.condition.to_dict(), view):
                satisfied_ids.append(cond.rule_id)
            elif cond.semantics:
                fuzzy_candidates.append(cond)

        if fuzzy_candidates:
            fuzzy_ids = await self._fuzzy.evaluate(fuzzy_candidates, view)
            satisfied_ids.extend(fuzzy_ids)

        return EvaluateConditionsResponse(satisfied_rule_ids=satisfied_ids)
