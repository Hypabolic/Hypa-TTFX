using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>burn, ported from effects/effect_burn.py. Transcribed from <c>effects/burn.rs</c>.</summary>
public sealed class BurnConfig
{
    public Color StartingColor { get; set; } = Color.FromHex("837373");
    public List<Color> BurnColors { get; set; } = new List<Color>();
    public double SmokeChance { get; set; } = 0.5;
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Burn : IEffect
{
    /// <summary>EventHandler.Callback(lambda c: self._emit_smoke(c.input_coord, ...)).</summary>
    private const uint CbEmitSmoke = 0;

    /// <summary>ParticlePool.reclaim_on_event's reclaim closure.</summary>
    private const uint CbReclaimSmoke = 1;

    private static readonly string[] BurnCharOrder =
    [
        "'", ".", "▖", "▙", "█", "▜", "▀", "▝", ".",
    ];

    private static readonly string[] SmokeSymbols =
    [
        ".", ",", "'", "`", "#", "*",
    ];

    private readonly BurnConfig _config;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, Color> _characterFinalColorMap;
    /// <summary>PrimsSimple.char_link_order, consumed FIFO in next_frame (burn.rs:367).</summary>
    private readonly List<CharId> _charLinkOrder;
    /// <summary>
    /// Option so EmitSmoke can move the pool out of self while on_emit needs
    /// &amp;mut self for event dispatch (see emit_smoke).
    /// </summary>
    private ParticlePool? _smokeParticles;
    /// <summary>
    /// Makes each reclaim Callback registration unique, mirroring Python's
    /// fresh closure object per reclaim_on_event call.
    /// </summary>
    private long _emissionCounter;

    public Burn(BurnConfig config)
    {
        _config = config;
        _characterFinalColorMap = new Dictionary<CharId, Color>();
        _charLinkOrder = new List<CharId>();
        _smokeParticles = null;
        _emissionCounter = 0;
    }

    public static Burn FromOptions(Dictionary<string, object> options)
    {
        return new Burn(new BurnConfig
        {
            StartingColor = (Color)options["--starting-color"],
            BurnColors = TypedList<Color>(options, "--burn-colors"),
            SmokeChance = (double)options["--smoke-chance"],
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    /// <summary>BurnIterator._has_input_colors.</summary>
    private static bool HasInputColors(EngineWorld world, CharId id)
    {
        Animation anim = world.Terminal.Arena[(int)id.Value].Animation;
        return anim.InputFgColor is not null || anim.InputBgColor is not null;
    }

    /// <summary>BurnIterator._is_burnable.</summary>
    private bool IsBurnable(EngineWorld world, CharId id)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        return ch.InputSymbol != " "
            || (world.Terminal.Config.ExistingColorHandling != ExistingColorHandling.Ignore
                && HasInputColors(world, id));
    }

    /// <summary>BurnIterator._make_smoke_pool's initialize_smoke.</summary>
    private static void InitializeSmoke(EngineWorld world, CharId id)
    {
        string inputSymbol;
        bool usesPre;
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            inputSymbol = ch.InputSymbol;
            usesPre = ch.UsesInputPreexistingColors;
        }

        Gradient gradient = Gradient.WithSteps(
            [Color.FromHex("504F4F"), Color.FromHex("C7C7C7")],
            9,
            false);
        EffectCharacter chMut = world.Terminal.Arena[(int)id.Value];
        string smokeScn = chMut.Animation.NewScene(false, null, null, "smoke", usesPre);
        Scene scene = chMut.Animation.Scenes.Get(smokeScn)
            ?? throw new EngineInvariantException("smoke scene");
        foreach (Color color in gradient.Spectrum)
        {
            scene.AddFrame(
                inputSymbol,
                10,
                new VisualParams { Colors = ColorPair.New(color, null) });
        }

        chMut.Layer = 2;
    }

    /// <summary>BurnIterator._emit_smoke.</summary>
    private void EmitSmoke(EngineWorld world, Coord origin)
    {
        if (world.Rng.Random() > _config.SmokeChance)
        {
            return;
        }

        ParticleReset reset = ParticleReset.Default;
        _emissionCounter += 1;
        long emissionId = _emissionCounter;
        ParticlePool pool = _smokeParticles
            ?? throw new EngineInvariantException("smoke pool");
        _smokeParticles = null;
        pool.Emit(
            world,
            origin,
            null,
            true,
            reset,
            InitializeSmoke,
            (ctx, nextParticle) =>
            {
                ctx.Terminal.Arena[(int)nextParticle.Value].Animation.Scenes.Get("smoke")!
                    .ResetScene();
                string smokePath = ctx.Terminal.Arena[(int)nextParticle.Value].Motion.NewPath(
                    0.5,
                    null,
                    null,
                    0,
                    false,
                    "");
                Coord riseTargetCoord = Coord.New(
                    ctx.Rng.Randint(origin.Column - 4, origin.Column + 4),
                    ctx.Terminal.Canvas.Top + 1);
                ctx.Terminal.Arena[(int)nextParticle.Value].Motion.Paths.Get(smokePath)!
                    .NewWaypoint(riseTargetCoord, null, "");
                ctx.ActivatePath(this, nextParticle, smokePath);
                ctx.ActivateScene(this, nextParticle, "smoke");
                // burn.rs:178-185 — payload is emission_id at registration, not a loop capture.
                ctx.RegisterEvent(
                    nextParticle,
                    Event.SceneComplete,
                    new CallerKey.Scene("smoke"),
                    new EventAction.Callback(
                        new EffectCallback(CbReclaimSmoke, [new CallbackValue.Int(emissionId)])));
            });
        _smokeParticles = pool;
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
        switch (callback.Id)
        {
            case CbEmitSmoke:
            {
                Coord origin = world.Terminal.Arena[(int)character.Value].InputCoord;
                EmitSmoke(world, origin);
                break;
            }
            case CbReclaimSmoke:
                _smokeParticles?.Reclaim(world, character, true, true);
                break;
        }
    }

    public void Build(EngineWorld world)
    {
        PrimsSimple algo = PrimsSimple.New(world, null, true);
        ParticlePool pool = ParticlePool.New(
            new List<string>(SmokeSymbols),
            2000,
            null);
        pool.Preallocate(world, 2000, InitializeSmoke);
        _smokeParticles = pool;

        List<string> burnCharOrder = new List<string>(BurnCharOrder);
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
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            Coord coord = world.Terminal.Arena[(int)id.Value].InputCoord;
            _characterFinalColorMap[id] = finalGradientMapping.Get(coord)
                ?? throw new EngineInvariantException("gradient mapping");
        }

        Gradient fireGradient = Gradient.WithSteps(_config.BurnColors, 10, false);
        while (!algo.Complete)
        {
            algo.Step(world);
        }

        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
        characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            world.Terminal.SetCharacterVisibility(id, true);
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

            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.SetAppearance(
                    inputSymbol,
                    usesPre,
                    inputSymbol,
                    ColorPair.New(_config.StartingColor, null));
            }

            string burnScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                burnScn = ch.Animation.NewScene(false, null, null, "burn", usesPre);
                ch.Animation.Scenes.Get(burnScn)!
                    .ApplyGradientToSymbols(burnCharOrder, 4, fireGradient, null);
            }

