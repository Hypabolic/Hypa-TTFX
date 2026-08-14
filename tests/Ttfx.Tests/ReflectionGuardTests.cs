using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Ttfx.Tests;

internal static class ReflectionGuardTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("no reflection in Cli/", NoReflectionInCli);
    }

    private static void NoReflectionInCli()
    {
        string cliDir = Path.Combine(Harness.FindRepoRoot(), "src", "Ttfx", "Cli");
        var banned = new Regex(@"GetType\s*\(|GetProperties\s*\(|GetMethod\s*\(|\.Invoke\s*\(");
        foreach (string file in Directory.GetFiles(cliDir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            Match match = banned.Match(text);
            Harness.AssertTrue(
                $"no reflection in {Path.GetFileName(file)}",
                !match.Success);
        }
    }
}
