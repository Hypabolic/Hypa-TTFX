using System;
using System.Collections.Generic;
using System.Text;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Effect-side hook for CALLBACK actions. The effect struct and the EngineWorld
/// are disjoint ownership trees, so the callback may freely recurse into
/// engine calls with the provided world.
/// Transcribed from <c>engine/ctx.rs</c> EffectHooks.
/// </summary>
public interface IEffectHooks
{
    void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback);
}

/// <summary>
/// One effect: Build() once (upstream iterator __init__/build), then
/// NextFrame() until null (upstream __next__/StopIteration). Every effect
/// also implements IEffectHooks for its registered callbacks.
/// Transcribed from <c>engine/effect.rs</c> Effect.
/// </summary>
public interface IEffect : IEffectHooks
{
    void Build(EngineWorld world);
    string? NextFrame(EngineWorld world);
}

/// <summary>Hooks implementation for engine-internal use (no effect callbacks registered).</summary>
public sealed class NoopHooks : IEffectHooks
{
    public static readonly NoopHooks Instance = new NoopHooks();

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }
}

/// <summary>
/// EngineWorld: the mutable engine world (terminal + arena + rng + clock +
/// active characters) and every stepping routine that can fire events.
///
/// Python executes event actions synchronously at the emission point, deep in
/// the middle of Path.step / Motion.move / Animation.step_animation, and those
/// actions reentrantly mutate the same structures being stepped. To preserve
/// that observable ordering (plan.md §4.2), all stepping logic lives here:
/// state is re-fetched by id after every emission point, and segment walks are
/// index-based so reentrant list mutation behaves like Python list iteration.
/// Transcribed from <c>engine/ctx.rs</c>.
/// </summary>
public sealed class EngineWorld
{
    private const string OriginWaypointId = "origin";

    public Terminal Terminal { get; }
    public Rng Rng { get; }
    public Clock Clock { get; }
    public ActiveCharacters ActiveCharacters { get; } = new ActiveCharacters();
    public bool PreexistingColorsPresent { get; }

    /// <summary>When non-null, every event emission appends a trace line (test harness).</summary>
    public List<string>? EventLog { get; set; }

    public EngineWorld(Terminal terminal, Rng rng, Clock clock)
    {
        Terminal = terminal;
        Rng = rng;
        Clock = clock;
        PreexistingColorsPresent = terminal.PreexistingColorsPresent();
    }

    public static EngineWorld New(string inputData, TerminalConfig config, Rng rng, Clock clock)
    {
        return new EngineWorld(Terminal.New(inputData, config), rng, clock);
    }

    // ------------------------------------------------------------------
    // event dispatch (EventHandler._handle_event)
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether an emission of <paramref name="ev"/> on <paramref name="id"/> can
    /// have any observable effect — false lets hot emission sites skip building
    /// the CallerKey entirely.
    /// </summary>
    private bool ObservesEvent(CharId id, Event ev)
    {
        return EventLog is not null || Terminal.Arena[(int)id.Value].EventHandler.Subscribes(ev);
    }

