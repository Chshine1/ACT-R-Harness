import math
import random
import time
from typing import Optional

from .interfaces import AbstractDeclarativeMemory
from .models import Chunk


class DeclarativeMemory(AbstractDeclarativeMemory):
    def __init__(self, decay: float = 0.5, noise_sd: float = 0.25):
        self.chunks: dict[str, Chunk] = {}
        self.access_log: dict[str, list[float]] = {}
        self.decay = decay
        self.noise_sd = noise_sd

    def add_chunk(self, chunk: Chunk):
        self.chunks[chunk.id] = chunk
        self.access_log[chunk.id] = [chunk.creation_time]

    def _base_activation(self, chunk_id: str, current_time: float) -> float:
        if chunk_id not in self.access_log:
            return -1e6
        times = self.access_log[chunk_id]
        sum_term = sum((current_time - t) ** (-self.decay) for t in times)
        if sum_term <= 0:
            return -1e6
        return math.log(sum_term)

    def retrieve(self, cue: dict) -> Optional[Chunk]:
        best = None
        best_act = -float('inf')
        now = time.time()
        for chunk in self.chunks.values():
            if all(chunk.slots.get(k) == v for k, v in cue.items()):
                base = self._base_activation(chunk.id, now)
                noise = random.gauss(0, self.noise_sd)
                act = base + noise
                if act > best_act:
                    best_act = act
                    best = chunk
        if best:
            self.access_log[best.id].append(now)
        return best

    def update_activation(self, chunk_id: str, delta: float):
        pass
