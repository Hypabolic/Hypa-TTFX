# hypa-ttfx

A C# / .NET 10 Native-AOT terminal text-effects CLI and library with 37 effects.
Pipe text in, pick an effect:

```sh
printf 'Hello\n' | ./artifacts/ttfx wipe
fortune | ./artifacts/ttfx --random-effect
```

## Examples

Every clip is [`hypa-logo.txt`](hypa-logo.txt) piped through that effect with `--seed 42`.

<p align="center">
  <img src="docs/examples/wipe.gif" alt="wipe">
</p>

| beams | binarypath | blackhole |
| :---: | :---: | :---: |
| ![beams](docs/examples/beams.gif) | ![binarypath](docs/examples/binarypath.gif) | ![blackhole](docs/examples/blackhole.gif) |
| **bouncyballs** | **bubbles** | **burn** |
| ![bouncyballs](docs/examples/bouncyballs.gif) | ![bubbles](docs/examples/bubbles.gif) | ![burn](docs/examples/burn.gif) |
| **colorshift** | **crumble** | **decrypt** |
| ![colorshift](docs/examples/colorshift.gif) | ![crumble](docs/examples/crumble.gif) | ![decrypt](docs/examples/decrypt.gif) |
| **errorcorrect** | **expand** | **fireworks** |
| ![errorcorrect](docs/examples/errorcorrect.gif) | ![expand](docs/examples/expand.gif) | ![fireworks](docs/examples/fireworks.gif) |
| **highlight** | **laseretch** | **matrix** |
| ![highlight](docs/examples/highlight.gif) | ![laseretch](docs/examples/laseretch.gif) | ![matrix](docs/examples/matrix.gif) |
| **middleout** | **orbittingvolley** | **overflow** |
| ![middleout](docs/examples/middleout.gif) | ![orbittingvolley](docs/examples/orbittingvolley.gif) | ![overflow](docs/examples/overflow.gif) |
| **pour** | **print** | **rain** |
| ![pour](docs/examples/pour.gif) | ![print](docs/examples/print.gif) | ![rain](docs/examples/rain.gif) |
| **randomsequence** | **rings** | **scattered** |
| ![randomsequence](docs/examples/randomsequence.gif) | ![rings](docs/examples/rings.gif) | ![scattered](docs/examples/scattered.gif) |
| **slice** | **slide** | **smoke** |
| ![slice](docs/examples/slice.gif) | ![slide](docs/examples/slide.gif) | ![smoke](docs/examples/smoke.gif) |
| **spotlights** | **spray** | **swarm** |
| ![spotlights](docs/examples/spotlights.gif) | ![spray](docs/examples/spray.gif) | ![swarm](docs/examples/swarm.gif) |
| **sweep** | **synthgrid** | **thunderstorm** |
| ![sweep](docs/examples/sweep.gif) | ![synthgrid](docs/examples/synthgrid.gif) | ![thunderstorm](docs/examples/thunderstorm.gif) |
| **unstable** | **vhstape** | **waves** |
| ![unstable](docs/examples/unstable.gif) | ![vhstape](docs/examples/vhstape.gif) | ![waves](docs/examples/waves.gif) |

Regenerate with `./bin/build` and `tools/render_examples.py` (Pillow + ffmpeg).

## Origins

