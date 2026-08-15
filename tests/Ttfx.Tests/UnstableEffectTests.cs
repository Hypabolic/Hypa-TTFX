using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;

namespace Ttfx.Tests;

internal static class UnstableEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("unstable defaults", () =>
        {
            ParseResult r = CliParser.Parse(["unstable"]);
            Harness.AssertEqual("explosion speed", 1.0, (double)r.EffectOptions["--explosion-speed"]);
        });
    }
}
