using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>errorcorrect, ported from effects/effect_errorcorrect.py. Transcribed from <c>effects/errorcorrect.rs</c>.</summary>
public sealed class ErrorCorrectConfig
{
    public double ErrorPairs { get; set; } = 0.1;
    public long SwapDelay { get; set; } = 6;
    public Color ErrorColor { get; set; } = Color.FromHex("e74c3c");
    public Color CorrectColor { get; set; } = Color.FromHex("45bf55");
    public double MovementSpeed { get; set; } = 0.9;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class ErrorCorrect : IEffect
{
    private static readonly string[] BlockWipeStart = ["▁", "▂", "▃", "▄", "▅", "▆", "▇", "█"];
    private static readonly string[] BlockWipeEnd = ["▇", "▆", "▅", "▄", "▃", "▂", "▁"];

    private readonly ErrorCorrectConfig _config;
    private readonly List<(CharId Char1, CharId Char2)> _swapped;
    private long _swapDelay;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public ErrorCorrect(ErrorCorrectConfig config)
    {
        _config = config;
        _swapped = new List<(CharId, CharId)>();
        _swapDelay = 0;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
    }

    public static ErrorCorrect FromOptions(Dictionary<string, object> options)
    {
        return new ErrorCorrect(new ErrorCorrectConfig
        {
            ErrorPairs = (double)options["--error-pairs"],
            SwapDelay = (long)options["--swap-delay"],
            ErrorColor = (Color)options["--error-color"],
            CorrectColor = (Color)options["--correct-color"],
            MovementSpeed = (double)options["--movement-speed"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>ErrorCorrectIterator._get_dynamic_final_scene.</summary>
    private string GetDynamicFinalScene(EngineWorld world, CharId id)
    {
        string inputSymbol;
        Color? inputFg;
        Color? inputBg;
        bool usesPre;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            inputSymbol = ch.InputSymbol;
            inputFg = ch.Animation.InputFgColor;
            inputBg = ch.Animation.InputBgColor;
            usesPre = ch.UsesInputPreexistingColors;
        }

        string finalScene;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            finalScene = ch.Animation.NewScene(false, null, null, "", usesPre);
        }

        Gradient? fgGradient = inputFg is Color fg
            ? Gradient.WithSteps([_config.CorrectColor, fg], 10, false)
            : null;
        Gradient? bgGradient = inputBg is Color bg
            ? Gradient.WithSteps([_config.CorrectColor, bg], 10, false)
            : null;

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            Scene scene = ch.Animation.Scenes.Get(finalScene)
                ?? throw new EngineInvariantException("dynamic final scene");
            if (fgGradient is not null || bgGradient is not null)
            {
                scene.ApplyGradientToSymbols([inputSymbol], 3, fgGradient, bgGradient);
            }
            else
            {
                scene.AddFrame(
                    inputSymbol,
                    3,
                    new VisualParams { Colors = ColorPair.New(null, null) });
            }
        }

        return finalScene;
    }

    /// <summary>ErrorCorrectIterator._configure_swapped_character.</summary>
    private void ConfigureSwappedCharacter(EngineWorld world, CharId id, Gradient correctingGradient)
    {
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        string inputSymbol;
        bool usesPre;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            inputSymbol = ch.InputSymbol;
            usesPre = ch.UsesInputPreexistingColors;
        }

        string firstBlockWipe;
        string lastBlockWipe;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            firstBlockWipe = ch.Animation.NewScene(false, null, null, "", usesPre);
            lastBlockWipe = ch.Animation.NewScene(false, null, null, "", usesPre);
        }

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            Scene scene = ch.Animation.Scenes.Get(firstBlockWipe)
                ?? throw new EngineInvariantException("first block wipe scene");
            foreach (string block in BlockWipeStart)
            {
                scene.AddFrame(
                    block,
                    3,
                    new VisualParams { Colors = ColorPair.New(_config.ErrorColor, null) });
            }
        }

