from actr_harness.generated.grpc.actr import (
    BufferState,
)

from typing import Any


class BuffersView:
    def __init__(self, buffer_states: list[BufferState]):
        self._data: dict[str, Any] = {}
        for bs in buffer_states:
            self._data[bs.module_id] = bs.data.to_dict() if hasattr(bs.data, "to_dict") else bs.data

    def get(self, path: str) -> Any:
        """path like 'declarative_memory.retrieved_chunk.slots.keywords'"""
        parts = path.split(".")
        obj = self._data
        for part in parts:
            if isinstance(obj, dict):
                obj = obj.get(part)
            elif isinstance(obj, list):
                try:
                    idx = int(part)
                    obj = obj[idx]
                except (ValueError, IndexError):
                    return None
            else:
                return None
        return obj

    def to_dict(self) -> dict[str, Any]:
        return self._data
