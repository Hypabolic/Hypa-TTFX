using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>typing.Literal["vertical", "horizontal"].</summary>
public enum ExpandDirection
{
    Vertical,
    Horizontal,
}

/// <summary>
/// middleout, ported from effects/effect_middleout.py. Transcribed from <c>effects/middleout.rs</c>.
///
/// Ordering note: upstream's __next__ iterates the freshly rebuilt
/// active_characters set (effect_middleout.py:229-232) to activate the
/// "full" path/scene. Canonical order here is ascending CharacterId
/// (docs/ordering-inventory.md).
/// </summary>
public sealed class MiddleoutConfig
{
    public Color StartingColor { get; set; } = Color.FromHex("ffffff");
    public ExpandDirection ExpandDirection { get; set; } = ExpandDirection.Vertical;
    public double CenterMovementSpeed { get; set; } = 0.6;
    public double FullMovementSpeed { get; set; } = 0.6;
    public Easing CenterEasing { get; set; } = Easing.InOutSine;
    public Easing FullEasing { get; set; } = Easing.InOutSine;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Middleout : IEffect
{
    private enum Phase
    {
        Center,
        Full,
    }

    private readonly MiddleoutConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private Phase _phase;

    public Middleout(MiddleoutConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _phase = Phase.Center;
    }

    /// <summary>middleout.rs parse_expand_direction.</summary>
    public static object ParseExpandDirection(string s)
    {
        return s switch
        {
            "vertical" => ExpandDirection.Vertical,
            "horizontal" => ExpandDirection.Horizontal,
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'vertical', 'horizontal')"),
        };
    }

    public static Middleout FromOptions(Dictionary<string, object> options)
    {
        return new Middleout(new MiddleoutConfig
        {
            StartingColor = (Color)options["--starting-color"],
            ExpandDirection = (ExpandDirection)options["--expand-direction"],
            CenterMovementSpeed = (double)options["--center-movement-speed"],
            FullMovementSpeed = (double)options["--full-movement-speed"],
            CenterEasing = (Easing)options["--center-easing"],
            FullEasing = (Easing)options["--full-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    public void Build(EngineWorld world)
    {
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
        List<CharId> characters;
        {
            CharacterFilter filter = CharacterFilter.Default;
            characters = world.Terminal.GetCharacters(
                world.Rng,
                filter,
                CharacterSort.TopToBottomLeftToRight);
        }

        foreach (CharId id in characters)
        {
            Color? inputFg;
            Color? inputBg;
            Coord inputCoord;
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            ColorPair finalColors;
            if (dynamic)
            {
                finalColors = ColorPair.New(inputFg, inputBg);
            }
            else
            {
                Color mapped = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("gradient mapping missing");
                finalColors = ColorPair.New(mapped, null);
            }

            _characterFinalColorMap[id] = finalColors;
            Coord center = world.Terminal.Canvas.Center;
            world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(center);
            // setup waypoints
            long column;
            long row;
            switch (_config.ExpandDirection)
            {
                case ExpandDirection.Vertical:
                    column = inputCoord.Column;
                    row = world.Terminal.Canvas.CenterRow;
                    break;
                default:
                    column = world.Terminal.Canvas.CenterColumn;
                    row = inputCoord.Row;
                    break;
            }

            string centerPath;
            {
                Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
                string pathId = motion.NewPath(
                    _config.CenterMovementSpeed,
                    _config.CenterEasing,
                    null,
                    0,
                    false,
                    "");
                Path path = motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("center path");
                path.NewWaypoint(Coord.New(column, row), null, "");
                centerPath = pathId;
            }

            {
                Motion motion = world.Terminal.Arena[(int)id.Value].Motion;
                motion.NewPath(
                    _config.FullMovementSpeed,
                    _config.FullEasing,
                    null,
                    0,
                    false,
                    "full");
                Path path = motion.Paths.Get("full")
                    ?? throw new EngineInvariantException("full path");
                path.NewWaypoint(inputCoord, null, "full");
            }

            // setup scenes
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.NewScene(false, null, null, "full", usesPre);
                Scene scene = ch.Animation.Scenes.Get("full")
                    ?? throw new EngineInvariantException("full scene");
                Color? finalFgColor = finalColors.FgColor;
                Color? finalBgColor = finalColors.BgColor;
                if (dynamic)
                {
                    Gradient? fgGradient = finalFgColor is Color fg
                        ? Gradient.WithSteps([_config.StartingColor, fg], 10, false)
                        : null;
                    Gradient? bgGradient = finalBgColor is Color bg
                        ? Gradient.WithSteps([_config.StartingColor, bg], 10, false)
                        : null;
                    if (fgGradient is not null || bgGradient is not null)
                    {
                        scene.ApplyGradientToSymbols([inputSymbol], 6, fgGradient, bgGradient);
                    }
                    else
                    {
                        scene.AddFrame(
                            inputSymbol,
                            6,
                            new VisualParams { Colors = ColorPair.New(null, null) });
                    }
                }
                else
                {
                    Color resolvedFg = finalFgColor
                        ?? throw new EngineInvariantException("gradient mapping fg");
                    Gradient fullGradient = Gradient.WithSteps(
                        [_config.StartingColor, resolvedFg],
                        10,
                        false);
                    scene.ApplyGradientToSymbols([inputSymbol], 6, fullGradient, null);
                }
            }

            // initialize character state
            world.ActivatePath(this, id, centerPath);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string appearanceSymbol = ch.InputSymbol;
                bool appearanceUsesPre = ch.UsesInputPreexistingColors;
                ch.Animation.SetAppearance(
                    appearanceSymbol,
                    appearanceUsesPre,
                    appearanceSymbol,
                    ColorPair.New(_config.StartingColor, null));
            }

            world.Terminal.SetCharacterVisibility(id, true);
            world.ActiveCharacters.Insert(
                id,
                world.Terminal.Arena[(int)id.Value].CharacterId);
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase == Phase.Center && world.ActiveCharacters.IsEmpty)
        {
            _phase = Phase.Full;
            List<CharId> characters;
            {
                CharacterFilter filter = CharacterFilter.Default;
                characters = world.Terminal.GetCharacters(
                    world.Rng,
                    filter,
                    CharacterSort.TopToBottomLeftToRight);
            }

            world.ActiveCharacters.Clear();
            foreach (CharId id in characters)
            {
                world.ActiveCharacters.Insert(
                    id,
                    world.Terminal.Arena[(int)id.Value].CharacterId);
            }

            // middleout.rs:251-256 — upstream iterates the rebuilt set here;
            // canonical order is ascending CharacterId (docs/ordering-inventory.md).
            // Snapshot() is that order (not arena index, not Dictionary order).
            foreach (CharId id in world.ActiveCharacters.Snapshot())
            {
                world.ActivatePath(this, id, "full");
                world.ActivateScene(this, id, "full");
            }
        }

        if (!world.ActiveCharacters.IsEmpty)
        {
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
