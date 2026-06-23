# ACT-R Harness: A Neuro-Symbolic Agent Middleware

## 1. System Description

The ACT‑R Harness is a hybrid agent framework that couples a procedural rule engine with modular, stateful buffers and
an LLM‑powered semantic adapter. It treats the LLM not as the sole decision-maker, but as a translator between
ambiguous, high‑level concepts and the structured symbolic operations that the agent can safely execute.

### 1.1 Core Components

**Procedural Core (NeuroCore)**  
A gRPC service that evaluates production rules against current buffer states and decodes action intents into sequences
of buffer operations. It exports two main endpoints:

- `EvaluateConditions`: Given buffer snapshots and a set of procedural conditions (each with a rule ID, a symbolic
  condition tree, and an optional semantic hint), returns the set of rule IDs whose conditions are currently satisfied.
  Symbolic matching handles `and`, `or`, `not`, `exist`, and equality constraints. If a condition cannot be satisfied
  symbolically but carries a semantic hint, it is deferred to the LLM for fuzzy evaluation.
- `DecodeAction`: Given an action intent (concrete commands plus semantic supplements), current buffer states, and
  module command schemas, generates the final list of `BufferOperation` objects. Concrete commands are passed through
  unchanged; semantic hints of the form `command:<name>` or `neuro:<intent>` are sent to the LLM, which outputs
  additional structured operations that respect the allowed modules and parameter types.

**Modular Buffers**  
Typed, structured memory slots that represent cognitive modules:

- `perception&motor`: Current environmental snapshot (e.g., sensor readings) and motor entrypoint.
- `declarative memory`: The currently retrieved chunk from declarative memory (or empty).
- `goal&intention`: The active goal state (task, urgency, strategy, etc.).
- Additional buffers (e.g., `visual_array`, `motor`) can be added as needed.

**Declarative Memory (Module)**  
A content‑addressable store of chunks (facts). Each chunk carries activation computed from:

- Base‑level activation: $B_i = \log(\sum_j t_j^{-d})$, where $t_j$ is the time since the $j$-th reference.
- Spreading activation from buffers (e.g., current goal or perception slots boost related chunks).

**Semantic Adapter (LLM interface)**  
The LLM is invoked only for two purposes:

- **Condition evaluation**: Given a batch of unsatisfied symbolic conditions that have semantic hints, decide which are
  actually true based on the full buffer contents and declarative memory context.
- **Action decoding**: Translate partial command parameters and high‑level intents (e.g., “prevent an accelerating
  drop”) into concrete buffer operations that obey module schemas.

LLM calls are cached by hashing the prompt, eliminating duplicate work.

### 1.2 Information Flow

1. At each decision cycle, buffers are updated with the latest perception and any prior memory retrievals.
2. The procedural core evaluates all production rules. Symbolic matches are fast and deterministic; fuzzy matches are
   batched and resolved via LLM.
3. A conflict set of satisfied rules is produced. An external decision mechanism (e.g., utility‑based selection) picks
   one rule to fire.
4. The selected rule’s action intent is decoded: concrete commands are executed directly; semantic supplements are
   expanded by the LLM into additional operations.
5. Buffer operations are performed, potentially modifying the goal, storing new declarative chunks, or sending commands
   to actuators.
6. Rule utilities are updated based on the reward accumulated after firing.

---

## 2. Claims and Expected Advantages

From an AI engineering standpoint, the Harness addresses five key limitations of pure LLM‑based agents.

### 2.1 Long‑Context Memory via Activation‑Based Forgetting

Instead of feeding the entire history into a growing prompt window, facts are stored as declarative chunks. Activation
dynamics automatically surface only the most relevant items into the buffer. The LLM sees a small, fixed‑size set of
highly pertinent facts, avoiding quadratic token growth and the lost‑in‑middle problem.

**Expected gain**: Consistent recall over hundreds of steps while keeping per‑step LLM token consumption low and
bounded.

### 2.2 Full Decision Traceability

Every decision is accompanied by:

- Which rule fired and why (symbolic match or semantic hint accepted).
- The exact buffer states before and after the rule.
- The LLM’s translation output when semantic hints were used.

This white‑box trace enables debugging, auditing, and safety verification that natural‑language chain‑of‑thought alone
cannot provide.

### 2.3 Safe Guardrails with Soft Flexibility

Safety‑critical rules (e.g., “if level < 15, set pump to 100%”) can be expressed purely symbolically and executed
**without any LLM involvement**, eliminating hallucination risk. Non‑critical, flexible behaviors (e.g., “if the
situation resembles a past near‑failure”) are routed through the semantic layer, where the LLM operates within a
constrained action space (allowed modules, command schemas) and cannot emit arbitrary text.

