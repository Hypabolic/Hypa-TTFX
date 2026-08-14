# 0012 — Signals and the resize debounce

**Labels:** `enhancement`, `ready-for-agent`

## What to build

POSIX signal handling and the terminal-resize state machine, using `PosixSignalRegistration`
(BCL, AOT-clean, and confirmed to carry `PosixSignal.SIGWINCH` in .NET 10).

**Verify against an AOT-published binary, not `dotnet run`.** Ref-assembly presence is not
behavioral proof and the runtime's signal plumbing differs between hosts.

### Behaviors

- **SIGINT** → set a flag, unwind normally so teardown (cursor restore) runs; exit **1**, no
  message. `Cancel = true` so the runtime does not terminate first. **No inherited script
  tests this** — `sigterm_behavior.py` covers SIGTERM only. Write `sigint_behavior.py`.
- **SIGTERM** → teardown, then die *from the signal* so a supervisor sees signal death. The
  reference does `signal(SIGTERM, SIG_DFL); raise(SIGTERM)`. Whether `PosixSignalRegistration`
  can hand control back to the default action, and what status results, **must be established
  empirically** — do not plan it from documentation. If it cannot be matched, plan §1's
  "identical exit codes" goal is amended explicitly to carve out SIGTERM rather than the goal
  quietly outranking the finding.
- **SIGPIPE** → an accepted divergence: exit 0, not 141. But "catch `IOException` with `EPIPE`"
  is not an API — `IOException.HResult` after `Stream.Write` is not reliably errno 32, and a
  bare `catch (IOException)` would swallow disk-full and genuine write failures as success.
  Try `LibraryImport`ing `write(2)` and checking `Marshal.GetLastPInvokeError() == EPIPE`
  first; we already own the raw stdout path, so it is a small addition, and 141 is the one
  user-visible divergence a shell user would notice. Fall back to documenting it if not.
- **SIGWINCH** → the debounce state machine below. Registered **only when stdout is a tty** —
  SIGWINCH reaches every process in the foreground group regardless of where stdout points, and
  reacting under redirection would write a truncated run followed by a complete one.

### The resize debounce

`terminal.rs:622-640` is the specification, and the inherited `resize_behavior.py` is calibrated
to it (`first_delay=0.30`, `gap=0.05`). A port that rebuilds on the first signal strobes
through a window drag and fails the test:

1. A 50 ms quiet window that **restarts on every further SIGWINCH**
2. Then, in order: skip if `--ignore-terminal-dimensions`; skip if the queried dimensions are
   unchanged; skip if the recomputed layout is unchanged; only then rebuild.

On rebuild: wipe the area, park the cursor at its top (leave it hidden — showing it would
strobe through a drag), rebuild in place, and **carry the RNG state forward**. `--reuse-canvas`
governs only the first run.

### Threading

Handlers run on a **thread-pool thread**, not the animation thread. The reference's flags are
`AtomicBool`; the C# equivalents must be `volatile` or `Interlocked`, never plain fields, or an
optimized AOT build can miss the signal. The debounce timestamp is shared the same way.

Hold every `PosixSignalRegistration` in a static for process lifetime — one that goes out of
scope is a handler that silently stops firing mid-animation. Do not register twice for one
signal (.NET runs multiple registrations in reverse order); assert that.

Uses `--probe` (issue 0003) as the long-running fixture so signal behavior is tested in
isolation from any effect.

## Acceptance criteria

- [ ] `sigterm_behavior.py` passes, or the divergence is documented **and** plan §1's goal is
      amended explicitly
- [ ] A new `sigint_behavior.py` passes: teardown ran, cursor restored, exit 1, no message
- [ ] `resize_behavior.py` passes, including the debounce timing it is calibrated to
- [ ] All four suppression checks are implemented and individually tested
- [ ] RNG state continues across a rebuild rather than reseeding
- [ ] SIGWINCH and SIGTERM handlers are registered only when stdout is a tty; a redirected run
      gains no teardown bytes
- [ ] Broken pipe exits quietly; EPIPE is distinguished from other write failures, or the
      inability to distinguish is documented
- [ ] Signal flags are `volatile`/`Interlocked`; registrations are held for process lifetime
- [ ] All of the above verified against an AOT-published binary

## Blocked by

- 0003 — CLI parser core (for `--probe`)
- 0011 — First effect end-to-end: `wipe` (for the tty output path)
