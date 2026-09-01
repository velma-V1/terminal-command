# Stage 1 — Historical Terminal Core

This document records the original Stage-1 vertical slice. It is **not the current product boundary**. The feature branch subsequently completed the staged plan through capabilities, projects/workflows/checkpoints, engineering and daily packs, defensive security/web/remote adapters, and lifecycle/update/benchmark work.

For the current implemented boundary, install instructions, permission model, updater behavior, and limitations, use [`../README.md`](../README.md).

## Original Stage-1 contribution

Stage 1 established the authority model that remains intact:

```text
router/model -> proposes structured Action
policy       -> allow / require approval / deny
executor     -> performs approved action
history      -> records evidence
```

It introduced ordinary shell passthrough, deterministic natural-language routing, optional Ollama intent routing, native/WSL execution, SQLite evidence, slash completion, and Windows/Linux CI.

The important invariant survives all later checkpoints: **model output is not execution authority**. Typed capabilities, workflows, security adapters, remote adapters, and update operations still pass through deterministic system controls.

See [`superpowers/plans/2026-09-01-complete-product.md`](superpowers/plans/2026-09-01-complete-product.md) for the checkpoint implementation plan.
