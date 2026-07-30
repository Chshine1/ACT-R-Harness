import logging
import os

from actr_harness.generated.grpc.actr.services import (
    DecodeActionRequest,
    DecodeActionResponse,
    EvaluateConditionsRequest,
    EvaluateConditionsResponse,
    NeuroCoreBase,
)
from actr_harness.observability import log_event, observe_boundary

from ..llm_client import LLMClient
from .action_resolver import ActionResolver
from .conditions import ConditionEvaluator

logger = logging.getLogger(__name__)


class NeuroCore(NeuroCoreBase):
    def __init__(self, llm_client: LLMClient | None = None):
        if llm_client is None:
            model = os.getenv("NEURO_LLM_MODEL")
            key = os.getenv("OPENAI_API_KEY")
            base = os.getenv("OPENAI_BASE_URL")
            if model is None or key is None or base is None:
                raise ValueError(
                    "NeuroCore requires NEURO_LLM_MODEL, OPENAI_API_KEY, and "
                    "OPENAI_BASE_URL."
                )
            llm_client = LLMClient(model, key, base)
            model_name = model
        else:
            model_name = "injected"

        self._llm = llm_client
        self._condition_evaluator = ConditionEvaluator(self._llm)
        self._action_resolver = ActionResolver(self._llm)
        log_event(
            logger,
            logging.INFO,
            "service.initialized",
            "NeuroCore initialized.",
            model=model_name,
        )

    @observe_boundary("neuro_core.evaluate_conditions")
    async def evaluate_conditions(
            self,
            evaluate_conditions_request: EvaluateConditionsRequest,
    ) -> EvaluateConditionsResponse:
        response = await self._condition_evaluator.evaluate(
            evaluate_conditions_request.conditions,
            evaluate_conditions_request.buffer_states
        )
        log_event(
            logger,
            logging.DEBUG,
            "rpc.evaluate_conditions.completed",
            "Completed EvaluateConditions RPC.",
            rule_count=len(evaluate_conditions_request.conditions),
            buffer_count=len(evaluate_conditions_request.buffer_states),
            satisfied_rule_ids=list(response.satisfied_rule_ids),
        )
        return response

    @observe_boundary("neuro_core.decode_action")
    async def decode_action(
            self,
            decode_action_request: DecodeActionRequest,
    ) -> DecodeActionResponse:
        response = await self._action_resolver.decode_action(
            decode_action_request.action_intent,
            decode_action_request.current_states,
            decode_action_request.schemas
        )
        log_event(
            logger,
            logging.DEBUG,
            "rpc.decode_action.completed",
            "Completed DecodeAction RPC.",
            rule_id=decode_action_request.action_intent.rule_id,
            operation_count=len(response.operations),
        )
        return response