hypa-ttfx began as a C# / .NET Native-AOT port of
[ttfx](https://github.com/omacom-io/ttfx), itself derived from
[TerminalTextEffects](https://github.com/ChrisBuilds/terminaltexteffects)
by [ChrisBuilds](https://github.com/ChrisBuilds). It is now a standalone
package and continues to diverge from those sources.

Legal attribution is in [LICENSE](LICENSE) and [NOTICE](NOTICE).

## Deliberate divergences

| Topic | Behavior here | Why |
|---|---|---|
| Random number generator | xoshiro256++ (same as ttfx) | Inherited from ttfx; `--seed` matches ttfx and this port, not Python TTE's Mersenne Twister |
| Broken-pipe exit status | 0 | Upstream Python swallows `EPIPE`; we match that contract |
| SIGTERM exit status | Matches ttfx (signal 15 via `WIFSIGNALED`) | Not a divergence |
| Shell completions | Hand-written templates | Zero NuGet packages — no `clap_complete`; text differs from ttfx's generated scripts |
| Plugin effects | Not supported | No Python interpreter to load them |
| Cell width | One codepoint = one cell (`Rune`) | Faithfully reproduces upstream; no `wcwidth` |
| Byte-exact parity | Verified on tested RIDs only | See **Fidelity** below |

## Fidelity

This is a *parity port*: given the same input, config, and seed, hypa-ttfx aims for
**byte-identical frame output** to the pinned ttfx binary (`REFERENCE.md`).

**Verified on this project's CI / local testing:**

| RID | Status |
|---|---|
| `osx-x64` | Verified locally (AOT publish on this developer host) |
| `osx-arm64` | Expected on Apple Silicon hosts (same gate; build with `./bin/build osx-arm64`) |
| `linux-x64` | Verified in CI (oracle suites behind Linux gate) |
| `linux-arm64` | Expected but unverified until a matching CI runner executes the oracle suites |

Publishing four RIDs under a claim tested on one machine would overclaim — maths-library
behavior, AOT codegen, and signal delivery can differ by architecture. The README states
what has actually been measured.

`./bin/test` runs the full gate: unit goldens, AOT publish, CLI corpus, signal tests, and
(on Linux or when `reference/ttfx` is present on macOS) byte-exact parity suites.

## Library

Other .NET 10+ programs consume the engine as `Hypa.Ttfx`:

```sh
dotnet add package Hypa.Ttfx
```

```csharp
using Ttfx;

IReadOnlyList<string> frames = TextEffects.Render(
    "wipe",
    "Hello from .NET\n",
    new TextEffectOptions { Seed = 42 });

foreach (string frame in TextEffects.EnumerateFrames("smoke", "Hello\n", new TextEffectOptions
{
    Seed = 42,
    EffectArguments = ["--final-gradient-stops", "8A008A", "00D1FF"],
}))
{
    Console.Write(frame);
}
```

`Render` / `EnumerateFrames` use a virtual clock and ignore the process TTY
(canvas sizes to the input). `TextEffects.Run` writes a live animation to a
stream. Per-effect flags are the same tokens as the CLI.

The CLI is also a `dotnet tool` (framework-dependent; Native-AOT builds are
the GitHub Release assets):

```sh
dotnet tool install -g Hypa.Ttfx.Tool
printf 'Hello\n' | ttfx wipe
```

## Building

```sh
./bin/build              # AOT publish to artifacts/ttfx (host RID)
./bin/build linux-x64    # cross-RID publish when toolchain is available
./bin/test
./bin/pack               # Hypa.Ttfx + Hypa.Ttfx.Tool into artifacts/nuget
```

This repo consumes zero NuGet packages — everything comes from
`Microsoft.NETCore.App`. Downstream apps add `Hypa.Ttfx` as a package
reference; that is the supported way to reuse the engine.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build, test, and pull-request
expectations, and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Security reports
go through [SECURITY.md](SECURITY.md), not a public issue.

## Releasing

Version lives in `Directory.Build.props`. A tag `vX.Y.Z` (semver, optional
prerelease suffix) runs `.github/workflows/release.yml`: macOS + Linux test
gates, Native-AOT binaries for `osx-arm64` / `osx-x64` / `linux-x64` /
`linux-arm64`, both nupkgs, a GitHub Release, nuget.org, and GitHub Packages.

```sh
# bump <Version> in Directory.Build.props, commit, then:
git tag v0.3.1
git push origin v0.3.1
```

One-time nuget.org setup (Trusted Publishing — no long-lived API key):

1. On nuget.org → Trusted Publishing, add a policy for this repo, workflow
   file `release.yml`, environment `release`.
2. Store your nuget.org **username** (profile name, not email) as the
   `NUGET_USER` Actions secret.
3. Optional fallback: `NUGET_API_KEY` if OIDC is not available yet.

`workflow_dispatch` stamps a version and uploads artifacts; turn on
`publish` only when you intend to cut the release.

## Usage

```
<producer> | ttfx [terminal options] <effect> [effect options]

ttfx --help
ttfx <effect> --help
ttfx --random-effect        # --include-effects / --exclude-effects to filter
ttfx --print-completion bash|zsh
```

Terminal options go before the effect name; effect options after it. Names and defaults
match ttfx / TTE.

## Reference pins

See [REFERENCE.md](REFERENCE.md) for the pinned ttfx and upstream TTE commits.
