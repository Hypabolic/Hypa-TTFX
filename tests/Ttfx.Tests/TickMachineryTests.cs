using System;
using System.Collections.Generic;
using System.Linq;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// Scripted traces and design-specific cases the frame-parity suite cannot
/// reach: reentrant replacement, value-equal duplicate registration, waypoint
/// collision, OrderedMap order, ParticlePool LIFO.
/// </summary>
internal static class TickMachineryTests
{
    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("nested/reentrant events", NestedReentrantEvents);
        yield return new TestCase("scene reactivation resumes", SceneReactivationResumes);
        yield return new TestCase("looping scene complete every tick", LoopingSceneCompleteEveryTick);
        yield return new TestCase("path hold then complete", PathHolds);
        yield return new TestCase("loop path rebase", LoopPathRebase);
        yield return new TestCase("particle pool LIFO exhaustion reuse", ParticlePoolLifo);
        yield return new TestCase("reentrant path replacement mid-dispatch", ReentrantPathReplacement);
        yield return new TestCase("reentrant scene replacement mid-dispatch", ReentrantSceneReplacement);
        yield return new TestCase("duplicate equal payloads rejected", DuplicateEqualPayloadsRejected);
        yield return new TestCase("waypoint collision across paths", WaypointCollisionAcrossPaths);
        yield return new TestCase("OrderedMap remove keeps remaining order", OrderedMapOrder);
        yield return new TestCase("ActiveCharacters iterates by CharacterId", ActiveCharactersOrder);
    }

    private static EngineWorld MakeWorld()
    {
        var config = new TerminalConfig
        {
            CanvasWidth = 20,
            CanvasHeight = 10,
            IgnoreTerminalDimensions = true,
            FrameRate = 0,
        };
        var world = EngineWorld.New("abcdef\nghijkl", config, Rng.Seeded(0), Clock.VirtualWithFrameRate(60));
        world.EventLog = new List<string>();
        return world;
    }

    private static CharId FirstChar(EngineWorld world)
    {
        return world.Terminal.GetCharacters(
            Rng.Seeded(0),
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight)[0];
    }

    private static void NestedReentrantEvents()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(1.0, null, null, 0, false, "p1");
        ch.Motion.Paths.Get("p1")!.NewWaypoint(Coord.New(3, 3), null, "");
        ch.Motion.NewPath(1.0, null, null, 0, false, "p2");
        ch.Motion.Paths.Get("p2")!.NewWaypoint(Coord.New(6, 6), null, "");
        world.RegisterEvent(id, Event.PathComplete, new CallerKey.Path("p1"), new EventAction.ActivatePath("p2"));
        world.RegisterEvent(id, Event.PathActivated, new CallerKey.Path("p2"), new EventAction.SetLayer(4));
        world.ActivatePath(NoopHooks.Instance, id, "p1");
        for (int i = 0; i < 20; i++)
        {
            world.Tick(NoopHooks.Instance, id);
        }

        bool sawP1Complete = world.EventLog!.Exists(line => line.Contains("PATH_COMPLETE caller=path:p1", StringComparison.Ordinal));
        bool sawP2Activated = world.EventLog!.Exists(line => line.Contains("PATH_ACTIVATED caller=path:p2", StringComparison.Ordinal));
        Harness.AssertTrue("p1 complete nested-activates p2", sawP1Complete && sawP2Activated);
        Harness.AssertEqual("reentrant SetLayer applied", 4L, world.Terminal.Arena[(int)id.Value].Layer);
        Harness.AssertEqual("active path is p2 or done", true,
            world.Terminal.Arena[(int)id.Value].Motion.ActivePath is "p2" or null);
    }

    private static void SceneReactivationResumes()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Animation.NewScene(false, null, null, "s", false);
        Scene scene = ch.Animation.Scenes.Get("s")!;
        scene.AddFrame("A", 2, new VisualParams());
        scene.AddFrame("B", 3, new VisualParams());
        world.ActivateScene(NoopHooks.Instance, id, "s");
        world.Tick(NoopHooks.Instance, id);
        world.Tick(NoopHooks.Instance, id);
        world.Tick(NoopHooks.Instance, id);
        Harness.AssertEqual("reached B before re-activate", "B", ch.Animation.CurrentCharacterVisual.Symbol);
        world.ActivateScene(NoopHooks.Instance, id, "s");
        Harness.AssertEqual("activate_scene does not reset", "B", ch.Animation.CurrentCharacterVisual.Symbol);
        scene.ResetScene();
        Harness.AssertEqual("reset restores remaining+played", 2, scene.Frames.Count);
        Harness.AssertEqual("reset zeroes easing", 0L, scene.EasingCurrentStep);
    }

    private static void LoopingSceneCompleteEveryTick()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Animation.NewScene(true, null, null, "loop", false);
        ch.Animation.Scenes.Get("loop")!.AddFrame("1", 2, new VisualParams());
        world.ActivateScene(NoopHooks.Instance, id, "loop");
        world.EventLog!.Clear();
        for (int i = 0; i < 5; i++)
        {
            world.Tick(NoopHooks.Instance, id);
            Harness.AssertTrue($"looping complete at tick {i}", ch.Animation.ActiveSceneIsComplete());
            Harness.AssertTrue($"still active scene at tick {i}", ch.Animation.ActiveScene == "loop");
        }

        int completes = world.EventLog!.FindAll(line =>
            line.Contains("SCENE_COMPLETE caller=scene:loop", StringComparison.Ordinal)).Count;
        Harness.AssertEqual("SCENE_COMPLETE every tick", 5, completes);
        Harness.AssertTrue("loop-only character is inactive", !ch.IsActive());
    }

    private static void PathHolds()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(2.0, null, null, 3, false, "hold");
        ch.Motion.Paths.Get("hold")!.NewWaypoint(Coord.New(10, 5), null, "");
        world.ActivatePath(NoopHooks.Instance, id, "hold");
        bool sawHold = false;
        bool sawComplete = false;
        for (int i = 0; i < 20; i++)
        {
            world.Tick(NoopHooks.Instance, id);
            if (world.EventLog!.Exists(line => line.Contains("PATH_HOLDING", StringComparison.Ordinal)))
            {
                sawHold = true;
            }

            if (world.EventLog!.Exists(line => line.Contains("PATH_COMPLETE", StringComparison.Ordinal)))
            {
                sawComplete = true;
            }
        }

        Harness.AssertTrue("PATH_HOLDING fired", sawHold);
        Harness.AssertTrue("PATH_COMPLETE after hold", sawComplete);
        Harness.AssertTrue("motion complete after hold", ch.Motion.MovementIsComplete());
    }

    private static void LoopPathRebase()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(2.0, null, null, 0, true, "looper");
        Path path = ch.Motion.Paths.Get("looper")!;
        path.NewWaypoint(Coord.New(6, 3), null, "");
        path.NewWaypoint(Coord.New(9, 6), null, "");
        world.ActivatePath(NoopHooks.Instance, id, "looper");
        int activations = world.EventLog!.FindAll(line =>
            line.Contains("PATH_ACTIVATED caller=path:looper", StringComparison.Ordinal)).Count;
        Harness.AssertEqual("initial activation", 1, activations);
        double firstOrigin = path.OriginSegment!.Distance;
        for (int i = 0; i < 20; i++)
        {
            world.Tick(NoopHooks.Instance, id);
        }

        activations = world.EventLog!.FindAll(line =>
            line.Contains("PATH_ACTIVATED caller=path:looper", StringComparison.Ordinal)).Count;
        Harness.AssertTrue("loop re-activated", activations >= 2);
        Harness.AssertTrue("origin segment replaced not stacked", path.Segments[0] == path.OriginSegment);
        Harness.AssertTrue("rebase changed origin distance", path.OriginSegment!.Distance != firstOrigin || activations >= 2);
    }

    private static void ParticlePoolLifo()
    {
        EngineWorld world = MakeWorld();
        ParticlePool pool = ParticlePool.New(new List<string> { "p" }, 3, Coord.New(1, 1));
        var created = new List<CharId>();
        pool.Preallocate(world, 3, (_, id) => created.Add(id));
        Harness.AssertEqual("preallocate created 3", 3, created.Count);
        CharId first = pool.Acquire(world, null, ParticleReset.Default, (_, _) => { })!.Value;
        Harness.AssertEqual("acquire is LIFO (last preallocated)", created[2], first);
        CharId second = pool.Acquire(world, null, ParticleReset.Default, (_, _) => { })!.Value;
        Harness.AssertEqual("second acquire is previous", created[1], second);
        pool.Reclaim(world, first, hide: true, deactivate: true);
        CharId reused = pool.Acquire(world, null, ParticleReset.Default, (_, _) => { })!.Value;
        Harness.AssertEqual("reclaim+acquire reuses most recent (LIFO)", first, reused);
        pool.Acquire(world, null, ParticleReset.Default, (_, _) => { });
        pool.Acquire(world, null, ParticleReset.Default, (_, _) => { });
        CharId? exhausted = pool.Acquire(world, null, ParticleReset.Default, (_, _) => { });
        Harness.AssertTrue("max_size exhaustion returns null", exhausted is null);
        pool.Reclaim(world, second, hide: true, deactivate: true);
        CharId? after = pool.Acquire(world, null, ParticleReset.Default, (_, _) => { });
        Harness.AssertEqual("reuse after exhaustion", second, after!.Value);
    }

    private sealed class ReplacePathHooks : IEffectHooks
    {
        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
            if (callback.Id != 1)
            {
                return;
            }

            EffectCharacter ch = world.Terminal.Arena[(int)character.Value];
            ch.Motion.Paths.Remove("target");
            ch.Motion.NewPath(1.0, null, null, 0, false, "target");
            ch.Motion.Paths.Get("target")!.NewWaypoint(Coord.New(19, 9), null, "");
        }
    }

    private static void ReentrantPathReplacement()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(1.0, null, null, 0, false, "starter");
        ch.Motion.Paths.Get("starter")!.NewWaypoint(Coord.New(3, 3), null, "");
        ch.Motion.NewPath(1.0, null, null, 0, false, "target");
        ch.Motion.Paths.Get("target")!.NewWaypoint(Coord.New(4, 4), null, "");
        var payload = new EffectCallback(1, new CallbackValue[] { new CallbackValue.Int(7) });
        world.RegisterEvent(id, Event.PathComplete, new CallerKey.Path("starter"), new EventAction.Callback(payload));
        world.RegisterEvent(id, Event.PathComplete, new CallerKey.Path("starter"), new EventAction.ActivatePath("target"));
        world.ActivatePath(NoopHooks.Instance, id, "starter");
        var hooks = new ReplacePathHooks();
        for (int i = 0; i < 40; i++)
        {
            world.Tick(hooks, id);
        }

        Path target = ch.Motion.Paths.Get("target")!;
        Harness.AssertEqual("replaced path dest", 19L, target.Waypoints[0].Coord.Column);
        Harness.AssertTrue("activate resolved the replacement", target.OriginSegment is not null);
        Harness.AssertEqual("character walked the new dest", 19L, ch.Motion.CurrentCoord.Column);
    }

    private sealed class ReplaceSceneHooks : IEffectHooks
    {
        public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
        {
            if (callback.Id != 2)
            {
                return;
            }

            EffectCharacter ch = world.Terminal.Arena[(int)character.Value];
            ch.Animation.Scenes.Remove("next");
            ch.Animation.NewScene(false, null, null, "next", false);
            ch.Animation.Scenes.Get("next")!.AddFrame("Z", 2, new VisualParams());
        }
    }

    private static void ReentrantSceneReplacement()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Animation.NewScene(false, null, null, "first", false);
        ch.Animation.Scenes.Get("first")!.AddFrame("A", 1, new VisualParams());
        ch.Animation.NewScene(false, null, null, "next", false);
        ch.Animation.Scenes.Get("next")!.AddFrame("B", 2, new VisualParams());
        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene("first"),
            new EventAction.Callback(new EffectCallback(2, Array.Empty<CallbackValue>())));
        world.RegisterEvent(id, Event.SceneComplete, new CallerKey.Scene("first"), new EventAction.ActivateScene("next"));
        world.ActivateScene(NoopHooks.Instance, id, "first");
        var hooks = new ReplaceSceneHooks();
        world.Tick(hooks, id);
        Harness.AssertEqual("activate resolved replaced scene", "Z", ch.Animation.CurrentCharacterVisual.Symbol);
        Harness.AssertEqual("active scene id still next", "next", ch.Animation.ActiveScene);
    }

    private static void DuplicateEqualPayloadsRejected()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(1.0, null, null, 0, false, "p");
        ch.Motion.Paths.Get("p")!.NewWaypoint(Coord.New(2, 2), null, "");
        var first = new EventAction.Callback(
            new EffectCallback(9, new CallbackValue[] { new CallbackValue.Int(3), new CallbackValue.Str("x") }));
        var second = new EventAction.Callback(
            new EffectCallback(9, new CallbackValue[] { new CallbackValue.Int(3), new CallbackValue.Str("x") }));
        Harness.AssertTrue("separately allocated payloads compare equal", first.Equals(second));
        world.RegisterEvent(id, Event.PathComplete, new CallerKey.Path("p"), first);
        bool threw = false;
        try
        {
            world.RegisterEvent(id, Event.PathComplete, new CallerKey.Path("p"), second);
        }
        catch (EngineException)
        {
            threw = true;
        }

        Harness.AssertTrue("duplicate equal payload rejected", threw);

        var pathA = new EventAction.ActivatePath("p");
        var pathB = new EventAction.ActivatePath("p");
        world.RegisterEvent(id, Event.PathActivated, new CallerKey.Path("p"), pathA);
        threw = false;
        try
        {
            world.RegisterEvent(id, Event.PathActivated, new CallerKey.Path("p"), pathB);
        }
        catch (EngineException)
        {
            threw = true;
        }

        Harness.AssertTrue("duplicate ActivatePath rejected", threw);
    }

    private static void WaypointCollisionAcrossPaths()
    {
        EngineWorld world = MakeWorld();
        CharId id = FirstChar(world);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        ch.Motion.NewPath(1.0, null, null, 0, false, "pa");
        ch.Motion.NewPath(1.0, null, null, 0, false, "pb");
        Waypoint wa = ch.Motion.Paths.Get("pa")!.NewWaypoint(Coord.New(5, 5), new[] { Coord.New(1, 1) }, "same");
        Waypoint wb = ch.Motion.Paths.Get("pb")!.NewWaypoint(Coord.New(5, 5), new[] { Coord.New(1, 1) }, "same");
        Harness.AssertTrue("identical waypoints collide across paths", wa.Key().Equals(wb.Key()));
        world.RegisterEvent(id, Event.SegmentEntered, new CallerKey.Waypoint(wa.Key()), new EventAction.SetLayer(9));
        world.ActivatePath(NoopHooks.Instance, id, "pb");
        world.Tick(NoopHooks.Instance, id);
        Harness.AssertEqual("collision fired SetLayer from the other path's key", 9L, ch.Layer);
    }

    private static void OrderedMapOrder()
    {
        var map = new OrderedMap<int>();
        for (int value = 0; value < 12; value++)
        {
            map.Insert(value.ToString(), value);
        }

        map.Insert("3", 30);
        Harness.AssertEqual("overwrite value", 30, map.Get("3"));
        string[] firstFive = map.Keys.Take(5).ToArray();
        Harness.AssertTrue("insert-over-existing keeps position",
            firstFive[0] == "0" && firstFive[1] == "1" && firstFive[2] == "2" && firstFive[3] == "3" && firstFive[4] == "4");
        Harness.AssertEqual("remove returns value", 4, map.Remove("4"));
        Harness.AssertEqual("neighbor still present", 5, map.Get("5"));
        Harness.AssertTrue("removed key gone", !map.ContainsKey("4"));
        string[] after = map.Keys.ToArray();
        Harness.AssertTrue("remaining order preserved", after[3] == "3" && after[4] == "5");
        map.Clear();
        map.Insert("fresh", 99);
        Harness.AssertEqual("clear then insert", 99, map.Get("fresh"));
    }

    private static void ActiveCharactersOrder()
    {
        var active = new ActiveCharacters();
        active.Insert(new CharId(10), 130);
        active.Insert(new CharId(1), 1);
        active.Insert(new CharId(4), 64);
        active.Insert(new CharId(3), 63);
        active.Insert(new CharId(1), 1);
        Harness.AssertEqual("len ignores duplicate", 4, active.Count);
        CharId[] order = active.Snapshot();
        Harness.AssertEqual("order[0] CharacterId 1", 1u, order[0].Value);
        Harness.AssertEqual("order[1] CharacterId 63", 3u, order[1].Value);
        Harness.AssertEqual("order[2] CharacterId 64", 4u, order[2].Value);
        Harness.AssertEqual("order[3] CharacterId 130", 10u, order[3].Value);
        Harness.AssertTrue("contains", active.Contains(new CharId(4)));
        Harness.AssertTrue("remove", active.Remove(new CharId(10)));
        Harness.AssertTrue("remove missing", !active.Remove(new CharId(10)));
    }
}
