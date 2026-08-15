using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;
using Ttfx.Engine;

namespace Ttfx.Tests;

internal static class WavesEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("waves defaults", () =>
        {
            ParseResult r = CliParser.Parse(["waves"]);
            Harness.AssertEqual("wave count", 7L, (long)r.EffectOptions["--wave-count"]);
            Harness.AssertEqual(
                "wave direction",
                CharacterGroup.ColumnLeftToRight,
                (CharacterGroup)r.EffectOptions["--wave-direction"]);
        });
    }
}
