using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>
/// --etch-pattern accepts either a CharacterGroup name or the literal
/// "algorithm" (upstream dual-type parser).
/// </summary>
public enum EtchPatternKind
{
    Group,
    Algorithm,
}

public readonly record struct EtchPattern(EtchPatternKind Kind, CharacterGroup Group = default);

/// <summary>laseretch, ported from effects/effect_laseretch.py. Transcribed from <c>effects/laseretch.rs</c>.</summary>
public sealed class LaserEtchConfig
{
    public EtchPattern EtchPattern { get; set; } = new EtchPattern(EtchPatternKind.Algorithm);
    public long EtchSpeed { get; set; } = 1;
    public long EtchDelay { get; set; } = 1;
    public List<Color> CoolGradientStops { get; set; } = new List<Color>();
    public List<Color> LaserGradientStops { get; set; } = new List<Color>();
    public List<Color> SparkGradientStops { get; set; } = new List<Color>();
    public long SparkCoolingFrames { get; set; } = 7;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public long FinalGradientFrames { get; set; } = 4;
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

/// <summary>LaserEtchIterator.Laser state (methods live on LaserEtch for hooks access).</summary>
public sealed class LaserState
{
    public Coord Position { get; set; }
    public List<CharId> BeamChars { get; set; } = new List<CharId>();
    public required Gradient SparkGradient { get; set; }
    public required ParticlePool SparksPool { get; set; }
}

public sealed class LaserEtch : IEffect
{
    /// <summary>sparks_pool.reclaim(spark, hide=True, deactivate=True).</summary>
    private const uint CbReclaimSpark = 0;

    private readonly LaserEtchConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;
    private readonly List<CharId> _pendingChars;
    private long _charDelay;
    private LaserState? _laser;

    public LaserEtch(LaserEtchConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        _pendingChars = new List<CharId>();
        _charDelay = 0;
        _laser = null;
    }

    /// <summary>laseretch.rs parse_etch_pattern.</summary>
    public static object ParseEtchPattern(string s)
    {
        if (s == "algorithm")
        {
            return new EtchPattern(EtchPatternKind.Algorithm);
        }

        return new EtchPattern(EtchPatternKind.Group, (CharacterGroup)ValueParsers.ParseCharacterGroup(s));
    }

    public static LaserEtch FromOptions(Dictionary<string, object> options)
    {
        return new LaserEtch(new LaserEtchConfig
        {
            EtchPattern = (EtchPattern)options["--etch-pattern"],
            EtchSpeed = (long)options["--etch-speed"],
            EtchDelay = (long)options["--etch-delay"],
            CoolGradientStops = TypedList<Color>(options, "--cool-gradient-stops"),
            LaserGradientStops = TypedList<Color>(options, "--laser-gradient-stops"),
            SparkGradientStops = TypedList<Color>(options, "--spark-gradient-stops"),
            SparkCoolingFrames = (long)options["--spark-cooling-frames"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientFrames = (long)options["--final-gradient-frames"],
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    /// <summary>Laser._make_sparks_pool initialize_sparks closure.</summary>
    private static void InitializeSpark(
        EngineWorld world,
        CharId spark,
        IReadOnlyList<Color> sparkColors,
        long coolingFrames)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)spark.Value];
        ch.Layer = 2;
        string inputSymbol = ch.InputSymbol;
        bool usesPre = ch.UsesInputPreexistingColors;
        string sparkScn = ch.Animation.NewScene(false, null, null, "spark", usesPre);
        Scene scene = ch.Animation.Scenes.Get(sparkScn)
            ?? throw new EngineInvariantException("spark scene");
        foreach (Color color in sparkColors)
        {
            scene.AddFrame(
                inputSymbol,
                coolingFrames,
                new VisualParams { Colors = ColorPair.New(color, null) });
        }
    }

    /// <summary>LaserEtchIterator._has_input_colors.</summary>
    private static bool HasInputColors(EngineWorld world, CharId id)
    {
        Animation anim = world.Terminal.Arena[(int)id.Value].Animation;
        return anim.InputFgColor is not null || anim.InputBgColor is not null;
    }

    /// <summary>Laser.__init__ (+ _make_sparks_pool).</summary>
    private LaserState MakeLaser(EngineWorld world)
    {
        // laseretch.rs:166 — VecDeque rotation via pop_front/push_back.
        List<Color> laserGradient = Gradient.New(_config.LaserGradientStops, [6], true, true).Spectrum;
        Gradient sparkGradient = Gradient.New(_config.SparkGradientStops, [3, 8], false, false);
        List<Color> sparkColors = sparkGradient.Spectrum;
        long coolingFrames = _config.SparkCoolingFrames;

        ParticlePool sparksPool = ParticlePool.New([".", ",", "*"], null, null);
        sparksPool.Preallocate(
            world,
            2000,
            (ctx, spark) => InitializeSpark(ctx, spark, sparkColors, coolingFrames));
        foreach (CharId spark in sparksPool.Particles)
        {
            world.RegisterEvent(
                spark,
                Event.SceneComplete,
                new CallerKey.Scene("spark"),
                new EventAction.Callback(new EffectCallback(CbReclaimSpark, [])));
        }

        long row = 0;
        long col = 0;
        var beamChars = new List<CharId>();
        long canvasTop = world.Terminal.Canvas.Top;
        while (row <= canvasTop)
        {
            string symbol = beamChars.Count == 0 ? "*" : "/";
            CharId charId = world.Terminal.AddCharacter(symbol, Coord.New(col, row));
            world.Terminal.Arena[(int)charId.Value].Layer = 2;
            world.Terminal.SetCharacterVisibility(charId, true);
            row += 1;
            col += 1;
            beamChars.Add(charId);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)charId.Value];
                string inputSymbol = ch.InputSymbol;
                bool usesPre = ch.UsesInputPreexistingColors;
                string laserScn = ch.Animation.NewScene(true, null, null, "laser", usesPre);
                Scene scene = ch.Animation.Scenes.Get(laserScn)
                    ?? throw new EngineInvariantException("laser scene");
                foreach (Color color in laserGradient)
                {
                    scene.AddFrame(
                        inputSymbol,
                        3,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }
            }

            // laseretch.rs:225-226 — deque.rotate(-1)
            if (laserGradient.Count > 0)
            {
                Color front = laserGradient[0];
                laserGradient.RemoveAt(0);
                laserGradient.Add(front);
            }

            world.ActivateScene(this, charId, "laser");
        }

