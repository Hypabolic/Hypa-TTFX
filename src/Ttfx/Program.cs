using System;
using System.Reflection;

namespace Ttfx;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            Version version = typeof(Program).Assembly.GetName().Version!;
            Console.WriteLine($"ttfx {version.Major}.{version.Minor}.{version.Build}");
            return 0;
        }

        return 2;
    }
}