    /// <summary>
    /// Execute all actions registered for (event, caller) on <paramref name="id"/>,
    /// in registration order, inline and reentrantly. The action list is indexed
    /// per iteration because a callback may append more actions to it.
    /// </summary>
    public void HandleEvent(IEffectHooks hooks, CharId id, Event ev, CallerKey caller)
    {
        if (EventLog is not null)
        {
            uint characterId = Terminal.Arena[(int)id.Value].CharacterId;
            string eventName = ev switch
            {
                Event.SegmentEntered => "SEGMENT_ENTERED",
                Event.SegmentExited => "SEGMENT_EXITED",
                Event.PathActivated => "PATH_ACTIVATED",
                Event.PathComplete => "PATH_COMPLETE",
                Event.PathHolding => "PATH_HOLDING",
                Event.SceneActivated => "SCENE_ACTIVATED",
                Event.SceneComplete => "SCENE_COMPLETE",
                _ => ev.ToString(),
            };
            string callerLabel = caller switch
            {
                CallerKey.Path path => $"path:{path.Id}",
                CallerKey.Waypoint wp => $"wp:{wp.Key.WaypointId}",
                CallerKey.Scene scene => $"scene:{scene.Id}",
                _ => "?",
            };
            EventLog.Add($"EVENT char={characterId} {eventName} caller={callerLabel}");
        }

        int? entryIndex = Terminal.Arena[(int)id.Value].EventHandler.ActionsIndex(ev, caller);
        if (entryIndex is null)
        {
            return;
        }

        int actionIndex = 0;
        // Explicit loop re-reading actions.Count each pass (ctx.rs:169-204):
        // a reentrant callback may append more actions to this entry.
        while (true)
        {
            EventAction action;
            {
                EventHandler handler = Terminal.Arena[(int)id.Value].EventHandler;
                IReadOnlyList<EventAction> actions = handler.Actions(entryIndex.Value);
                if (actionIndex >= actions.Count)
                {
                    break;
                }

                action = actions[actionIndex];
            }

            switch (action)
            {
                case EventAction.ActivatePath activatePath:
                    ActivatePath(hooks, id, activatePath.PathId);
                    break;
                case EventAction.ActivateScene activateScene:
                    ActivateScene(hooks, id, activateScene.SceneId);
                    break;
                case EventAction.DeactivatePath deactivatePath:
                    Terminal.Arena[(int)id.Value].Motion.DeactivatePath(deactivatePath.PathId);
                    break;
                case EventAction.DeactivateScene deactivateScene:
                    DeactivateScene(id, deactivateScene.SceneId);
                    break;
                case EventAction.ResetAppearance:
                {
                    EffectCharacter ch = Terminal.Arena[(int)id.Value];
                    string inputSymbol = ch.InputSymbol;
                    bool uses = ch.UsesInputPreexistingColors;
                    ch.Animation.SetAppearance(inputSymbol, uses, inputSymbol, null);
                    break;
                }
                case EventAction.SetLayer setLayer:
                    Terminal.Arena[(int)id.Value].Layer = setLayer.Layer;
                    break;
                case EventAction.SetCoordinate setCoordinate:
                    Terminal.Arena[(int)id.Value].Motion.CurrentCoord = setCoordinate.Coord;
                    break;
                case EventAction.Callback callback:
                    hooks.DispatchCallback(this, id, callback.Value);
                    break;
            }

            actionIndex += 1;
        }
    }

    /// <summary>
    /// EventHandler.register_event: resolves/validates existence for id-based
    /// callers and targets, rejects duplicates.
    /// </summary>
    public void RegisterEvent(CharId id, Event ev, CallerKey caller, EventAction action)
    {
        EffectCharacter ch = Terminal.Arena[(int)id.Value];
        switch (caller)
        {
            case CallerKey.Path path when !ch.Motion.Paths.ContainsKey(path.Id):
                throw new EngineException($"path not found: {path.Id}");
            case CallerKey.Scene scene when !ch.Animation.Scenes.ContainsKey(scene.Id):
                throw new EngineException($"scene not found: {scene.Id}");
        }

        switch (action)
        {
            case EventAction.ActivatePath activatePath when !ch.Motion.Paths.ContainsKey(activatePath.PathId):
                throw new EngineException($"path not found: {activatePath.PathId}");
            case EventAction.DeactivatePath { PathId: string pid } when !ch.Motion.Paths.ContainsKey(pid):
                throw new EngineException($"path not found: {pid}");
            case EventAction.ActivateScene activateScene when !ch.Animation.Scenes.ContainsKey(activateScene.SceneId):
                throw new EngineException($"scene not found: {activateScene.SceneId}");
            case EventAction.DeactivateScene { SceneId: string sid } when !ch.Animation.Scenes.ContainsKey(sid):
                throw new EngineException($"scene not found: {sid}");
        }

        ch.EventHandler.Push(ev, caller, action);
    }

    // ------------------------------------------------------------------
    // motion (Motion.activate_path / Path.step / Motion.move)
    // ------------------------------------------------------------------

