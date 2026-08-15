using System;
using System.Collections.Generic;
using System.Linq;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>pop_condition choices.</summary>
public enum PopCondition
{
    Row,
    Bottom,
    Anywhere,
}

/// <summary>BubblesIterator.Bubble state (methods live on Bubbles for hooks access).</summary>
public sealed class BubbleState
{
    public List<CharId> Characters { get; set; } = new List<CharId>();
    public long Radius { get; set; }
    public CharId AnchorChar { get; set; }
    public long LowestRow { get; set; }
    public bool Landed { get; set; }
}

/// <summary>bubbles, ported from effects/effect_bubbles.py. Transcribed from <c>effects/bubbles.rs</c>.</summary>
public sealed class BubblesConfig
{
    public bool Rainbow { get; set; }
    public List<Color> BubbleColors { get; set; } = new List<Color>();
    public Color PopColor { get; set; } = Color.FromHex("ffffff");
    public double BubbleSpeed { get; set; } = 0.5;
    public long BubbleDelay { get; set; } = 20;
    public PopCondition PopCondition { get; set; } = PopCondition.Row;
    public Easing MovementEasing { get; set; } = Easing.InOutSine;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Diagonal;
}

public sealed class Bubbles : IEffect
{
    private readonly BubblesConfig _config;
    private readonly List<BubbleState> _bubbles;
    private readonly List<BubbleState> _animatingBubbles;
    private readonly Gradient _rainbowGradient;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    private long _stepsSinceLastBubble;

    public Bubbles(BubblesConfig config)
    {
        Color[] rainbowStops =
        [
            Color.FromHex("e81416"),
            Color.FromHex("ffa500"),
            Color.FromHex("faeb36"),
            Color.FromHex("79c314"),
            Color.FromHex("487de7"),
            Color.FromHex("4b369d"),
            Color.FromHex("70369d"),
        ];
        _config = config;
        _bubbles = new List<BubbleState>();
        _animatingBubbles = new List<BubbleState>();
        _rainbowGradient = Gradient.WithSteps(rainbowStops, 5, false);
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _stepsSinceLastBubble = 0;
    }

    /// <summary>bubbles.rs parse_pop_condition.</summary>
    public static object ParsePopCondition(string s)
    {
        return s switch
        {
            "row" => PopCondition.Row,
            "bottom" => PopCondition.Bottom,
            "anywhere" => PopCondition.Anywhere,
            _ => throw new UsageError($"invalid choice: '{s}' (choose from 'row', 'bottom', 'anywhere')"),
        };
    }

