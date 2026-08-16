using System;
using System.Collections.Generic;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Event system, ported from engine/base_character.py EventHandler.
///
/// Storage is plain data; dispatch lives on EngineWorld so that actions
/// execute inline at the exact upstream emission points, reentrantly.
/// Effect callbacks are (id, payload) records — never
/// closures that capture loop variables.
/// Transcribed from <c>engine/events.rs</c>.
/// </summary>
public enum Event
{
    SegmentEntered,
    SegmentExited,
    PathActivated,
    PathComplete,
    PathHolding,
    SceneActivated,
    SceneComplete,
}

/// <summary>
/// Waypoint identity for event keying: upstream Waypoint is a frozen dataclass
/// hashed/compared by ALL fields (id, coord, bezier controls) — two waypoints
/// with identical fields in different paths collide, faithfully.
///
/// Field order is load-bearing for speed, not for meaning: the derived
/// comparison short-circuits in declaration order, and <c>coord</c> rejects
/// non-matches with two integer compares instead of a string memcmp.
/// </summary>
public sealed class WaypointKey : IEquatable<WaypointKey>
{
    public Coord Coord { get; }
    public string WaypointId { get; }
    public Coord[]? BezierControl { get; }

    public WaypointKey(Coord coord, string waypointId, Coord[]? bezierControl)
    {
        Coord = coord;
        WaypointId = waypointId;
        BezierControl = bezierControl;
    }

    public bool Equals(WaypointKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Coord != other.Coord)
        {
            return false;
        }

        if (!string.Equals(WaypointId, other.WaypointId, StringComparison.Ordinal))
        {
            return false;
        }