    /// <summary>Motion.activate_path.</summary>
    public void ActivatePath(IEffectHooks hooks, CharId id, string pathId)
    {
        EffectCharacter ch = Terminal.Arena[(int)id.Value];
        Path path = ch.Motion.Paths.Get(pathId) ?? throw new EngineInvariantException("activate_path: path not found");
        if (path.Waypoints.Count == 0)
        {
            throw new EngineInvariantException($"activate_path: empty path {pathId}");
        }

        Coord currentCoord = ch.Motion.CurrentCoord;
        Waypoint firstWaypoint = path.Waypoints[0];
        double distanceToFirstWaypoint = firstWaypoint.BezierControl is Coord[] control
            ? Geometry.FindLengthOfBezierCurve(currentCoord, control, firstWaypoint.Coord)
            : Geometry.FindLengthOfLine(currentCoord, firstWaypoint.Coord, true);
        var newOriginSegment = Segment.New(
            new Waypoint(OriginWaypointId, currentCoord, null),
            firstWaypoint,
            distanceToFirstWaypoint);

        ch.Motion.ActivePath = ch.Motion.Paths.SharedKey(pathId);
        path = ch.Motion.Paths.Get(pathId)!;
        path.TotalDistance += distanceToFirstWaypoint;
        if (path.OriginSegment is Segment origin)
        {
            path.TotalDistance -= origin.Distance;
            path.Segments[0] = newOriginSegment;
        }
        else
        {
            path.Segments.Insert(0, newOriginSegment);
        }

        path.OriginSegment = newOriginSegment;
        path.CurrentStep = 0;
        path.HoldTimeRemaining = path.HoldTime;
        path.MaxSteps = PyCompat.RoundHalfEven(path.TotalDistance / path.Speed);
        // Length captured once: flag reset does not emit (ctx.rs:284-287).
        int segmentCount = path.Segments.Count;
        for (int i = 0; i < segmentCount; i++)
        {
            path.Segments[i].EnterEventTriggered = false;
            path.Segments[i].ExitEventTriggered = false;
        }

        long? layer = path.Layer;
        if (layer is long layerValue)
        {
            Terminal.Arena[(int)id.Value].Layer = layerValue;
        }

        if (ObservesEvent(id, Event.PathActivated))
        {
            HandleEvent(hooks, id, Event.PathActivated, new CallerKey.Path(pathId));
        }
    }