**Expected gain**: Robustness against LLM errors while retaining the ability to interpret vague or novel conditions.

### 2.4 Structured Multi‑Agent Coordination

Because buffers are typed and shared, multiple agents can coordinate through:

- A shared declarative memory (blackboard) where facts propagate via spreading activation.
- Goal buffer alignment through explicit rule actions.
- Semantic negotiation: an LLM translates high‑level coordination messages into concrete buffer modifications.

This avoids the ambiguity and information loss of multi‑turn natural‑language conversations.

### 2.5 Sample‑Efficient Utility Learning

The agent learns only to select among a small set of production rules, not from a low‑level action space. Rules embed
substantial prior knowledge. Utility values (estimated reward minus cost) are updated by simple temporal‑difference
methods, converging in orders of magnitude fewer interactions than end‑to‑end RL.

**Expected gain**: Rapid adaptation to new tasks by adding/removing rules, with learning curves that are stable and
explainable.

---

## 3. Experimental Plan

To demonstrate the architectural advantages quantitatively, we propose a long‑horizon interactive task that stresses
memory, ambiguous instruction interpretation, and sample efficiency.

### 3.1 Task: Long‑Term Household Manager

**Environment**  
A simulated home with continuous time‑varying variables: temperature (room‑level), humidity, air quality, energy
consumption, device states (lights, HVAC, appliances), and occupancy. The agent receives a mixed stream of:

- **Sensor readings**: structured numeric/boolean values per room, updated each step.
- **User instructions**: interleaved exact commands (“turn off the living room light”) and fuzzy requests (“make the
  house more comfortable”, “the electricity bill seems too high lately, do something”).

Episodes last 200–500 steps. Events such as device failures, sudden weather changes, or unexpected visitors occur
stochastically.

### 3.2 Baselines

- **Baseline 1 – Pure LLM Agent (Full Context)**  
  At each step, the entire history of sensor readings and user instructions is concatenated into the prompt.
  Summarization heuristics are applied when the token limit is approached. The LLM outputs a natural‑language action
  that is parsed into device commands.

- **Baseline 2 – LLM + RAG (Vector DB)**  
  Historical observations are embedded and stored. At decision time, the agent retrieves the top‑k most similar past
  entries to augment the prompt, following standard retrieval‑augmented generation practices.

- **ACT‑R Harness (Proposed)**  
  Production rule set: ~20 purely symbolic rules (hard thresholds, basic routines) and ~6 semantic‑enhanced rules (for
  trend detection, analogy to past events, fuzzy instruction interpretation). Declarative memory stores percept
  snapshots, user preferences, and past actions. Goal buffer encodes current task and strategy.

### 3.3 Evaluation Metrics

| Metric                       | Description                                                                                                                 |
|------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| Task success rate            | Percentage of episodes where all critical variables stay within safe bounds and user instructions are eventually satisfied. |
| Fuzzy instruction fulfilment | Human rating (1–5) of how well ambiguous requests were addressed.                                                           |
| Long‑range consistency       | Number of contradictions or forgotten commitments (e.g., turning off a device that was explicitly left on).                 |
| Token consumption            | Average LLM input tokens per decision step.                                                                                 |
| Explainability score         | Expert rating of the agent’s decision trace.                                                                                |
| Adaptation speed             | Steps required to stabilize performance after a new rule is introduced (e.g., “activate energy‑saving mode”).               |
| Sample efficiency            | Number of environment interactions needed to reach a target performance level relative to an RL‑tuned LLM policy.           |

### 3.4 Expected Results

- The Harness will consume **70‑80% fewer LLM tokens** per step than the full‑context baseline, while maintaining equal
  or higher task success rates.
- It will exhibit **significantly fewer long‑range consistency failures**, because declarative memory preserves user
  preferences and past states independent of prompt truncation.
- Fuzzy instruction fulfilment will be comparable or better, as the semantic layer can translate vague requests into
  structured queries and action sequences that the pure LLM often glosses over.
- Explainability scores will be markedly higher due to the structured trace.
- Sample efficiency in utility learning will surpass that of fine‑tuning an LLM policy, with convergence in tens of
  episodes rather than thousands.

---

*This document describes the ACT‑R Harness architecture and its expected AI‑centric benefits. The proposed experiments
aim to produce publishable evidence that a neuro‑symbolic middleware can overcome fundamental limitations of monolithic
LLM agents in long‑horizon, interpretable, and safe autonomous systems.*

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