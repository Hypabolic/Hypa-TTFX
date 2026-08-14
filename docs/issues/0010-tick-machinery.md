# 0010 — Tick machinery: motion, scenes, events, particles

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The engine that ticks — and the part of the port where the object-graph design differs most
from the Rust, so it carries the most design-specific risk.

Motion (`Waypoint`, `Segment`, `Path`), the scene machinery in `Animation`, the event system,
`ParticlePool`, the spanning-tree generators, `OrderedMap`, `ActiveCharacters`.

### Synchronous reentrant dispatch

Event actions execute **immediately at the emission point**, mid-`Path.step`, before the
coordinate is computed — so a `SET_COORDINATE` fired from a segment event is overwritten by the
move's own assignment. A deferred queue produces different frames from identical RNG draws.
**Never introduce one.**

Loop shapes are not interchangeable and must be classified per site:

- `for x in &v` / `for i in 0..v.len()` — length evaluated **once**; C# `for` over a captured count
- an explicit `loop` with a manual index re-reading state each pass — e.g. the segment walk at
  `ctx.rs:343-389`, which re-fetches `segments.len()` because a reentrant event may have
  replaced them; C# re-reads `.Count` each pass
- iteration over a snapshot taken before the walk (`active_characters` ticking)

`foreach` over a mutable `List<T>` throws on modification — that is a behavioral change, not a
safety net, and not a substitute for classification.

### Events: value keys, id targets

- `CallerKey` compares Scene and Path by **id string**, and Waypoint by **all fields** (id,
  coord, bezier controls) — faithfully reproducing upstream's frozen dataclass, including the
  quirk that two waypoints with identical fields in *different paths* collide.
- **Action targets stay string ids, re-resolved at dispatch** (`ctx.rs:252-262`). A reentrant
  callback can deactivate, replace, or recreate a path or scene between registration and
  dispatch; a retained object reference then operates on a detached object.
- **Callbacks keep an immutable `{ id, args }` record**, not delegates. Payloads are captured
  **by value** at registration (`burn.rs:178-185`, `synthgrid.rs:454-462`, both inside loops)
  and a C# lambda closes over the *variable*. Duplicate registration is a **structural**
  comparison of `EventAction` (`events.rs:168-183`).
- A C# positional `record` with an array member compares that array **by reference** — write
  `Equals`/`GetHashCode` explicitly over the whole `EventAction`.

### Quirks to reproduce

- `active_scene_is_complete()` returns **true** for looping scenes, and `SCENE_COMPLETE` fires
  **every tick** for them. Effects depend on both.
- `activate_scene` does **not** reset playback (resume semantics); `reset_scene` restores played
  + remaining frames in original order, zeroes `ticks_elapsed` and the easing step.
- Path re-activation **mutates the path**: synthetic origin segment from the current coordinate,
  previous origin distance subtracted and the new one added (rebase, not accumulation),
  `segments[0]` replaced or inserted (`ctx.rs:278`), `current_step`/`hold_time_remaining` reset,
  `max_steps` recomputed.
- Segment events key off the **end** waypoint, fire once per activation, and do not re-fire on
  backwards easing motion.
- `Path.step` deliberately allows `t > 1` / `t < 0` for overshooting easings, including
  travelling *past* the final waypoint via the for-else overshoot re-add; `_step_eased_scene`
  clamps instead. Both behaviors are copied.
- Synced-scene formulas (`STEP`/`DISTANCE`) with their `max(...,1)` guards and `round()`
  indexing; a missing active path jumps to the last frame and force-completes.

### Collections

Per `docs/translation-checklist.md` §4: `ParticlePool.available` is **LIFO** (`push_back` +
`pop_back`) — a `Queue<T>` reverses reuse order. `Scene.frames` is FIFO. `terminal.rs:409` uses
**both ends** by flag (the outside/middle alternate-pop interleave). `OrderedMap.Remove` must
preserve the order of remaining entries (`rings.rs:117` removes a path by key from a map that
is iterated elsewhere).

## Acceptance criteria

- [ ] `engine_traces.txt` state-machine traces pass
- [ ] `terminal_grouping` tests pass, including the destructive alternate-pop interleave and the
      grouped variants' exact bucketing
- [ ] Scripted traces cover: nested/reentrant events, scene reactivation-resume, looping scenes
      (complete-and-fires-every-tick), path holds, loop rebase, pool exhaustion and reuse
- [ ] **Reentrant path/scene replacement mid-dispatch** is covered — the case the frame-parity
      suite cannot reach and where retained references would break
- [ ] Duplicate registration with two **separately allocated but equal** payloads is rejected
- [ ] Waypoints with identical fields in different paths collide, as upstream
- [ ] `ParticlePool` reuse order is LIFO, pinned by test
- [ ] `OrderedMap` removal preserves remaining order; insert-over-existing keeps position
- [ ] No deferred event queue exists anywhere
- [ ] Every loop that can span an emission carries a comment naming its shape and the Rust line

## Blocked by

- 0008 — PyCompat helpers and the RNG
- 0009 — Easing, geometry, and goldens
