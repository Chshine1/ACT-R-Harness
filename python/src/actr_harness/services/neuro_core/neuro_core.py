from actr_harness.generated.grpc.actr.services import (
    NeuroCoreBase,
    EvaluateConditionsRequest,
    EvaluateConditionsResponse,
    DecodeActionRequest,
    DecodeActionResponse,
)

import os
from ..llm_client import LLMClient
from .action_resolver import ActionResolver
from .conditions import ConditionEvaluator


class NeuroCore(NeuroCoreBase):
    def __init__(self, llm_client: LLMClient | None = None):
        if llm_client is None:
            model = os.getenv("NEURO_LLM_MODEL")
            key = os.getenv("OPENAI_API_KEY")
            base = os.getenv("OPENAI_BASE_URL")
            if model is None or key is None or base is None:
                raise ValueError("NeuroCore requires OpenAI API key")
            llm_client = LLMClient(model, key, base)
        self._llm = llm_client
        self._condition_evaluator = ConditionEvaluator(self._llm)
        self._action_resolver = ActionResolver(self._llm)

    # noinspection PyPep8Naming
    async def EvaluateConditions(self, request: EvaluateConditionsRequest) -> EvaluateConditionsResponse:
        return await self._condition_evaluator.evaluate(request.conditions, request.buffer_states)

    # noinspection PyPep8Naming
    async def DecodeAction(self, request: DecodeActionRequest) -> DecodeActionResponse:
        return await self._action_resolver.decode_action(
            request.action_intent,
            request.current_states,
            request.schemas
        )
