import math
import random
import time

from betterproto.lib.std.google.protobuf import Empty

from actr_harness.generated.grpc.actr import MemoryChunk
from actr_harness.generated.grpc.actr.services import (
    AddChunkRequest,
    DeclarativeMemoryBase,
    RetrieveRequest,
    RetrieveResponse,
    TickMemoryRequest,
)


class DeclarativeMemory(DeclarativeMemoryBase):
    def __init__(self, decay: float = 0.5, noise_sd: float = 0.25):
        self.chunks: dict[str, MemoryChunk] = {}
        self.access_log: dict[str, list[float]] = {}
        self.decay = decay
        self.noise_sd = noise_sd
        self._sim_time = time.time()

    async def add_chunk(self, add_chunk_request: AddChunkRequest) -> Empty:
        chunk = add_chunk_request.chunk
        self.chunks[chunk.id] = chunk
        self.access_log[chunk.id] = [self._sim_time]
        return Empty()

    def _base_activation(self, chunk_id: str, current_time: float) -> float:
        if chunk_id not in self.access_log:
            return -1e6
        times = self.access_log[chunk_id]
        sum_term = sum((current_time - t) ** (-self.decay) for t in times)
        if sum_term <= 0:
            return -1e6
        return math.log(sum_term)

    async def retrieve(self, retrieve_request: RetrieveRequest) -> RetrieveResponse:
        best = None
        best_act = -float('inf')
        now = self._sim_time
        for chunk in self.chunks.values():
            cue_score = self._cue_score(chunk, retrieve_request.cue)
            if cue_score <= 0:
                continue

            base = self._base_activation(chunk.id, now)
            noise = random.gauss(0, self.noise_sd) if self.noise_sd > 0 else 0.0
            act = base + cue_score + noise
            if act > best_act:
                best_act = act
                best = chunk
        if best:
            self.access_log[best.id].append(now)
        return RetrieveResponse(chunk=best)

    async def tick_memory(self, tick_memory_request: TickMemoryRequest) -> Empty:
        self._sim_time += tick_memory_request.delta_time
        return Empty()

    def _cue_score(self, chunk: MemoryChunk, cue: dict[str, str]) -> float:
        if not cue:
            return 0.0

        total = 0.0
        for key, expected in cue.items():
            actual = chunk.slots.get(key)
            score = self._slot_match_score(actual, expected)
            if score <= 0:
                return 0.0
            total += score
        return total

    def _slot_match_score(self, actual: str | None, expected: str) -> float:
        if actual is None:
            return 0.0

        norm_actual = self._normalize(actual)
        norm_expected = self._normalize(expected)
        if not norm_actual or not norm_expected:
            return 0.0
        if norm_actual == norm_expected:
            return 1.0

        actual_tokens = set(norm_actual.split())
        expected_tokens = set(norm_expected.split())
        overlap = actual_tokens & expected_tokens
        if not overlap:
            return 0.0
        if (
            actual_tokens.issubset(expected_tokens)
            or expected_tokens.issubset(actual_tokens)
        ):
            return 0.85

        return len(overlap) / max(len(actual_tokens), len(expected_tokens))

    @staticmethod
    def _normalize(text: str) -> str:
        normalized = "".join(ch.lower() if ch.isalnum() else " " for ch in text)
        return " ".join(normalized.split())
