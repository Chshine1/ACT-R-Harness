import json
from openai import AsyncOpenAI
from typing import Any


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
        completion = await client.chat.completions.create(
            model=self._model,
            messages=[
                {"role": "system", "content": system},
                {"role": "user", "content": json.dumps(user_data, ensure_ascii=False)}
            ]
        )
        content = completion.choices[0].message.content
        if content is None:
            return None
        try:
            return json.loads(content)
        except json.JSONDecodeError:
            return content
