import logging

from actr_harness.services.llm_client import LLMClient
from actr_harness.observability import log_event, observe_boundary
from ..buffers_view import BuffersView
from actr_harness.generated.grpc.actr import (
    ProceduralCondition,
)

logger = logging.getLogger(__name__)


class FuzzyConditionEvaluator:
    def __init__(self, llm: LLMClient):
        self._llm = llm

    @observe_boundary("fuzzy_condition_evaluator.evaluate")
    async def evaluate(self, conditions: list[ProceduralCondition], view: BuffersView) -> list[str]:
        prompt = {
            "buffers": view.to_dict(),
            "conditions": [
                {
                    "rule_id": c.rule_id,
                    "symbolic": c.condition.to_dict(),
                    "semantic_hint": c.semantics.to_dict(),
                }
                for c in conditions
            ]
        }
        system = (
            "You are given buffers (world state) and conditions with optional semantic hints. "
            "Determine which conditions are satisfied. "
            "Return ONLY a JSON array of the satisfied rule_id strings. "
            "No extra text, no explanation."
        )
        result = await self._llm.chat_json(prompt, system)
        if not isinstance(result, list):
            log_event(
                logger,
                logging.WARNING,
                "fuzzy_condition_evaluation.invalid_payload",
                "Fuzzy condition evaluator returned a non-list payload.",
                payload_type=type(result).__name__,
            )
            return []

        log_event(
            logger,
            logging.DEBUG,
            "fuzzy_condition_evaluation.completed",
            "Resolved fuzzy conditions.",
            candidate_rule_ids=[condition.rule_id for condition in conditions],
            matched_rule_ids=result,
        )
        return result
