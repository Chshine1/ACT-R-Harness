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

    # noinspection PyPep8Naming
    async def EvaluateConditions(  # noqa: N802
            self,
            request: EvaluateConditionsRequest,
    ) -> EvaluateConditionsResponse:
        logger.info(
            "EvaluateConditions request: rules=%d buffers=%d",
            len(request.conditions),
            len(request.buffer_states),
        )
        try:
            response = await self._condition_evaluator.evaluate(request.conditions, request.buffer_states)
        except Exception:
            logger.exception(
                "EvaluateConditions failed: rules=%d buffers=%d",
                len(request.conditions),
                len(request.buffer_states),
            )
            raise

        logger.info(
            "EvaluateConditions response: satisfied=%d rule_ids=%s",
            len(response.satisfied_rule_ids),
            response.satisfied_rule_ids,
        )
        return response

    # noinspection PyPep8Naming
    async def DecodeAction(  # noqa: N802
            self,
            request: DecodeActionRequest,
    ) -> DecodeActionResponse:
        logger.info(
            "DecodeAction request: rule=%s buffers=%d schemas=%d commands=%d semantics=%d",
            request.action_intent.rule_id,
            len(request.current_states),
            len(request.schemas),
            len(request.action_intent.commands),
            len(request.action_intent.semantics),
        )
        try:
            response = await self._action_resolver.decode_action(
                request.action_intent,
                request.current_states,
                request.schemas
            )
        except Exception:
            logger.exception(
                "DecodeAction failed for rule=%s",
                request.action_intent.rule_id,
            )
            raise

        logger.info(
            "DecodeAction response: operations=%d",
            len(response.operations),
        )
        return response