        return new LaserState
        {
            Position = Coord.New(0, 0),
            BeamChars = beamChars,
            SparkGradient = sparkGradient,
            SparksPool = sparksPool,
        };
    }

    /// <summary>Laser.reposition.</summary>
    private void LaserReposition(EngineWorld world, Coord target)
    {
        LaserState laser = _laser ?? throw new EngineInvariantException("laser missing");
        _laser = null;
        laser.Position = target;
        long beamRow = target.Row;
        long beamCol = target.Column;
        foreach (CharId charId in laser.BeamChars)
        {
            world.Terminal.Arena[(int)charId.Value].Motion.SetCoordinate(Coord.New(beamCol, beamRow));
            beamRow += 1;
            beamCol += 1;
        }

        LaserEmitSparks(world, laser, 1);
        _laser = laser;
    }

    /// <summary>Laser.emit_sparks (+ its setup_spark_path closure).</summary>
    private void LaserEmitSparks(EngineWorld world, LaserState laser, int sparkCount)
    {
        List<Color> sparkColors = laser.SparkGradient.Spectrum;
        long coolingFrames = _config.SparkCoolingFrames;
        Coord position = laser.Position;
        long bottom = world.Terminal.Canvas.Bottom;
        for (int i = 0; i < sparkCount; i++)
        {
            laser.SparksPool.Emit(
                world,
                position,
                null,
                true,
                ParticleReset.Default,
                (ctx, spark) => InitializeSpark(ctx, spark, sparkColors, coolingFrames),
                (ctx, spark) =>
                {
                    ctx.Terminal.Arena[(int)spark.Value].Motion.SetCoordinate(position);
                    string sparkPath = ctx.Terminal.Arena[(int)spark.Value].Motion.NewPath(
                        0.3,
                        Easing.OutSine,
                        null,
                        0,
                        false,
                        "");
                    Coord fallTargetCoord = Coord.New(
                        ctx.Rng.Randint(position.Column - 20, position.Column + 20),
                        bottom);
                    Coord control = Coord.New(
                        fallTargetCoord.Column,
                        position.Row + ctx.Rng.Randint(-10, 20));
                    ctx.Terminal.Arena[(int)spark.Value].Motion.Paths.Get(sparkPath)!
                        .NewWaypoint(fallTargetCoord, [control], "");
                    ctx.ActivatePath(this, spark, sparkPath);
                    ctx.ActivateScene(this, spark, "spark");
                });
        }
    }

