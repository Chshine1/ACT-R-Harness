# Task 1: Procedural Rules Loading Design

## Background

The current Python `ProceduralMemory` service exposes gRPC methods, but it never loads any production rules into `self.rules`. As a result, `GetAllConditions` returns an empty list and the runtime cannot supply the active rules defined in `shared/ruleset/lab.yml`.

Task 1 is limited to the Python side. It should make the procedural-memory service load the active rules from `lab.yml`, convert them into the current runtime objects, and expose them through the existing gRPC interface. This task may also add one minimal configuration entry for overriding the ruleset path.

## Goal

When the Python service starts, it should load the three active rules from `shared/ruleset/lab.yml` and store them as `Rule` objects containing:

- a `ProceduralCondition`
- a `NeuroAction`
- an initial utility value

After that change, `ProceduralMemory.get_all_conditions()` should return the currently active conditions defined by the ruleset, without requiring any change to the existing protobuf contracts.

## Scope

In scope:

- add a small rules-loading layer on the Python side
- support a minimal environment-variable override for the ruleset path
- convert YAML rules into runtime `Rule`, `ProceduralCondition`, and `NeuroAction` objects
- add focused unit tests for rule loading and service behavior

Out of scope:

- changing the protobuf schema
- changing NeuroCore condition evaluation or action decoding behavior
- loading commented-out future rules from `lab.yml`
- adding host-side orchestration changes

## Design

### 1. Split loading from service behavior

`ProceduralMemory` should remain responsible for:

- holding loaded rules
- returning all conditions through gRPC
- selecting one applicable rule by utility
- learning utility updates

YAML parsing and object construction should move into a dedicated helper module, proposed as:

- `python/src/actr_harness/services/procedural_rules.py`

This keeps the gRPC service small and makes the loading logic directly testable without spinning up the server.

### 2. Ruleset path resolution

The loading helper should expose a small path-resolution function:

- `resolve_ruleset_path() -> Path`

Resolution order:

1. If `ACTR_RULESET_PATH` is set, use that path.
2. Otherwise, use the repository default:
   - `shared/ruleset/lab.yml`

The service startup path in `python/src/actr_harness/main.py` should use this resolution path indirectly by constructing `ProceduralMemory` with the default loading behavior.

This configuration entry is intentionally minimal. It exists only to make Task 1 usable with alternate rulesets during local development and future demos.

### 3. YAML-to-runtime mapping

The loading helper should expose:

- `load_rules(path: Path | None = None) -> dict[str, Rule]`

The loader reads the YAML document, iterates over `rules`, and converts each active rule directly into runtime objects.

Mapping rules:

- `Rule.id` comes from YAML `id`
- `Rule.utility` starts at `0.0`
- `ProceduralCondition.rule_id` comes from YAML `id`
- `ProceduralCondition.condition` comes from `condition.symbolic`
- `ProceduralCondition.semantics` is empty unless explicit condition semantics are added in a future ruleset
- `NeuroAction.rule_id` comes from YAML `id`
- `NeuroAction.commands` is built from `action.commands`
- `NeuroAction.semantics` is built from `action.semantics`

`action.commands` entries should be converted into `BufferOperation` values by preserving:

- `target_module_id`
- `command`
- `params` when present

The loader should not invent or transform rule meaning. It should behave as a direct translation layer between the YAML structure and the current protobuf-backed runtime objects.

### 4. Error handling

Task 1 should fail fast during startup if the ruleset cannot be loaded correctly.

The loader should raise a clear exception when:

- the resolved ruleset path does not exist
- the YAML file cannot be parsed
- the top-level payload is not a mapping
- the top-level `rules` key is missing or not a list
- a rule is missing `id`
- a rule is missing `condition.symbolic`
- a rule is missing `action.commands`

The service should not silently continue with an empty rules table when configuration or rule data is invalid.

Runtime behavior outside loading remains unchanged:

- `GetAllConditions` returns all loaded conditions
- `SelectRule` still raises when no applicable rule is available
- `LearnUtility` still updates only known rules

### 5. Service construction

`ProceduralMemory` should support normal startup without making callers manually load YAML first.

Recommended behavior:

- if rules are explicitly passed in, use them
- otherwise, load rules through the helper during initialization

That gives tests two options:

- inject a tiny in-memory rules dictionary when testing service behavior in isolation
- rely on the default loader when testing integration with the real `lab.yml`

### 6. Tests

Add focused tests in the Python test suite.

Rules-loader coverage:

- default path loading returns exactly the three active rules:
  - `rule-init`
  - `rule-memory-hit`
  - `rule-memory-miss`
- `rule-init` maps its symbolic condition and action commands correctly
- `rule-memory-hit` preserves both deterministic commands and semantic payloads
- `rule-memory-miss` maps the `push_subgoal` command correctly
- `ACTR_RULESET_PATH` overrides the default path

Service coverage:

- `ProceduralMemory` initializes with loaded rules
- `get_all_conditions()` returns three conditions with the expected `rule_id` values
- `learn_utility()` updates a loaded rule utility from the default baseline

The tests should stay small and deterministic. This task does not need networked gRPC integration tests.

## File Changes

Expected files to modify or create:

- Create: `python/src/actr_harness/services/procedural_rules.py`
- Modify: `python/src/actr_harness/services/procedural_memory.py`
- Modify: `python/src/actr_harness/main.py`
- Create: `python/tests/test_procedural_rules.py`
- Create: `python/tests/test_procedural_memory.py`

## Acceptance Criteria

Task 1 is complete when all of the following are true:

- the Python service loads active rules from `shared/ruleset/lab.yml` during normal startup
- `ProceduralMemory.get_all_conditions()` exposes those loaded conditions
- the initial utility for each loaded rule is defined and deterministic
- the ruleset path can be overridden with `ACTR_RULESET_PATH`
- unit tests verify the real three-rule `lab.yml` mapping and the service behavior

## Non-Goals

This task does not attempt to solve any of the following:

- host-side module registration
- embedding service implementation
- multi-step loop termination in the C# runtime
- logging and report generation
- later commented-out rules in `lab.yml`
