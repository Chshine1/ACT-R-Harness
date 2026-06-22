from abc import ABC, abstractmethod
from typing import Any, Dict


class AbstractPerceptionMotor(ABC):
    @abstractmethod
    def query_visual(self, description: str) -> Dict[str, Any]:
        ...

    @abstractmethod
    def execute_manual(self, action: str, params: Dict[str, Any]) -> Any:
        ...
