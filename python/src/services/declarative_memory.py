import math
import time

import random
from betterproto.lib.std.google.protobuf import Empty
from ..generated.grpc.actr import MemoryChunk
from ..generated.grpc.actr.services import DeclarativeMemoryBase, AddChunkRequest, RetrieveRequest, RetrieveResponse, \
    UpdateActivationRequest


class DeclarativeMemory(DeclarativeMemoryBase):
    def __init__(self, decay: float = 0.5, noise_sd: float = 0.25):
        self.chunks: dict[str, MemoryChunk] = {}
        self.access_log: dict[str, list[float]] = {}
        self.decay = decay
        self.noise_sd = noise_sd

    async def add_chunk(self, add_chunk_request: AddChunkRequest) -> Empty:
        chunk = add_chunk_request.chunk
        self.chunks[chunk.id] = chunk
        self.access_log[chunk.id] = [chunk.creation_time]
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
        now = time.time()
        for chunk in self.chunks.values():
            if all(chunk.slots.get(k) == v for k, v in retrieve_request.cue.items()):
                base = self._base_activation(chunk.id, now)
                noise = random.gauss(0, self.noise_sd)
                act = base + noise
                if act > best_act:
                    best_act = act
                    best = chunk
        if best:
            self.access_log[best.id].append(now)
        return RetrieveResponse(chunk=best)

    async def update_activation(self, update_activation_request: UpdateActivationRequest) -> Empty:
        _ = update_activation_request
        raise NotImplementedError()