    public static Bubbles FromOptions(Dictionary<string, object> options)
    {
        return new Bubbles(new BubblesConfig
        {
            Rainbow = options.ContainsKey("--rainbow"),
            BubbleColors = TypedList<Color>(options, "--bubble-colors"),
            PopColor = (Color)options["--pop-color"],
            BubbleSpeed = (double)options["--bubble-speed"],
            BubbleDelay = (long)options["--bubble-delay"],
            PopCondition = (PopCondition)options["--pop-condition"],
            MovementEasing = (Easing)options["--movement-easing"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>Bubble.set_character_coordinates.</summary>
    private void BubbleSetCharacterCoordinates(EngineWorld world, BubbleState bubble)
    {
        Coord anchorCoord = world.Terminal.Arena[(int)bubble.AnchorChar.Value].Motion.CurrentCoord;
        List<Coord> points = Geometry.FindCoordsOnCircle(
            anchorCoord,
            bubble.Radius,
            bubble.Characters.Count,
            false);
        for (int i = 0; i < bubble.Characters.Count; i++)
        {
            CharId id = bubble.Characters[i];
            Coord point = points[i];
            world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(point);
            if (point.Row == bubble.LowestRow)
            {
                bubble.Landed = true;
            }
        }

        if (_config.PopCondition == PopCondition.Anywhere && world.Rng.Random() < 0.002)
        {
            bubble.Landed = true;
        }
    }

    /// <summary>Bubble.__init__ (+ make_waypoints + make_gradients).</summary>
    private BubbleState MakeBubble(EngineWorld world, Coord origin, List<CharId> characters)
    {
        long radius = Math.Max(characters.Count / 5, 1);
        CharId anchorChar = world.Terminal.AddCharacter(" ", origin);
        long lowestRow;
        if (_config.PopCondition == PopCondition.Row)
        {
            // bubbles.rs:156 — .min().unwrap() on empty throws
            lowestRow = characters
                .Select(id => world.Terminal.Arena[(int)id.Value].InputCoord.Row)
                .Min();
        }
        else
        {
            lowestRow = world.Terminal.Canvas.Bottom;
        }

        var bubble = new BubbleState
        {
            Characters = characters,
            Radius = radius,
            AnchorChar = anchorChar,
            LowestRow = lowestRow,
            Landed = false,
        };
        BubbleSetCharacterCoordinates(world, bubble);
        bubble.Landed = false;

        long waypointColumn = world.Rng.Randint(world.Terminal.Canvas.Left, world.Terminal.Canvas.Right);
        {
            EffectCharacter ch = world.Terminal.Arena[(int)anchorChar.Value];
            string floorPath = ch.Motion.NewPath(_config.BubbleSpeed, null, null, 0, false, "");
            ch.Motion.Paths.Get(floorPath)!
                .NewWaypoint(Coord.New(waypointColumn, bubble.LowestRow), null, "");
            world.ActivatePath(this, anchorChar, floorPath);
        }

        if (_config.Rainbow)
        {
            var rainbowGradient = new List<Color>(_rainbowGradient.Spectrum);
            int gradientOffset = 0;
            foreach (CharId id in bubble.Characters)
            {
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                string sheenScene;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    sheenScene = ch.Animation.NewScene(false, null, null, "", usesPre);
                    Scene scene = ch.Animation.Scenes.Get(sheenScene)
                        ?? throw new EngineInvariantException("sheen scene");
                    foreach (Color step in rainbowGradient)
                    {
                        scene.AddFrame(
                            inputSymbol,
                            4,
                            new VisualParams { Colors = ColorPair.New(step, null) });
                    }
                }

                gradientOffset += 2;
                gradientOffset %= rainbowGradient.Count;
                var rotated = new List<Color>();
                rotated.AddRange(rainbowGradient.GetRange(gradientOffset, rainbowGradient.Count - gradientOffset));
                rotated.AddRange(rainbowGradient.GetRange(0, gradientOffset));
                rainbowGradient = rotated;
                world.ActivateScene(this, id, sheenScene);
                string? activeScene = world.Terminal.Arena[(int)id.Value].Animation.ActiveScene;
                if (activeScene is not null)
                {
                    world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(activeScene)!.IsLooping = true;
                }
            }
        }
        else
        {
            Color bubbleColor = world.Rng.Choice(_config.BubbleColors);
            foreach (CharId id in bubble.Characters)
            {
                string inputSymbol;
                bool usesPre;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    inputSymbol = ch.InputSymbol;
                    usesPre = ch.UsesInputPreexistingColors;
                }

                string sheenScene;
                {
                    EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                    sheenScene = ch.Animation.NewScene(false, null, null, "", usesPre);
                    ch.Animation.Scenes.Get(sheenScene)!
                        .AddFrame(
                            inputSymbol,
                            1,
                            new VisualParams { Colors = ColorPair.New(bubbleColor, null) });
                }

                world.ActivateScene(this, id, sheenScene);
            }
        }

        return bubble;
    }

    /// <summary>Bubble.pop.</summary>
    private void BubblePop(EngineWorld world, BubbleState bubble)
    {
        Coord anchorCoord = world.Terminal.Arena[(int)bubble.AnchorChar.Value].Motion.CurrentCoord;
        List<Coord> points = Geometry.FindCoordsOnCircle(
            anchorCoord,
            bubble.Radius + 3,
            bubble.Characters.Count,
            true);
        int zipCount = Math.Min(bubble.Characters.Count, points.Count);
        for (int i = 0; i < zipCount; i++)
        {
            CharId id = bubble.Characters[i];
            Coord point = points[i];
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string popOutPath = ch.Motion.NewPath(0.3, Easing.OutExpo, null, 0, false, "pop_out");
                ch.Motion.Paths.Get(popOutPath)!.NewWaypoint(point, null, "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path("pop_out"),
                new EventAction.ActivatePath("final"));
        }

        foreach (CharId id in bubble.Characters)
        {
            world.ActivateScene(this, id, "pop_1");
            world.ActivatePath(this, id, "pop_out");
        }
    }

    /// <summary>Bubble.move.</summary>
    private void BubbleMove(EngineWorld world, BubbleState bubble)
    {
        world.MotionMove(this, bubble.AnchorChar);
        BubbleSetCharacterCoordinates(world, bubble);
        for (int i = 0; i < bubble.Characters.Count; i++)
        {
            CharId id = bubble.Characters[i];
            world.StepAnimation(this, id);
        }
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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
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

            _characterFinalColorMap[id] = finalGradientMapping.Get(inputCoord)
                ?? throw new EngineInvariantException("gradient mapping missing");
            world.Terminal.Arena[(int)id.Value].Layer = 1;
            string pop1Scene;
            string pop2Scene;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                pop1Scene = ch.Animation.NewScene(false, null, null, "pop_1", usesPre);
                pop2Scene = ch.Animation.NewScene(false, null, null, "", usesPre);
                ch.Animation.Scenes.Get(pop1Scene)!
                    .AddFrame("*", 9, new VisualParams { Colors = ColorPair.New(_config.PopColor, null) });
                ch.Animation.Scenes.Get(pop2Scene)!
                    .AddFrame("'", 9, new VisualParams { Colors = ColorPair.New(_config.PopColor, null) });
            }

            string finalScene;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                finalScene = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            if (dynamic)
            {
                Gradient? fgGradient = inputFg is not null
                    ? Gradient.WithSteps([_config.PopColor, inputFg], 8, false)
                    : null;
                Gradient? bgGradient = inputBg is not null
                    ? Gradient.WithSteps([_config.PopColor, inputBg], 8, false)
                    : null;
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                Scene scene = ch.Animation.Scenes.Get(finalScene)
                    ?? throw new EngineInvariantException("final scene");
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
                Color finalColor = _characterFinalColorMap[id];
                Gradient charFinalGradient = Gradient.WithSteps([_config.PopColor, finalColor], 8, false);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get(finalScene)!
                    .ApplyGradientToSymbols([inputSymbol], 6, charFinalGradient, null);
            }

            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(pop1Scene),
                new EventAction.ActivateScene(pop2Scene));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(pop2Scene),
                new EventAction.ActivateScene(finalScene));
            string finalPath;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                finalPath = ch.Motion.NewPath(0.3, Easing.InOutExpo, null, 0, false, "final");
                ch.Motion.Paths.Get(finalPath)!.NewWaypoint(inputCoord, null, "");
            }

            world.RegisterEvent(
                id,
                Event.PathComplete,
                new CallerKey.Path(finalPath),
                new EventAction.SetLayer(0));
        }

        var unbubbledChars = new List<CharId>();
        foreach (List<CharId> charList in world.Terminal.GetCharactersGrouped(
                     CharacterFilter.Default,
                     CharacterGroup.RowBottomToTop))
        {
            unbubbledChars.AddRange(charList);
        }

        _bubbles.Clear();
        while (unbubbledChars.Count > 0)
        {
            var bubbleGroup = new List<CharId>();
            if (unbubbledChars.Count < 5)
            {
                bubbleGroup.AddRange(unbubbledChars);
                unbubbledChars.Clear();
            }
            else
            {
                long count = world.Rng.Randint(5, Math.Min(unbubbledChars.Count, 20));
                for (long i = 0; i < count; i++)
                {
                    // bubbles.rs:475 — FIFO remove(0)
                    CharId next = unbubbledChars[0];
                    unbubbledChars.RemoveAt(0);
                    bubbleGroup.Add(next);
                }
            }

            Coord bubbleOrigin = Coord.New(
                world.Rng.Randint(world.Terminal.Canvas.Left, world.Terminal.Canvas.Right),
                world.Terminal.Canvas.Top + 10);
            BubbleState newBubble = MakeBubble(world, bubbleOrigin, bubbleGroup);
            _bubbles.Add(newBubble);
        }

        _animatingBubbles.Clear();
        _stepsSinceLastBubble = 0;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_animatingBubbles.Count > 0 || !world.ActiveCharacters.IsEmpty || _bubbles.Count > 0)
        {
            if (_bubbles.Count > 0 && _stepsSinceLastBubble >= _config.BubbleDelay)
            {
                // bubbles.rs:493 — FIFO remove(0)
                BubbleState nextBubble = _bubbles[0];
                _bubbles.RemoveAt(0);
                foreach (CharId id in nextBubble.Characters)
                {
                    world.Terminal.SetCharacterVisibility(id, true);
                }

                _animatingBubbles.Add(nextBubble);
                _stepsSinceLastBubble = 0;
            }

            _stepsSinceLastBubble += 1;

            var animating = new List<BubbleState>(_animatingBubbles);
            _animatingBubbles.Clear();
            foreach (BubbleState bubble in animating)
            {
                if (bubble.Landed)
                {
                    BubblePop(world, bubble);
                    foreach (CharId id in bubble.Characters)
                    {
                        world.ActiveCharacters.Insert(
                            id,
                            world.Terminal.Arena[(int)id.Value].CharacterId);
                    }
                }
            }

            foreach (BubbleState bubble in animating)
            {
                if (!bubble.Landed)
                {
                    _animatingBubbles.Add(bubble);
                }
            }

            foreach (BubbleState bubble in _animatingBubbles)
            {
                BubbleMove(world, bubble);
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
