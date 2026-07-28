import json
import logging
from openai import AsyncOpenAI
from typing import Any

logger = logging.getLogger(__name__)


class LLMClient:
    def __init__(self, model: str, api_key: str, base_url: str):
        self._model = model
        self._api_key = api_key
        self._base_url = base_url

    async def chat_json(self, user_data: Any, system: str) -> Any:
        client = AsyncOpenAI(
            api_key=self._api_key,
            base_url=self._base_url,
        )
        logger.debug(
            "Submitting LLM JSON request: model=%s payload_type=%s",
            self._model,
            type(user_data).__name__,
        )
        try:
            completion = await client.chat.completions.create(
                model=self._model,
                messages=[
                    {"role": "system", "content": system},
                    {"role": "user", "content": json.dumps(user_data, ensure_ascii=False)}
                ]
            )
        except Exception:
            logger.exception("LLM JSON request failed for model=%s", self._model)
            raise

        content = completion.choices[0].message.content
        if content is None:
            logger.warning("LLM response content was empty for model=%s", self._model)
            return None
        try:
            return json.loads(content)
        except json.JSONDecodeError:
            logger.warning(
                "LLM response was not valid JSON for model=%s. Returning raw content preview=%r",
                self._model,
                content[:200],
            )
            return content