    /// <summary>
    /// Path.step on the given path of <paramref name="id"/>. Index-based segment walk with
    /// re-borrow per access so reentrant mutation behaves like Python.
    ///
    /// The path's slot is resolved once and re-resolved after every emission,
    /// since only a reentrant action can move or drop it.
    /// </summary>
    private Coord PathStep(IEffectHooks hooks, CharId id, string pathId)
    {
        int slot = Terminal.Arena[(int)id.Value].Motion.Paths.Slot(pathId)
            ?? throw new EngineInvariantException("path_step: path removed mid-step");

        Path PathAt() => Terminal.Arena[(int)id.Value].Motion.Paths.At(slot);

        void ResolveSlot()
        {
            slot = Terminal.Arena[(int)id.Value].Motion.Paths.Slot(pathId)
                ?? throw new EngineInvariantException("path_step: path removed mid-step");
        }

        double distanceToTravel;
        {
            Path p = PathAt();
            if (p.MaxSteps == 0 || p.CurrentStep >= p.MaxSteps || p.TotalDistance == 0.0)
            {
                return p.Segments[p.Segments.Count - 1].End.Coord;
            }

            p.CurrentStep += 1;
            double ratio = p.CurrentStep / (double)p.MaxSteps;
            double distanceFactor = p.Ease is Easing ease ? ease.Ease(ratio) : ratio;
            double distance = distanceFactor * p.TotalDistance;
            p.LastDistanceReached = distance;
            distanceToTravel = distance;
        }

        int? activeSegmentIndex = null;
        int i = 0;
        // Explicit loop re-reading segments.Count each pass (ctx.rs:343-389):
        // a reentrant event may have replaced the segment list.
        while (true)
        {
            double segDistance;
            bool enterTriggered;
            bool exitTriggered;
            {
                Path p = PathAt();
                if (i >= p.Segments.Count)
                {
                    break;
                }

                Segment seg = p.Segments[i];
                segDistance = seg.Distance;
                enterTriggered = seg.EnterEventTriggered;
                exitTriggered = seg.ExitEventTriggered;
            }

            if (distanceToTravel <= segDistance)
            {
                activeSegmentIndex = i;
                if (!enterTriggered)
                {
                    if (ObservesEvent(id, Event.SegmentEntered))
                    {
                        WaypointKey segEndKey = PathAt().Segments[i].End.Key();
                        PathAt().Segments[i].EnterEventTriggered = true;
                        HandleEvent(hooks, id, Event.SegmentEntered, new CallerKey.Waypoint(segEndKey));
                        ResolveSlot();
                    }
                    else
                    {
                        PathAt().Segments[i].EnterEventTriggered = true;
                    }
                }

                break;
            }

            distanceToTravel -= segDistance;
            if (!enterTriggered || !exitTriggered)
            {
                bool observes = ObservesEvent(id, Event.SegmentEntered)
                    || ObservesEvent(id, Event.SegmentExited);
                if (!observes)
                {
                    Segment seg = PathAt().Segments[i];
                    seg.EnterEventTriggered = true;
                    seg.ExitEventTriggered = true;
                }
                else
                {
                    WaypointKey segEndKey = PathAt().Segments[i].End.Key();
                    if (!enterTriggered)
                    {
                        PathAt().Segments[i].EnterEventTriggered = true;
                        HandleEvent(hooks, id, Event.SegmentEntered, new CallerKey.Waypoint(segEndKey));
                        ResolveSlot();
                    }

                    if (!exitTriggered)
                    {
                        PathAt().Segments[i].ExitEventTriggered = true;
                        HandleEvent(hooks, id, Event.SegmentExited, new CallerKey.Waypoint(segEndKey));
                        ResolveSlot();
                    }
                }
            }

            i += 1;
        }

        // Python for-else: overshoot past the last waypoint re-adds the final
        // segment's distance and travels beyond it (eased overshoot).
        int resolvedIndex;
        if (activeSegmentIndex is int idx)
        {
            resolvedIndex = idx;
        }
        else
        {
            Path p = PathAt();
            resolvedIndex = p.Segments.Count - 1;
            distanceToTravel += p.Segments[resolvedIndex].Distance;
        }

        {
            Path p = PathAt();
            Segment seg = p.Segments[resolvedIndex];
            double segDistance = seg.Distance;
            double t;
            if (segDistance == 0.0)
            {
                t = 0.0;
            }
            else if (p.Ease is not null)
            {
                t = distanceToTravel / segDistance; // unclamped: eased overshoot goes past the waypoint
            }
            else
            {
                t = PyCompat.FMin(distanceToTravel / segDistance, 1.0);
            }

            return seg.End.BezierControl is Coord[] bezier
                ? Geometry.FindCoordOnBezierCurve(seg.Start.Coord, bezier, seg.End.Coord, t)
                : Geometry.FindCoordOnLine(seg.Start.Coord, seg.End.Coord, t);
        }
    }

