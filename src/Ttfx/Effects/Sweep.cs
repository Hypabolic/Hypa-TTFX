using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>sweep, ported from effects/effect_sweep.py. Transcribed from <c>effects/sweep.rs</c>.</summary>
public sealed class SweepConfig
{
    public List<string> SweepSymbols { get; set; } = new List<string>();
    public CharacterGroup FirstSweepDirection { get; set; } = CharacterGroup.ColumnRightToLeft;
    public CharacterGroup SecondSweepDirection { get; set; } = CharacterGroup.ColumnLeftToRight;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Sweep : IEffect
{
    private readonly SweepConfig _config;
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private readonly List<Color> _dynamicSecondSweepPalette;
    private bool _complete;
    private bool _firstPhase;
    private SequenceEaser<List<CharId>>? _easer;
    private List<List<CharId>> _groupsSecondSweep;

    public Sweep(SweepConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _dynamicSecondSweepPalette = new List<Color>();
        _complete = false;
        _firstPhase = true;
        _easer = null;
        _groupsSecondSweep = new List<List<CharId>>();
    }

    public static Sweep FromOptions(Dictionary<string, object> options)
    {
        return new Sweep(new SweepConfig
        {
            SweepSymbols = TypedList<string>(options, "--sweep-symbols"),
            FirstSweepDirection = (CharacterGroup)options["--first-sweep-direction"],
            SecondSweepDirection = (CharacterGroup)options["--second-sweep-direction"],
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
        Gradient finalFgGradient = Gradient.New(
            _config.FinalGradientStops,
            _config.FinalGradientSteps,
            false,
            false);
        CoordColorMap finalGradientMapping = finalFgGradient.BuildCoordinateColorMapping(
            world.Terminal.Canvas.TextBottom,
            world.Terminal.Canvas.TextTop,
            world.Terminal.Canvas.TextLeft,
            world.Terminal.Canvas.TextRight,
            _config.FinalGradientDirection);

        Color[] shadesOfGray =
        [
            Color.FromHex("A0A0A0"),
            Color.FromHex("808080"),
            Color.FromHex("404040"),
            Color.FromHex("202020"),
            Color.FromHex("101010"),
        ];

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        if (dynamic)
        {
            List<CharId> paletteChars = world.Terminal.GetCharacters(
                world.Rng,
                CharacterFilter.Default,
                CharacterSort.TopToBottomLeftToRight);
            foreach (CharId id in paletteChars)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                if (ch.Animation.InputFgColor is not null)
                {
                    _dynamicSecondSweepPalette.Add(ch.Animation.InputFgColor);
                }

                if (ch.Animation.InputBgColor is not null)
                {
                    _dynamicSecondSweepPalette.Add(ch.Animation.InputBgColor);
                }
            }

            if (_dynamicSecondSweepPalette.Count == 0)
            {
                _dynamicSecondSweepPalette.AddRange(finalFgGradient.Spectrum);
            }
        }

        CharacterFilter fillsFilter = new CharacterFilter(true, true, true, false);
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            fillsFilter,
            CharacterSort.TopToBottomLeftToRight);

        foreach (CharId id in characters)
        {
            bool isFill;
            Color? inputFg;
            Color? inputBg;
            Coord inputCoord;
            string inputSymbol;
            bool usesPre;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                isFill = ch.IsFillCharacter;
                inputFg = ch.Animation.InputFgColor;
                inputBg = ch.Animation.InputBgColor;
                inputCoord = ch.InputCoord;
                inputSymbol = ch.InputSymbol;
                usesPre = ch.UsesInputPreexistingColors;
            }

            if (!isFill)
            {
                ColorPair finalColors = dynamic
                    ? ColorPair.New(inputFg, inputBg)
                    : ColorPair.New(
                        finalGradientMapping.Get(inputCoord)
                            ?? throw new EngineInvariantException("gradient mapping missing"),
                        null);
                _characterFinalColorMap[id] = finalColors;
            }

            world.Terminal.Arena[(int)id.Value].Animation.NewScene(false, null, null, "initial_sweep", usesPre);
            foreach (string symbol in _config.SweepSymbols)
            {
                Color color = world.Rng.Choice(shadesOfGray);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("initial_sweep")!
                    .AddFrame(symbol, 5, new VisualParams { Colors = ColorPair.New(color, null) });
            }

            world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("initial_sweep")!
                .AddFrame(
                    inputSymbol,
                    1,
                    new VisualParams { Colors = ColorPair.New(Color.FromHex("#808080"), null) });
            world.Terminal.Arena[(int)id.Value].Animation.NewScene(false, null, null, "second_sweep", usesPre);

            foreach (string symbol in _config.SweepSymbols)
            {
                Color color = dynamic
                    ? world.Rng.Choice(_dynamicSecondSweepPalette)
                    : world.Rng.Choice(finalFgGradient.Spectrum);
                world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("second_sweep")!
                    .AddFrame(symbol, 5, new VisualParams { Colors = ColorPair.New(color, null) });
            }

            ColorPair finalColorsFrame;
            if (!isFill)
            {
                finalColorsFrame = _characterFinalColorMap[id];
            }
            else if (dynamic)
            {
                finalColorsFrame = new ColorPair();
            }
            else
            {
                finalColorsFrame = ColorPair.New(Color.FromHex("000000"), null);
            }

            world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get("second_sweep")!
                .AddFrame(inputSymbol, 1, new VisualParams { Colors = finalColorsFrame });
        }

        List<List<CharId>> groupsFirstSweep = world.Terminal.GetCharactersGrouped(
            fillsFilter,
            _config.FirstSweepDirection);
        _easer = new SequenceEaser<List<CharId>>(groupsFirstSweep, Easing.InOutCirc, 100);
        _groupsSecondSweep = world.Terminal.GetCharactersGrouped(
            fillsFilter,
            _config.SecondSweepDirection);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (!world.ActiveCharacters.IsEmpty || !_complete)
        {
            SequenceEaser<List<CharId>> easer = _easer!;
            _easer = null;
            SequenceStep<List<CharId>> step = easer.Step();
            foreach (List<CharId> group in step.Added)
            {
                foreach (CharId id in group)
                {
                    if (_firstPhase)
                    {
                        world.Terminal.SetCharacterVisibility(id, true);
                    }

                    string sceneId = _firstPhase ? "initial_sweep" : "second_sweep";
                    world.ActivateScene(this, id, sceneId);
                }

                foreach (CharId id in group)
                {
                    world.ActiveCharacters.Insert(
                        id,
                        world.Terminal.Arena[(int)id.Value].CharacterId);
                }
            }

            bool easerComplete = easer.IsComplete();
            if (easerComplete && _firstPhase)
            {
                easer.Sequence.Clear();
                easer.Sequence.AddRange(_groupsSecondSweep);
                _groupsSecondSweep.Clear();
                easer.Reset();
                _firstPhase = false;
            }
            else if (easerComplete && !_firstPhase)
            {
                _complete = true;
            }

            _easer = easer;
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
