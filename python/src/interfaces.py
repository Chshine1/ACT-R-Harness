from abc import ABC, abstractmethod
from typing import Any, Dict, List, Optional

from .models import BufferSet, Chunk, Op, Rule


class AbstractPerceptionMotor(ABC):
    @abstractmethod
    def query_visual(self, description: str) -> Dict[str, Any]:
        ...

    @abstractmethod
    def execute_manual(self, action: str, params: Dict[str, Any]) -> Any:
        ...


class AbstractDeclarativeMemory(ABC):
    @abstractmethod
    def retrieve(self, cue: Dict[str, Any]) -> Optional[Chunk]:
        ...

    @abstractmethod
    def add_chunk(self, chunk: Chunk):
        ...

    @abstractmethod
    def update_activation(self, chunk_id: str, delta: float):
        ...


class AbstractProceduralSystem(ABC):
    @abstractmethod
    def select_rule(self, buffers: BufferSet) -> Rule:
        ...

    @abstractmethod
    def learn_utility(self, rule_id: str, reward: float):
        ...

    @abstractmethod
    def add_rule(self, rule: Rule):
        ...


class AbstractNeuroCore(ABC):
    @abstractmethod
    def translate_action(self, action_desc: str, context: Dict[str, Any]) -> List[Op]:
        ...

    @abstractmethod
    def condition_holds(self, condition_desc: str, context: Dict[str, Any]) -> bool:
        ...

    @abstractmethod
    def propose_rule(self, trajectory: List[Dict]) -> Optional[Rule]:
        ...
