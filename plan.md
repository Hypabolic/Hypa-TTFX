# hypa-ttfx — C# / .NET port of ttfx

A port of [`ttfx`](https://github.com/…/ttfx) (itself a parity port of
[terminaltexteffects](https://github.com/ChrisBuilds/terminaltexteffects) v0.15.0, commit
`7a91dd9`) to C# on .NET 10, shipped as a single Native-AOT binary with **zero NuGet
dependencies**.

Reference checkout in this workspace: `~/Development/reference-implementations/ttfx`
(v0.3.1, ~22.3k lines of Rust). Its own design document is `plan.md` in that tree; this
document is deliberately structured against the same section numbering so the two are
diffable, and it **does not restate** the twenty Python-fidelity traps catalogued in the Rust
`plan.md §5` — those are already solved and readable in the Rust source, which is our source
of truth (§2).

---

## 1. Goals and non-goals

**Goals**

- **Byte-identical frame output to the Rust `ttfx` binary** given the same input, config, and
  `--seed`. This is a mechanically checkable contract, not an aspiration — see §7.
- **CLI compatibility**: identical effect names, option names, defaults, choices, exit codes,
  and stream routing (stdout vs stderr) as `ttfx`, which in turn matches upstream `tte`.
  Two exit-status carve-outs are already known: broken pipe (§8.2, decided) and possibly
  SIGTERM (§8.2, an M0 measurement that may amend this goal).
- All 37 effects.
- Single self-contained Native-AOT binary, no runtime install, no shared framework.
- **No external packages.** `PackageReference` count must be zero; everything comes from
  `Microsoft.NETCore.App`. This forces a hand-rolled CLI parser and a hand-rolled test
  harness (§4.5, §7.6) — both are accepted costs, and both are small next to the effects.
- macOS and Linux, x64 and arm64.

**Non-goals**

- Bit-identical randomness with *CPython*. Inherited from ttfx: the RNG is xoshiro256++, not
  Mersenne Twister. We match ttfx, and ttfx documents this divergence from Python.
- Matching Rust's exact *startup latency*. Native AOT starts in low single-digit
  milliseconds; the Rust binary claims 0.5 ms. Both are far under Python's ~65 ms, and both
  are imperceptible in a shell pipeline. Do not contort the design chasing the last 2 ms.
- Windows. The signal model, the ioctl-based terminal query, and the ANSI teardown dance are
  POSIX. A Windows target would be a separate piece of work.
- Python plugin effects; the TTE library API; wide-character (`wcwidth`) correctness — all
  non-goals upstream and in ttfx, and reproducing the one-codepoint-one-cell assumption is
  required for parity anyway.
- Micro-optimizations that exist in the Rust source purely for speed. See §5.8 for the
  explicit list of what may be dropped.

---

## 2. Source of truth

**Port from the Rust source, not from the Python.** The Rust tree has already resolved every
Python-semantics question (banker's rounding, integer-floor-division gradients, the truncated
bezier arc length, the doubled row deltas, the unclamped eased `t`, the `hex_to_xterm`
metric, looping-scene quirks, path-reactivation rebase, the input-parser error taxonomy,
`visible_top × visible_right` frame dimensions, …) and the resolutions are readable in
ordinary code with comments citing upstream line numbers. Re-deriving them from Python would
be weeks of duplicated archaeology.

Consult Python only when a Rust comment points at an upstream line and the intent is still
unclear.

Practical rules:

- Each Rust file maps to one C# file, same name, same order of members. `src/effects/beams.rs`
  → `src/Effects/Beams.cs`. Keep function names (PascalCased) and internal structure so a
  side-by-side diff is possible during review.
- Preserve Rust's comments. They are the porting notes; losing them loses the *why*.
- Where the Rust code carries a `plan.md §N` reference, keep the reference.
- Pin the reference: record the ttfx commit hash in `REFERENCE.md` at the repo root and in
  the parity harness. Upgrades are a diff of that checkout plus a re-run of §7.

Scale expectation: ~22k lines of Rust → roughly 25–30k lines of C# (C# is more verbose per
line for the config structs and the CLI table, less verbose where the borrow checker forced
indirection).

---

## 3. Project layout

Single solution, three projects, no packages.

```
hypa-ttfx/
  hypa-ttfx.slnx
  REFERENCE.md                    # pinned ttfx commit + upstream TTE commit
  Directory.Build.props           # shared AOT/lang settings, enforced zero-package rule
  bin/
    build                         # dotnet publish -c Release (AOT)
    test                          # everything CI runs, one command (mirrors ttfx bin/test)
    release
  src/Ttfx/
    Ttfx.csproj
    Program.cs                    # main.rs: arg dispatch, input, effect selection, run loop
    Cli/
      CliSpec.cs                  # option/arg spec types (table-driven, no reflection)
      CliParser.cs                # the parser: root options + subcommand + effect options
      RootOptions.cs              # cli.rs: the 15 TerminalConfig options + globals
      ValueParsers.cs             # argutils analogues: PositiveInt, Ratio, ColorArg, ...
      Completions.cs              # hand-written bash/zsh completion templates
      HelpFormatter.cs            # --help rendering
    Engine/
      ActiveCharacters.cs  Animation.cs  Canvas.cs  EffectCharacter.cs
      EngineWorld.cs       Effect.cs     EngineError.cs  Events.cs
      InputParser.cs       Motion.cs     Particles.cs    Terminal.cs
    Utils/
      Ansi.cs  Easing.cs  Geometry.cs  Graphics.cs  HextermTable.cs  Hexterm.cs
      OrderedMap.cs  PyCompat.cs  Rng.cs  SpanningTree.cs  Sorting.cs
    Platform/
      TerminalSize.cs             # ioctl(TIOCGWINSZ) via LibraryImport
      Signals.cs                  # PosixSignalRegistration wrappers
      StdIo.cs                    # raw byte stdout/stdin, no Console
    Effects/
      Common.cs  Beams.cs … Wipe.cs      # 37 effects + shared helpers
      EffectRegistry.cs           # static registry: name -> (spec, factory)
  tests/Ttfx.Tests/
    Ttfx.Tests.csproj             # plain console app, hand-rolled asserts (§7.6)
    Harness.cs
    EasingGoldens.cs  GeometryGoldens.cs  GraphicsGoldens.cs
    EngineTraces.cs   TerminalGrouping.cs  RngVectors.cs  OrderedMapTests.cs
    fixtures/                     # copied verbatim from the ttfx tree
  docs/
    translation-checklist.md      # enumerated trap sites, swept from the reference (§4.3, §5)
    ordering-inventory.md         # copied from ttfx, extended
  tools/
    check-prereqs.sh              # SDK, AOT pack, clang, linker, objcopy, python3, pty (§9)
    parity/
      cases.txt                   # copied from ttfx (177 cases) + our additions
      fetch_reference.sh          # clone ttfx at the pinned commit, cargo build --release
      reference.sh                # ref_dump / ref_m0 / ref_tty adapters (§7.2)
      rngdump.rs                  # dropped into the fetched checkout to export RNG vectors
      run_suite.sh                # ttfx binary vs hypa-ttfx binary
      tty_compare.sh              # needs a Rust-side pty launcher (§7.2)
      m0_matrix.sh                # generated 9×9 anchor matrix + inherited variants (§6)
      diff_frames.py              # first-divergence decoder (§7.4)
    tests/
      cli_corpus.sh  sigterm_behavior.py  sigint_behavior.py  resize_behavior.py  bench.py
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <!-- C# compiler warnings only. Do NOT let this escalate ILC/ILLink trim and AOT
         warnings, which would fail every publish on a clean tree; those are gated
         separately per §6, milestone by milestone. -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <InvariantGlobalization>true</InvariantGlobalization>
    <InvariantTimezone>true</InvariantTimezone>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <EventSourceSupport>false</EventSourceSupport>
    <MetadataUpdaterSupport>false</MetadataUpdaterSupport>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
  </PropertyGroup>
</Project>
```

`src/Ttfx/Ttfx.csproj` adds `<PublishAot>true</PublishAot>`, `<StripSymbols>true</StripSymbols>`,
`<OptimizationPreference>Speed</OptimizationPreference>`, `<AssemblyName>ttfx</AssemblyName>`.

Verified locally: SDK 10.0.400 is installed with the `Microsoft.NETCore.App.Runtime.NativeAOT.*`
pack. **Not** verified: that an AOT publish succeeds here — it would currently fail, because
`StripSymbols=true` needs `objcopy`/`llvm-objcopy` and this machine has neither. See §9 for the
full prerequisite list and the M0 decision.

**Zero-package enforcement**: `bin/test` greps every `.csproj` for `PackageReference` and
fails on any hit. Keep it that blunt.

Zero *packages* is not zero *prerequisites*, and the plan should not pretend otherwise.
Everything the product needs — CLI parsing, `Rune`, raw I/O, `ioctl` interop, signals,
entropy, stable sorting, the test harness — is reachable from the BCL. But Native AOT
additionally requires the **platform C toolchain** (clang + the system linker, plus
`build-essential`/Xcode CLT on the respective runners), and the harness still depends on
bash, python3, and pty tooling. `[LibraryImport]` is only AOT-appropriate in its
source-generated form — `static partial` methods on a `partial` type; a hand-written
`[DllImport]` falls back to runtime marshalling stubs.

---

## 4. Core architecture decisions

### 4.1 Object graph, not arena + IDs — the single biggest simplification

The Rust port's arena (`Vec<EffectCharacter>` addressed by `CharId`), the `EngineCtx`
god-object, the `CallbackId` + owned-`Payload` indirection, and the `EffectHooks` trait exist
**solely to satisfy the borrow checker**. `ctx.rs`'s own header says so. C# has none of those
constraints, so the port goes back to the shape of the Python original:

- `EffectCharacter` is a class holding direct references to its `Animation`, `Motion`, and
  `EventHandler`.
- `neighbors` / `links` hold `EffectCharacter` references.
- Event actions hold direct `Scene` / `Path` / delegate targets.
- `Terminal` owns the character list; effects hold a `Terminal` reference.

Net effect: `EngineCtx` disappears as a concept. What remains of it is an `EngineWorld` class
holding `Terminal`, `Rng`, `Clock`, `ActiveCharacters`, `PreexistingColorsPresent` — the
things that genuinely are process-wide engine state, passed to effects as one object. Every
stepping routine that Rust hoisted onto `EngineCtx` (`StepPath`, `MoveMotion`,
`StepAnimation`, `HandleEvent`, …) goes back onto the class that owns the data, exactly where
Python has it.

This removes several hundred lines of ceremony and makes the C# read closer to the Python
than the Rust does. **But it forfeits two things the arena gave for free — see 4.2 and 4.3.**

### 4.2 Synchronous, reentrant event dispatch — free, but still a contract

Python (and therefore ttfx) executes event actions **immediately at the emission point**,
mid-`Path.step`, before the coordinate is computed, so a `SET_COORDINATE` fired from a
segment event gets overwritten by the move's own assignment. A deferred queue produces
different frames from identical RNG draws.

