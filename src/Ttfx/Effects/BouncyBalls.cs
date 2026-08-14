using System.Collections.Generic;
using System.Linq;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>bouncyballs, ported from effects/effect_bouncyballs.py. Transcribed from <c>effects/bouncyballs.rs</c>.</summary>
public sealed class BouncyBallsConfig
{
    public List<Color> BallColors { get; set; } = new List<Color>();
    public List<string> BallSymbols { get; set; } = new List<string>();
    public long BallDelay { get; set; } = 4;
    public double MovementSpeed { get; set; } = 0.45;
    public Easing MovementEasing { get; set; } = Easing.OutBounce;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class BouncyBalls : IEffect
{
    private readonly BouncyBallsConfig _config;
    private readonly List<CharId> _pendingChars;
    // BTreeMap in bouncyballs.rs — SortedDictionary min-key iteration matches.
    private readonly SortedDictionary<long, List<CharId>> _groupByRow;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private long _ballDelay;

    public BouncyBalls(BouncyBallsConfig config)
    {
        _config = config;
        _pendingChars = new List<CharId>();
        _groupByRow = new SortedDictionary<long, List<CharId>>();
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _ballDelay = 0;
    }

    public static BouncyBalls FromOptions(Dictionary<string, object> options)
    {
        return new BouncyBalls(new BouncyBallsConfig
        {
            BallColors = TypedList<Color>(options, "--ball-colors"),
            BallSymbols = TypedList<string>(options, "--ball-symbols"),
            BallDelay = (long)options["--ball-delay"],
            MovementSpeed = (double)options["--movement-speed"],
            MovementEasing = (Easing)options["--movement-easing"],
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

        long canvasTop = world.Terminal.Canvas.Top;
        foreach (CharId id in characters)
        {
            Coord inputCoord;
            string inputSymbol;
            Color? inputFg;
            Color? inputBg;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                usesPre = ch.UsesInputPreexistingColors;
            }

            Color mapped = finalGradientMapping.Get(inputCoord)
                ?? throw new EngineInvariantException("gradient mapping missing");
            _characterFinalColorMap[id] = mapped;
            Color color = world.Rng.Choice(_config.BallColors);
            string symbol = world.Rng.Choice(_config.BallSymbols);
            string ballScene;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string sceneId = ch.Animation.NewScene(false, null, null, "", usesPre);
                Scene scene = ch.Animation.Scenes.Get(sceneId)
                    ?? throw new EngineInvariantException("ball scene");
                scene.AddFrame(
                    symbol,
                    1,
                    new VisualParams { Colors = ColorPair.New(color, null) });
                ballScene = sceneId;
            }

            string finalScene;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                finalScene = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            if (dynamic)
            {
                Gradient? fgGradient = inputFg is Color fg
                    ? Gradient.WithSteps([color, fg], 10, false)
                    : null;
                Gradient? bgGradient = inputBg is Color bg
                    ? Gradient.WithSteps([color, bg], 10, false)
                    : null;
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(finalScene)
                    ?? throw new EngineInvariantException("final scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols(
                        [inputSymbol],
                        6,
                        fgGradient,
                        bgGradient);
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
                Color finalColor = _characterFinalColorMap.TryGetValue(id, out Color? stored)
                    ? stored
                    : throw new EngineInvariantException("final color missing");
                Gradient charFinalGradient = Gradient.WithSteps([color, finalColor], 10, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(finalScene)
                    ?? throw new EngineInvariantException("final scene");
                scene.ApplyGradientToSymbols(
                    [inputSymbol],
                    6,
                    charFinalGradient,
                    null);
            }

            // Coord(input column, int(canvas.top * uniform(1.0, 1.5))) — int() truncation
            // bouncyballs.rs:187 — (canvas_top as f64 * rng.uniform(1.0, 1.5)) as i64
            long dropRow = PyCompat.TruncToI64(canvasTop * world.Rng.Uniform(1.0, 1.5));
            string inputCoordPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Motion.SetCoordinate(Coord.New(inputCoord.Column, dropRow));
                string pathId = ch.Motion.NewPath(
                    _config.MovementSpeed,
                    _config.MovementEasing,
                    null,
                    0,
                    false,
                    "");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("input coord path");
                path.NewWaypoint(inputCoord, null, "");
                inputCoordPath = pathId;
            }

            world.ActivatePath(this, id, inputCoordPath);
            world.ActivateScene(this, id, ballScene);
            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(inputCoordPath),
                new EventAction.ActivateScene(finalScene));
            _pendingChars.Add(id);
        }

        // bouncyballs.rs:215 — sort_by_key is stable; List.Sort is not
        List<CharId> sortedChars = _pendingChars
            .OrderBy(id => world.Terminal.Arena[(int)id.Value].InputCoord.Row)
            .ToList();
        foreach (CharId id in sortedChars)
        {
            long row = world.Terminal.Arena[(int)id.Value].InputCoord.Row;
            if (!_groupByRow.TryGetValue(row, out List<CharId>? group))
            {
                group = new List<CharId>();
                _groupByRow[row] = group;
            }

            group.Add(id);
        }

        _pendingChars.Clear();
        _ballDelay = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_groupByRow.Count > 0 || !world.ActiveCharacters.IsEmpty || _pendingChars.Count > 0)
        {
            if (_pendingChars.Count == 0 && _groupByRow.Count > 0)
            {
                long minRow = 0;
                foreach (long key in _groupByRow.Keys)
                {
                    minRow = key;
                    break;
                }

                if (!_groupByRow.Remove(minRow, out List<CharId>? group))
                {
                    throw new EngineInvariantException("group_by_row missing min row");
                }

                _pendingChars.AddRange(group);
            }

            if (_pendingChars.Count > 0)
            {
                if (_ballDelay == 0)
                {
                    long drops = world.Rng.Randint(2, 6);
                    for (long i = 0; i < drops; i++)
                    {
                        if (_pendingChars.Count == 0)
                        {
                            break;
                        }

                        // bouncyballs.rs:238-239 — Randint(0, pending.Count-1) then RemoveAt
                        int index = (int)world.Rng.Randint(0, _pendingChars.Count - 1);
                        CharId nextCharacter = _pendingChars[index];
                        _pendingChars.RemoveAt(index);
                        world.Terminal.SetCharacterVisibility(nextCharacter, true);
                        world.ActiveCharacters.Insert(
                            nextCharacter,
                            world.Terminal.Arena[(int)nextCharacter.Value].CharacterId);
                    }

                    _ballDelay = _config.BallDelay;
                }
                else
                {
                    _ballDelay -= 1;
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
