from __future__ import annotations

import contextlib
import contextvars
import functools
import json
import logging
import time
import traceback
from collections.abc import Callable, Generator, Mapping, Sequence
from typing import Any, ParamSpec, TypeVar, cast

P = ParamSpec("P")
R = TypeVar("R")

_run_id: contextvars.ContextVar[str | None] = contextvars.ContextVar("run_id", default=None)
_epoch: contextvars.ContextVar[int | None] = contextvars.ContextVar("epoch", default=None)
_step: contextvars.ContextVar[int | None] = contextvars.ContextVar("step", default=None)
_correlation_id: contextvars.ContextVar[str | None] = contextvars.ContextVar("correlation_id", default=None)
_operation: contextvars.ContextVar[str | None] = contextvars.ContextVar("operation", default=None)


def configure_logging(level: int) -> None:
    root = logging.getLogger()
    root.handlers.clear()

    handler = logging.StreamHandler()
    handler.setFormatter(_JsonFormatter())
    root.addHandler(handler)
    root.setLevel(level)


def attach_grpc_observability(server: Any) -> None:
    from grpclib.events import RecvRequest, listen

    async def on_recv_request(event: Any) -> None:
        method_name = cast(str, getattr(event, "method_name", "<grpc>"))
        metadata = getattr(event, "metadata", None)
        original_method = event.method_func

        async def wrapped(stream: Any) -> Any:
            with bind_context(
                run_id=_metadata_value(metadata, "x-harness-run-id"),
                epoch=_parse_optional_int(_metadata_value(metadata, "x-harness-epoch")),
                step=_parse_optional_int(_metadata_value(metadata, "x-harness-step")),
                correlation_id=_metadata_value(metadata, "x-harness-correlation-id"),
                operation=_metadata_value(metadata, "x-harness-operation") or method_name,
            ):
                return await original_method(stream)

        event.method_func = wrapped

    listen(server, RecvRequest, on_recv_request)


def current_context() -> dict[str, Any]:
    return {
        "run_id": _run_id.get(),
        "epoch": _epoch.get(),
        "step": _step.get(),
        "correlation_id": _correlation_id.get(),
        "operation": _operation.get(),
    }


@contextlib.contextmanager
def bind_context(
    *,
    run_id: str | None = None,
    epoch: int | None = None,
    step: int | None = None,
    correlation_id: str | None = None,
    operation: str | None = None,
) -> Generator[None, None, None]:
    tokens: list[tuple[contextvars.ContextVar[Any], contextvars.Token[Any]]] = []
    if run_id is not None:
        tokens.append((_run_id, _run_id.set(run_id)))
    if epoch is not None:
        tokens.append((_epoch, _epoch.set(epoch)))
    if step is not None:
        tokens.append((_step, _step.set(step)))
    if correlation_id is not None:
        tokens.append((_correlation_id, _correlation_id.set(correlation_id)))
    if operation is not None:
        tokens.append((_operation, _operation.set(operation)))

    try:
        yield
    finally:
        for var, token in reversed(tokens):
            var.reset(token)


def log_event(
    logger: logging.Logger,
    level: int,
    event_name: str,
    message: str,
    **fields: Any,
) -> None:
    logger.log(
        level,
        message,
        extra={
            "event_name": event_name,
            "event_fields": _sanitize(fields),
        },
    )