In C# this is just a method call, and reentrancy is legal. So the contract is only:

- **Never** introduce a deferred/queued dispatch. Call the handler inline where the Rust
  calls `HandleEvent`.
- **Callbacks keep the `{ id, args }` record — this reverses the obvious simplification.**
  The tempting move is a plain `Action<EffectCharacter>` closure, matching Python. Two things
  in the Rust make that wrong:
  - **The payload is captured by value at registration.** `burn` registers a callback carrying
    `CallbackValue::Int(emission_id)` (`burn.rs:178-185`) and `synthgrid` one carrying a group
    number (`synthgrid.rs:454-462`), both inside loops. A C# lambda closes over the *variable*,
    not its value — and a `for`-loop variable is shared across iterations, so every registered
    callback would see the final value. (`foreach` variables are per-iteration since C# 5, so
    the bug appears only in some loop shapes, which is worse: it's inconsistent.)
  - **Duplicate registration is a structural comparison.** `EventHandler::push` rejects a
    duplicate by comparing `EventAction` values (`events.rs:168-183`, deriving `PartialEq`
    over the payload). Delegates compare by target+method, not by captured state, so two
    registrations that Rust rejects as identical would both be accepted.

  So: keep an immutable `EffectCallback` record with **hand-written** value equality,
  dispatched through a per-effect switch. This is one of the few places the
  borrow-checker-driven design was also the semantically correct one.

  Two traps inside the fix itself:

  - **A C# positional `record` does not give you this.** `record EffectCallback(int Id,
    CallbackArg[] Args)` compares `Args` by *reference*, so two separately-allocated equal
    payloads compare unequal and both get registered — the exact bug the record was meant to
    prevent. Write `Equals`/`GetHashCode` explicitly over the element sequence, and make
    `CallbackArg` a value type with real equality (note `Color`'s own quirk, §5.10).
  - **The whole `EventAction` needs Rust-compatible value equality, not just the callback.**
    The duplicate check compares the *action value* (`events.rs:132-142`, `:170-181`), so
    `ActivatePath("x")` registered twice must compare equal. That means…

- **…action targets stay IDs, not direct references.** This narrows §4.1's "direct references
  everywhere". Rust stores target ids as strings and **re-resolves them at dispatch time**
  (`ctx.rs:252-262`: `ch.motion.paths.get(path_id)`). Two reasons that matters:
  - a reentrant callback can deactivate, replace, or recreate a path or scene between
    registration and dispatch; a retained C# object reference then operates on a detached
    object while the character's map holds a different one;
  - reference equality would make two actions naming the same path id compare unequal,
    changing duplicate-registration behavior.

  So `EventAction` variants carry `string` ids and are resolved through the character's
  `OrderedMap` at the emission point, every time — same as the Rust. Only *characters*
  (which are never replaced) are held as direct references.
- Where a reentrant action can mutate the collection being iterated, the loop form matters and
  **must be classified per site — there is no blanket rule.** Rust has three shapes here and
  they are not interchangeable:
  - `for x in &v` / `for i in 0..v.len()` — the length is evaluated **once**, up front. The
    C# equivalent is a `for` loop over a *captured* count, not a live `.Count`.
  - an explicit `loop` with a manual index that re-reads state each pass — e.g. the segment
    walk at `ctx.rs:343-389`, which re-fetches `p.segments.len()` and the segment fields
    every iteration because a reentrant event may have replaced them. The C# equivalent
    re-reads `.Count` each pass.
  - iteration over a snapshot taken before the walk (`active_characters` ticking).

  A `foreach` over a `List<T>` throws `InvalidOperationException` on concurrent modification —
  that is a behavioral change, not a safety net, and it is *also* not a substitute for
  classification: a site that must skip newly-appended items and a site that must process them
  both compile fine and produce different frames. **Every loop that can span an event emission
  gets a one-line comment recording which of the three shapes it is and why**, checked at
  review time against the Rust line.

### 4.3 Ordering is behavior — and the arena is no longer enforcing it

In Rust, the arena slot index and `character_id` are *equal by construction* — every
`next_character_id += 1` is paired with an arena push, including for the orphans that later
get overwritten by cursor movement, popped as trailing whitespace, or cropped by the canvas
(`input.rs:143-175`, and the comment at `ctx.rs:97`). So "iterate the arena in order" and
"iterate in canonical order" were the same statement, and the ordering came for free.

**In C# they stop being the same the moment orphans are dropped.** A list of surviving
characters has indices that no longer match their ids. Every tick, sort, link, and render key
must therefore read the explicit `CharacterId` **field** — never a list position. This is the
one place where the object-graph choice silently removes a guarantee rather than just moving
it, so ordering becomes an explicit, tested contract.

`docs/ordering-inventory.md` in the ttfx tree is the authoritative list (79 lines, audit
marked complete across all 37 effects). **Copy it into this repo verbatim** and treat it as a
checklist. The rules:

| Site | Canonical order | C# representation |
|---|---|---|
| `active_characters` ticking | ascending `CharacterId` | `ActiveCharacters` (§5.8) — ordered set, snapshot to an array before ticking |
| render layer ties | `(layer, CharacterId)` | **stable** sort, or explicit two-key comparison |
| `EffectCharacter.Links` (spanning trees, `BreadthFirst`) | ascending `CharacterId` | `List<EffectCharacter>` kept sorted |
| `Motion.Paths`, `Animation.Scenes` | insertion order | `OrderedMap<T>` (§5.8) |
| `Terminal._input_colors_frequency` ties | insertion order | insertion-ordered `ColorFrequency` |
| `Gradient.BuildCoordinateColorMapping` result | insertion order | `CoordColorMap` |
| `character.Neighbors` | north, east, south, west | fixed-field struct |
| `Scene.FrameIndexMap` | index order | array |
| `PrimsWeighted` pending links | order-independent `min` | `SortedDictionary` |
| `GetCharactersGrouped` CENTER/OUTSIDE buckets | insertion order | list of buckets |
| middleout, unstable set iteration | ascending `CharacterId` | ordered snapshot |

**Ordering is not enough — the *operations* are semantics too.** An inventory of which
container is ordered says nothing about how elements enter and leave it, and several Rust
operations have C# lookalikes with different behavior. M1 owes a **source-level operation
map** alongside the ordering inventory, built by grepping the Rust for these constructs:

| Rust construct | Behavior | Wrong C# translation |
|---|---|---|
| `visible_characters.swap_remove(pos)` (`terminal.rs:336`) | removes by swapping in the **last** element — reorders the vector | `List.RemoveAt` shifts instead. **Not currently a frame bug**: the painter does not sort, it walks the list and keeps the max `(layer, character_id)` per cell (`terminal.rs:527-548`), so iteration order does not change the painted result. Use `RemoveAt`-with-swap anyway — it is the operation the Rust performs, and the moment anyone reimplements the painter as "sort then paint" the divergence appears |
| `ParticlePool.available: VecDeque<CharId>` (`particles.rs:44`) | acquires/returns at a specific **end** — reuse order is observable | `Queue<T>` (FIFO only) or `Stack<T>` (LIFO only) reverses particle reuse |
| `Scene.frames` / `played_frames: VecDeque<usize>` (`animation.rs:226-228`) | two deques with `append`, plus `reset_scene` restoring played+remaining in original order | any single-ended structure |
| `remove(0)` in effects | intentional FIFO drain | `List.RemoveAt(0)` is right; `Remove(item)` or a `Stack` is not |
| **`Vec::remove(i)` at an RNG-chosen index** — `spanning_tree.rs:87-94`, `errorcorrect.rs:386-389`, `unstable.rs:167`, `rain.rs:240-241` | removes at an arbitrary index and **shifts** the tail down | `RemoveAt` is correct here — but `Remove(value)` (removes the *first equal* element, not the indexed one) or a swap-removal changes which element every *subsequent* draw selects. These sites sit immediately after a `randrange`/`randint`, so a wrong removal desynchronizes the RNG-driven selection for the rest of the run |

Each site gets its C# counterpart named explicitly in the map before its file is ported.
The map must enumerate **every** removal in the Rust source with its index semantics — not
just the FIFO and swap cases, which are merely the ones easiest to spot by name. The
RNG-indexed removals are the ones that actually desynchronize output; `swap_remove` is
currently latent.

**This is done: see [`docs/translation-checklist.md`](docs/translation-checklist.md).** Every
trap class in §5 has been swept across the reference and its sites enumerated — 18 float→int
truncations, 47 `powf`/`hypot`/float-min-max calls, 17 exact float comparisons, 11 RNG-indexed
removals, 8 deques classified by which ends they use, 11 stable sorts, 8 `.chars()` sites.
The checklist carries the regeneration commands, so a reference version bump produces a diff
rather than a re-review.

Per-file porting reads the checklist first. Classification of what a site *means* still happens
when its file is ported — that needs the surrounding effect — but *finding* the sites is done.

**Event keys are compared by value, not by reference.** Rust's `CallerKey` compares Scene and
Path by their **id string**, and Waypoint by **all fields** — id, coord, and bezier controls
(`events.rs:30-49`, `events.rs:101-107`), which faithfully reproduces upstream's frozen
dataclass, including the quirk that two waypoints with identical fields in *different paths*
collide. Moving to an object graph does **not** give this for free: C# classes default to
reference equality, so holding direct `Scene`/`Path`/`Waypoint` references as event keys would
silently change which registrations match. Keep explicit value-typed key structs with
hand-written `Equals`/`GetHashCode` mirroring the Rust field sets, even though the *targets*
are now direct references and delegates.

Two more hard C# rules:

1. **`Dictionary<K,V>` iteration order is not contractual and breaks on removal.** It is
   allowed only for lookup-only data — never `foreach` over one whose order reaches output.
   Where the Rust uses `HashMap` for lookup only, `Dictionary` is fine; there are ~155
   `HashMap`/`HashSet`/`BTree*` sites in the Rust source and each needs a one-line
   classification during its file's port.
2. **`List<T>.Sort` and `Array.Sort` are unstable** (introsort). Rust uses `sort_by` (stable)
   at all 11 sort sites and `sort_unstable` at zero. So every ported sort must be stable:
   either `Enumerable.OrderBy` (stable, allocates) or a `Sorting.StableSort` helper that
   decorates with the original index. **Do not translate `sort_by` to `List.Sort`.** This is
   the single easiest way to introduce a silent frame divergence.

`character_id` allocation must still consume exactly as the Python/Rust parser does — ids are
allocated for characters later overwritten by cursor movement, popped as trailing whitespace,
or cropped by the canvas, so surviving characters have id *gaps*, and id-ordered iteration
downstream depends on the original allocation order. Keep the id an explicit field and keep
the counter's increment sites identical to the Rust parser's.

### 4.4 Effects: interface + static registry

```csharp
public interface IEffect
{
    void Build(EngineWorld world);
    // returns null when the animation is complete (Rust Option<String> / Python StopIteration)
    byte[]? NextFrame(EngineWorld world);
    // Rust `Effect: EffectHooks` (ctx.rs:82-84, effect.rs:11) — the engine calls this at the
    // emission point for a Callback action; the effect switches on callback.Id (§4.2).
    // Declare it now: without it the engine cannot dispatch, and the whole callback design
    // in §4.2 has no entry point.
    void DispatchCallback(EngineWorld world, EffectCharacter character, EffectCallback callback);
}
```

`EffectRegistry` is a static array of `EffectSpec` records — name, description, option
table, and a `Func<ParsedArgs, IEffect>` factory. No reflection, no assembly scanning
(both hostile to AOT and to startup time).

**The registry's enumeration order is observable.** `--random-effect` picks by
`rng.ChoiceIndex(names.Count)`; ttfx's list comes from clap's subcommand order, which is the
`EffectCommand` enum's declaration order — alphabetical, with `randomsequence` under `R`,
`print` under `P`. Reproduce the exact list from `src/effects/mod.rs` and pin it with a test
asserting the 37 names in order.

Also inherited: a randomly selected effect runs with **pure default** effect config —
effect-specific CLI args are ignored on that path.

### 4.5 Config and CLI — table-driven, hand-rolled

No `System.CommandLine` (a package), no reflection-based binding. The largest block of
mechanical work in the port: 37 subcommands × ~10 options each.

Design:

```csharp
sealed record OptionSpec(
    string Long, char? Short, string MetaVar, string Help,
    OptionArity Arity,            // Flag | One | AtLeastOne | Exactly(n)
    string? Default,
    Func<string, object> Parse);  // throws UsageError with argparse-shaped message