    /// <summary>Laser.disable.</summary>
    private void LaserDisable(EngineWorld world)
    {
        List<CharId> beamChars = _laser?.BeamChars
            ?? throw new EngineInvariantException("laser missing");
        foreach (CharId charId in beamChars)
        {
            world.Terminal.SetCharacterVisibility(charId, false);
        }
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        if (callback.Id == CbReclaimSpark && _laser is LaserState laser)
        {
            laser.SparksPool.Reclaim(world, character, true, true);
        }
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

            Color? finalFgColor;
            Color? finalBgColor;
            Gradient coolGradient;
            if (dynamic)
            {
                ColorPair pair = ColorPair.New(inputFg, inputBg);
                _characterFinalColorMap[id] = pair;
                coolGradient = Gradient.WithSteps(_config.CoolGradientStops, 8, false);
                finalFgColor = pair.FgColor;
                finalBgColor = pair.BgColor;
            }
            else
            {
                Color mapped = finalGradientMapping.Get(inputCoord)
                    ?? throw new EngineInvariantException("gradient mapping");
                ColorPair pair = ColorPair.New(mapped, null);
                _characterFinalColorMap[id] = pair;
                var stops = new List<Color>(_config.CoolGradientStops);
                stops.Add(mapped);
                coolGradient = Gradient.WithSteps(stops, 8, false);
                finalFgColor = pair.FgColor;
                finalBgColor = pair.BgColor;
            }

            Color coolLast = coolGradient.Spectrum[^1];
            Gradient? fgGradient = null;
            Gradient? bgGradient = null;
            Gradient? whiteCooldown = null;
            if (dynamic)
            {
                if (finalFgColor is not null || finalBgColor is not null)
                {
                    if (finalFgColor is Color fg)
                    {
                        fgGradient = Gradient.WithSteps([coolLast, fg], 8, false);
                    }

                    if (finalBgColor is Color bg)
                    {
                        bgGradient = Gradient.WithSteps([coolLast, bg], 8, false);
                    }
                }
                else
                {
                    whiteCooldown = Gradient.WithSteps([coolLast, Color.FromHex("ffffff")], 8, false);
                }
            }

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                string spawnScn = ch.Animation.NewScene(false, null, null, "spawn", usesPre);
                Scene scene = ch.Animation.Scenes.Get(spawnScn)
                    ?? throw new EngineInvariantException("spawn scene");
                scene.AddFrame(
                    "^",
                    3,
                    new VisualParams { Colors = ColorPair.New(Color.FromHex("ffe680"), null) });
                foreach (Color color in coolGradient.Spectrum)
                {
                    scene.AddFrame(
                        inputSymbol,
                        3,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }

                if (dynamic)
                {
                    if (finalFgColor is not null || finalBgColor is not null)
                    {
                        scene.ApplyGradientToSymbols([inputSymbol], 3, fgGradient, bgGradient);
                    }
                    else
                    {
                        scene.ApplyGradientToSymbols([inputSymbol], 3, whiteCooldown, null);
                        scene.AddFrame(
                            inputSymbol,
                            3,
                            new VisualParams { Colors = new ColorPair() });
                    }
                }
            }

            world.ActivateScene(this, id, "spawn");
        }

        if (_config.EtchPattern.Kind == EtchPatternKind.Group)
        {
            // Dead upstream branch — see module docs. pending_chars stays empty.
        }
        else
        {
            RecursiveBacktracker algo = RecursiveBacktracker.New(world, null, true);
            while (!algo.Complete)
            {
                algo.Step(world);
            }

            _pendingChars.Clear();
            _pendingChars.AddRange(algo.CharLinkOrder);
        }

        _charDelay = 0;
        LaserState laser = MakeLaser(world);
        foreach (CharId id in laser.BeamChars)
        {
            world.ActiveCharacters.Insert(id, world.Terminal.Arena[(int)id.Value].CharacterId);
        }

        _laser = laser;
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_pendingChars.Count == 0 && world.ActiveCharacters.IsEmpty)
        {
            return null;
        }

        if (_charDelay == 0)
        {
            for (long i = 0; i < _config.EtchSpeed; i++)
            {
                if (_pendingChars.Count == 0)
                {
                    break;
                }

                CharId nextChar = _pendingChars[0];
                _pendingChars.RemoveAt(0);
                while (world.Terminal.Arena[(int)nextChar.Value].InputSymbol == " "
                    && !HasInputColors(world, nextChar))
                {
                    if (_pendingChars.Count > 0)
                    {
                        nextChar = _pendingChars[0];
                        _pendingChars.RemoveAt(0);
                    }
                    else
                    {
                        break;
                    }
                }

                world.Terminal.SetCharacterVisibility(nextChar, true);
                world.ActiveCharacters.Insert(
                    nextChar,
                    world.Terminal.Arena[(int)nextChar.Value].CharacterId);
                Coord target = world.Terminal.Arena[(int)nextChar.Value].InputCoord;
                LaserReposition(world, target);
            }

            _charDelay = _config.EtchDelay;
        }
        else
        {
            _charDelay -= 1;
        }

        if (_pendingChars.Count > 0)
        {
            List<CharId> beamChars = _laser?.BeamChars
                ?? throw new EngineInvariantException("laser missing");
            foreach (CharId id in beamChars)
            {
                world.ActiveCharacters.Insert(id, world.Terminal.Arena[(int)id.Value].CharacterId);
            }
        }
        else
        {
            LaserDisable(world);
        }

        world.Update(this);
        return world.Frame();
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