        if (!_characterFinalColorMap.TryGetValue(id, out ColorPair? finalColors))
        {
            throw new EngineInvariantException("final colors missing");
        }

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            Scene scene = ch.Animation.Scenes.Get(lastBlockWipe)
                ?? throw new EngineInvariantException("last block wipe scene");
            if (dynamic)
            {
                for (int i = 0; i < BlockWipeEnd.Length - 1; i++)
                {
                    scene.AddFrame(
                        BlockWipeEnd[i],
                        3,
                        new VisualParams { Colors = ColorPair.New(_config.CorrectColor, null) });
                }

                scene.AddFrame(
                    BlockWipeEnd[BlockWipeEnd.Length - 1],
                    3,
                    new VisualParams { Colors = finalColors });
            }
            else
            {
                foreach (string block in BlockWipeEnd)
                {
                    scene.AddFrame(
                        block,
                        3,
                        new VisualParams { Colors = ColorPair.New(_config.CorrectColor, null) });
                }
            }
        }

        string initialScene;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string sceneId = ch.Animation.NewScene(false, null, null, "", usesPre);
            Scene scene = ch.Animation.Scenes.Get(sceneId)
                ?? throw new EngineInvariantException("initial scene");
            scene.AddFrame(
                inputSymbol,
                1,
                new VisualParams { Colors = ColorPair.New(_config.ErrorColor, null) });
            initialScene = sceneId;
        }

        world.ActivateScene(this, id, initialScene);

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string errorScene = ch.Animation.NewScene(false, null, null, "error", usesPre);
            Scene scene = ch.Animation.Scenes.Get(errorScene)
                ?? throw new EngineInvariantException("error scene");
            for (int i = 0; i < 10; i++)
            {
                scene.AddFrame(
                    "▓",
                    3,
                    new VisualParams { Colors = ColorPair.New(_config.ErrorColor, null) });
                scene.AddFrame(
                    inputSymbol,
                    3,
                    new VisualParams { Colors = ColorPair.New(Color.FromHex("ffffff"), null) });
            }
        }

        string correctingScene;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string sceneId = ch.Animation.NewScene(false, SyncMetric.Distance, null, "", usesPre);
            Scene scene = ch.Animation.Scenes.Get(sceneId)
                ?? throw new EngineInvariantException("correcting scene");
            scene.ApplyGradientToSymbols(["█"], 3, correctingGradient, null);
            correctingScene = sceneId;
        }

        string finalScene;
        if (dynamic)
        {
            finalScene = GetDynamicFinalScene(world, id);
        }
        else
        {
            Color finalFg = FinalColorsFg(_characterFinalColorMap, id);
            Gradient charFinalGradient = Gradient.WithSteps([_config.CorrectColor, finalFg], 10, false);
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            string sceneId = ch.Animation.NewScene(false, null, null, "", usesPre);
            Scene scene = ch.Animation.Scenes.Get(sceneId)
                ?? throw new EngineInvariantException("final scene");
            scene.ApplyGradientToSymbols([inputSymbol], 3, charFinalGradient, null);
            finalScene = sceneId;
        }

        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene("error"),
            new EventAction.ActivateScene(firstBlockWipe));
        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene(firstBlockWipe),
            new EventAction.ActivateScene(correctingScene));
        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene(firstBlockWipe),
            new EventAction.ActivatePath("input_coord"));
        world.RegisterEvent(
            id,
            Event.PathActivated,
            new CallerKey.Path("input_coord"),
            new EventAction.SetLayer(1));
        world.RegisterEvent(
            id,
            Event.PathComplete,
            new CallerKey.Path("input_coord"),
            new EventAction.SetLayer(0));
        world.RegisterEvent(
            id,
            Event.PathComplete,
            new CallerKey.Path("input_coord"),
            new EventAction.ActivateScene(lastBlockWipe));
        world.RegisterEvent(
            id,
            Event.SceneComplete,
            new CallerKey.Scene(lastBlockWipe),
            new EventAction.ActivateScene(finalScene));
    }

    private static Color FinalColorsFg(Dictionary<CharId, ColorPair> map, CharId id)
    {
        if (!map.TryGetValue(id, out ColorPair? pair))
        {
            throw new EngineInvariantException("gradient mapping missing");
        }

        return pair.FgColor ?? throw new EngineInvariantException("gradient mapping fg");
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
            ColorPair finalColors;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
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
            }

            _characterFinalColorMap[id] = finalColors;
        }

        foreach (CharId id in characters)
        {
            if (!_characterFinalColorMap.TryGetValue(id, out ColorPair? spawnColors))
            {
                throw new EngineInvariantException("final colors missing");
            }

            string spawnScene;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                bool usesPre = ch.UsesInputPreexistingColors;
                string inputSymbol = ch.InputSymbol;
                string sceneId = ch.Animation.NewScene(false, null, null, "", usesPre);
                Scene scene = ch.Animation.Scenes.Get(sceneId)
                    ?? throw new EngineInvariantException("spawn scene");
                scene.AddFrame(
                    inputSymbol,
                    1,
                    new VisualParams { Colors = spawnColors });
                spawnScene = sceneId;
            }

            world.ActivateScene(this, id, spawnScene);
            world.Terminal.SetCharacterVisibility(id, true);
        }

        var allCharacters = new List<CharId>(world.Terminal.InputCharacters);
        Gradient correctingGradient = Gradient.WithSteps(
            [_config.ErrorColor, _config.CorrectColor],
            10,
            false);

        // errorcorrect.rs:381 — (error_pairs * characters.len() as f64) as i64
        long pairCount = PyCompat.TruncToI64(_config.ErrorPairs * characters.Count);
        for (long n = 0; n < pairCount; n++)
        {
            if (allCharacters.Count < 2)
            {
                break;
            }

            // errorcorrect.rs:386-389 — two RNG-indexed removals in sequence;
            // the list shrinks between draws, so the second range depends on the first RemoveAt.
            int index1 = (int)world.Rng.Randrange(0, allCharacters.Count);
            CharId char1 = allCharacters[index1];
            allCharacters.RemoveAt(index1);
            int index2 = (int)world.Rng.Randrange(0, allCharacters.Count);
            CharId char2 = allCharacters[index2];
            allCharacters.RemoveAt(index2);

            Coord char1InputCoord = world.Terminal.Arena[(int)char1.Value].InputCoord;
            Coord char2InputCoord = world.Terminal.Arena[(int)char2.Value].InputCoord;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)char1.Value];
                ch.Motion.SetCoordinate(char2InputCoord);
                string pathId = ch.Motion.NewPath(
                    _config.MovementSpeed,
                    null,
                    null,
                    0,
                    false,
                    "input_coord");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("char1 input_coord path");
                path.NewWaypoint(char1InputCoord, null, "");
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)char2.Value];
                ch.Motion.SetCoordinate(char1InputCoord);
                string pathId = ch.Motion.NewPath(
                    _config.MovementSpeed,
                    null,
                    null,
                    0,
                    false,
                    "input_coord");
                Path path = ch.Motion.Paths.Get(pathId)
                    ?? throw new EngineInvariantException("char2 input_coord path");
                path.NewWaypoint(char2InputCoord, null, "");
            }

            _swapped.Add((char1, char2));
            foreach (CharId swappedId in new[] { char1, char2 })
            {
                ConfigureSwappedCharacter(world, swappedId, correctingGradient);
            }
        }
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_swapped.Count > 0 && _swapDelay == 0)
        {
            // errorcorrect.rs:430 — remove(0) FIFO, not pop last
            (CharId char1, CharId char2) = _swapped[0];
            _swapped.RemoveAt(0);
            foreach (CharId id in new[] { char1, char2 })
            {
                world.ActivateScene(this, id, "error");
                world.ActiveCharacters.Insert(
                    id,
                    world.Terminal.Arena[(int)id.Value].CharacterId);
            }

            _swapDelay = _config.SwapDelay;
        }
        else if (_swapDelay != 0)
        {
            _swapDelay -= 1;
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