```

- `RootOptions` holds the 15 terminal options with ttfx's exact defaults
  (`--tab-width 4`, `--frame-rate 60`, `--canvas-width -1`, `--anchor-canvas sw`, …).
- Each effect ships `static OptionSpec[] Options` plus a config record with the same field
  names as the Rust config struct.
- **Integer widths are pinned to Rust's.** `i64` → `long`, `u64` → `ulong` (seeds are `u64`
  at `cli.rs:125`, `max_frames` likewise). Coordinates, counts, and delays are all `i64`;
  translating them to `int` changes overflow behavior, and `randint`'s `(b - a + 1) as u64`
  arithmetic differs outright. `List.Count` staying `int` is fine at terminal scale.
- **`Gradient`'s `steps_was_int` flag is a parser obligation, not just a constructor
  argument.** `graphics.rs:205-217` validates differently depending on whether `--*-steps`
  was a single scalar or a list, so the parser must thread "was this one int or many?"
  through to `Gradient.New`. Related: the shared `final_gradient_*` options have **per-effect
  default overrides**, so a single shared `OptionSpec[]` reused verbatim across 37 effects
  produces wrong defaults; merge shared specs with each effect's overrides (the Rust plan
  §4.5 hit the same problem).
- **The numeric grammars are not `double.Parse`.** Rust reaches these values through
  `str::parse::<f64>()` (`effects/common.rs:6-20`, `:47-64`, `:135-145`), whose accepted
  spellings differ from .NET's invariant parse — `inf`/`infinity`/`NaN` casing, leading `+`,
  underscores, whitespace handling, and overflow-to-infinity all need checking rather than
  assuming. Generate the acceptance/rejection corpus by running the *Rust* parser over a token
  list, then assert the C# parser agrees; don't hand-write a list of examples.
- Value parsers mirror `argutils`: `PositiveInt`, `NonNegativeInt`, `CanvasDimension` (≥ -1),
  `Ratio`, `ColorArg` (≤ 3 chars → xterm 0–255, else hex), `Anchor`, `EasingName`, and the
  odd ones the Rust plan flagged (laseretch's dual-type etch pattern, `nargs`-style
  multi-value options, negative-looking values).
- Parse order matters: root options **before** the effect name, effect options after. The
  parser is a two-phase scan, same as clap's model. `cli_corpus.sh` tests this.
- **A real inherited case already breaks a naive parser.** `cases.txt:2` contains
  `--beam-row-symbols - =` — a multi-value option whose first value is a lone `-`. A parser
  that treats a leading `-` as an option marker, or that stops an `AtLeastOne` scan at the
  first dash-prefixed token, rejects a case the suite requires to pass. Specify and test:
  lone `-`, `--` as a terminator, negative numbers as values, option-*looking* symbol values
  (`.`, `:`, `=`), repeated options, and clap's exact stopping rule for variadic values.
  **These tests land in M0/M1 with the parser, not in M6** — the parser is new code with no
  clap underneath it, and every later phase runs on top of it.
- **Exit 2** on usage errors (argparse/clap convention), **exit 1** on runtime errors.
- `--print-completion bash|zsh`: ttfx generates these from the clap model via
  `clap_complete`. With no package, these become **two hand-written script templates** driven
  by the registry (effect names + option names). Checked: `cli_corpus.sh` asserts *nothing*
  about completion output, so this divergence is free — but it also means completions are
  currently untested on both sides. Add our own case: the script is non-empty, sourceable
  under `bash -n` / `zsh -n`, and mentions all 37 effect names. Byte-parity with clap's
  generator is not a goal.
- Help formatting is not byte-parity either; option *surface* is.

### 4.6 RNG — the parity keystone

`Utils/Rng.cs` reimplements `src/utils/rng.rs` bit-exactly. It is pure integer arithmetic and
translates directly:

| Rust | C# |
|---|---|
| `u64` state `[u64; 4]` | `ulong[4]` / four `ulong` fields |
| `wrapping_add`, `wrapping_mul` | `unchecked(a + b)`, `unchecked(a * b)` (C# default) |
| `rotate_left(n)` | `BitOperations.RotateLeft(x, n)` |
| `leading_zeros()` | `BitOperations.LeadingZeroCount(x)` |
| `>> 11` on u64, `* (1.0/(1<<53))` | identical on `ulong`/`double` |

Semantics to preserve exactly (the Rust file calls them "the parity contract"):
SplitMix64 seed expansion; xoshiro256++ `next`; `randbelow` by bit-mask rejection;
`randint(a,b)` inclusive; `randrange(a,b)` half-open; `choice = seq[randbelow(len)]`;
`uniform(a,b) = a + (b-a)*random()`; `shuffle` = Fisher–Yates from the top
(`for i in reversed(range(1, len)): j = randbelow(i+1); swap`).

Two things that are easy to get wrong:

- **`Rng` is an instance on `EngineWorld`, never a static.** `main.rs` carries the RNG
  *forward* across a terminal-resize rebuild (`rng = ctx.rng`) — the stream continues rather
  than resetting. Observable, and the resize behavior test will catch it late if missed.
- Unseeded runs read 8 bytes from `/dev/urandom` in Rust. C#: `RandomNumberGenerator.Fill`
  (BCL, AOT-clean) is fine — unseeded output is not compared.

**M1 deliverable**: `RngVectors.cs` asserting the first 10k draws of each helper against
reference vectors. Get this green before anything else; every downstream parity failure is
otherwise indistinguishable from an RNG bug.

**The vectors cannot be dumped from the shipped binary** — `next_u64` and `randbelow` are
private (`rng.rs:40`, `rng.rs:59`) and there is no RNG-dump flag. Options, in order of
preference:

1. Have `fetch_reference.sh` (§12, item 2 — moved into M0) drop a tiny `examples/rngdump.rs` into the fetched ttfx
   checkout and `cargo run --example rngdump`. The public helpers (`random`, `randint`,
   `randrange`, `choice`, `uniform`, `shuffle`) are reachable from an example; `next_u64` and
   `randbelow` are exercised indirectly through them, which is what actually matters.
2. Failing that, commit a generated fixture with the generator script alongside it.

Cover the **rejection loop** explicitly — `randbelow` retries when the masked draw exceeds `n`,
so ranges that are not powers of two (e.g. `randint(0, 2)`) consume a variable number of
`next_u64` calls. A test that only samples `random()` sequentially will not catch a wrong
mask width, and a wrong mask width desynchronizes every effect.

### 4.7 Clock injection

Unchanged from ttfx: `Clock` is `Real` (wall + monotonic from a `Stopwatch` + a captured
Unix epoch) or `Virtual` (advances a fixed `dt` per emitted frame). matrix reads wall time,
thunderstorm reads monotonic. The hidden `--virtual-clock` and `--parity-dump` flags select
the virtual one, and the parity harness depends on it.

**`dt` is not simply `1/frame_rate`.** `ctx.rs:50-52` substitutes `1/60` whenever the frame
rate is nonpositive — and the oracle contract (§7.1) runs at `--frame-rate 0`, so this is not
an edge case, it is *the* case the parity suite exercises. A literal translation divides by
zero (giving ∞ or a frozen clock, depending on how it's written) and changes the frame count
of both clock-budgeted effects. Transcribe the guard.

**And the clock advances inside `frame()`, not once per `NextFrame` return.** `ctx.rs:697-702`
puts `advance_frame()` in `EngineCtx::frame`, alongside frame-rate pacing. That distinction is
observable because an effect can call `frame()` more than once while producing a single
returned frame — `unstable.rs` has five `ctx.frame()` call sites (`:382`, `:397`, `:433`,
`:437`, `:470`) with fall-through paths that discard the earlier string but *keep* its clock
advance. Hoisting the advance into the C# run loop — the obvious reading of "advances per
emitted frame" — desynchronizes virtual time the first time this happens, and the effect that
notices is matrix or thunderstorm several phases later.

`Stopwatch.GetTimestamp()` / `Stopwatch.Frequency` for monotonic. For wall time, match the
Rust shape at `ctx.rs:43-47`: capture the epoch **once** as *fractional* seconds and add
high-resolution monotonic elapsed time to it thereafter. Do **not** derive it from
`ToUnixTimeMilliseconds()` — millisecond truncation can move matrix's rain-phase transition
across a boundary. Do not use `Environment.TickCount64` (resolution) or re-read wall time per
call (the Rust reads it once).

---

## 5. C#-specific fidelity traps

The twenty Python traps in ttfx `plan.md §5` are already handled by the Rust code we are
transcribing. These are the *new* ones — places where a natural C# translation of correct
Rust silently diverges.

### 5.1 `char` is not a codepoint

Rust `str::chars()` yields Unicode scalar values; C# `string` is UTF-16 and `foreach (char c in s)`
yields *code units*, splitting non-BMP characters into surrogate pairs. TTE/ttfx treat one
codepoint as one cell. Every one of the 8 `.chars()` sites and the input parser must use
`s.EnumerateRunes()` / `Rune`, and `input_symbol` must be a string (a `Rune`'s UTF-16 form can
be two chars).

`Rune` alone does not finish the job. Two sites convert a codepoint to a *number*, and they are
the concrete traps:

- **`binarypath.rs:158-160`**: `symbol.chars().next() as u32` formatted `{:08b}` — the first
  Unicode *scalar value* becomes the character's binary representation. Taking a UTF-16 `char`
  here yields the high surrogate for astral input, so the rendered binary string is wrong.
  Must be `Rune.Value`.
- **`swarm.rs:111-114`**: `c.to_digit(10)` on the first character of a path id. C# has no
  direct equivalent — write the helper with Rust's semantics (ASCII digits only, `None`
  outside range), not `char.GetNumericValue` (which accepts Unicode digit forms and returns
  `double`).

More generally, Rust `char::is_ascii_digit` / `is_whitespace` / `is_alphanumeric` are not
interchangeable with `Rune.IsDigit` and friends — the Rust ASCII-scoped predicates have
different membership than the Unicode ones. Match per site.
- **Culture sensitivity is a parity bug waiting to happen**, and it reaches further than
  `Parse`/`ToLower`:
  - `ToLower()`/`ToUpper()`/`double.Parse`/`int.Parse`/`ToString()`/`$"{x}"` are all
    culture-sensitive by default. Use invariant everywhere, explicitly.
  - **`StartsWith`/`EndsWith`/`IndexOf(string)` are culture-sensitive too**, and the input
    parser dispatches CSI sequences with `starts_with("\x1b[")` (`input.rs:63`). Every
    byte-oriented compare needs `StringComparison.Ordinal`. `InvariantGlobalization=true`
    makes most of this safe by accident — do not rely on it here.
  - **`long.Parse`/`double.Parse` accept surrounding whitespace; Rust's `str::parse` does
    not.** `--tab-width ' 4 '` would be accepted by a naive C# parser and rejected by ttfx.
    Use `NumberStyles` that exclude whitespace, and cover it in the parser corpus (§4.5).

### 5.1a "Strict UTF-8" is not what the obvious C# APIs do

Rust's `String::from_utf8` **rejects** invalid bytes (`main.rs:19-24`), and §8.3 requires a
decode failure to print a message and exit 1. But `Encoding.UTF8.GetString`,
`File.ReadAllText`, and `Console.InputEncoding` all **replace** invalid bytes with U+FFFD and
never throw. A lossy decode makes the malformed input succeed and then animate garbage.

Use `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)` for
both the file path and stdin, and catch `DecoderFallbackException`. This is not theoretical:
`cli_corpus.sh:33` already ships a `bad-utf8-file` case asserting exit 1, so a lossy decode
fails an inherited test — but only if that test is run, which is why this belongs in §5 and
in M0 rather than surfacing during M6 polish.

### 5.1b Hex colour parsing has two quirks

`hexterm.rs:74-79`: `is_valid_hex_color` accepts a stripped length of **6 or 7** — seven-digit
hex is valid, and `parse_rgb` then uses the first six (`hexterm.rs:20-26`, `:131-133`).
`Convert.FromHexString` throws on odd-length input, so it cannot be used here.

The two `#`-stripping calls also differ deliberately: the length check uses
`trim_start_matches('#')` (leading only), the radix parse uses `trim_matches('#')` (both
ends). Transcribe both, and note no inherited fixture covers either — add cases.