    /// <summary>Motion.move.</summary>
    public void MotionMove(IEffectHooks hooks, CharId id)
    {
        {
            Motion motion = Terminal.Arena[(int)id.Value].Motion;
            motion.PreviousCoord = motion.CurrentCoord;
        }

        string? pathId;
        {
            Motion motion = Terminal.Arena[(int)id.Value].Motion;
            if (motion.ActivePath is string pid)
            {
                Path? active = motion.Paths.Get(pid);
                pathId = active is not null && active.Segments.Count > 0 ? pid : null;
            }
            else
            {
                pathId = null;
            }
        }

        if (pathId is null)
        {
            return;
        }

        Coord newCoord = PathStep(hooks, id, pathId);
        Terminal.Arena[(int)id.Value].Motion.CurrentCoord = newCoord;

        // Python re-reads self.active_path after step (a callback may have
        // swapped it); None here would be an upstream AttributeError.
        string activePathId = Terminal.Arena[(int)id.Value].Motion.ActivePath
            ?? throw new EngineInvariantException("active path cleared mid-move (would be an upstream crash)");
        int slot = Terminal.Arena[(int)id.Value].Motion.Paths.Slot(activePathId)
            ?? throw new EngineInvariantException("active path missing");
        Path p = Terminal.Arena[(int)id.Value].Motion.Paths.At(slot);
        long currentStep = p.CurrentStep;
        long maxSteps = p.MaxSteps;
        long holdTime = p.HoldTime;
        long holdTimeRemaining = p.HoldTimeRemaining;
        bool loop = p.Loop;
        int segmentCount = p.Segments.Count;
        if (currentStep == maxSteps)
        {
            if (holdTime != 0 && holdTimeRemaining == holdTime)
            {
                if (ObservesEvent(id, Event.PathHolding))
                {
                    HandleEvent(hooks, id, Event.PathHolding, new CallerKey.Path(activePathId));
                }

                Terminal.Arena[(int)id.Value].Motion.Paths.Get(activePathId)!.HoldTimeRemaining -= 1;
                return;
            }

            if (holdTimeRemaining != 0)
            {
                Terminal.Arena[(int)id.Value].Motion.Paths.At(slot).HoldTimeRemaining -= 1;
                return;
            }

            if (loop && segmentCount > 1)
            {
                Terminal.Arena[(int)id.Value].Motion.DeactivatePath(activePathId);
                ActivatePath(hooks, id, activePathId);
            }
            else
            {
                {
                    Motion motion = Terminal.Arena[(int)id.Value].Motion;
                    motion.CompletedPath = activePathId;
                    motion.DeactivatePath(activePathId);
                }

                if (ObservesEvent(id, Event.PathComplete))
                {
                    HandleEvent(hooks, id, Event.PathComplete, new CallerKey.Path(activePathId));
                }
            }
        }
    }

    /// <summary>Motion.chain_paths.</summary>
    public void ChainPaths(CharId id, IReadOnlyList<string> paths, bool loop)
    {
        if (paths.Count < 2)
        {
            return;
        }

        // Length captured once: chain_paths only registers, no emission (ctx.rs:487).
        int count = paths.Count;
        for (int i = 1; i < count; i++)
        {
            RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(paths[i - 1]),
                new EventAction.ActivatePath(paths[i]));
        }

