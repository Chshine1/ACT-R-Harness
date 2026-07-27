FROM ghcr.io/prefix-dev/pixi:latest AS builder
WORKDIR /app/python
COPY python/pyproject.toml python/pixi.lock ./
RUN --mount=type=cache,target=/root/.cache/rattler \
    pixi install --frozen -e dev
COPY python /app/python
COPY shared /app/shared
RUN pixi run -e dev protos-gen


FROM ghcr.io/prefix-dev/pixi:latest AS final
WORKDIR /app/python
COPY python/pyproject.toml python/pixi.lock ./
RUN --mount=type=cache,target=/root/.cache/rattler \
    pixi install --frozen
COPY --from=builder /app/python /app/python
COPY --from=builder /app/shared /app/shared
ENV PORT=50051
ENTRYPOINT ["pixi", "run", "start"]
