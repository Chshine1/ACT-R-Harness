import logging
import os

from actr_harness.generated.grpc.actr.services import (
    DecodeActionRequest,
    DecodeActionResponse,
    EvaluateConditionsRequest,
    EvaluateConditionsResponse,
    NeuroCoreBase,
)

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

        logger.info("NeuroCore initialized")
        self._llm = llm_client
        self._condition_evaluator = ConditionEvaluator(self._llm)
        self._action_resolver = ActionResolver(self._llm)

    async def evaluate_conditions(
            self,
            evaluate_conditions_request: EvaluateConditionsRequest,
    ) -> EvaluateConditionsResponse:
        logger.info(
            "EvaluateConditions request: rules=%d buffers=%d",
            len(evaluate_conditions_request.conditions),
            len(evaluate_conditions_request.buffer_states),
        )
        try:
            response = await self._condition_evaluator.evaluate(
                evaluate_conditions_request.conditions,
                evaluate_conditions_request.buffer_states
            )
        except Exception:
            logger.exception(
                "EvaluateConditions failed: rules=%d buffers=%d",
                len(evaluate_conditions_request.conditions),
                len(evaluate_conditions_request.buffer_states),
            )
            raise

        logger.info(
            "EvaluateConditions response: satisfied=%d rule_ids=%s",
            len(response.satisfied_rule_ids),
            response.satisfied_rule_ids,
        )
        return response

    async def decode_action(
            self,
            decode_action_request: DecodeActionRequest,
    ) -> DecodeActionResponse:
        logger.info(
            "DecodeAction request: rule=%s buffers=%d schemas=%d commands=%d semantics=%d",
            decode_action_request.action_intent.rule_id,
            len(decode_action_request.current_states),
            len(decode_action_request.schemas),
            len(decode_action_request.action_intent.commands),
            len(decode_action_request.action_intent.semantics),
        )
        try:
            response = await self._action_resolver.decode_action(
                decode_action_request.action_intent,
                decode_action_request.current_states,
                decode_action_request.schemas
            )
        except Exception:
            logger.exception(
                "DecodeAction failed for rule=%s",
                decode_action_request.action_intent.rule_id,
            )
            raise

        logger.info(
            "DecodeAction response: operations=%d",
            len(response.operations),
        )
        return response
