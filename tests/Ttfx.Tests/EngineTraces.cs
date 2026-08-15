using System;
using System.Collections.Generic;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// Engine state-machine traces vs the reference fixture
/// (tests/Ttfx.Tests/fixtures/engine_traces.txt). Scenarios transcribed from
/// <c>tests/engine_traces.rs</c> are reference-verified. The path-reactivation
/// and scene-overwrite scenarios at the end of the fixture are C# self-consistency
/// checks only — they have no Rust counterpart in engine_traces.rs; they guard
/// rebase/overwrite behavior transcribed from ctx.rs but are not oracle-verified.
/// </summary>
internal static class EngineTraces
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("engine_traces.txt state-machine traces", EngineTracesMatchPython);
    }

    private static EngineWorld MakeCtx()
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 20,
            CanvasHeight = 10,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        var world = EngineWorld.New(
            "abcdef\nghijkl",
            config,
            Rng.Seeded(0),
            Clock.VirtualWithFrameRate(60));
        world.EventLog = new List<string>();
        return world;
    }

    private static List<CharId> Chars(EngineWorld world, int n)
    {
        List<CharId> all = world.Terminal.GetCharacters(
            Rng.Seeded(0),
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        return all.GetRange(0, n);
    }

    private static string Esc(string s) => s.Replace("\x1b", "\\e");

    private static void Snapshot(EngineWorld world, List<string> log, long tick, IReadOnlyList<CharId> ids)
    {
        log.AddRange(world.EventLog!);
        world.EventLog!.Clear();
        int count = ids.Count;
        for (int i = 0; i < count; i++)
        {
            CharId id = ids[i];
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string ap = ch.Motion.ActivePath ?? "-";
            string sc = ch.Animation.ActiveScene ?? "-";
            string active = ch.IsActive() ? "True" : "False";
            log.Add(
                $"tick={tick} char={ch.CharacterId} coord={ch.Motion.CurrentCoord.Column},{ch.Motion.CurrentCoord.Row} layer={ch.Layer} path={ap} scene={sc} vis={Esc(ch.Animation.CurrentCharacterVisual.FormattedSymbol.AsStr())} active={active}");
        }
    }

    private static void RunTicks(EngineWorld world, List<string> log, IReadOnlyList<CharId> ids, long n, long start)
    {
        var active = new SortedSet<CharId>(Comparer<CharId>.Create((a, b) =>
        {
            uint ca = world.Terminal.Arena[(int)a.Value].CharacterId;
            uint cb = world.Terminal.Arena[(int)b.Value].CharacterId;
            return ca.CompareTo(cb);
        }));
        int idCount = ids.Count;
        for (int i = 0; i < idCount; i++)
        {
            active.Add(ids[i]);
        }

        for (long tick = start; tick < start + n; tick++)
        {
            CharId[] snapshotIds = new CharId[active.Count];
            active.CopyTo(snapshotIds);
            int snapCount = snapshotIds.Length;
            for (int i = 0; i < snapCount; i++)
            {
                world.Tick(NoopHooks.Instance, snapshotIds[i]);
            }

            var inactive = new List<CharId>();
            foreach (CharId id in active)
            {
                if (!world.Terminal.Arena[(int)id.Value].IsActive())
                {
                    inactive.Add(id);
                }
            }

            int inactiveCount = inactive.Count;
            for (int i = 0; i < inactiveCount; i++)
            {
                active.Remove(inactive[i]);
            }

            Snapshot(world, log, tick, ids);
        }
    }

    private static void Flush(EngineWorld world, List<string> log)
    {
        log.AddRange(world.EventLog!);
        world.EventLog!.Clear();
    }

    private static void ScenarioMotionBasic(List<string> log)
    {
        log.Add("=== scenario_motion_basic ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 2);
        CharId a = ids[0];
        CharId b = ids[1];
        {
            Motion motion = world.Terminal.Arena[(int)a.Value].Motion;
            motion.NewPath(0.7, null, null, 0, false, "pa");
            Path pa = motion.Paths.Get("pa")!;
            pa.NewWaypoint(Coord.New(15, 8), null, "");
            pa.NewWaypoint(Coord.New(18, 2), new[] { Coord.New(1, 1) }, "");
            motion = world.Terminal.Arena[(int)b.Value].Motion;
            motion.NewPath(1.3, Easing.OutBack, null, 0, false, "pb");
            motion.Paths.Get("pb")!.NewWaypoint(Coord.New(3, 9), null, "");
        }

        world.ActivatePath(NoopHooks.Instance, a, "pa");
        world.ActivatePath(NoopHooks.Instance, b, "pb");
        RunTicks(world, log, ids, 30, 0);
        Flush(world, log);
    }

    private static void ScenarioHoldAndLoop(List<string> log)
    {
        log.Add("=== scenario_hold_and_loop ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 2);
        CharId a = ids[0];
        CharId b = ids[1];
        {
            Motion motion = world.Terminal.Arena[(int)a.Value].Motion;
            motion.NewPath(2.0, null, null, 3, false, "hold");
            motion.Paths.Get("hold")!.NewWaypoint(Coord.New(10, 5), null, "");
            motion = world.Terminal.Arena[(int)b.Value].Motion;
            motion.NewPath(2.0, null, null, 0, true, "looper");
            Path pb = motion.Paths.Get("looper")!;
            pb.NewWaypoint(Coord.New(6, 3), null, "");
            pb.NewWaypoint(Coord.New(9, 6), null, "");
        }

        world.ActivatePath(NoopHooks.Instance, a, "hold");
        world.ActivatePath(NoopHooks.Instance, b, "looper");
        RunTicks(world, log, ids, 20, 0);
        Flush(world, log);
    }

    private static void ScenarioChainedPathsAndEvents(List<string> log)
    {
        log.Add("=== scenario_chained_paths_and_events ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 1);
        CharId a = ids[0];
        {
            Motion motion = world.Terminal.Arena[(int)a.Value].Motion;
            motion.NewPath(1.5, null, null, 0, false, "p1");
            motion.Paths.Get("p1")!.NewWaypoint(Coord.New(5, 5), null, "");
            motion.NewPath(1.5, null, 2, 0, false, "p2");
            motion.Paths.Get("p2")!.NewWaypoint(Coord.New(10, 2), null, "");
            motion.NewPath(1.5, null, null, 0, false, "p3");
            motion.Paths.Get("p3")!.NewWaypoint(Coord.New(1, 1), null, "");
        }

        world.ChainPaths(a, new[] { "p1", "p2", "p3" }, false);
        world.RegisterEvent(
            a,
            Event.PathComplete,
            new CallerKey.Path("p3"),
            new EventAction.SetCoordinate(Coord.New(19, 9)));
        world.RegisterEvent(a, Event.PathHolding, new CallerKey.Path("p1"), new EventAction.SetLayer(7));
        world.ActivatePath(NoopHooks.Instance, a, "p1");
        RunTicks(world, log, ids, 25, 0);
        Flush(world, log);
    }

    private static void ScenarioScenes(List<string> log)
    {
        log.Add("=== scenario_scenes ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 3);
        CharId a = ids[0];
        CharId b = ids[1];
        CharId c = ids[2];
        {
            EffectCharacter ch = world.Terminal.Arena[(int)a.Value];
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.NewScene(false, null, null, "plain", uses);
            Scene scene = ch.Animation.Scenes.Get("plain")!;
            scene.AddFrame(
                "X",
                2,
                new VisualParams
                {
                    Colors = ColorPair.New(Color.FromHex("ff0000"), null),
                });
            scene.AddFrame(
                "Y",
                3,
                new VisualParams
                {
                    Colors = ColorPair.New(Color.FromHex("00ff00"), Color.FromXterm(21)),
                });
            scene.AddFrame("Z", 1, new VisualParams { Bold = true });
        }

        world.ActivateScene(NoopHooks.Instance, a, "plain");
        {
            EffectCharacter ch = world.Terminal.Arena[(int)b.Value];
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.NewScene(true, null, null, "looping", uses);
            Scene scene = ch.Animation.Scenes.Get("looping")!;
            scene.AddFrame("1", 2, new VisualParams());
            scene.AddFrame("2", 2, new VisualParams());
        }

        world.ActivateScene(NoopHooks.Instance, b, "looping");
        {
            EffectCharacter ch = world.Terminal.Arena[(int)c.Value];
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.NewScene(false, null, Easing.InOutCubic, "eased", uses);
            Gradient grad = Gradient.WithSteps(
                new[] { Color.FromHex("000000"), Color.FromHex("ffffff") },
                8,
                false);
            Scene scene = ch.Animation.Scenes.Get("eased")!;
            scene.ApplyGradientToSymbols(new[] { "*", "+", "o" }, 2, grad, null);
        }

        world.ActivateScene(NoopHooks.Instance, c, "eased");
        RunTicks(world, log, ids, 24, 0);
        Flush(world, log);
    }

    private static void ScenarioSyncedScene(List<string> log)
    {
        log.Add("=== scenario_synced_scene ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 2);
        (SyncMetric Sync, string Pid)[] specs = { (SyncMetric.Step, "sp"), (SyncMetric.Distance, "dp") };
        for (int idx = 0; idx < specs.Length; idx++)
        {
            CharId id = ids[idx];
            string pid = specs[idx].Pid;
            string sceneId = $"sync_{pid}";
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.NewPath(0.9, null, null, 0, false, pid);
                Path path = ch.Motion.Paths.Get(pid)!;
                path.NewWaypoint(Coord.New(16, 9), null, "");
                path.NewWaypoint(Coord.New(2, 2), null, "");
                bool uses = ch.UsesInputPreexistingColors;
                ch.Animation.NewScene(false, specs[idx].Sync, null, sceneId, uses);
                Scene scene = ch.Animation.Scenes.Get(sceneId)!;
                string[] symbols = { "a", "b", "c", "d", "e", "f", "g", "h" };
                for (int s = 0; s < symbols.Length; s++)
                {
                    scene.AddFrame(symbols[s], 1, new VisualParams());
                }
            }

            world.ActivatePath(NoopHooks.Instance, id, pid);
            world.ActivateScene(NoopHooks.Instance, id, sceneId);
        }

        RunTicks(world, log, ids, 30, 0);
        Flush(world, log);
    }

    private static void ScenarioSceneEventsAndResume(List<string> log)
    {
        log.Add("=== scenario_scene_events_and_resume ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 1);
        CharId a = ids[0];
        {
            EffectCharacter ch = world.Terminal.Arena[(int)a.Value];
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.NewScene(false, null, null, "s1", uses);
            Scene s1 = ch.Animation.Scenes.Get("s1")!;
            s1.AddFrame("A", 3, new VisualParams());
            s1.AddFrame("B", 3, new VisualParams());
            ch.Animation.NewScene(false, null, null, "s2", uses);
            ch.Animation.Scenes.Get("s2")!.AddFrame("C", 2, new VisualParams());
            ch.Motion.NewPath(1.0, null, null, 0, false, "mover");
            ch.Motion.Paths.Get("mover")!.NewWaypoint(Coord.New(8, 8), null, "");
        }

        world.RegisterEvent(
            a,
            Event.SceneComplete,
            new CallerKey.Scene("s1"),
            new EventAction.ActivateScene("s2"));
        world.RegisterEvent(
            a,
            Event.SceneComplete,
            new CallerKey.Scene("s2"),
            new EventAction.ActivatePath("mover"));
        world.ActivateScene(NoopHooks.Instance, a, "s1");
        RunTicks(world, log, ids, 2, 0);
        world.ActivateScene(NoopHooks.Instance, a, "s1");
        RunTicks(world, log, ids, 20, 2);
        Flush(world, log);
    }

    /// <summary>
    /// Self-consistency only (no Rust engine_traces.rs scenario). Exercises
    /// path re-activation rebase from EngineWorld.ActivatePath / ctx.rs.
    /// </summary>
    private static void ScenarioPathReactivation(List<string> log)
    {
        log.Add("=== scenario_path_reactivation ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 1);
        CharId a = ids[0];
        {
            Motion motion = world.Terminal.Arena[(int)a.Value].Motion;
            motion.NewPath(1.0, null, null, 0, false, "move");
            motion.Paths.Get("move")!.NewWaypoint(Coord.New(12, 4), null, "");
        }

        world.ActivatePath(NoopHooks.Instance, a, "move");
        RunTicks(world, log, ids, 3, 0);
        world.ActivatePath(NoopHooks.Instance, a, "move");
        RunTicks(world, log, ids, 8, 3);
        Flush(world, log);
    }

    /// <summary>
    /// Self-consistency only (no Rust engine_traces.rs scenario). Exercises
    /// Animation.NewScene silent overwrite (faithful to upstream).
    /// </summary>
    private static void ScenarioSceneOverwrite(List<string> log)
    {
        log.Add("=== scenario_scene_overwrite ===");
        EngineWorld world = MakeCtx();
        List<CharId> ids = Chars(world, 1);
        CharId a = ids[0];
        {
            EffectCharacter ch = world.Terminal.Arena[(int)a.Value];
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.NewScene(false, null, null, "dup", uses);
            ch.Animation.Scenes.Get("dup")!.AddFrame("A", 2, new VisualParams());
            ch.Animation.NewScene(false, null, null, "dup", uses);
            ch.Animation.Scenes.Get("dup")!.AddFrame("B", 2, new VisualParams());
        }

        world.ActivateScene(NoopHooks.Instance, a, "dup");
        RunTicks(world, log, ids, 6, 0);
        Flush(world, log);
    }

    private static void EngineTracesMatchPython()
    {
        string fixture = System.IO.File.ReadAllText(
            System.IO.Path.Combine(Harness.FindRepoRoot(), "tests", "Ttfx.Tests", "fixtures", "engine_traces.txt"));
        string[] expected = fixture.Replace("\r\n", "\n").Split('\n');
        if (expected.Length > 0 && expected[expected.Length - 1].Length == 0)
        {
            Array.Resize(ref expected, expected.Length - 1);
        }

        var log = new List<string>();
        ScenarioMotionBasic(log);
        ScenarioHoldAndLoop(log);
        ScenarioChainedPathsAndEvents(log);
        ScenarioScenes(log);
        ScenarioSyncedScene(log);
        ScenarioSceneEventsAndResume(log);
        ScenarioPathReactivation(log);
        ScenarioSceneOverwrite(log);

        int mismatches = 0;
        int limit = Math.Max(expected.Length, log.Count);
        for (int i = 0; i < limit; i++)
        {
            string e = i < expected.Length ? expected[i] : "<missing>";
            string a = i < log.Count ? log[i] : "<missing>";
            if (e != a)
            {
                if (mismatches < 8)
                {
                    Console.Error.WriteLine($"line {i}:\n  expected: {e}\n    actual: {a}");
                }

                mismatches++;
            }
        }

        Harness.AssertEqual("engine_traces mismatches", 0, mismatches);
    }
}
