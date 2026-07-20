from actr_harness.services.llm_client import LLMClient
from ..buffers_view import BuffersView
from actr_harness.generated.grpc.actr import (
    ProceduralCondition,
)


class FuzzyConditionEvaluator:
    def __init__(self, llm: LLMClient):
        self._llm = llm

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
        return await self._llm.chat_json(prompt, system)
