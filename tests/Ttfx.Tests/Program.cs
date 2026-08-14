using System;
using System.Collections.Generic;

namespace Ttfx.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<TestCase>();
        tests.AddRange(CliParserTests.All());
        tests.AddRange(ValueParserTests.All());
        tests.AddRange(NumericCorpusTests.All());
        tests.AddRange(ReflectionGuardTests.All());

        foreach (TestCase test in tests)
        {
            try
            {
                test.Run();
            }
            catch (Exception ex)
            {
                Harness.Failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
            }
        }

        Console.WriteLine($"tests: {Harness.Passes} passed, {Harness.Failures} failed");
        return Harness.Failures == 0 ? 0 : 1;
    }
}
