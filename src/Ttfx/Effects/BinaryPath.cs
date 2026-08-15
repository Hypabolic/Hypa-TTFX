using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>BinaryPathIterator.Phase.</summary>
public enum BinaryPathPhase
{
    Travel,
    Wipe,
}

/// <summary>typing.Literal["col", "row"].</summary>
public enum BinaryPathOrientation
{
    Col,
    Row,
}

/// <summary>BinaryPathIterator._BinaryRepresentation.</summary>
public sealed class BinaryRepresentation
{
    public CharId Character { get; set; }
    public List<CharId> BinaryCharacters { get; set; } = new List<CharId>();
    public List<CharId> PendingBinaryCharacters { get; set; } = new List<CharId>();
    public Coord InputCoord { get; set; }
    public bool IsActive { get; set; }

    /// <summary>_BinaryRepresentation._travel_complete.</summary>
    public bool TravelComplete(EngineWorld world)
    {
        foreach (CharId binChar in BinaryCharacters)
        {
            if (world.Terminal.Arena[(int)binChar.Value].Motion.CurrentCoord != InputCoord)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>binarypath, ported from effects/effect_binarypath.py. Transcribed from <c>effects/binarypath.rs</c>.</summary>
public sealed class BinaryPathConfig
{
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Radial;
    public List<Color> BinaryColors { get; set; } = new List<Color>();
    public double MovementSpeed { get; set; } = 1.0;
    public double ActiveBinaryGroups { get; set; } = 0.08;
}

public sealed class BinaryPath : IEffect
{
    private readonly BinaryPathConfig _config;
    private readonly List<BinaryRepresentation> _pendingBinaryRepresentations;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private bool _lastFrameProvided;
    private readonly List<BinaryRepresentation> _activeBinaryReps;
    private bool _complete;
    private BinaryPathPhase _phase;
    private List<List<CharId>> _finalWipeChars;
    private long _maxActiveBinaryGroups;

    public BinaryPath(BinaryPathConfig config)
    {
        _config = config;
        _pendingBinaryRepresentations = new List<BinaryRepresentation>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _lastFrameProvided = false;
        _activeBinaryReps = new List<BinaryRepresentation>();
        _complete = false;
        _phase = BinaryPathPhase.Travel;
        _finalWipeChars = new List<List<CharId>>();
        _maxActiveBinaryGroups = 0;
    }

    public static BinaryPath FromOptions(Dictionary<string, object> options)
    {
        return new BinaryPath(new BinaryPathConfig
        {
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
            BinaryColors = TypedList<Color>(options, "--binary-colors"),
            MovementSpeed = (double)options["--movement-speed"],
            ActiveBinaryGroups = (double)options["--active-binary-groups"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    public void Build(EngineWorld world)
    {
        // __init__: final_wipe_chars computed before build()
        _finalWipeChars = world.Terminal.GetCharactersGrouped(
            CharacterFilter.Default,
            CharacterGroup.DiagonalTopRightToBottomLeft);

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
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ColorPair finalColors;
            if (dynamic)
            {
                finalColors = ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor);
            }
            else
            {
                Color mapped = finalGradientMapping.Get(ch.InputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                finalColors = ColorPair.New(mapped, null);
            }

            _characterFinalColorMap[id] = finalColors;
        }

        foreach (CharId id in characters)
        {
            string symbol;
            Coord inputCoord;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                symbol = ch.Animation.CurrentCharacterVisual.Symbol;
                inputCoord = ch.InputCoord;
            }

            // binarypath.rs:159 — symbol.chars().next() as u32 → {:08b}; must use Rune.Value
            string binaryString = Unicode.SymbolToBinary(symbol);
            var binRep = new BinaryRepresentation
            {
                Character = id,
                InputCoord = inputCoord,
            };
            foreach (char binaryChar in binaryString)
            {
                CharId added = world.Terminal.AddCharacter(binaryChar.ToString(), Coord.New(0, 0));
                binRep.BinaryCharacters.Add(added);
                binRep.PendingBinaryCharacters.Add(added);
            }

            _pendingBinaryRepresentations.Add(binRep);
        }

        var pendingReps = new List<BinaryRepresentation>(_pendingBinaryRepresentations);
        _pendingBinaryRepresentations.Clear();
        foreach (BinaryRepresentation binRep in pendingReps)
        {
            var pathCoords = new List<Coord>();
            Coord startingCoord = world.Terminal.Canvas.RandomCoord(world.Rng, true, false);
            pathCoords.Add(startingCoord);
            BinaryPathOrientation lastOrientation = world.Rng.Choice(
                new[] { BinaryPathOrientation.Col, BinaryPathOrientation.Row });
            Coord nextCoord = startingCoord;
            Coord inputCoord = binRep.InputCoord;
            while (pathCoords[pathCoords.Count - 1] != inputCoord)
            {
                Coord lastCoord = pathCoords[pathCoords.Count - 1];
                long columnDirection = lastCoord.Column > inputCoord.Column
                    ? -1
                    : lastCoord.Column == inputCoord.Column
                        ? 0
                        : 1;
                long rowDirection = lastCoord.Row > inputCoord.Row
                    ? -1
                    : lastCoord.Row == inputCoord.Row
                        ? 0
                        : 1;
                long maxColumnDistance = Math.Abs(lastCoord.Column - inputCoord.Column);
                long maxRowDistance = Math.Abs(lastCoord.Row - inputCoord.Row);
                if (lastOrientation == BinaryPathOrientation.Col && maxRowDistance > 0)
                {
                    // binarypath.rs:206 — min(max_row_distance, max(10, int(canvas.right * 0.2)))
                    long limit = Math.Min(
                        maxRowDistance,
                        Math.Max(10, PyCompat.TruncToI64(world.Terminal.Canvas.Right * 0.2)));
                    nextCoord = Coord.New(
                        lastCoord.Column,
                        lastCoord.Row + world.Rng.Randint(1, limit) * rowDirection);
                    lastOrientation = BinaryPathOrientation.Row;
                }
                else if (lastOrientation == BinaryPathOrientation.Row && maxColumnDistance > 0)
                {
                    nextCoord = Coord.New(
                        lastCoord.Column + world.Rng.Randint(1, Math.Min(maxColumnDistance, 4)) * columnDirection,
                        lastCoord.Row);
                    lastOrientation = BinaryPathOrientation.Col;
                }
                else
                {
                    nextCoord = inputCoord;
                }

                pathCoords.Add(nextCoord);
            }

            pathCoords.Add(nextCoord);
            Coord finalCoord = inputCoord;
            pathCoords.Add(finalCoord);
            foreach (CharId binEffectChar in binRep.BinaryCharacters)
            {
                string digitalPath;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)binEffectChar.Value];
                    ch.Motion.SetCoordinate(pathCoords[0]);
                    string pathId = ch.Motion.NewPath(
                        _config.MovementSpeed,
                        null,
                        null,
                        0,
                        false,
                        "");
                    Path path = ch.Motion.Paths.Get(pathId)
                        ?? throw new EngineInvariantException("digital path");
                    foreach (Coord coord in pathCoords)
                    {
                        path.NewWaypoint(coord, null, "");
                    }

                    digitalPath = pathId;
                }

                world.ActivatePath(this, binEffectChar, digitalPath);
                world.Terminal.Arena[(int)binEffectChar.Value].Layer = 1;
                Color color = world.Rng.Choice(_config.BinaryColors);
                string colorScn;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)binEffectChar.Value];
                    string binSymbol = ch.Animation.CurrentCharacterVisual.Symbol;
                    bool usesPre = ch.UsesInputPreexistingColors;
                    colorScn = ch.Animation.NewScene(false, null, null, "", usesPre);
                    ch.Animation.Scenes.Get(colorScn)!
                        .AddFrame(
                            binSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(color, null) });
                }

                world.ActivateScene(this, binEffectChar, colorScn);
            }
        }

        _pendingBinaryRepresentations.AddRange(pendingReps);

        foreach (CharId id in characters)
        {
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair finalColors = _characterFinalColorMap[id];
            Color? finalFgColor = finalColors.FgColor;
            Color? finalBgColor = finalColors.BgColor;
            Color? dimFgColor = finalFgColor is not null
                ? Animation.AdjustColorBrightness(finalFgColor, 0.5)
                : null;
            Color? dimBgColor = finalBgColor is not null
                ? Animation.AdjustColorBrightness(finalBgColor, 0.5)
                : null;
            Gradient? collapseFgGradient = dimFgColor is not null
                ? Gradient.WithSteps([Color.FromHex("ffffff"), dimFgColor], 7, false)
                : null;
            Gradient? collapseBgGradient = dimBgColor is not null
                ? Gradient.WithSteps([Color.FromHex("ffffff"), dimBgColor], 7, false)
                : null;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string collapseScn = ch.Animation.NewScene(
                    false,
                    null,
                    Easing.InQuad,
                    "collapse_scn",
                    usesPre);
                Scene scene = ch.Animation.Scenes.Get(collapseScn)
                    ?? throw new EngineInvariantException("collapse scene");
                if (collapseFgGradient is not null || collapseBgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        3,
                        collapseFgGradient,
                        collapseBgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        3,
                        new VisualParams { Colors = ColorPair.New(null, null) });
                }
            }

            Gradient? brightenFgGradient = dimFgColor is not null && finalFgColor is not null
                ? Gradient.WithSteps([dimFgColor, finalFgColor], 10, false)
                : null;
            Gradient? brightenBgGradient = dimBgColor is not null && finalBgColor is not null
                ? Gradient.WithSteps([dimBgColor, finalBgColor], 10, false)
                : null;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string brightenScn = ch.Animation.NewScene(
                    false,
                    null,
                    null,
                    "brighten_scn",
                    usesPre);
                Scene scene = ch.Animation.Scenes.Get(brightenScn)
                    ?? throw new EngineInvariantException("brighten scene");
                if (brightenFgGradient is not null || brightenBgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        2,
                        brightenFgGradient,
                        brightenBgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        2,
                        new VisualParams { Colors = ColorPair.New(null, null) });
                }
            }
        }

        // binarypath.rs:354 — int() truncation
        _maxActiveBinaryGroups = Math.Max(
            1,
            PyCompat.TruncToI64(_config.ActiveBinaryGroups * _pendingBinaryRepresentations.Count));
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!_complete || !world.ActiveCharacters.IsEmpty)
        {
            if (_phase == BinaryPathPhase.Travel)
            {
                while (_activeBinaryReps.Count < _maxActiveBinaryGroups
                       && _pendingBinaryRepresentations.Count > 0)
                {
                    // binarypath.rs:366-367 — randrange(0, len) then RNG-indexed remove
                    int index = (int)world.Rng.Randrange(0, _pendingBinaryRepresentations.Count);
                    BinaryRepresentation nextBinaryRep = _pendingBinaryRepresentations[index];
                    _pendingBinaryRepresentations.RemoveAt(index);
                    nextBinaryRep.IsActive = true;
                    _activeBinaryReps.Add(nextBinaryRep);
                }

                if (_activeBinaryReps.Count > 0)
                {
                    var activeReps = new List<BinaryRepresentation>(_activeBinaryReps);
                    _activeBinaryReps.Clear();
                    foreach (BinaryRepresentation activeRep in activeReps)
                    {
                        if (activeRep.PendingBinaryCharacters.Count > 0)
                        {
                            // binarypath.rs:376 — FIFO remove(0)
                            CharId nextChar = activeRep.PendingBinaryCharacters[0];
                            activeRep.PendingBinaryCharacters.RemoveAt(0);
                            world.ActiveCharacters.Insert(
                                nextChar,
                                world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                            world.Terminal.SetCharacterVisibility(nextChar, true);
                        }
                        else if (activeRep.TravelComplete(world))
                        {
                            foreach (CharId binChar in activeRep.BinaryCharacters)
                            {
                                world.Terminal.SetCharacterVisibility(binChar, false);
                            }

                            activeRep.IsActive = false;
                            world.Terminal.SetCharacterVisibility(activeRep.Character, true);
                            world.ActivateScene(this, activeRep.Character, "collapse_scn");
                            world.ActiveCharacters.Insert(
                                activeRep.Character,
                                world.Terminal.Arena[(int)activeRep.Character.Value].CharacterId);
                        }
                    }

                    foreach (BinaryRepresentation rep in activeReps)
                    {
                        if (rep.IsActive)
                        {
                            _activeBinaryReps.Add(rep);
                        }
                    }
                }

                if (world.ActiveCharacters.IsEmpty)
                {
                    _phase = BinaryPathPhase.Wipe;
                }
            }

            if (_phase == BinaryPathPhase.Wipe)
            {
                for (long i = 0; i < 2; i++)
                {
                    if (_finalWipeChars.Count > 0)
                    {
                        // binarypath.rs:403 — FIFO remove(0)
                        List<CharId> nextGroup = _finalWipeChars[0];
                        _finalWipeChars.RemoveAt(0);
                        foreach (CharId character in nextGroup)
                        {
                            world.ActivateScene(this, character, "brighten_scn");
                            world.Terminal.SetCharacterVisibility(character, true);
                            world.ActiveCharacters.Insert(
                                character,
                                world.Terminal.Arena[(int)character.Value].CharacterId);
                        }
                    }
                    else
                    {
                        _complete = true;
                    }
                }
            }

            world.Update(this);
            return world.Frame();
        }

        if (!_lastFrameProvided)
        {
            _lastFrameProvided = true;
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
