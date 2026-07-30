import json
import logging
from typing import Any

from openai import AsyncOpenAI

from actr_harness.observability import log_event, observe_boundary

logger = logging.getLogger(__name__)


class LLMClient:
    def __init__(self, model: str, api_key: str, base_url: str):
        self._model = model
        self._api_key = api_key
        self._base_url = base_url

    @observe_boundary("llm_client.chat_json")
    async def chat_json(self, user_data: Any, system: str) -> Any:
        client = AsyncOpenAI(
            api_key=self._api_key,
            base_url=self._base_url,
        )
        log_event(
            logger,
            logging.DEBUG,
            "llm.request_submitted",
            "Submitting LLM JSON request.",
            model=self._model,
            payload_type=type(user_data).__name__,
        )
        completion = await client.chat.completions.create(
            model=self._model,
            messages=[
                {"role": "system", "content": system},
                {"role": "user", "content": json.dumps(user_data, ensure_ascii=False)}
            ]
        )

        content = completion.choices[0].message.content
        if content is None:
            log_event(
                logger,
                logging.WARNING,
                "llm.response_empty",
                "LLM response content was empty.",
                model=self._model,
            )
            return None
        try:
            return json.loads(content)
        except json.JSONDecodeError:
            log_event(
                logger,
                logging.WARNING,
                "llm.response_invalid_json",
                "LLM response was not valid JSON.",
                model=self._model,
                preview=content[:200],
            )
            return content
