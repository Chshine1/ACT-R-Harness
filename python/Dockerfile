FROM ghcr.io/prefix-dev/pixi:latest AS builder
WORKDIR /app
COPY python/pyproject.toml python/pixi.lock ./
RUN --mount=type=cache,target=/root/.cache/rattler \
    pixi install --frozen -e dev
COPY python /app/python
COPY shared /app/shared
WORKDIR /app/python
RUN pixi run -e dev protos-gen


FROM ghcr.io/prefix-dev/pixi:latest AS final
WORKDIR /app
COPY python/pyproject.toml python/pixi.lock ./
RUN --mount=type=cache,target=/root/.cache/rattler \
    pixi install --frozen
COPY --from=builder /app/python /app/python
WORKDIR /app/python
ENTRYPOINT ["pixi", "run", "start"]