        return BezierEquals(BezierControl, other.BezierControl);
    }

    public override bool Equals(object? obj) => obj is WaypointKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Coord);
        hash.Add(WaypointId, StringComparer.Ordinal);
        if (BezierControl is Coord[] controls)
        {
            // Length captured once: hashing does not emit events.
            int count = controls.Length;
            for (int i = 0; i < count; i++)
            {
                hash.Add(controls[i]);
            }
        }

        return hash.ToHashCode();
    }

    private static bool BezierEquals(Coord[]? a, Coord[]? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }

        int count = a.Length;
        for (int i = 0; i < count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Event caller identity. Scene/Path compare by id (their upstream __eq__).
/// </summary>
public abstract class CallerKey : IEquatable<CallerKey>
{
    private CallerKey()
    {
    }

    public sealed class Scene : CallerKey
    {
        public string Id { get; }

        public Scene(string id)
        {
            Id = id;
        }

        public override bool Equals(CallerKey? other) =>
            other is Scene scene && string.Equals(Id, scene.Id, StringComparison.Ordinal);

        public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class Path : CallerKey
    {
        public string Id { get; }

        public Path(string id)
        {
            Id = id;
        }

        public override bool Equals(CallerKey? other) =>
            other is Path path && string.Equals(Id, path.Id, StringComparison.Ordinal);

        public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class Waypoint : CallerKey
    {
        public WaypointKey Key { get; }

        public Waypoint(WaypointKey key)
        {
            Key = key;
        }

        public override bool Equals(CallerKey? other) =>
            other is Waypoint waypoint && Key.Equals(waypoint.Key);

        public override int GetHashCode() => Key.GetHashCode();
    }

    public abstract bool Equals(CallerKey? other);

    public override bool Equals(object? obj) => obj is CallerKey other && Equals(other);

    public override int GetHashCode() => base.GetHashCode();

    public bool Matches(CallerKey caller) => Equals(caller);
}

/// <summary>
/// Typed payload values for effect callbacks (upstream Callback *args).
/// </summary>
public abstract class CallbackValue : IEquatable<CallbackValue>
{
    private CallbackValue()
    {
    }

    public sealed class Int : CallbackValue
    {
        public long Value { get; }

        public Int(long value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) => other is Int i && Value == i.Value;

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class Float : CallbackValue
    {
        public double Value { get; }

        public Float(double value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) => other is Float f && Value.Equals(f.Value);

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class Str : CallbackValue
    {
        public string Value { get; }

        public Str(string value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) =>
            other is Str s && string.Equals(Value, s.Value, StringComparison.Ordinal);

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class CoordVal : CallbackValue
    {
        public Coord Value { get; }

        public CoordVal(Coord value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) => other is CoordVal c && Value == c.Value;

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class Char : CallbackValue
    {
        public CharId Value { get; }

        public Char(CharId value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) => other is Char c && Value == c.Value;

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class ColorVal : CallbackValue
    {
        public Color Value { get; }

        public ColorVal(Color value)
        {
            Value = value;
        }

        public override bool Equals(CallbackValue? other) => other is ColorVal c && Value.Equals(c.Value);

        public override int GetHashCode() => Value.GetHashCode();
    }

    public abstract bool Equals(CallbackValue? other);

    public override bool Equals(object? obj) => obj is CallbackValue other && Equals(other);

    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>
/// An effect-defined callback: the id selects behavior inside the effect's
/// dispatch_callback; args are owned data captured at registration.
/// </summary>
public sealed class EffectCallback : IEquatable<EffectCallback>
{
    public uint Id { get; }
    public CallbackValue[] Args { get; }

    public EffectCallback(uint id, CallbackValue[] args)
    {
        Id = id;
        Args = args;
    }

    public bool Equals(EffectCallback? other)
    {
        if (other is null || Id != other.Id || Args.Length != other.Args.Length)
        {
            return false;
        }

        // Length captured once: equality does not emit events.
        int count = Args.Length;
        for (int i = 0; i < count; i++)
        {
            if (!Args[i].Equals(other.Args[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EffectCallback other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        int count = Args.Length;
        for (int i = 0; i < count; i++)
        {
            hash.Add(Args[i]);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A registered action. Targets stay string ids, re-resolved at dispatch
/// (<c>ctx.rs:252-262</c>). Equals/GetHashCode cover the whole value including
/// array contents (not reference) so duplicate registration is structural.
/// </summary>
public abstract class EventAction : IEquatable<EventAction>
{
    private EventAction()
    {
    }

    public sealed class ActivatePath : EventAction
    {
        public string PathId { get; }

        public ActivatePath(string pathId)
        {
            PathId = pathId;
        }

        public override bool Equals(EventAction? other) =>
            other is ActivatePath a && string.Equals(PathId, a.PathId, StringComparison.Ordinal);

        public override int GetHashCode() => PathId.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class ActivateScene : EventAction
    {
        public string SceneId { get; }

        public ActivateScene(string sceneId)
        {
            SceneId = sceneId;
        }

        public override bool Equals(EventAction? other) =>
            other is ActivateScene a && string.Equals(SceneId, a.SceneId, StringComparison.Ordinal);

        public override int GetHashCode() => SceneId.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class DeactivatePath : EventAction
    {
        public string? PathId { get; }

        public DeactivatePath(string? pathId)
        {
            PathId = pathId;
        }

        public override bool Equals(EventAction? other) =>
            other is DeactivatePath a && string.Equals(PathId, a.PathId, StringComparison.Ordinal);

        public override int GetHashCode() => PathId is null ? 0 : PathId.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class DeactivateScene : EventAction
    {
        public string? SceneId { get; }

        public DeactivateScene(string? sceneId)
        {
            SceneId = sceneId;
        }

        public override bool Equals(EventAction? other) =>
            other is DeactivateScene a && string.Equals(SceneId, a.SceneId, StringComparison.Ordinal);

        public override int GetHashCode() => SceneId is null ? 0 : SceneId.GetHashCode(StringComparison.Ordinal);
    }

    public sealed class ResetAppearance : EventAction
    {
        public override bool Equals(EventAction? other) => other is ResetAppearance;

        public override int GetHashCode() => 1;
    }

    public sealed class SetLayer : EventAction
    {
        public long Layer { get; }

        public SetLayer(long layer)
        {
            Layer = layer;
        }

        public override bool Equals(EventAction? other) => other is SetLayer a && Layer == a.Layer;

        public override int GetHashCode() => Layer.GetHashCode();
    }

    public sealed class SetCoordinate : EventAction
    {
        public Coord Coord { get; }

        public SetCoordinate(Coord coord)
        {
            Coord = coord;
        }

        public override bool Equals(EventAction? other) => other is SetCoordinate a && Coord == a.Coord;

        public override int GetHashCode() => Coord.GetHashCode();
    }

    public sealed class Callback : EventAction
    {
        public EffectCallback Value { get; }

        public Callback(EffectCallback value)
        {
            Value = value;
        }

        public override bool Equals(EventAction? other) => other is Callback a && Value.Equals(a.Value);

        public override int GetHashCode() => Value.GetHashCode();
    }

    public abstract bool Equals(EventAction? other);

    public override bool Equals(object? obj) => obj is EventAction other && Equals(other);

    public override int GetHashCode() => base.GetHashCode();
}

/// <summary>
/// Per-character event table: insertion-ordered (event, caller) -&gt; actions.
///
/// <c>subscribed</c> mirrors the table as a bitmask of registered event kinds.
/// </summary>
public sealed class EventHandler
{
    private readonly List<RegisteredEvent> _registeredEvents = new List<RegisteredEvent>();
    private byte _subscribed;

    /// <summary>
    /// register_event with the duplicate check (upstream raises
    /// DuplicateEventRegistrationError). Caller/target id resolution and type
    /// validation happen in EngineWorld.RegisterEvent, which has arena access.
    /// </summary>
    public void Push(Event ev, CallerKey caller, EventAction action)
    {
        RegisteredEvent? existing = null;
        // Length captured once: push does not emit (events.rs:170).
        int count = _registeredEvents.Count;
        for (int i = 0; i < count; i++)
        {
            RegisteredEvent entry = _registeredEvents[i];
            if (entry.Event == ev && entry.Caller.Equals(caller))
            {
                existing = entry;
                break;
            }
        }

        if (existing is not null)
        {
            // Length captured once: contains is a scan, no emission (events.rs:174).
            int actionCount = existing.Actions.Count;
            for (int i = 0; i < actionCount; i++)
            {
                if (existing.Actions[i].Equals(action))
                {
                    throw new EngineException(
                        $"duplicate event registration: {existing.Event} {existing.Caller} {action}");
                }
            }

            existing.Actions.Add(action);
        }
        else
        {
            _registeredEvents.Add(new RegisteredEvent(ev, caller, new List<EventAction> { action }));
        }

        _subscribed |= EventBit(ev);
    }

    /// <summary>
    /// True when at least one action is registered for this event kind, for any
    /// caller. A false answer means <c>ActionsIndex</c> cannot match.
    /// </summary>
    public bool Subscribes(Event ev) => (_subscribed & EventBit(ev)) != 0;

    public int? ActionsIndex(Event ev, CallerKey caller)
    {
        if (!Subscribes(ev))
        {
            return null;
        }

        // Length captured once: lookup does not emit (events.rs:204-212).
        int count = _registeredEvents.Count;
        for (int i = 0; i < count; i++)
        {
            RegisteredEvent entry = _registeredEvents[i];
            if (entry.Event == ev && entry.Caller.Matches(caller))
            {
                return i;
            }
        }

        return null;
    }

    public IReadOnlyList<EventAction> Actions(int index) => _registeredEvents[index].Actions;

    public void Clear()
    {
        _registeredEvents.Clear();
        _subscribed = 0;
    }

    private static byte EventBit(Event ev) => (byte)(1 << (int)ev);

    private sealed class RegisteredEvent
    {
        public Event Event { get; }
        public CallerKey Caller { get; }
        public List<EventAction> Actions { get; }

        public RegisteredEvent(Event ev, CallerKey caller, List<EventAction> actions)
        {
            Event = ev;
            Caller = caller;
            Actions = actions;
        }
    }
}
