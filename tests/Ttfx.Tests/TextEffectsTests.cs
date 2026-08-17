using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ttfx;
using Ttfx.Engine;

namespace Ttfx.Tests;

internal static class TextEffectsTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("library names match registry order", NamesMatchRegistry);
        yield return new TestCase("library exists wipe and rejects unknown", ExistsAndUnknown);
        yield return new TestCase("library render is deterministic under seed", RenderDeterministic);
        yield return new TestCase("library render honors max-frames", RenderMaxFrames);
        yield return new TestCase("library render rejects empty input", RenderEmptyInput);
        yield return new TestCase("library render accepts effect arguments", RenderEffectArguments);
        yield return new TestCase("library run writes a frame to a stream", RunToStream);
    }

    private static TextEffectOptions Seeded() => new TextEffectOptions
    {
        Seed = 1,
        FrameRate = 0,
        CanvasWidth = 20,
        CanvasHeight = 8,
    };

    private static void NamesMatchRegistry()
    {
        Harness.AssertEqual("count", 37, TextEffects.Names.Count);
        Harness.AssertEqual("first", "beams", TextEffects.Names[0]);
        Harness.AssertEqual("last", "wipe", TextEffects.Names[36]);
    }

    private static void ExistsAndUnknown()
    {
        Harness.AssertTrue("wipe", TextEffects.Exists("wipe"));
        Harness.AssertTrue("unknown", !TextEffects.Exists("not-an-effect"));
        Harness.AssertThrows<ArgumentException>(
            "render unknown",
            () => TextEffects.Render("not-an-effect", "hi", Seeded()));
    }

    private static void RenderDeterministic()
    {
        IReadOnlyList<string> a = TextEffects.Render("wipe", "Hi\n", Seeded());
        IReadOnlyList<string> b = TextEffects.Render("wipe", "Hi\n", Seeded());
        Harness.AssertTrue("non-empty", a.Count > 0);
        Harness.AssertEqual("count", a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Harness.AssertEqual($"frame[{i}]", a[i], b[i]);
        }
    }

    private static void RenderMaxFrames()
    {
        IReadOnlyList<string> all = TextEffects.Render("wipe", "Hi\n", Seeded());
        IReadOnlyList<string> one = TextEffects.Render("wipe", "Hi\n", Seeded(), maxFrames: 1);
        IReadOnlyList<string> none = TextEffects.Render("wipe", "Hi\n", Seeded(), maxFrames: 0);
        Harness.AssertEqual("one", 1, one.Count);
        Harness.AssertEqual("prefix", all[0], one[0]);
        Harness.AssertEqual("zero", 0, none.Count);
    }

    private static void RenderEmptyInput()
    {
        Harness.AssertThrows<ArgumentException>("empty", () => TextEffects.Render("wipe", "", Seeded()));
        Harness.AssertThrows<ArgumentException>("ws", () => TextEffects.Render("wipe", "  \n", Seeded()));
    }

    private static void RenderEffectArguments()
    {
        IReadOnlyList<string> frames = TextEffects.Render(
            "wipe",
            "Hi\n",
            new TextEffectOptions
            {
                Seed = 1,
                FrameRate = 0,
                CanvasWidth = 20,
                CanvasHeight = 8,
                EffectArguments = ["--wipe-delay", "2"],
            },
            maxFrames: 2);
        Harness.AssertEqual("count", 2, frames.Count);
    }

    private static void RunToStream()
    {
        using var stdout = new MemoryStream();
        RunOutcome outcome = TextEffects.Run("wipe", "Hi\n", stdout, Seeded());
        Harness.AssertEqual("outcome", RunOutcome.Complete, outcome);
        Harness.AssertTrue("wrote", stdout.Length > 0);
        string text = Encoding.UTF8.GetString(stdout.ToArray());
        Harness.AssertTrue("has text", text.Contains('H') || text.Contains('i'));
    }
}
