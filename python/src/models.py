from dataclasses import dataclass, field
from typing import Any, Dict, Optional, Callable


@dataclass(frozen=True)
class Chunk:
    id: str
    slots: Dict[str, Any]
    creation_time: float = 0.0


@dataclass
class BufferSet:
    goal: Dict[str, Any] | None = None
    retrieval: Optional[Chunk] = None
    visual: Dict[str, Any] | None = None
    manual: Optional[str] = None


@dataclass
class Rule:
    id: str
    condition_desc: str
    action_desc: str
    utility: float = 0.0
    condition_fn: Optional[Callable] = None
    action_fn: Optional[Callable] = None


@dataclass
class Op:
    target: str
    command: str
    params: Dict[str, Any] = field(default_factory=dict)
