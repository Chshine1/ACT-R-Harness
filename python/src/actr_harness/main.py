import asyncio
import logging
import os

from grpclib.server import Server
from grpclib.utils import graceful_exit

from .services.declarative_memory import DeclarativeMemory
from .services.neuro_core.neuro_core import NeuroCore
from .services.procedural_memory import ProceduralMemory


async def main():
    port = int(os.getenv("PORT", "50051"))
    temperature = float(os.getenv("TEMPERATURE", "0.5"))
    lr = float(os.getenv("LEARNING_RATE", "0.1"))
    rules_path = os.getenv("RULESET_PATH")
    default_rule_utility = float(os.getenv("DEFAULT_RULE_UTILITY", "0.0"))
    procedural_random_seed = os.getenv("PROCEDURAL_RANDOM_SEED")
    memory_decay = float(os.getenv("DECLARATIVE_MEMORY_DECAY", "0.5"))
    memory_noise_sd = float(os.getenv("DECLARATIVE_MEMORY_NOISE_SD", "0.25"))

    server = Server([
        DeclarativeMemory(decay=memory_decay, noise_sd=memory_noise_sd),
        NeuroCore(),
        ProceduralMemory(
            temperature=temperature,
            learning_rate=lr,
            rules_path=rules_path,
            default_utility=default_rule_utility,
            random_seed=int(procedural_random_seed) if procedural_random_seed else None,
        ),
    ])

    host, port_str = '0.0.0.0', str(port)
    logging.info("Starting gRPC server on %s:%s", host, port_str)

    try:
        with graceful_exit([server]):
            await server.start(host, int(port_str))
            await server.wait_closed()
    except NotImplementedError:
        await server.start(host, int(port_str))
        await server.wait_closed()


if __name__ == '__main__':
    logging.basicConfig(level=logging.INFO)
    asyncio.run(main())