            string finalColorScn;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                finalColorScn = ch.Animation.NewScene(false, null, null, "", usesPre);
            }

            Color fireLast = fireGradient.Spectrum[^1];
            if (dynamic)
            {
                Gradient? fgGradient = inputFg is Color fg
                    ? Gradient.WithSteps([fireLast, fg], 8, false)
                    : null;
                Gradient? bgGradient = inputBg is Color bg
                    ? Gradient.WithSteps([fireLast, bg], 8, false)
                    : null;
                Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(finalColorScn)
                    ?? throw new EngineInvariantException("final color scene");
                if (fgGradient is not null || bgGradient is not null)
                {
                    scene.ApplyGradientToSymbols([inputSymbol], 4, fgGradient, bgGradient);
                }
                else
                {
                    scene.AddFrame(
                        inputSymbol,
                        4,
                        new VisualParams { Colors = new ColorPair() });
                }
            }
            else
            {
                Color finalColor = _characterFinalColorMap[id];
                Gradient charGradient = Gradient.WithSteps([fireLast, finalColor], 8, false);
                Scene scene = world.Terminal.Arena[(int)id.Value].Animation.Scenes.Get(finalColorScn)
                    ?? throw new EngineInvariantException("final color scene");
                foreach (Color color in charGradient.Spectrum)
                {
                    scene.AddFrame(
                        inputSymbol,
                        4,
                        new VisualParams { Colors = ColorPair.New(color, null) });
                }
            }

            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(burnScn),
                new EventAction.ActivateScene(finalColorScn));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene(burnScn),
                new EventAction.Callback(new EffectCallback(CbEmitSmoke, [])));
        }

        _charLinkOrder.Clear();
        _charLinkOrder.AddRange(algo.CharLinkOrder);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_charLinkOrder.Count > 0 || !world.ActiveCharacters.IsEmpty)
        {
            long iterations = world.Rng.Randint(2, 4);
            for (long i = 0; i < iterations; i++)
            {
                if (_charLinkOrder.Count > 0)
                {
                    CharId nextChar = _charLinkOrder[0];
                    _charLinkOrder.RemoveAt(0);
                    if (!IsBurnable(world, nextChar))
                    {
                        continue;
                    }

                    world.ActivateScene(this, nextChar, "burn");
                    world.ActiveCharacters.Insert(
                        nextChar,
                        world.Terminal.Arena[(int)nextChar.Value].CharacterId);
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
