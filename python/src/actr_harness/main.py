import asyncio
import logging
import os

from grpclib.server import Server
from grpclib.utils import graceful_exit

from .services.declarative_memory import DeclarativeMemory
from .services.neuro_core.neuro_core import NeuroCore
from .services.procedural_memory import ProceduralMemory


def _resolve_log_level(raw_level: str) -> int:
    return getattr(logging, raw_level.upper(), logging.INFO)


async def main():
    port = int(os.getenv("PORT", "50051"))
    log_level = os.getenv("LOG_LEVEL", "INFO")
    temperature = float(os.getenv("TEMPERATURE", "0.5"))
    lr = float(os.getenv("LEARNING_RATE", "0.1"))
    rules_path = os.getenv("RULESET_PATH")
    default_rule_utility = float(os.getenv("DEFAULT_RULE_UTILITY", "0.0"))
    procedural_random_seed = os.getenv("PROCEDURAL_RANDOM_SEED")
    memory_decay = float(os.getenv("DECLARATIVE_MEMORY_DECAY", "0.5"))
    memory_noise_sd = float(os.getenv("DECLARATIVE_MEMORY_NOISE_SD", "0.25"))
    resolved_log_level = _resolve_log_level(log_level)

    logging.basicConfig(
        level=resolved_log_level,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    logger = logging.getLogger(__name__)

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
    logger.info(
        "Starting gRPC server on %s:%s with log_level=%s",
        host,
        port_str,
        logging.getLevelName(resolved_log_level),
    )

    try:
        with graceful_exit([server]):
            await server.start(host, int(port_str))
            await server.wait_closed()
    except NotImplementedError:
        await server.start(host, int(port_str))
        await server.wait_closed()


if __name__ == '__main__':
    asyncio.run(main())
