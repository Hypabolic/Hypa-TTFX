using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>spotlights, ported from effects/effect_spotlights.py. Transcribed from <c>effects/spotlights.rs</c>.</summary>
public sealed class SpotlightsConfig
{
    public double BeamWidthRatio { get; set; } = 2.0;
    public double BeamFalloff { get; set; } = 0.3;
    public long SearchDuration { get; set; } = 550;
    public (double Lower, double Upper) SearchSpeedRange { get; set; } = (0.35, 0.75);
    public long SpotlightCount { get; set; } = 3;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Spotlights : IEffect
{
    private static readonly IComparer<CharId> CharIdComparer =
        Comparer<CharId>.Create((a, b) => a.Value.CompareTo(b.Value));

    private static readonly Color DynamicNeutralGray = Color.FromHex("#808080");

    private readonly SpotlightsConfig _config;
    private readonly SortedSet<CharId> _illuminatedChars;
    private readonly Dictionary<CharId, (ColorPair Bright, ColorPair Dark)> _characterColorMap;
    private List<CharId> _spotlights;
    private long _illuminateRange;
    private long _searchDuration;
    private bool _searching;
    private bool _expanding;
    private bool _complete;

    public Spotlights(SpotlightsConfig config)
    {
        _config = config;
        _illuminatedChars = new SortedSet<CharId>(CharIdComparer);
        _characterColorMap = new Dictionary<CharId, (ColorPair Bright, ColorPair Dark)>();
        _spotlights = new List<CharId>();
        _illuminateRange = 1;
        _searchDuration = 0;
        _searching = true;
        _expanding = false;
        _complete = false;
    }

    public static Spotlights FromOptions(Dictionary<string, object> options)
    {
        (double lower, double upper) searchSpeed = ((double, double))options["--search-speed-range"];
        return new Spotlights(new SpotlightsConfig
        {
            BeamWidthRatio = (double)options["--beam-width-ratio"],
            BeamFalloff = (double)options["--beam-falloff"],
            SearchDuration = (long)options["--search-duration"],
            SearchSpeedRange = searchSpeed,
            SpotlightCount = (long)options["--spotlight-count"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    private static ColorPair AdjustColorPairBrightness(ColorPair colors, double brightnessFactor)
    {
        return ColorPair.New(
            colors.FgColor is not null ? Animation.AdjustColorBrightness(colors.FgColor, brightnessFactor) : null,
            colors.BgColor is not null ? Animation.AdjustColorBrightness(colors.BgColor, brightnessFactor) : null);
    }

    private static bool HasInputColors(EngineWorld world, CharId id)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        return ch.Animation.InputFgColor is not null || ch.Animation.InputBgColor is not null;
    }

    private static bool IsSpotlightable(EngineWorld world, CharId id)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        return ch.InputSymbol != " " || HasInputColors(world, id);
    }

    private ColorPair? GetExpandColorOverride(EngineWorld world, CharId id)
    {
        if (world.Terminal.Config.ExistingColorHandling != ExistingColorHandling.Dynamic || !_expanding)
        {
            return null;
        }

        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        if (ch.Animation.InputFgColor is null && ch.Animation.InputBgColor is not null)
        {
            return ColorPair.New(null, ch.Animation.InputBgColor);
        }

        if (!HasInputColors(world, id))
        {
            return new ColorPair();
        }

        return null;
    }

    private List<CharId> MakeSpotlights(EngineWorld world, long numSpotlights)
    {
        var spotlights = new List<CharId>();
        long minimumDistance = PyCompat.FloorDiv(world.Terminal.Canvas.Right, 4);
        for (long i = 0; i < numSpotlights; i++)
        {
            Coord spawnCoord = world.Terminal.Canvas.RandomCoord(world.Rng, true, false);
            CharId spotlight = world.Terminal.AddCharacter("O", spawnCoord);
            spotlights.Add(spotlight);

            var spotlightTargetCoords = new List<Coord>();
            Coord lastCoord = world.Terminal.Canvas.RandomCoord(world.Rng, false, false);
            spotlightTargetCoords.Add(lastCoord);
            for (int j = 0; j < 10; j++)
            {
                Coord nextCoord = FindCoordAtMinimumDistance(world, lastCoord, minimumDistance);
                spotlightTargetCoords.Add(nextCoord);
                lastCoord = nextCoord;
            }

            var paths = new List<string>();
            foreach (Coord coord in spotlightTargetCoords)
            {
                double speed = world.Rng.Uniform(_config.SearchSpeedRange.Lower, _config.SearchSpeedRange.Upper);
                string pathId = paths.Count.ToString();
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)spotlight.Value];
                    pathId = ch.Motion.NewPath(speed, Easing.InOutQuad, null, 0, false, pathId);
                }

                Coord bezierControl = world.Terminal.Canvas.RandomCoord(world.Rng, true, false);
                world.Terminal.Arena[(int)spotlight.Value].Motion.Paths.Get(pathId)!
                    .NewWaypoint(coord, [bezierControl], "");
                paths.Add(pathId);
            }

            world.ChainPaths(spotlight, paths, true);

            Coord canvasCenter = world.Terminal.Canvas.Center;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)spotlight.Value];
                string centerPath = ch.Motion.NewPath(0.5, Easing.InOutSine, null, 0, false, "center");
                ch.Motion.Paths.Get(centerPath)!
                    .NewWaypoint(canvasCenter, null, "");
            }
        }

        return spotlights;
    }

    private static Coord FindCoordAtMinimumDistance(EngineWorld world, Coord originCoord, long minimumDistance)
    {
        while (true)
        {
            Coord coord = world.Terminal.Canvas.RandomCoord(world.Rng, false, false);
            double distance = Geometry.FindLengthOfLine(originCoord, coord, false);
            if (distance >= minimumDistance)
            {
                return coord;
            }
        }
    }

    private void IlluminateChars(EngineWorld world, long range)
    {
        var coordsInRange = new List<Coord>();
        foreach (CharId spotlight in _spotlights)
        {
            Coord currentCoord = world.Terminal.Arena[(int)spotlight.Value].Motion.CurrentCoord;
            coordsInRange.AddRange(Geometry.FindCoordsInCircle(currentCoord, range));
        }

        var charsInRange = new SortedSet<CharId>(CharIdComparer);
        foreach (Coord coord in coordsInRange)
        {
            CharId? id = world.Terminal.GetCharacterByInputCoord(coord);
            if (id is CharId charId && IsSpotlightable(world, charId))
            {
                charsInRange.Add(charId);
            }
        }

        var charsNoLongerInRange = new List<CharId>();
        foreach (CharId id in _illuminatedChars)
        {
            if (!charsInRange.Contains(id))
            {
                charsNoLongerInRange.Add(id);
            }
        }

        foreach (CharId id in charsNoLongerInRange)
        {
            ColorPair? expandOverride = GetExpandColorOverride(world, id);
            ColorPair colors = expandOverride ?? _characterColorMap[id].Dark;
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string inputSymbol = ch.InputSymbol;
            bool usesPre = ch.UsesInputPreexistingColors;
            ch.Animation.SetAppearance(inputSymbol, usesPre, inputSymbol, colors);
        }

        foreach (CharId id in charsInRange)
        {
            Coord inputCoord = world.Terminal.Arena[(int)id.Value].InputCoord;
            double distance = double.PositiveInfinity;
            foreach (CharId spotlight in _spotlights)
            {
                Coord currentCoord = world.Terminal.Arena[(int)spotlight.Value].Motion.CurrentCoord;
                double d = Geometry.FindLengthOfLine(currentCoord, inputCoord, true);
                distance = PyCompat.FMin(distance, d);
            }

            ColorPair adjustedColor;
            if (distance > range * (1.0 - _config.BeamFalloff))
            {
                // spotlights.rs:235 — .max(0.2) is f64 max
                double brightnessFactor = PyCompat.FMax(
                    1.0 - (distance - range * (1.0 - _config.BeamFalloff)) / (range * _config.BeamFalloff),
                    0.2);
                adjustedColor = AdjustColorPairBrightness(_characterColorMap[id].Bright, brightnessFactor);
            }
            else
            {
                adjustedColor = _characterColorMap[id].Bright;
            }

            ColorPair? expandOverride = GetExpandColorOverride(world, id);
            ColorPair colors = expandOverride ?? adjustedColor;
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string inputSymbol = ch.InputSymbol;
            bool usesPre = ch.UsesInputPreexistingColors;
            ch.Animation.SetAppearance(inputSymbol, usesPre, inputSymbol, colors);
        }

        _illuminatedChars.Clear();
        foreach (CharId id in charsInRange)
        {
            _illuminatedChars.Add(id);
        }
    }

    public void Build(EngineWorld world)
    {
        _spotlights = MakeSpotlights(world, _config.SpotlightCount);

        Gradient finalGradient = Gradient.New(
            _config.FinalGradientStops,
            _config.FinalGradientSteps,
            false,
            false);
        CoordColorMap finalGradientMapping = finalGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.TextBottom,
            world.Terminal.Canvas.TextTop,
            world.Terminal.Canvas.TextLeft,
            world.Terminal.Canvas.TextRight,
            _config.FinalGradientDirection);

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);

        foreach (CharId id in characters)
        {
            Coord inputCoord;
            Color? inputFg;
            Color? inputBg;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputCoord = ch.InputCoord;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
            }

            ColorPair brightPair;
            ColorPair darkPair;
            if (dynamic)
            {
                if (inputFg is not null || inputBg is not null)
                {
                    Color? brightFg = inputFg;
                    if (brightFg is null && inputBg is not null)
                    {
                        brightFg = DynamicNeutralGray;
                    }

                    brightPair = ColorPair.New(brightFg, inputBg);
                    darkPair = ColorPair.New(
                        brightFg is not null ? Animation.AdjustColorBrightness(brightFg, 0.2) : null,
                        inputBg is not null ? Animation.AdjustColorBrightness(inputBg, 0.2) : null);
                }
                else
                {
                    brightPair = ColorPair.New(DynamicNeutralGray, null);
                    darkPair = ColorPair.New(Animation.AdjustColorBrightness(DynamicNeutralGray, 0.2), null);
                }
            }
            else
            {
                Color colorBright = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                darkPair = ColorPair.New(Animation.AdjustColorBrightness(colorBright, 0.2), null);
                brightPair = ColorPair.New(colorBright, null);
            }

            world.Terminal.SetCharacterVisibility(id, true);
            _characterColorMap[id] = (brightPair, darkPair);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.SetAppearance(ch.InputSymbol, ch.UsesInputPreexistingColors, ch.InputSymbol, darkPair);
            }
        }

        long smallestDimension = Math.Min(world.Terminal.Canvas.Right, world.Terminal.Canvas.Top);
        // spotlights.rs:324 — floor then f64 min then trunc
        double ranged = PyCompat.FMin(
            Math.Floor(smallestDimension / _config.BeamWidthRatio),
            smallestDimension);
        _illuminateRange = Math.Max(PyCompat.TruncToI64(ranged), 1);
        _searchDuration = _config.SearchDuration;
        _searching = true;
        _expanding = false;
        _complete = false;

        List<CharId> spotlightsCopy = new List<CharId>(_spotlights);
        foreach (CharId spotlight in spotlightsCopy)
        {
            world.ActivatePath(this, spotlight, "0");
            world.ActiveCharacters.Insert(
                spotlight,
                world.Terminal.Arena[(int)spotlight.Value].CharacterId);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!_complete)
        {
            IlluminateChars(world, _illuminateRange);
            if (_searching)
            {
                _searchDuration -= 1;
                if (_searchDuration == 0)
                {
                    List<CharId> spotlightsCopy = new List<CharId>(_spotlights);
                    foreach (CharId spotlight in spotlightsCopy)
                    {
                        world.ActivatePath(this, spotlight, "center");
                    }

                    _searching = false;
                }
            }

            bool anyActive = false;
            foreach (CharId spotlight in _spotlights)
            {
                if (world.Terminal.Arena[(int)spotlight.Value].Motion.ActivePath is not null)
                {
                    anyActive = true;
                    break;
                }
            }

            if (!anyActive)
            {
                while (_spotlights.Count > 1)
                {
                    _spotlights.RemoveAt(_spotlights.Count - 1);
                }

                _expanding = true;
                _illuminateRange += 1;
                double limit = Math.Floor(Math.Max(world.Terminal.Canvas.Right, world.Terminal.Canvas.Top) / 1.5);
                if (_illuminateRange > limit)
                {
                    _complete = true;
                }
            }

            world.Update(this);
            return world.Frame();
        }

        return null;
    }

    private static List<T> TypedList<T>(Dictionary<string, object> options, string key)
    {
        var raw = (List<object>)options[key];
        var result = new List<T>(raw.Count);
        foreach (object item in raw)
        {
            result.Add((T)item);
        }

        return result;
    }
}