def observe_boundary(boundary_name: str) -> Callable[[Callable[P, Any]], Callable[P, Any]]:
    def decorator(func: Callable[P, Any]) -> Callable[P, Any]:
        logger = logging.getLogger(func.__module__)

        if _is_async_callable(func):

            @functools.wraps(func)
            async def async_wrapper(*args: P.args, **kwargs: P.kwargs) -> Any:
                started = time.perf_counter()
                log_event(
                    logger,
                    logging.DEBUG,
                    "boundary.enter",
                    f"Entering {boundary_name}.",
                    boundary=boundary_name,
                    args=_summarize_call(args, kwargs),
                )
                try:
                    result = await func(*args, **kwargs)
                except Exception as exc:
                    log_event(
                        logger,
                        logging.ERROR,
                        "boundary.error",
                        f"Boundary {boundary_name} failed.",
                        boundary=boundary_name,
                        duration_ms=_elapsed_ms(started),
                        error_type=type(exc).__name__,
                        error_message=str(exc),
                    )
                    raise

                log_event(
                    logger,
                    logging.DEBUG,
                    "boundary.exit",
                    f"Leaving {boundary_name}.",
                    boundary=boundary_name,
                    duration_ms=_elapsed_ms(started),
                    result=_summarize_value(result),
                )
                return result

            return async_wrapper

        @functools.wraps(func)
        def sync_wrapper(*args: P.args, **kwargs: P.kwargs) -> Any:
            started = time.perf_counter()
            log_event(
                logger,
                logging.DEBUG,
                "boundary.enter",
                f"Entering {boundary_name}.",
                boundary=boundary_name,
                args=_summarize_call(args, kwargs),
            )
            try:
                result = func(*args, **kwargs)
            except Exception as exc:
                log_event(
                    logger,
                    logging.ERROR,
                    "boundary.error",
                    f"Boundary {boundary_name} failed.",
                    boundary=boundary_name,
                    duration_ms=_elapsed_ms(started),
                    error_type=type(exc).__name__,
                    error_message=str(exc),
                )
                raise

            log_event(
                logger,
                logging.DEBUG,
                "boundary.exit",
                f"Leaving {boundary_name}.",
                boundary=boundary_name,
                duration_ms=_elapsed_ms(started),
                result=_summarize_value(result),
            )
            return result

        return sync_wrapper

    return decorator


class _JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, Any] = {
            "timestamp": self.formatTime(record, "%Y-%m-%dT%H:%M:%S"),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "event": getattr(record, "event_name", "log"),
            **current_context(),
        }

        event_fields = getattr(record, "event_fields", None)
        if event_fields:
            payload["fields"] = _sanitize(event_fields)

        if record.exc_info:
            payload["exception"] = "".join(traceback.format_exception(*record.exc_info))

        return json.dumps(payload, ensure_ascii=False)


def _elapsed_ms(started: float) -> float:
    return round((time.perf_counter() - started) * 1000, 3)


def _is_async_callable(func: Callable[..., Any]) -> bool:
    code = getattr(func, "__code__", None)
    if code is None:
        return False
    return bool(code.co_flags & 0x80)


def _summarize_call(args: Sequence[Any], kwargs: Mapping[str, Any]) -> dict[str, Any]:
    positional = list(args)
    if positional and hasattr(positional[0], "__class__"):
        positional = positional[1:]

    return {
        "args": [_summarize_value(arg) for arg in positional],
        "kwargs": {key: _summarize_value(value) for key, value in kwargs.items()},
    }


def _summarize_value(value: Any, depth: int = 0) -> Any:
    if value is None or isinstance(value, bool | int | float):
        return value
    if isinstance(value, str):
        return value if len(value) <= 160 else f"{value[:157]}..."
    if depth >= 3:
        return type(value).__name__
    if isinstance(value, Mapping):
        keys = list(value.keys())
        preview_keys = keys[:8]
        preview = {str(key): _summarize_value(value[key], depth + 1) for key in preview_keys}
        if len(keys) > len(preview_keys):
            preview["_truncated"] = len(keys) - len(preview_keys)
        return preview
    if isinstance(value, Sequence) and not isinstance(value, str | bytes | bytearray):
        preview = [_summarize_value(item, depth + 1) for item in value[:6]]
        return {"count": len(value), "preview": preview}
    if hasattr(value, "to_dict"):
        return _summarize_value(value.to_dict(), depth + 1)
    return type(value).__name__


def _sanitize(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {str(key): _sanitize(inner) for key, inner in value.items()}
    if isinstance(value, Sequence) and not isinstance(value, str | bytes | bytearray):
        return [_sanitize(item) for item in value]
    if hasattr(value, "to_dict"):
        return _sanitize(value.to_dict())
    if isinstance(value, bool | int | float | str) or value is None:
        return value
    return _summarize_value(value)


def _metadata_value(metadata: Any, key: str) -> str | None:
    if metadata is None:
        return None
    if hasattr(metadata, "get"):
        value = metadata.get(key)
        if isinstance(value, list):
            return cast(str | None, value[0] if value else None)
        return cast(str | None, value)
    try:
        return cast(str | None, metadata[key])
    except Exception:
        return None


def _parse_optional_int(value: str | None) -> int | None:
    if value is None:
        return None
    try:
        return int(value)
    except ValueError:
        return None