### 5.2 `.len()` is bytes

Rust `str::len()` is the **byte** length. Mechanically translating it to `String.Length`
(UTF-16 code units) is wrong for any non-ASCII string. Audit all of them:
`parse_color`'s `s.len() <= 3` is ASCII-safe by construction; the frame-length prefix in
`--parity-dump` and the `FormattedSymbol` capacity check are genuinely byte counts and must
stay byte counts. Building frames as `byte[]` (§5.7) makes most of these disappear.

### 5.3 Stable sorts

Covered in §4.3 rule 2, repeated here because it is the highest-risk mechanical trap:
`sort_by` (stable) → `OrderBy` or `Sorting.StableSort`, never `List.Sort`.

### 5.4 Dictionary iteration order

Covered in §4.3 rule 1. `Dictionary<K,V>` for lookup only.

### 5.5 Floating point

- Use `double` everywhere Rust uses `f64`. Never `float`.
- Transcribe expression order and grouping literally — the Rust source already preserves
  Python's. Do not let a "simplification" reassociate anything.
- **Do not substitute an "equivalent" function for a slower one.** Three specific bans, all
  present in the source and all tempting to clean up:
  - `powf(2.0)` → **not** `x * x`; `powf(0.5)` → **not** `Math.Sqrt`. Both appear in
    `geometry.rs:84-88` and `geometry.rs:233-234`. `Math.Sqrt` is correctly rounded and
    `Math.Pow(x, 0.5)` is not, so they differ in the last ulp on some inputs — and these feed
    coordinate quantization, where a last-ulp difference at a `.5` boundary flips a cell.
    Use `Math.Pow` with the same exponent literal.
  - `f64::hypot` (`geometry.rs:200-206`) → **not** `Math.Sqrt(x*x + y*y)`. `hypot` avoids
    intermediate overflow/underflow and has different rounding. Use `double.Hypot` (.NET 7+).
  - `f64::max` / `f64::min` (`animation.rs:588-613`, `ctx.rs:411`, `easing.rs:306`'s
    `.min(1.0).max(0.0)` clamp, `spotlights.rs:229`'s `fold(f64::INFINITY, f64::min)`)
    → **not** `Math.Max`/`Math.Min`.
    Rust's return the **non-NaN** operand when one is NaN; .NET's **propagate** NaN. They also
    differ on signed zero. Write `PyCompat.FMax`/`FMin` with Rust's semantics and use them at
    every float site. (Integer `Math.Max`/`Min` are fine.)
- C# `double` on x64/arm64 under RyuJIT/ILC uses SSE2/NEON double ops; no x87 excess
  precision, no automatic FMA contraction. Basic ops (+ − × ÷ √) are bit-reproducible.
- `Math.Sin/Cos/Pow/Exp/Atan2`: **there is no .NET guarantee of bit-identical transcendental
  results with Rust's**, on any platform. It is *plausible* that both route to the same
  platform libm on the same machine, but that is an implementation detail of both runtimes,
  not a contract. Treat it as an open measurement (§7.7) resolved per RID, not as a fact.
  Quantization to integer coordinates and hex colors absorbs almost all ulp noise; what it
  does not absorb is a value sitting exactly on a rounding boundary, which is what the
  fine-grained easing/geometry goldens exist to surface.
- `Math.Round(double)` defaults to `MidpointRounding.ToEven`, which *is* Python's banker's
  rounding — so `PyCompat.RoundHalfEven` is a thin wrapper. But "thin wrapper" is not "no
  test": pin it against the Rust helper across NaN, ±∞, magnitudes beyond `long`, negative
  zero, and exact-half cases, where the two may legitimately differ (Rust's helper returns
  `i64`, so its out-of-range behavior is saturating, and .NET's is not the same code path).
- Integer floor division still needs `PyCompat.FloorDiv` — C# `/` truncates toward zero, like
  Rust's, and neither matches Python's `//` for negative operands. The Rust source already
  calls the helper at every site; keep them one-to-one.
- **`as i64` on a float is truncation toward zero, and it is everywhere.** This is the most
  widespread unlisted trap in the port. It is *not* `Math.Round` (banker's), *not*
  `Convert.ToInt64` (which rounds), and *not* `(int)` on a value that may exceed `int`. Sites
  include `errorcorrect.rs:381`, `binarypath.rs:354`, `spray.rs:222`, `random_sequence.rs:72`,
  `orbittingvolley.rs:158-168,368`, `matrix.rs:165`, `fireworks.rs:413`, `bouncyballs.rs:187`,
  `colorshift.rs:167`, `geometry.rs:88` (`powf(0.5) as i64` — truncation, *not*
  `round_half_even`), and `graphics.rs:361` (Python's `int()` truncation, deliberately
  different from `round` at that site). One of these silently rounding instead of truncating
  changes a count, which changes the number of RNG draws, which desynchronizes the rest of
  the run.
  Add `PyCompat.TruncToI64(double)`, use it at every site, and put `as i64` in the M1 grep
  list alongside the collection operations.
  Special case: `easing.rs:356-357` is `as i64 as usize` — a negative eased value truncates
  to a negative `i64` and then *wraps* to a huge `usize`. C# `(long)` then `(int)` or a
  `checked` context does something else entirely. Transcribe the two-step cast deliberately.
- `(long)someDouble` saturates on .NET Core 3.0+ (matching Rust's `as` saturation), but write
  the intent explicitly where it matters.
- **Do not "clean up" a float equality comparison with an epsilon.** `animation.rs:593-615`
  branches on `max_val == min_val`, `max_val == normalized_red`, and `saturation == 0.0` in
  the HSL conversion. An epsilon flips the hue branch, and the result feeds
  `round_half_even(channel * 255)` — a visible colour change, not a rounding wobble.

### 5.6 Exceptions vs `Result`

Rust returns `Result<T, EngineError>` for the parser and a handful of validators. C# port:
a single `EngineException` hierarchy (`UnsupportedAnsiSequenceException`, `EngineException`)
thrown at the same sites, caught in `Program.Main`, mapped to the same messages/streams/exit
codes. Do **not** convert error conditions into clamping or defaults — the Rust plan's §5
item 18 specifically requires `Gradient.GetColorAtFraction` and
`FindNormalizedDistanceFromCenter` to *reject* out-of-range input rather than clamp.

**Panics are a separate category and are not covered by this.** The Rust release profile sets
`panic = "abort"` (`Cargo.toml:17`), and the source carries `assert!`, `unwrap`, and `expect`
on invariant violations — in the RNG (`rng.rs:59-85`: `randbelow(0)`, empty `choice`, inverted
`randint` range), terminal sizing (`terminal.rs:516-519`), and engine state. On those paths
ttfx dies from `SIGABRT` with no orderly teardown; a C# port that lets the equivalent surface
as an ordinary exception exits 1 with a stack trace and a *restored cursor*. Decide the mapping
explicitly:

- Invariant failures get their own `EngineInvariantException`, distinct from `EngineException`.
- `Program.Main` does **not** catch it. Let it escape, which on .NET produces a crash with a
  non-zero status distinct from the exit-1 error path.
- Accept and document that the exit status differs from Rust's 134; what must match is that
  invariant failures are *not* silently converted into a graceful error or a clamped value.
- Add unit tests asserting each invariant site throws rather than returning a plausible value —
  a `randbelow(0)` that quietly returns 0 is a divergence the parity suite may never reach.

### 5.7 Output is bytes

Rust builds a `String` and writes UTF-8. C# should build directly into a pooled
`byte[]` / `ArrayBufferWriter<byte>` and write to the raw stream from
`Console.OpenStandardOutput()`. Reasons:

- `Console.Write` on .NET does encoding, `TextWriter` synchronization, and autoflush — it is
  the single easiest way to make this port slow.
- The `--parity-dump` length prefix is a **byte** count; building bytes makes it trivial.
- The frame string is rows joined `"\n"`, top row first, with SGR order
  bold, italic, underline, blink, reverse, hidden, strike, fg, bg, symbol, `\x1b[0m` —
  and frame dimensions are `visible_top × visible_right`, not canvas dimensions. All of that
  is in `terminal.rs`; transcribe it.

Reuse one output buffer across frames (ttfx does, via `recycle_output_string`) — with a GC
this matters more, not less.

### 5.8 Rust perf hacks: what to keep, what to drop

The Rust source contains optimizations that are **not** semantics. Porting them faithfully
would produce unidiomatic, harder-to-audit C# for no gain. Explicit ruling:

| Rust mechanism | Semantic half (**keep**) | Representation half (**drop**) |
|---|---|---|
| `ActiveCharacters` bitmap/sparse promotion | ascending-`CharacterId` iteration order | the packed-word bitmap and PROMOTE/DEMOTE thresholds → `SortedSet<int>` keyed by id, or a `List` + sorted snapshot |
| `OrderedMap` `Rc<str>` key sharing + index threshold | Python-dict insert/overwrite/iteration semantics | `Rc` pointer-equality fast path, `INDEX_THRESHOLD` → `List<KeyValuePair>` + `Dictionary` index, always |
| `EventHandler` FNV fingerprints + `subscribed` bitmask | registration-order action lists, duplicate-registration error | fingerprint short-circuit (keep the bitmask; it's four lines and it genuinely pays) |
| `FormattedSymbol` inline 63-byte buffer | the precomputed-on-visual-change caching | the inline/heap union → cached `byte[]` |
| `mem::forget` teardown skip | — | entirely (GC, no destructors doing work) |
| string recycling | — | keep as buffer reuse (§5.7) |

Rule of thumb: if a Rust comment explains a *speed* reason, the C# may differ; if it explains
a *behavior* reason, it may not.

### 5.9 `foreach` over a mutating collection

See §4.2, which classifies the three Rust loop shapes. The short version: C# throws where
Rust's index loop silently continues, so `foreach` over a mutable `List<T>` is never the
translation — but which `for` shape replaces it depends on whether the Rust site captures the
length once or re-reads it, and that must be read off the Rust source per site rather than
assumed.

### 5.10 Struct vs class for `Coord`

`Coord` is `Copy` in Rust and used as a dictionary key. Make it a `readonly record struct` with
explicit `Equals`/`GetHashCode`; it is on the hottest paths in the engine. Same for
`ColorCode`. `Color` keeps ttfx's quirk that equality is on the **original argument**
(`Color(255) != Color("ffffff")`) — needed for `input_colors_frequency` keying.

---

## 6. Porting strategy and phases

Transcription, not reimagination. Each phase lands with its verification green before the
next starts.

**A note on the M0/M1 boundary.** M0's exit criterion is `--m0-dump` byte-identity across the
option matrix, and that matrix includes `--xterm-colors`, `--no-color`,
`--existing-color-handling always` and the colour-bearing input fixtures. Reaching it requires
`Color`/`ColorArg` equality, SGR formatting, `hex_to_xterm`, and `existing_color_handling`
(which applies at *parse* time in the `Always` mode, `input.rs:169-172`) — all listed below
under M1. The boundary is therefore drawn by *what the frame needs*, not by file:
**M0 = parse + canvas + character visual + renderer**, and it pulls `Graphics`, `Hexterm`, and
the static half of `Animation` (CharacterVisual, SGR assembly) forward with it. **M1 = the
things that tick**: `Rng`, `Clock`, `Easing`, `Motion`, `Events`, `Particles`, `SpanningTree`,
and the scene machinery. M0 is the largest phase in the plan; treating it as "skeleton" is how
it gets underestimated.

- **M0 — parse, canvas, visuals, renderer, platform.**
  Project scaffold + AOT publish working; `Platform/` (terminal size, signals, raw stdio);
  hand-rolled CLI parser with the 15 root options; stdin/file input with strict UTF-8;
  the ANSI input parser; `Canvas` + anchoring; fill characters; neighbors; the renderer;
  the tty prep/frame/restore dance.
  **Exit criterion**: `--m0-dump` output byte-identical to the Rust binary's across the
  option matrix. Note the inherited `m0_matrix.sh` is **not** the full cross-product it looks
  like — it is 14 hand-picked variants (`m0_matrix.sh:29-44`) touching only the `c`, `ne`, and
  one mixed `n`/`se` anchor pair, with no cross-product between anchoring and the colour or
  wrap options. Since anchoring is precisely where the Rust plan's §5 item 16 says every frame
  gains leading blank rows/columns, **generate the real matrix here**: all nine
  `--anchor-canvas` × nine `--anchor-text` combinations, crossed with clipped and unclipped
  canvas sizes, and keep the 14 inherited variants as the option-interaction cases on top.
  Also in M0, as their own tracked items because they must be settled empirically, not from
  memory: the `TIOCGWINSZ` constant per platform, which fd is queried, and the SIGTERM /
  SIGINT / SIGWINCH exit-status behavior (§8). The signal and resize tests need something that
  runs long enough to be signalled, and no effect exists yet — so M0 also ships a no-op probe
  that emits blank frames forever, as the harness's stand-in until M2's `wipe` and afterwards
  as the isolated signal-behavior fixture. **It must be a hidden root flag (`--probe`), not an
  `EffectRegistry` entry**: `--random-effect` selects by `rng.ChoiceIndex(names.Count)` over
  the registry (§4.4), so a 38th name would change every random selection for a given seed and
  break parity with ttfx. The M0 registry test asserts the count is exactly 37 and that the
  probe is absent.
  Add the non-BMP/combining-mark input fixture here too (§7.3) — M0 is where the parser lands.
  **`tools/parity/fetch_reference.sh` also lands in M0, not M2.** M0's own exit criterion is a
  byte comparison against the Rust binary, and M1's RNG vectors are generated from the fetched
  checkout — so every phase from M0 onward depends on it. Scheduling it with the rest of the
  harness in M2 was a dependency inversion.
  M0 also owns three things previously scattered or missing: the hidden `--parity-dump`,
  `--virtual-clock` and `--max-frames` flags (M2 cannot run without them); the **resize
  debounce state machine** (§8.2 — it is 20 lines of `terminal.rs` and the inherited test is
  calibrated to it); and a test asserting the registry lists exactly the 37 effect names.
  The CLI parser's token-edge tests (§4.5) land here too, with the parser.

- **M1 — the engine that ticks.**
  `Rng`, `Clock`, `Easing` (31 named + `MakeEasing` Newton–Raphson), `Motion`, `Events`,
  `Particles`, `SpanningTree`, the scene machinery in `Animation`, `ActiveCharacters`.
  (`PyCompat`, `Geometry`, `Graphics`, `Hexterm`, `OrderedMap` and `CharacterVisual` came
  forward into M0 — see the boundary note above.)
  **Exit criteria**: (a) the ttfx golden fixtures — `easing_goldens.bin`,
  `geometry_goldens.txt`, `graphics_goldens.txt` — consumed verbatim as *data*, asserted with
  ttfx's own tolerance schedule (§7.6), and run against an **AOT-published** binary rather
  than `dotnet run`;
  (b) `engine_traces.txt` state-machine traces green; (c) `RngVectors` green;
  (d) `docs/ordering-inventory.md` copied in and every row assigned a C# representation;
  (e) **the transcendental measurement** (§7.7) — easing at 1e-3 steps and the geometry
  lattice dumped from both binaries and compared at ttfx's own tolerances, **per RID**,
  resolving where byte-exact parity CI can run.
  (The source-level operation map is no longer an M1 deliverable — it is done and lives in
  `docs/translation-checklist.md`.)

- **M2 — parity harness + first effect.** §7: the `reference.sh` adapter, the Rust-side pty
  launcher and the differ (the reference fetch/build itself lands in M0). Faster than ttfx's M2 because
  there is no shim, no pinned CPython, and no shim audit — but *not* "days": three reference
  drivers and a pty launcher are real work, and this is where cross-effect RNG divergence
  first has to be diagnosed with no prior tooling.
  **Exit criterion**: `wipe` — ported here rather than in M3, since M2 cannot demonstrate an
  end-to-end byte-identical stream without one real effect — passes its parity cases at both
  seeds.

- **M3 — effects wave 1** (motion + scene basics, ~11 remaining small effects): randomsequence,
  expand, slice, scattered, pour, slide, middleout, spray, rain, bouncyballs, errorcorrect.

- **M4 — effects wave 2** (gradients, synced scenes, sequence easers, no-motion effects, 18):
  colorshift, highlight, sweep, waves, decrypt, print, overflow, unstable, crumble, blackhole,
  swarm, spotlights, fireworks, bubbles, beams, rings, orbittingvolley, binarypath.

- **M5 — effects wave 3** (heavy machinery, 7): burn + smoke (spanning trees), laseretch
  (backtracker + particles + bezier), vhstape, synthgrid, matrix, thunderstorm.

- **M6 — CLI completion + polish.** `--print-completion`, `--version`, `--random-effect`
  filtering, error paths and exit codes, `cli_corpus.sh` green, README. Plus the two
  new parity cases from §7.3 (random-effect selection order, RNG continuity across a resize)
  and the completion-script case from §4.5 — none of which exist in the inherited harness.

**AOT publish is not a one-time M0 checkbox.** A skeleton publishing cleanly proves almost
nothing: the AOT-sensitive constructs — `Rune`, generated P/Invoke, delegate dispatch, generic
containers, any LINQ, signal registration, and 37 effect factories — arrive across M1–M5. Run
the publish *and* the AOT analyzers (`IlcTrimMetadata` warnings, `EnableAotAnalyzer`,
`EnableSingleFileAnalyzer`) at every milestone boundary, with warnings as blockers. Catching a
trimming problem at M5 is far more expensive than at M1.

- **M7 — release engineering.** AOT publish for `osx-arm64`, `osx-x64`, `linux-x64`,
  `linux-arm64`; CI running `bin/test` on macOS and Linux; benchmark against the Rust binary
  and against Python TTE; release artifacts.

Each effect PR ships: the effect, its option table + registry entry, and its parity-suite
entry turning green. **No effect merges with a failing parity case** — the whole point of the
oracle is that divergence is caught at port time, per-effect, not at the end.

One qualification on that gate, because it is weaker than it sounds: every inherited case runs
`--max-frames 400`, so a port can match a 400-frame *prefix* and still be wrong about
completion — the final gradient, the reset-to-final-appearance, the `StopIteration` condition,
or anything in a long tail phase. So each effect PR runs its case **twice**: once bounded at
400 frames, and once unbounded to natural completion, comparing total frame count as well as
bytes. For the two clock-budgeted effects (matrix, thunderstorm) the virtual clock makes the
unbounded run finite.

**Put a watchdog on the unbounded run.** Some configurations legitimately never terminate —
`colorshift --cycles 0` (`colorshift.rs:94`) loops forever by design — and a port bug that
leaks `active_characters` (easy, given the looping-scene quirk) turns an unbounded run into a
hung CI job rather than a failure. Cap it by wall clock and frame count, and treat the cap as
a failure except for the configurations known to be infinite, which are excluded by name.

---

## 7. Parity verification — the Rust binary is the oracle

This is the section that differs most from the ttfx plan, and it differs *in our favor*.

### 7.1 The contract — and its exact scope

> Under `--parity-dump` (which forces the virtual clock and suppresses the tty path), with an
> explicit `--canvas-width/height` plus `--ignore-terminal-dimensions`, `--frame-rate 0`, a
> fixed `--seed N`, and identical `COLUMNS`/`LINES`, on one machine and one pair of pinned
> binaries: `ttfx` and `hypa-ttfx` produce byte-identical frame streams.

Every clause is doing work. **Plain `ttfx --seed N <effect>` is not a deterministic oracle** —
it reads the real clock (`main.rs:131-169`) and runs the tty lifecycle with wall-clock frame
pacing (`effect.rs:83-106`), so matrix and thunderstorm alone will diverge on frame count.
Identical RNG state is necessary, not sufficient; the stream can still legitimately differ on
transcendental results at a quantization boundary, terminal dimensions or environment, resize
timing, and invariant-failure behavior. Where the tty *escape* stream is compared
(`tty_compare.sh`), the same scoping applies via `--virtual-clock`.

**And the contract holds only on RIDs where it has been tested.** §9 publishes four RIDs;
the parity suite runs on whichever runners CI actually has. Math-library behavior, AOT
codegen, signal delivery, and the `winsize` layout can all differ by architecture, and the M1
measurement cannot be executed for `linux-arm64` from a macOS host. So either CI gains a
parity job per target RID, or the README says byte-exact parity is *verified on
`linux-x64`* (and whichever others get a runner) and *expected but unverified* elsewhere.
Do not publish four binaries under a claim tested on one.

State the contract this way in the README too. A contract that overclaims is one that gets
quietly abandoned the first time it's violated for a legitimate reason.

ttfx could not make even the scoped claim against Python: CPython's Mersenne Twister is not xoshiro, so
it needed a monkeypatching shim, a pinned CPython, a pinned upstream checkout, and a separate
"shim audit" to prove it wasn't proving parity against a modified reference. **All of that
goes away.** Our reference is a deterministic native binary with a documented PRNG and a
purpose-built dump mode.

Verified working locally:

```
$ cargo build --release      # 1m16s, clap + clap_complete + terminal_size only
$ printf 'hello world\nsecond line\n' | ./target/release/ttfx \
    --canvas-width 20 --canvas-height 4 --ignore-terminal-dimensions \
    --frame-rate 0 --seed 7 --parity-dump wipe
83
<frame bytes>
83
…
```

Length-prefixed frames on stdout, `frames=N` on stderr. Exactly what a differ needs.

### 7.2 Inherited assets — and the adapter they actually need

The *cases, fixtures and structure* are inherited. The **reference side of each script is not
a binary path** — in every parity script the reference is a Python driver, so swapping in the
Rust binary means writing a Rust-reference invocation, not editing a variable. Budget this as
real M2 work:

| Asset in the ttfx tree | What transfers | What must be built |
|---|---|---|
| `tools/parity/cases.txt` | **177 executable cases** (line 78 is a comment header), each at 2 seeds = **354 checks**. Copy verbatim. | nothing |
| `tools/parity/run_suite.sh` | input fixtures, seed pair, `--max-frames 400`, exit-code comparison, first-diff reporting | `PY="python3 tools/parity/dump.py"` becomes a ttfx invocation that **must add `--parity-dump`** — the Python driver has the dump behavior built in, the Rust binary needs the flag |
| `tools/parity/tty_compare.sh` | the 41 case definitions and the pty comparison logic | its reference side is `tools/parity/tty_run.py`, a Python pty launcher (`tty_compare.sh:9-44`). A Rust-side pty launcher must be written, or `tty_run.py` generalized to drive any binary |
| `tools/parity/m0_matrix.sh` | the option matrix | same shape: reference side is `m0_dump.py`; ours needs the Rust binary's `--m0-dump` |
| `tests/fixtures/*.bin`, `*.txt` | consume **unchanged** — Python-generated, so they check the C# against *Python*, not merely against the Rust | nothing |
| `tools/tests/cli_corpus.sh` | 19 exit-code / stream-routing checks | parameterize the binary path (genuinely one variable) |
| `tools/tests/sigterm_behavior.py`, `resize_behavior.py` | pty signal/resize drivers | parameterize the binary path; note neither covers SIGINT |
| `tools/tests/bench_full.py` | benchmark harness | add a third column |
| `docs/ordering-inventory.md` | the canonical-ordering checklist (§4.3) | extend with the operation map |

The right shape is a small `tools/parity/reference.sh` defining `ref_dump`, `ref_m0`, and
`ref_tty` as functions wrapping the Rust binary with the correct flags, which each suite calls
instead of `$PY`. Write that adapter first in M2; it is the thing all three suites need.

### 7.3 Gaps the inherited suites do not cover

`cases.txt` runs everything through `--parity-dump`, which per `main.rs` forces the virtual
clock and skips signal installation entirely; `m0_matrix.sh` only covers preprocessing; and
`resize_behavior.py` checks that a resize *restarts*, not what the RNG did across it. Grepping
`tools/` confirms `--random-effect` appears in no harness at all. So two behaviors identified
as observable in §4.4 and §4.6 have **zero coverage on either side**. Add both in M6:

1. **Registry enumeration order.** `ttfx --seed N --random-effect` must select the same effect
   as ours for a spread of seeds. Compare the selected effect (dump the frames and compare
   byte-for-byte; identical selection is implied, and a differing selection shows up
   immediately as a total mismatch). ~20 seeds is enough to catch an off-by-one in the name
   list.
2. **RNG continuity across a resize rebuild.** If the C# resets the RNG instead of carrying it
   forward, the rebuilt animation still looks perfectly plausible — so it needs a mechanical
   check. But the obvious one doesn't work: the inherited driver fires `SIGWINCH` after a
   wall-clock delay (`resize_behavior.py:62-80`), so the two binaries may legitimately have
   emitted different frame counts before the signal arrives, leaving their RNG states
   different *even when both are correct*. Comparing total output length instead would fail to
   notice a reset. So this needs a deterministic trigger, not a timed one: resize at a known
   frame boundary — e.g. have `--parity-dump` accept a "rebuild after frame N" hook that
   exercises the same rebuild path without a real signal, and compare the full stream. Test
   the actual `SIGWINCH` delivery separately, as behavior (does it rebuild at all) rather than
   as byte parity.

Both are cheap, and both cover mistakes whose output looks correct.

**And the inherited coverage is thinner than its size suggests.** 354 checks sounds
comprehensive; what it actually is: 177 cases truncated at 400 frames, at 2 seeds, over four
input fixtures (`basic.txt`, `single.txt`, `colored.txt`, `paragraph.txt`) — all ASCII, all
well-formed. The tty suite concentrates its variants on `randomsequence`. So the suite can go
green while the port is still wrong about:

- **non-BMP and combining input** — the single highest-risk C# divergence (§5.1) has *zero*
  coverage. Add a fixture with astral-plane characters (which `binarypath` will turn into a
  binary string, §5.1) and combining marks. A *lone surrogate* cannot appear in this fixture —
  input is strictly UTF-8 decoded before parsing (`main.rs:18-24`), and a surrogate code point
  is not representable in UTF-8; test malformed UTF-8 as a **rejection** case (message, exit 1)
  instead.
- **malformed and adversarial ANSI** — cursor-movement overwrites, ignored SGR parameter
  values, private modes, truncated CSI. The Rust plan's §5 item 13 lists the corpus; the
  shipped suite barely samples it.
- **numeric edge cases** in option parsing — negative-looking values, `0`, `-1`, values beyond
  `int`, `NaN`/`inf` spellings for ratio options.
- **multi-value options** — every `nargs`-style option, since our hand-rolled parser (§4.5) is
  new code where ttfx inherited clap's tested behavior.
- **long runs** — 400 frames truncates the slow effects well before their final phase.
- **the paths the object-graph change actually touches** — path/scene replacement during a
  reentrant dispatch, duplicate registrations with structurally equal payloads, and
  `--max-frames 0`. These are precisely where §4.1/§4.2's design differs from the Rust, and
  nothing in the inherited suite reaches them. They belong in the M1 engine traces, as
  scripted state tests, not in the frame-parity suite at all.

These are additions to `cases.txt` and a new `cli_corpus` block, not new machinery. Add the
Unicode fixture in **M0** (it exercises the input parser, which M0 delivers); the rest by M6.

### 7.4 The differ

`run_suite.sh` already reports "first diff byte". Add a small helper that, given two dumps,
decodes the first divergent frame into a row/column grid with escapes rendered readably —
ttfx's plan describes one and the same need applies. This is the debugging tool that makes a
failing effect tractable; budget half a day for it in M2.

### 7.5 Bidirectional value

An unexpected benefit: a divergence is not automatically *our* bug. Both implementations
descend from the same Python; a case where hypa-ttfx and ttfx disagree and Python agrees with
*ours* is a bug found in the Rust port. Report those upstream rather than "fixing" the C# to
match a Rust defect. (Judgment call each time, decided against the Python source.)

### 7.6 Unit tests without NuGet

`tests/Ttfx.Tests` is a plain console app: a `Harness` with `AssertEqual`, `AssertClose`,
`AssertThrows`, a test registry (`[TestList]`-free — a static array of named delegates), and
an exit code. `bin/test` runs it. xunit / NUnit / `Microsoft.NET.Test.Sdk` are all packages
and are out per §1. The harness is ~150 lines and needs no maintenance.

**The goldens must run under the same compiler as the product.** `dotnet run` on a test
console app compiles the engine with **RyuJIT**; `bin/build` compiles it with **ILC**. The two
can differ on exactly what this port is sensitive to — `Math.Pow` strength reduction, constant
folding, FMA contraction. ttfx already documents its own instance of this: optimized Rust
const-folds some `powf` calls and its release tests tolerate 1 ulp on `CubicBezier` for that
reason (`tests/easing_goldens.rs:64-68`). So M1 can be green under JIT and M2 fail on the same
source. Either publish the test project AOT for the target RID, or have the *published* `ttfx`
binary emit the golden dumps through a hidden flag and assert on those.

**Float assertions inherit ttfx's tolerances, not `cmp`.** The Rust test is bit-exact on
Linux/glibc *except* `CubicBezier` (1 ulp), and uses `1e-15` absolute elsewhere including
macOS (`easing_goldens.rs:60-72`). This resolves the contradiction between M1's "consume the
fixtures verbatim and green" and this section: **verbatim means the fixture data, not
bit-exact assertions**. Use ttfx's own tolerance schedule. And for §7.7's measurement, the
meaningful comparison target is the *quantized* geometry lattice — integers — not raw easing
floats, where a single legitimate ulp would wrongly freeze the platform gate.

### 7.7 `bin/test`

Mirrors ttfx's, in one command, nothing optional. Note the two things a fresh CI runner needs
before any parity step can run at all — a prerequisite probe and the reference build — which
the reference's own `bin/test:22-29` does and an earlier draft of this plan omitted:

```sh
tools/check-prereqs.sh          # fails loudly, naming the missing tool (see §9)
grep -r PackageReference --include=*.csproj . && exit 1   # zero-package rule
dotnet run --project tests/Ttfx.Tests -c Release   # goldens + traces + rng vectors
bin/build                                          # AOT publish, host RID
tools/tests/cli_corpus.sh
python3 tools/tests/sigterm_behavior.py
python3 tools/tests/sigint_behavior.py

if [ "$(uname -s)" = "Linux" ]; then               # see the platform gate below
  # Only the byte-exact suites need the oracle, so the fetch lives inside the gate —
  # otherwise macOS CI would need a Rust toolchain to run tests it then skips.
  tools/parity/fetch_reference.sh                  # clone + cargo build --release, cached
  tools/parity/m0_matrix.sh
  tools/parity/run_suite.sh
  tools/parity/tty_compare.sh
  python3 tools/tests/resize_behavior.py
fi
```

This is the *final* shape. It cannot run as written until M2 — `cli_corpus.sh` needs `wipe`
and `--parity-dump`, and the parity suites need the adapter. Grow the script phase by phase
rather than committing it whole in M0 and watching it fail.

**The platform gate is real and stays until measured away.** The byte-exact suites and the
timing-calibrated resize suite run on Linux only, exactly as `reference/bin/test:22-30` does.
macOS CI runs the prerequisite probe, the zero-package check, unit goldens (with the
boundary-tolerant easing assertion), the AOT publish, the CLI corpus, and the signal tests.
If §7.7's measurement below comes back clean, the gate widens; until then §9's "runs on both
platforms" means *this* script on both platforms, not the full parity suite on both.

**Measured on osx-x64 (2026-08-14), AOT `artifacts/ttfx` vs Rust `easingdump`/`geometrydump`
at the fetched pin.** Easing at 1e-3 steps (34 functions × 1001 samples): 34030/34034
bit-exact. The 4 mismatches are all `CubicBezier` (p=0.192 ulp 1; p=0.256 ulp 4; p=0.379
ulp 17; p=0.400 ulp 2); max abs 2.359e-16; **zero** samples exceed the 1e-15 macOS
schedule. Geometry: all 77 quantized coordinate lines byte-exact, and all 10 raw-float
lines (`bezier_len` / `line_len` / `norm_dist`) bit-exact. The quantized integer lattice
matches, so **byte-exact CI can run on macOS**.

---

## 8. Runtime behavior details

### 8.1 Terminal size

Replicate `get_terminal_dimensions` in `terminal.rs` exactly — which is `shutil.get_terminal_size`
semantics: `COLUMNS` and `LINES` env vars win if **both** parse as integers; otherwise query
the tty and use each env var as a per-axis override; on query failure, `(80, 24)`.
(`run_suite.sh` exports `COLUMNS=80 LINES=24`, so the parity path depends on this.)

The tty query: `LibraryImport`-generated P/Invoke to `ioctl(fd, TIOCGWINSZ, &winsize)`.
`[LibraryImport]` source generation is the AOT-clean path (no `DllImport` marshalling stubs).

**It is three file descriptors, not one.** The `terminal_size` crate tries **stdout, then
stderr, then stdin**, taking the first that is a tty *and* reports positive rows and columns;
if none qualifies it returns `None` and the caller falls back to the env vars, then `(80, 24)`.
A naive `ioctl(1, …)` diverges the moment stdout is redirected while stderr is still a
terminal — which is exactly the shape of a parity-harness invocation. Transcribe the full
cascade including the `rows > 0 && cols > 0` guard.

Note this is a *different* question from the `isatty` check that gates the tty lifecycle and
signal registration (§8.2), which tests stdout only. Keep the two decisions separate;
conflating them is easy and produces a binary that animates into a pipe.

**Verify, do not recall**: the `TIOCGWINSZ` constant differs between Linux (`0x5413`) and
macOS (`0x40087468`), and `struct winsize` field order/width must match the platform header.
M0 task: compile a two-line C program on each target that prints `TIOCGWINSZ` and
`sizeof(struct winsize)`, and assert the C# constants against it.

Do **not** use `Console.WindowWidth`/`WindowHeight`: it throws when stdout is not a terminal
and consults terminfo, which is a different code path from what we are matching.

### 8.2 Signals

`System.Runtime.InteropServices.PosixSignalRegistration` — BCL, AOT-clean, and confirmed to
carry `PosixSignal.SIGWINCH` in the .NET 10 ref assembly on this machine. **Ref-assembly
presence is not behavioral proof**; everything below is verified against an AOT-*published*
M0 probe binary (§6), not a `dotnet run` debug host, because the runtime's own signal
plumbing differs between hosts.

Four behaviors to pin by test, beyond the exit statuses:

- **Registration lifetime**: a `PosixSignalRegistration` is disposable and stops handling when
  collected or disposed. Hold them for process lifetime in a static field; a registration that
  goes out of scope is a handler that silently stops firing mid-animation.
- **Handlers run on a thread-pool thread, not the animation thread.** The Rust flags are
  `AtomicBool`; the C# equivalents must be `volatile bool` or `Interlocked`, never a plain
  field, or the run loop can miss the signal entirely under an optimized AOT build. The resize
  debounce's timestamp is shared the same way.
- **`Cancel`**: setting `context.Cancel = true` suppresses the runtime's default handling.
  Required for SIGINT (we want to unwind, not be killed) and central to the SIGTERM question.
- **Handler ordering**: .NET runs multiple registrations for one signal in **reverse**
  registration order. Only relevant if we ever register twice for a signal — so don't, and
  assert it.
- **.NET 10 changed the default SIGTERM handling** relative to earlier runtimes, which makes
  the redirected-output case behave differently than a pre-10 port would. Pin it explicitly
  rather than inheriting whatever the runtime currently does.

Note also that **no inherited script tests SIGINT** — `sigterm_behavior.py` covers SIGTERM
only. Write the SIGINT case (teardown ran, cursor restored, exit 1, no message).

- **SIGINT** → set a flag, let the run loop unwind normally so teardown (cursor restore) runs;
  exit **1**, no message. `Cancel = true` on the context so the runtime does not terminate us
  first.
- **SIGWINCH** → set a flag, then run a **debounce state machine**, not an immediate rebuild.
  `terminal.rs:622-640` is the specification and the inherited `resize_behavior.py` is
  calibrated to it (`first_delay=0.30`, `gap=0.05`): a 50 ms quiet window that **restarts on
  every further SIGWINCH**, then four suppression checks in order — skip if
  `--ignore-terminal-dimensions`; skip if the queried dimensions are unchanged; skip if the
  recomputed layout is unchanged; only then rebuild. A port that rebuilds on the first signal
  strobes through a window drag and fails the inherited test. This is **M0 platform work**,
  not M6 polish.
  On rebuild the run loop wipes the area and **carries the RNG state forward** (§4.6).
  Registered only when stdout is a tty — SIGWINCH reaches every process in the foreground
  group regardless of where stdout points, and reacting to it under redirection would write a
  truncated run followed by a complete one.
- **SIGTERM** → teardown, then die *from the signal* so a supervisor sees signal death.
  Rust does `signal(SIGTERM, SIG_DFL); raise(SIGTERM)`. **Whether `PosixSignalRegistration`
  can hand control back to the default action, and what exit status results, must be
  established empirically** against an M0 spike binary using the existing
  `tools/tests/sigterm_behavior.py`. Do not plan this from documentation. If it cannot be
  made to match, it becomes a documented divergence — but find out in M0, not in M6. Note this
  is in tension with §1's "identical exit codes" goal, so it is not purely an experiment: if
  the answer is no, **§1's goal changes** to "identical exit codes except signal-death status
  under SIGTERM", explicitly, rather than the goal quietly outranking the finding.

- **SIGPIPE** is an accepted divergence. Rust restores `SIG_DFL` so `ttfx | head` dies quietly
  with status 141. .NET's runtime ignores SIGPIPE by design and relies on `EPIPE` for its own
  IO; `PosixSignalRegistration` cannot install `SIG_DFL`, and P/Invoking `signal()` to do it
  fights the runtime. **Decision**: tear down quietly and exit 0 on a broken pipe — but note
  "catch `IOException` with `EPIPE`" is not actually an API. `IOException.HResult` after
  `Stream.Write` is not reliably errno 32, and a bare `catch (IOException)` would also swallow
  disk-full and genuine write failures as success. So either `LibraryImport` `write(2)`
  directly and check `Marshal.GetLastPInvokeError() == EPIPE` (we already own the raw stdout
  path per §5.7, so this is a small addition, and it is worth breaking the
  prefer-managed-APIs instinct for), or accept that non-EPIPE write failures are
  indistinguishable and say so. Try the errno route first — 141 is the one user-visible
  divergence a shell user would actually notice. Document "broken-pipe exit status is 0, not 141" in the README's
  divergences list. Checked: `cli_corpus.sh` asserts nothing about 141 or a broken pipe, so
  nothing inherited breaks — this is a documentation item only.

### 8.3 Input, exit codes, streams

Inherited verbatim from ttfx (which inherited them from TTE, quirks included):

- stdin (empty string when stdin is a tty), or `--input-file`. **Strict UTF-8** both ways;
  decode failure → message, exit 1.
- Empty/whitespace-only input → `NO INPUT.` on **stdout**, exit 1.
- File errors → message on **stdout** (yes, stdout), exit 1.
- Unsupported ANSI sequence → message on **stderr**, exit 1.
- Usage errors → exit **2**.
- Success → 0.
- `--print-completion` prints and returns before any input handling.

**`--max-frames N` emits the frame *before* checking the limit** (`effect.rs:92-101`), so
`--max-frames 0` still produces one frame. A natural C# `while (count < max)` pre-check
produces zero and silently shifts every bounded comparison in the suite by one frame. The flag
is central to the inherited harness, so test 0, 1, and a value past natural completion.

Error message *text* may differ; conditions, streams, and codes may not.

### 8.4 Frame pacing

`1/frame_rate` delay, monotonic check, sleep the remainder, timestamp taken **after** the
sleep so drift accumulates (faithful), `--frame-rate 0` disables pacing entirely.
`Thread.Sleep(TimeSpan)` has ~1 ms granularity on Unix, comparable to Rust's
`thread::sleep`; the pacing path is not compared byte-for-byte anyway (the parity suites run
with `--frame-rate 0`), but the *tty* suite does compare the escape stream, which is
pacing-independent.

### 8.5 Performance target

"Never the bottleneck." Rust does a fullscreen 200×50 canvas at 1,700–5,000 fps. Native AOT
with the object-graph design and a reused byte buffer should land within a small factor of
that — an order of magnitude clear of the 60 fps that actually matters, and still ~20× Python.
Measure in M7 with the inherited `bench_full.py`; do not pre-optimize. The upstream O(n²)
algorithms (outside-in sort, grouped scans) are kept for fidelity and are fine at terminal
scale.

Startup: expect 1–5 ms AOT vs 0.5 ms Rust vs ~65 ms Python. Measure it; do not chase it.

---

## 9. Build and release

- `bin/build [rid]` → `dotnet publish src/Ttfx -c Release -r <rid>` with `PublishAot=true`.
  **With no argument it publishes the host RID** — that is the form `bin/test` calls, and the
  form a developer gets locally. The release script loops over the RID list explicitly. Say
  this in the script's usage line; an undefined default here makes local and CI runs diverge.
- Pin the SDK with a `global.json` (`rollForward: latestFeature`). `LangVersion=latest` is
  otherwise a moving target across machines, which is the opposite of what a parity port
  wants.
- RIDs: `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`. Cross-compilation of AOT requires
  a matching toolchain, so CI builds each on its own runner rather than cross-linking.
- **Prerequisites are not just "clang and a linker", and `bin/test` probes for all of them**
  (`tools/check-prereqs.sh`), failing with the specific missing tool rather than a linker
  error 200 lines into a publish:
  - .NET SDK at the `global.json` version + the `Microsoft.NETCore.App.Runtime.NativeAOT.<rid>`
    pack;
  - clang and the system linker; on Linux the developer packages Native AOT links against
    (zlib headers among them); on macOS the Xcode Command Line Tools;
  - **`StripSymbols=true` stays on.** A first AOT publish with `StripSymbols=true` on this
    Mac **succeeded** without `objcopy` or `llvm-objcopy`. .NET 10 ILC on Apple platforms
    strips with `dsymutil` + `strip` (Xcode CLT), not `objcopy`; GNU binutils `objcopy` is
    the wrong tool for Mach-O anyway. `objcopy`/`llvm-objcopy` remain a Linux prerequisite
    only. Do not install Homebrew llvm/binutils for this.
  - bash, python3, and pty support for the harness; `zsh` only if the completion check
    (§4.5) runs, which is otherwise skipped with a notice rather than failing;
  - **`cargo`, `rustc`, and `git`, plus network access** — `fetch_reference.sh` clones and
    builds the Rust oracle, so a runner without a Rust toolchain fails before any parity step.
    Cache the built binary by pinned commit hash so this cost is paid once.
- Expected binary size: ~2–5 MB stripped (comparable to the Rust musl build's 3.3 MB).
- No `libicu` dependency thanks to `InvariantGlobalization`; the binary links only libc,
  libm, and the platform's threading/crypto libs.
- CI (`.github/workflows/ci.yml`): `bin/test` on `ubuntu-latest` and `macos-latest` — with
  `bin/test` gating the byte-exact parity suites on platform the way ttfx's does, until §7.7's
  measurement says the gate can be removed. Plus
  per-RID publish jobs uploading artifacts. Mirror the ttfx workflow's shape.

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| A sort silently becomes unstable and shifts one tie in one frame | §4.3 / §5.3 as a review checklist item on every PR; the parity suite catches it as a first-divergence with a byte offset |
| `Dictionary` iteration order reaches output | Same; plus the ordering inventory as a per-file checklist during the port |
| Rune vs char splits a non-BMP input character | A parity case with non-BMP input added to `cases.txt`; the input-parser golden covers it |
| SIGTERM exit-status contract unmatchable on .NET | Resolved empirically in **M0**, not M6; documented divergence is an acceptable outcome, a late surprise is not |
| The Rust reference has a bug the C# "fixes" | Parity failure forces the question; adjudicate against the Python source (§7.5) |
| Zero-package rule becomes painful (CLI parsing, tests) | Both costs are known and bounded: a table-driven parser (~600 lines) and a ~150-line assert harness. Enforced mechanically so it cannot erode by accident. |
| Transcendental ulp differences between .NET and Rust | Measured in M1 (§7.7), not assumed. Parity comparisons are on quantized output (rounded coords, hex colors), which absorbs ulp noise except at rounding boundaries; inherit ttfx's boundary-tolerant easing assertion; pin byte-exact CI to Linux if the measurement says to |
| Scope creep into "improving" ttfx or TTE | The only permitted divergences are the ones enumerated in this document (§1 non-goals, §4.5 completions, §5.8 perf hacks, §8.2 SIGPIPE). Everything else is transcription. |
| GC pauses visible at 60 fps | Workstation non-concurrent GC, reused frame buffers, cached formatted symbols. A frame's allocation budget is a few KB; nothing here should provoke a gen-2 collection. Measure in M7. |
| A collection *operation* (not order) diverges — `swap_remove`, deque end, `remove(0)`, RNG-indexed `remove(i)` | The operation map (§4.3) built in M1, before any effect is ported; each site names its C# counterpart explicitly. The indexed removals are the dangerous ones: they sit right after an RNG draw, so a wrong one desynchronizes the rest of the run |
| A callback closure captures a loop variable instead of its value | §4.2 keeps the immutable `{ id, args }` record rather than raw delegates — the capture bug is impossible if there is nothing to capture |
| A retained `Path`/`Scene` reference goes stale when a reentrant callback replaces it | §4.2: action targets are string ids re-resolved at every dispatch, as the Rust does (`ctx.rs:252-262`) |
| A C# positional `record` gives reference equality on an array member, defeating the duplicate-registration check | §4.2: hand-written `Equals`/`GetHashCode` over the whole `EventAction`, unit-tested with two separately-allocated equal payloads |
| Byte-exact parity claimed for four RIDs but tested on one | §7.1: either a parity job per target RID, or the README scopes the claim to tested RIDs |
| AOT breaks late, after the effect machinery lands | §6: publish + AOT analyzers at every milestone boundary, warnings as blockers — not once in M0 |
| Goldens pass under RyuJIT (`dotnet run`) while the shipped ILC build differs | §7.6: golden dumps come from an AOT-published binary for the target RID, not the test host |
| A float cast rounds where Rust truncates, shifting a count and desynchronizing the RNG | §5.5: `PyCompat.TruncToI64` at every `as i64`, with `as i64` in the M1 grep list |
| Lossy UTF-8 decode turns a rejection case into wrong frames | §5.1a: throwing `UTF8Encoding` on both input paths; `cli_corpus.sh:33` already asserts it |
| Resize rebuilds on the first SIGWINCH instead of debouncing | §8.2 transcribes the 50 ms restart-on-signal window and all four suppression checks; M0, not M6 |
| `--probe` enters the registry and shifts `--random-effect` selection | §6: root flag, not a registry entry; M0 test asserts exactly 37 names |
| An unbounded completion run hangs CI instead of failing | §6: wall-clock + frame-count watchdog, with the legitimately-infinite configs excluded by name |
| `Math.Pow`/`Math.Sqrt`/`Math.Max` substituted for the Rust equivalent | §5.5's three explicit bans, plus `PyCompat.FMax`/`FMin`; M1 goldens include NaN, ±∞, signed zero and `pow`/`hypot` boundary cases |
| Event-key equality silently becomes reference equality | §4.3: value-typed key structs with hand-written `Equals`/`GetHashCode`, unit-tested against the Rust field sets including the identical-waypoints-collide quirk |
| An invariant failure returns a plausible value instead of dying | §5.6: `EngineInvariantException`, uncaught, with a unit test per `assert!`/`unwrap` site — the parity suite may never reach these paths |
| The suite is green but coverage is shallow (ASCII-only, 400 frames, 2 seeds) | §7.3's additions: Unicode fixture in M0, ANSI/numeric/multi-value/long-run cases by M6 |
| **Effort underestimated.** The work is not 22k lines of transcription — it is also a new identity/equality model, callback ownership, 37 option surfaces, a from-scratch arg parser, the Rust-reference CI plumbing, POSIX probes, and diagnosing cross-effect RNG divergence with new tooling | Phase boundaries are cut so a partial release is clean (§12.1); M2 is explicitly no longer "days"; effects land one-per-PR so slippage is visible early rather than at the end |

---

## 11. Decisions taken (so they are not relitigated)

1. **Port from the Rust, not the Python.** §2.
2. **Object graph, not arena + IDs** — for *characters*, which are never replaced. §4.1, with
   ordering as an explicit contract (§4.3). **Narrowed**: event-action targets stay string ids
   re-resolved at dispatch (§4.2), because paths and scenes *are* replaced by reentrant
   callbacks and because duplicate detection compares action values.
3. ~~Delegates, not callback IDs.~~ **Reversed.** Callbacks keep an immutable `{ id, args }`
   record with value equality; only the non-callback action targets become direct references.
   Rust's payloads are captured by value and its duplicate-registration check is structural —
   C# delegates reproduce neither. §4.2.
4. **The Rust binary is the parity oracle** — under the scoped contract in §7.1, with its
   *cases and fixtures* inherited but a **reference adapter written in M2** (§7.2). "Inherited
   wholesale" was wrong: every parity script's reference side is a Python driver.
5. **Zero NuGet packages**, enforced in `bin/test`. §1, §3.
6. **Frames are bytes, written to the raw stdout stream.** §5.7.
7. **Rust perf hacks are optional; their semantic halves are not.** §5.8.
8. *(not a decision — an open measurement)* Whether byte-exact parity CI can run on macOS as
   well as Linux is **provisional**, settled by an M1 easing/geometry comparison on this Mac.
   §7.7. Plan CI for the Linux-pinned arrangement and widen it only if the measurement says
   so.
9. **SIGPIPE behavior is a documented divergence**; SIGTERM behavior is an M0 empirical
   question. §8.2.
10. **Shipped binary name is `ttfx`.** No `tte` alias (it would collide with a real
    terminaltexteffects install). For an end user, having both this and the Rust ttfx on
    `PATH` is their own call, and the two are byte-identical anyway — **but that reasoning does
    not extend to CI**, where the harness must invoke both in the same run. `bin/build` and
    `fetch_reference.sh` must therefore place them at distinct, explicitly-referenced paths
    (`artifacts/ttfx` and `reference/ttfx`), never rely on `PATH` resolution, and never write
    two files named `ttfx` into one directory.

---

## 12. Open questions

1. **Effect coverage vs. time.** 37 effects is the bulk of the work (~12.6k lines of the
   Python original). Is a partial first release — engine + waves 1–2, 30 effects — acceptable,
   with waves 3 following? The plan assumes all 37; the phase boundaries make a partial cut
   clean if wanted.
2. ~~Vendor vs. fetch the ttfx reference?~~ **Resolved, and moved to M0.** Nothing inherited
   provides the oracle — the reference tree's own `fetch_reference.sh` fetches *Python TTE*,
   not a Rust checkout. Write `tools/parity/fetch_reference.sh` for this repo: clone ttfx at
   the commit pinned in `REFERENCE.md`, `cargo build --release`, cache by commit hash, and
   drop in `rngdump.rs` (§4.6). It lands in **M0**, because M0's own exit criterion is a byte
   comparison against that binary and M1's RNG vectors come out of it — scheduling it in M2
   inverted the dependency. Adds `cargo`/`rustc`/`git` to the prerequisite list (§9), on the
   Linux parity runner only (§7.7).
3. **Licensing/attribution**: this is a port of a port. `LICENSE` must carry the original
   TerminalTextEffects copyright (MIT), ttfx's copyright, and ours; `NOTICE` needs the full
   attribution chain. Worth getting right before the first public commit.
