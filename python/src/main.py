import asyncio
import logging
import os
from grpclib.server import Server
from grpclib.utils import graceful_exit
from .services.frostpunk_game import FrostpunkGame
from .services.declarative_memory import DeclarativeMemory
from .services.neuro_core import NeuroCore
from .services.procedural_memory import ProceduralMemory


async def main():
    port = int(os.getenv("PORT", "50051"))
    temperature = float(os.getenv("TEMPERATURE", "0.5"))
    lr = float(os.getenv("LEARNING_RATE", "0.1"))

    server = Server([
        DeclarativeMemory(),
        NeuroCore(),
        FrostpunkGame(),
        ProceduralMemory(temperature=temperature, learning_rate=lr),
    ])

    host, port_str = '0.0.0.0', str(port)
    logging.info(f'Starting gRPC server on {host}:{port_str}')

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
