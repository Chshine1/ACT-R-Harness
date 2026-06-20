# ACT‑R Harnessed Agent for Frostpunk

An experimental cognitive architecture that combines **ACT‑R symbolic reasoning**,
a **frozen neural semantic core**, and **reinforcement learning** over production‑rule
utilities and declarative‑memory activations – designed to play (a simplified version of)
*Frostpunk*.

## Overview

Modern game‑playing agents often rely on end‑to‑end deep RL with massive black‑box
networks. This project explores a different path: a hybrid architecture where a
classical cognitive model (ACT‑R) is “harnessed” by a frozen large language model
that provides semantic grounding, and where learning happens entirely inside the
symbolic layer.

We ask:

> Can an agent learn **what to think about** and **which high‑level strategy to pick**
> without ever back‑propagating into the neural core?

The hypothesis is that this design yields more sample‑efficient, interpretable, and
transferable behaviour – especially in complex resource‑management domains like
*Frostpunk*.

## Architecture

The agent is built around three ACT‑R modules that communicate through buffers:

| Module | Role |
|--------|------|
| **Goal buffer** | Holds the current intention (e.g., *survive*, *find most urgent problem*). |
| **Declarative memory** | Stores and retrieves chunks (facts about resources, past events). |
| **Perception & Motor** | Encapsulates interaction with the environment – queries specific information and executes high‑level actions. |

A **procedural system** matches production rules against the buffer state. Rules are
abstract and close to human reasoning, e.g.:

> IF *multiple resource crises are present*  
> THEN *identify the most urgent one and address it*.

A **frozen Neuro Core** (a local LLM) translates these abstract descriptions into
concrete buffer operations and environment actions. It also *may* propose new rules
when the agent repeatedly fails – but rule generation is deliberately constrained to
avoid runaway complexity.

Crucially, **reinforcement learning is applied only to the symbolic layer**:

- **Utility learning** for production rules (which rule to select given the context).
- **Base‑level activation learning** for declarative chunks (which memory to retrieve).

There is no gradient‑based training of the neural core; it remains a stable, frozen
semantic engine.

### Why ACT‑R + LLM + RL?

- ACT‑R provides a cognitively plausible, transparent decision loop.
- The LLM bridges the gap between high‑level natural‑language strategies and the
  low‑level API of the environment.
- RL on symbolic parameters keeps the agent adaptive while preserving interpretability.

## Repository Structure

```
.
├── README.md
├── dotnet/                     # Future .NET orchestration & UI layer
├── python/                     # Cognitive engine, RL training, environment adapters
│   ├── pyproject.toml
│   ├── pixi.lock
│   ├── .gitattributes
│   ├── .gitignore
│   ├── src/
│   │   ├── interfaces.py       # Abstract base classes for all modules
│   │   ├── models.py           # Core data structures (Chunk, BufferSet, Rule, Op)
│   │   ├── procedural.py       # Production system with utility learning
│   │   ├── declarative.py      # Declarative memory with activation
│   │   ├── neuro_core_mock.py  # Mock semantic core for testing
│   │   ├── agent.py            # Main cognitive loop
│   │   └── env/
│   │       ├── abstract.py     # Abstract environment interface
│   │       ├── frostpunk_sim.py
│   │       └── perception.py   # Perception‑motor wrapper around environments
│   └── tests/
│       ├── test_procedural.py
│       ├── test_agent.py
│       └── test_perception.py
└── docs/                       # Additional documentation (future)
```

Currently all prototype work happens in `python/`. The `dotnet/` directory is
reserved for a future orchestration layer that will handle application runtime,
configuration management, and monitoring – tasks where .NET’s tooling and
performance are advantageous.

## Technology Stack (Python side)

### Environment & Package Management
- **Pixi** – fast, reproducible environment management built on conda‑forge.
- **Python 3.13** – the minimum supported version is 3.11, but we develop on 3.13.
- Dependencies: `pyyaml` for configuration; `pytest` + `decoy` for testing.

### Code Quality
The project enforces strict typing and linting from day one:

| Tool | Role |
|------|------|
| **mypy** | Full strict‑mode type checking (`disallow_any_*`, `warn_unused_*`). |
| **pyright** | Additional type checking with even more paranoid rules (`reportUnused*`, `reportOptional*`). |
| **ruff** | Fast Python linter replacing Flake8, isort, pyupgrade, and many plugins. |
| **decoy** | Mypy plugin that provides type‑safe, ergonomic mocks for tests. |

All checks are available as Pixi tasks:
- `pixi run typecheck` – runs both mypy and pyright.
- `pixi run lint` – runs ruff.
- `pixi run check` – runs both.

## Design Principles

1. **Abstraction First**  
   Every module is defined by an abstract interface (`ABC`). Concrete
   implementations can be swapped without touching the agent core. For example,
   `MockNeuroCore` can be replaced by a real local LLM, and `MiniFrostPunk` can
   be replaced by a full game connector.

2. **Dependency Injection**  
   The agent receives its modules at construction time. This makes testing
   trivial (inject mocks) and supports configuration‑driven assembly.

3. **Configuration over Code**  
   Behavioural parameters (learning rate, temperature, model path) live in YAML
   files, not hard‑coded.

4. **Testable from the Ground Up**  
   Every cognitive module has dedicated unit tests that can run without a real
   environment or LLM, enabling rapid iteration.

## Current State & Next Steps

- [x] Core abstractions (`interfaces.py`, `models.py`).
- [x] Production system with utility‑based stochastic selection and online TD‑style learning.
- [x] Declarative memory with base‑level activation and retrieval.
- [x] Mock neuro core for end‑to‑end testing.
- [x] Minimal Frostpunk simulator and perception wrapper.
- [x] Full test suite with mocks (`decoy` + `unittest.mock`).
- [ ] Replace mock neuro core with a real frozen LLM (e.g., Llama 3 via `llama.cpp`).
- [ ] Implement rule proposal / generation logic.
- [ ] Scale up the Frostpunk simulator or integrate a real game interface.
- [ ] Add .NET orchestration layer for experiment management and live monitoring.
- [ ] Evaluate learning dynamics and compare against pure RL baselines.

## Getting Started

### Prerequisites
- [Pixi](https://pixi.sh)
- Python 3.13 (Pixi will manage it)

### Setup & Run Tests
```bash
cd python
pixi install
pixi run check
pixi run -e dev pytest
```

### Run a Quick Training Demo
```bash
pixi run -e dev python main.py
```
(You may need to create a simple `main.py` that assembles the agent and runs a few
episodes.)

## Contributing

We welcome contributions that improve the cognitive architecture, the environment
interface, or the .NET orchestration layer.

---

*Project maintained by Chshine1 (chshine@qq.com).*