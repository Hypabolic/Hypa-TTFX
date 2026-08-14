using System;
using System.Collections.Generic;
using System.IO;

namespace Ttfx.Tests;

internal readonly record struct TestCase(string Name, Action Run);

internal static class Harness
{
    internal static int Failures;
    internal static int Passes;

    internal static void AssertTrue(string name, bool condition)
    {
        if (condition)
        {
            Passes++;
            return;
        }

        Failures++;
        Console.Error.WriteLine($"FAIL {name}");
    }

    internal static void AssertEqual<T>(string name, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            Passes++;
            return;
        }

        Failures++;
        Console.Error.WriteLine($"FAIL {name}: expected {expected} got {actual}");
    }

    internal static void AssertThrows<TException>(string name, Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Passes++;
            return;
        }
        catch (Exception ex)
        {
            Failures++;
            Console.Error.WriteLine($"FAIL {name}: expected {typeof(TException).Name} got {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Failures++;
        Console.Error.WriteLine($"FAIL {name}: expected {typeof(TException).Name}, nothing thrown");
    }

    internal static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "hypa-ttfx.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "hypa-ttfx.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find repo root (hypa-ttfx.slnx)");
    }
}
