using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Effects;

namespace Ttfx.Tests;

internal static class SweepEffectTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("sweep defaults", () =>
        {
            ParseResult r = CliParser.Parse(["sweep"]);
            var symbols = (List<object>)r.EffectOptions["--sweep-symbols"];
            Harness.AssertEqual("symbol count", 4, symbols.Count);
        });
    }
}