        if (loop)
        {
            RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(paths[paths.Count - 1]),
                new EventAction.ActivatePath(paths[0]));
        }
    }

    // ------------------------------------------------------------------
    // animation (Animation.activate_scene / step_animation)
    // ------------------------------------------------------------------

    /// <summary>Animation.activate_scene: does NOT reset playback (resume semantics).</summary>
    public void ActivateScene(IEffectHooks hooks, CharId id, string sceneId)
    {
        {
            EffectCharacter ch = Terminal.Arena[(int)id.Value];
            Scene scene = ch.Animation.Scenes.Get(sceneId)
                ?? throw new EngineInvariantException("activate_scene: scene not found");
            CharacterVisual visual = scene.Activate();
            ch.Animation.ActiveScene = ch.Animation.Scenes.SharedKey(sceneId);
            ch.Animation.ActiveSceneCurrentStep = 0;
            ch.Animation.CurrentCharacterVisual = visual;
        }

        if (ObservesEvent(id, Event.SceneActivated))
        {
            HandleEvent(hooks, id, Event.SceneActivated, new CallerKey.Scene(sceneId));
        }
    }

    /// <summary>Animation.deactivate_scene.</summary>
    public void DeactivateScene(CharId id, string? sceneId)
    {
        Animation animation = Terminal.Arena[(int)id.Value].Animation;
        if (sceneId is null)
        {
            animation.ActiveScene = null;
        }
        else if (string.Equals(animation.ActiveScene, sceneId, StringComparison.Ordinal))
        {
            animation.ActiveScene = null;
        }
    }

    /// <summary>
    /// Animation.step_animation.
    ///
    /// Nothing between here and complete_scene_if_finished can add or remove a
    /// scene, so the active scene's slot is resolved once and reused instead of
    /// looking the id up again at every step.
    /// </summary>
    public void StepAnimation(IEffectHooks hooks, CharId id)
    {
        int? sceneSlot;
        {
            Animation anim = Terminal.Arena[(int)id.Value].Animation;
            if (anim.ActiveScene is string sid)
            {
                int slot = anim.Scenes.Slot(sid) ?? throw new EngineInvariantException("active scene missing");
                sceneSlot = anim.Scenes.At(slot).Frames.Count > 0 ? slot : null;
            }
            else
            {
                sceneSlot = null;
            }
        }

        if (sceneSlot is null)
        {
            return;
        }

        int slotValue = sceneSlot.Value;
        Scene scene = Terminal.Arena[(int)id.Value].Animation.Scenes.At(slotValue);
        SyncMetric? sync = scene.Sync;
        Easing? ease = scene.Ease;

        if (sync is SyncMetric syncMetric)
        {
            StepSyncedScene(id, slotValue, syncMetric);
        }
        else if (ease is Easing easing)
        {
            StepEasedScene(id, slotValue, easing);
        }
        else
        {
            EffectCharacter ch = Terminal.Arena[(int)id.Value];
            CharacterVisual visual = ch.Animation.Scenes.At(slotValue).GetNextVisual();
            ch.Animation.CurrentCharacterVisual = visual;
        }

        CompleteSceneIfFinished(hooks, id, slotValue);
    }

    /// <summary>Animation._step_synced_scene + _synced_scene_frame_index.</summary>
    private void StepSyncedScene(CharId id, int sceneSlot, SyncMetric sync)
    {
        (long CurrentStep, long MaxSteps, double TotalDistance, double LastDistanceReached)? activePathState;
        {
            EffectCharacter ch = Terminal.Arena[(int)id.Value];
            if (ch.Motion.ActivePath is string pid)
            {
                Path p = ch.Motion.Paths.Get(pid) ?? throw new EngineInvariantException("active path missing");
                activePathState = (p.CurrentStep, p.MaxSteps, p.TotalDistance, p.LastDistanceReached);
            }
            else
            {
                activePathState = null;
            }
        }

        EffectCharacter character = Terminal.Arena[(int)id.Value];
        Scene scene = character.Animation.Scenes.At(sceneSlot);
        if (activePathState is null)
        {
            // no active path: jump to final frame and force-complete
            int last = scene.Frames[scene.Frames.Count - 1];
            character.Animation.CurrentCharacterVisual = scene.AllFrames[last].CharacterVisual;
            scene.PlayedFrames.AddRange(scene.Frames);
            scene.Frames.Clear();
        }
        else
        {
            (long currentStep, long maxSteps, double totalDistance, double lastDistanceReached) = activePathState.Value;
            long finalFrameIndex = scene.Frames.Count - 1L;
            double progressRatio = sync switch
            {
                SyncMetric.Step => Math.Max(currentStep, 1L) / (double)Math.Max(maxSteps, 1L),
                SyncMetric.Distance => DistanceProgress(totalDistance, lastDistanceReached),
                _ => 0.0,
            };
            long frameIndex = PyCompat.RoundHalfEven(finalFrameIndex * progressRatio);
            if (frameIndex > finalFrameIndex)
            {
                frameIndex = finalFrameIndex;
            }

            if (frameIndex < 0)
            {
                frameIndex = 0;
            }

            int frame = scene.Frames[(int)frameIndex];
            character.Animation.CurrentCharacterVisual = scene.AllFrames[frame].CharacterVisual;
        }
    }

    private static double DistanceProgress(double totalDistance, double lastDistanceReached)
    {
        double total = PyCompat.FMax(totalDistance, 1.0);
        double remaining = PyCompat.FMax(totalDistance - lastDistanceReached, 1.0);
        double reached = PyCompat.FMax(total - remaining, 1.0);
        return reached / total;
    }

    /// <summary>Animation._step_eased_scene (+ _ease_animation).</summary>
    private void StepEasedScene(CharId id, int sceneSlot, Easing ease)
    {
        EffectCharacter ch = Terminal.Arena[(int)id.Value];
        Scene scene = ch.Animation.Scenes.At(sceneSlot);
        double elapsedStepRatio = scene.EasingCurrentStep / (double)scene.EasingTotalSteps;
        double easingFactor = ease.Ease(elapsedStepRatio);
        long finalFrameIndex = Math.Max(scene.EasingTotalSteps - 1, 0);
        long frameIndex = PyCompat.RoundHalfEven(easingFactor * finalFrameIndex);
        if (frameIndex > finalFrameIndex)
        {
            frameIndex = finalFrameIndex;
        }

        if (frameIndex < 0)
        {
            frameIndex = 0;
        }

        int frame = scene.FrameIndexMap[(int)frameIndex];
        ch.Animation.CurrentCharacterVisual = scene.AllFrames[frame].CharacterVisual;

        scene.EasingCurrentStep += 1;
        if (scene.EasingCurrentStep == scene.EasingTotalSteps)
        {
            if (scene.IsLooping)
            {
                scene.EasingCurrentStep = 0;
            }
            else
            {
                scene.PlayedFrames.AddRange(scene.Frames);
                scene.Frames.Clear();
            }
        }
    }

    /// <summary>
    /// Animation._complete_scene_if_finished: fires SCENE_COMPLETE every tick
    /// for looping scenes, faithfully.
    /// </summary>
    private void CompleteSceneIfFinished(IEffectHooks hooks, CharId id, int sceneSlot)
    {
        {
            // The stepping above cannot clear active_scene, so the slot still
            // holds it and active_scene_is_complete reduces to its scene test.
            Animation anim = Terminal.Arena[(int)id.Value].Animation;
            Scene scene = anim.Scenes.At(sceneSlot);
            if (!(scene.Frames.Count == 0 || scene.IsLooping))
            {
                return;
            }
        }

        {
            Animation anim = Terminal.Arena[(int)id.Value].Animation;
            Scene scene = anim.Scenes.At(sceneSlot);
            if (!scene.IsLooping)
            {
                scene.ResetScene();
                anim.ActiveScene = null;
            }
        }

        if (ObservesEvent(id, Event.SceneComplete))
        {
            string sceneId = Terminal.Arena[(int)id.Value].Animation.Scenes.KeyAt(sceneSlot);
            HandleEvent(hooks, id, Event.SceneComplete, new CallerKey.Scene(sceneId));
        }
    }

    // ------------------------------------------------------------------
    // ticking (EffectCharacter.tick / BaseEffectIterator.update / frame)
    // ------------------------------------------------------------------

    /// <summary>EffectCharacter.tick: motion first, then animation.</summary>
    public void Tick(IEffectHooks hooks, CharId id)
    {
        MotionMove(hooks, id);
        StepAnimation(hooks, id);
    }

    /// <summary>
    /// BaseEffectIterator.update: tick a snapshot of active characters in
    /// canonical ascending-id order, then prune the not-is_active ones.
    /// </summary>
    public void Update(IEffectHooks hooks)
    {
        // Snapshot taken before the walk (ctx.rs:682-687).
        CharId[] snapshot = ActiveCharacters.Snapshot();
        // Length captured once: snapshot walk (ctx.rs:685).
        int count = snapshot.Length;
        for (int i = 0; i < count; i++)
        {
            Tick(hooks, snapshot[i]);
        }

        List<EffectCharacter> arena = Terminal.Arena;
        ActiveCharacters.Retain(id => arena[(int)id.Value].IsActive());
    }

    /// <summary>
    /// BaseEffectIterator.frame: enforce framerate (real clock only), then the
    /// formatted output string; advances the virtual clock by one frame.
    /// </summary>
    public string Frame()
    {
        if (Clock is Clock.Real && Terminal.Config.FrameRate != 0)
        {
            Terminal.EnforceFramerate();
        }

        Clock.AdvanceFrame();
        return Encoding.UTF8.GetString(Terminal.GetFormattedOutputString().Span);
    }
}
