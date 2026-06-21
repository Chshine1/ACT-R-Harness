# Unified Action Representation in ACT‑R

## Overview

Brief restatement (2–3 sentences) of the problem: how to let rules issue actions that range from pure symbolic to pure
neural without brittle type switches.

## Why Not `oneof`

Explanation of the deliberate choice to avoid `oneof`, enabling mixed actions and graceful degradation.

## Design Principles

1. **Single container, dual purpose**  
   (explanation with reference to proto fields)
2. **No artificial separation**  
   (philosophy of mixing)
3. **Key conventions**
    - `command:` prefix
    - `neuro:` prefix
    - `meta:` prefix
4. **Recursive merging and resolution**  
   Detailed algorithm steps, framed with bullet points or numbered list, as originally described.
5. **Graceful degradation**  
   How pure‑symbolic, pure‑neural, and mixed steps are handled uniformly.

## How NeuroCore Processes a NeuroAction

A step‑by‑step walk‑through of the decoding algorithm (start with commands, iterate semantics, resolve lingering natural
language, produce BufferOperations).

## Examples

Each example in a clean format (maybe as JSON snippets with brief commentary):

- Pure symbolic step
- Pure neural step
- Mixed step (symbolic scaffolding + neural hint)
- Multi-command expansion from a single neural intent

## Benefits

Summarise the benefits originally listed (modularity, observability, evolution, language‑agnostic).

## Relationship to Other Components

How ProceduralMemory, NeuroCore, and Modules interact around this message.