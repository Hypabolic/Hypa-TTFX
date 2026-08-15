using System;
using System.Collections.Generic;
using System.Globalization;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Waypoints are cloned constantly — into segments, into origin segments on
/// every path activation, and into event keys.
/// Transcribed from <c>engine/motion.rs</c>.
/// </summary>
public sealed class Waypoint
{
    public string WaypointId { get; }
    public Coord Coord { get; }
    public Coord[]? BezierControl { get; }

    public Waypoint(string waypointId, Coord coord, Coord[]? bezierControl)
    {
        WaypointId = waypointId;
        Coord = coord;
        BezierControl = bezierControl;
    }

    public WaypointKey Key() => new WaypointKey(Coord, WaypointId, BezierControl);
}

public sealed class Segment
{
    public Waypoint Start { get; set; }
    public Waypoint End { get; set; }
    public double Distance { get; set; }
    public bool EnterEventTriggered { get; set; }
    public bool ExitEventTriggered { get; set; }

    public Segment(Waypoint start, Waypoint end, double distance)
    {
        Start = start;
        End = end;
        Distance = distance;
        EnterEventTriggered = false;
        ExitEventTriggered = false;
    }

    public static Segment New(Waypoint start, Waypoint end, double distance) =>
        new Segment(start, end, distance);
}

public sealed class Path
{
    public string PathId { get; }
    public double Speed { get; set; }
    public Easing? Ease { get; }
    public long? Layer { get; }
    public long HoldTime { get; set; }
    public bool Loop { get; }
    public List<Segment> Segments { get; } = new List<Segment>();
    public List<Waypoint> Waypoints { get; } = new List<Waypoint>();
    public double TotalDistance { get; set; }
    public long CurrentStep { get; set; }
    public long MaxSteps { get; set; }
    public long HoldTimeRemaining { get; set; }
    public double LastDistanceReached { get; set; }

    /// <summary>
    /// Distance of the synthetic origin segment set at activation (upstream
    /// keeps the Segment object; only its distance is read back).
    /// </summary>
    public Segment? OriginSegment { get; set; }

    private Path(string pathId, double speed, Easing? ease, long? layer, long holdTime, bool loop)
    {
        PathId = pathId;
        Speed = speed;
        Ease = ease;
        Layer = layer;
        HoldTime = holdTime;
        Loop = loop;
        HoldTimeRemaining = holdTime;
    }

    public static Path New(
        string pathId,
        double speed,
        Easing? ease,
        long? layer,
        long holdTime,
        bool loop)
    {
        if (speed <= 0.0)
        {
            throw new EngineException($"Path speed must be greater than 0. Received: {speed}");
        }

        return new Path(pathId, speed, ease, layer, holdTime, loop);
    }

    /// <summary>Path.new_waypoint: auto-id like scenes; duplicate explicit id errors.</summary>
    public Waypoint NewWaypoint(Coord coord, IReadOnlyList<Coord>? bezierControl, string waypointId)
    {
        string resolvedId;
        if (waypointId.Length == 0)
        {
            int currentId = Waypoints.Count;
            while (true)
            {
                string candidate = currentId.ToString(CultureInfo.InvariantCulture);
                if (!HasWaypointId(candidate))
                {
                    resolvedId = candidate;
                    break;
                }

                currentId += 1;
            }
        }
        else
        {
            if (HasWaypointId(waypointId))
            {
                throw new EngineException($"duplicate waypoint id: {waypointId}");
            }

            resolvedId = waypointId;
        }

        // Python: empty tuple bezier_control is falsy -> None
        Coord[]? bezier = bezierControl is not null && bezierControl.Count > 0
            ? CopyCoords(bezierControl)
            : null;
        var waypoint = new Waypoint(resolvedId, coord, bezier);
        AddWaypointToPath(waypoint);
        return waypoint;
    }

    /// <summary>Path._add_waypoint_to_path.</summary>
    private void AddWaypointToPath(Waypoint waypoint)
    {
        Waypoints.Add(waypoint);
        if (Waypoints.Count < 2)
        {
            return;
        }

        Waypoint prev = Waypoints[Waypoints.Count - 2];
        double distanceFromPrevious = waypoint.BezierControl is Coord[] control
            ? Geometry.FindLengthOfBezierCurve(prev.Coord, control, waypoint.Coord)
            : Geometry.FindLengthOfLine(prev.Coord, waypoint.Coord, true);
        TotalDistance += distanceFromPrevious;
        Segments.Add(Segment.New(prev, waypoint, distanceFromPrevious));
        MaxSteps = PyCompat.RoundHalfEven(TotalDistance / Speed);
    }

    public Waypoint QueryWaypoint(string waypointId)
    {
        // Length captured once: query does not emit (motion.rs:144-149).
        int count = Waypoints.Count;
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(Waypoints[i].WaypointId, waypointId, StringComparison.Ordinal))
            {
                return Waypoints[i];
            }
        }

        throw new EngineException($"waypoint not found: {waypointId}");
    }

    private bool HasWaypointId(string waypointId)
    {
        int count = Waypoints.Count;
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(Waypoints[i].WaypointId, waypointId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Coord[] CopyCoords(IReadOnlyList<Coord> source)
    {
        var copy = new Coord[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            copy[i] = source[i];
        }

        return copy;
    }
}

/// <summary>
/// engine/motion.py Motion: per-character movement state. <c>active_path</c> and
/// <c>completed_path</c> are path ids (upstream holds object references; Path
/// equality is by id).
/// Transcribed from <c>engine/motion.rs</c>.
/// </summary>
public sealed class Motion
{
    public OrderedMap<Path> Paths { get; } = new OrderedMap<Path>();

    public Coord CurrentCoord { get; set; }

    public Coord PreviousCoord { get; set; }

    public string? ActivePath { get; set; }

    public string? CompletedPath { get; set; }

    private Motion(Coord inputCoord)
    {
        CurrentCoord = inputCoord;
        PreviousCoord = Coord.New(-1, -1);
    }

    public static Motion New(Coord inputCoord) => new Motion(inputCoord);

    public void SetCoordinate(Coord coord)
    {
        CurrentCoord = coord;
    }

    /// <summary>Motion.new_path: auto-id probing; duplicate explicit id errors.</summary>
    public string NewPath(
        double speed,
        Easing? ease,
        long? layer,
        long holdTime,
        bool loop,
        string pathId)
    {
        string resolvedId;
        if (pathId.Length == 0)
        {
            int currentId = Paths.Count;
            while (true)
            {
                string candidate = currentId.ToString(CultureInfo.InvariantCulture);
                if (!Paths.ContainsKey(candidate))
                {
                    resolvedId = candidate;
                    break;
                }

                currentId += 1;
            }
        }
        else
        {
            if (Paths.ContainsKey(pathId))
            {
                throw new EngineException($"duplicate path id: {pathId}");
            }

            resolvedId = pathId;
        }

        Path path = Path.New(resolvedId, speed, ease, layer, holdTime, loop);
        Paths.Insert(resolvedId, path);
        return resolvedId;
    }

    public bool MovementIsComplete() => ActivePath is null;

    /// <summary>
    /// Motion.deactivate_path: None clears unconditionally; otherwise only
    /// clears when the given path is the active one.
    /// </summary>
    public void DeactivatePath(string? pathId)
    {
        if (pathId is null)
        {
            ActivePath = null;
        }
        else if (string.Equals(ActivePath, pathId, StringComparison.Ordinal))
        {
            ActivePath = null;
        }
    }
}
