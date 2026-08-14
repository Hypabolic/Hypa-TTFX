# 0001 — Repo scaffold, AOT publish, prerequisite probe

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The buildable skeleton, proven by publishing a real Native-AOT binary that runs. Nothing in
this project can be verified until `bin/build` produces an executable, and one prerequisite is
already known to be missing on the development machine — so this slice exists to hit that wall
deliberately and early rather than during the first parity run.

End-to-end path: `dotnet publish` → native binary → `ttfx --version` prints and exits 0.

Two projects (`src/Ttfx`, `tests/Ttfx.Tests`), a `Directory.Build.props` carrying the shared
settings from plan §3, and a `global.json` pinning the SDK so `LangVersion=latest` stops being
a moving target across machines.

`bin/build [rid]` publishes the **host RID** when called with no argument — that is the form
`bin/test` and every developer will use, and leaving the default undefined makes local and CI
runs diverge.

`tools/check-prereqs.sh` probes and fails with the specific missing tool rather than a linker
error deep inside a publish: SDK at the `global.json` version, the
`Microsoft.NETCore.App.Runtime.NativeAOT.<rid>` pack, clang, the system linker,
`objcopy`/`llvm-objcopy`, and (on the parity runner only) `cargo`/`rustc`/`git`. Also bash,
python3 and pty support for the harness; `zsh` is optional and its absence skips the completion
check with a notice rather than failing.

**Known blocker to resolve here, not later:** `StripSymbols=true` requires
`objcopy` or `llvm-objcopy`, and the development machine has **neither** — a publish would fail
today. Either install binutils/llvm or drop `StripSymbols`. Decide it in this issue.

Note `TreatWarningsAsErrors` must stay scoped to C# compiler warnings. Escalating ILC/ILLink
trim and AOT warnings would fail every publish on a clean tree; those are gated per-milestone
instead (plan §6), with the analyzers enabled but not fatal.

## Acceptance criteria

- [ ] `bin/build` with no argument publishes a self-contained Native-AOT binary for the host RID
- [ ] The published binary runs: `ttfx --version` prints a version and exits 0
- [ ] `bin/build <rid>` publishes for an explicitly named RID
- [ ] `tools/check-prereqs.sh` passes on the dev machine, and fails with a precise, named
      diagnostic when a required tool is removed from `PATH`
- [ ] The `objcopy`/`StripSymbols` question is resolved — either the tool is a documented
      prerequisite or `StripSymbols` is off — and the decision is recorded in the plan
- [ ] `global.json` pins the SDK; a mismatched SDK produces a clear error
- [ ] Zero `PackageReference` entries in any `.csproj`, enforced by a grep in `bin/test`
- [ ] `EnableAotAnalyzer`, `EnableTrimAnalyzer` and `EnableSingleFileAnalyzer` are on, and the
      publish is warning-free
- [ ] Publish succeeds on both macOS and Linux runners

## Blocked by

None — can start immediately.
