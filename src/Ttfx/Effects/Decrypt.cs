using System;
using System.Collections.Generic;
using Ttfx.Cli;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Effects;

/// <summary>DecryptIterator.Phase.</summary>
public enum DecryptPhase
{
    Typing,
    Decrypting,
}

/// <summary>decrypt, ported from effects/effect_decrypt.py. Transcribed from <c>effects/decrypt.rs</c>.</summary>
public sealed class DecryptConfig
{
    public long TypingSpeed { get; set; } = 2;
    public List<Color> CiphertextColors { get; set; } = new List<Color>();
    public List<Color> FinalGradientStops { get; set; } = new List<Color>();
    public List<long> FinalGradientSteps { get; set; } = new List<long>();
    public GradientDirection FinalGradientDirection { get; set; } = GradientDirection.Vertical;
}

public sealed class Decrypt : IEffect
{
    private readonly DecryptConfig _config;
    private readonly List<CharId> _typingPendingChars;
    /// <summary>Upstream is a set; membership only feeds active_characters (decrypt.rs:53-54).</summary>
    private readonly List<CharId> _decryptingPendingChars;
    private DecryptPhase _phase;
    private readonly List<string> _encryptedSymbols;
    // HashMap in the reference: lookup only, iteration order is not contractual.
    private readonly Dictionary<CharId, ColorPair> _characterFinalColorMap;

    public Decrypt(DecryptConfig config)
    {
        _config = config;
        _typingPendingChars = new List<CharId>();
        _decryptingPendingChars = new List<CharId>();
        _phase = DecryptPhase.Typing;
        _encryptedSymbols = new List<string>();
        _characterFinalColorMap = new Dictionary<CharId, ColorPair>();
        MakeEncryptedSymbols();
    }

    public static Decrypt FromOptions(Dictionary<string, object> options)
    {
        return new Decrypt(new DecryptConfig
        {
            TypingSpeed = (long)options["--typing-speed"],
            CiphertextColors = TypedList<Color>(options, "--ciphertext-colors"),
            FinalGradientStops = TypedList<Color>(options, "--final-gradient-stops"),
            FinalGradientSteps = TypedList<long>(options, "--final-gradient-steps"),
            FinalGradientDirection = (GradientDirection)options["--final-gradient-direction"],
        });
    }

    public void DispatchCallback(EngineWorld world, CharId character, EffectCallback callback)
    {
    }

    /// <summary>DecryptIterator.make_encrypted_symbols (_DecryptChars ranges).</summary>
    private void MakeEncryptedSymbols()
    {
        for (uint n = 33; n < 127; n++)
        {
            _encryptedSymbols.Add(char.ConvertFromUtf32((int)n).ToString());
        }

        for (uint n = 9608; n < 9632; n++)
        {
            _encryptedSymbols.Add(char.ConvertFromUtf32((int)n).ToString());
        }

        for (uint n = 9472; n < 9599; n++)
        {
            _encryptedSymbols.Add(char.ConvertFromUtf32((int)n).ToString());
        }

        for (uint n = 174; n < 452; n++)
        {
            _encryptedSymbols.Add(char.ConvertFromUtf32((int)n).ToString());
        }
    }

    /// <summary>DecryptIterator.make_decrypting_animation_scenes.</summary>
    private void MakeDecryptingAnimationScenes(EngineWorld world, CharId id)
    {
        bool dynamic = world.Terminal.Config.ExistingColorHandling == ExistingColorHandling.Dynamic;
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
            ch.Animation.NewScene(false, null, null, "fast_decrypt", usesPre);
        }

        Color color = world.Rng.Choice(_config.CiphertextColors);
        for (int i = 0; i < 80; i++)
        {
            string symbol = world.Rng.Choice(_encryptedSymbols);
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Animation.Scenes.Get("fast_decrypt")!
                .AddFrame(
                    symbol,
                    2,
                    new VisualParams { Colors = ColorPair.New(color, null) });
        }

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Animation.NewScene(false, null, null, "slow_decrypt", usesPre);
        }

        long slowCount = world.Rng.Randint(1, 15);
        for (long i = 0; i < slowCount; i++)
        {
            string symbol = world.Rng.Choice(_encryptedSymbols);
            long duration = world.Rng.Randint(0, 100) <= 30
                ? world.Rng.Randrange(35, 60)
                : world.Rng.Randrange(3, 6);
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Animation.Scenes.Get("slow_decrypt")!
                .AddFrame(
                    symbol,
                    duration,
                    new VisualParams { Colors = ColorPair.New(color, null) });
        }

        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Animation.NewScene(false, null, null, "discovered", usesPre);
        }

        Color white = Color.FromHex("ffffff");
        if (dynamic)
        {
            Gradient? fgGradient = inputFg is not null
                ? Gradient.WithSteps([white, inputFg], 10, false)
                : null;
            Gradient? bgGradient = inputBg is not null
                ? Gradient.WithSteps([white, inputBg], 10, false)
                : null;
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            Scene scene = ch.Animation.Scenes.Get("discovered")
                ?? throw new EngineInvariantException("discovered scene");
            if (fgGradient is not null || bgGradient is not null)
            {
                scene.ApplyGradientToSymbols([inputSymbol], 5, fgGradient, bgGradient);
            }
            else
            {
                scene.AddFrame(
                    inputSymbol,
                    5,
                    new VisualParams { Colors = ColorPair.New(null, null) });
            }
        }
        else
        {
            Color finalFg = _characterFinalColorMap[id].FgColor
                ?? throw new EngineInvariantException("gradient mapping fg");
            Gradient discoveredGradient = Gradient.WithSteps([white, finalFg], 10, false);
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Animation.Scenes.Get("discovered")!
                .ApplyGradientToSymbols([inputSymbol], 5, discoveredGradient, null);
        }
    }

    /// <summary>DecryptIterator.prepare_data_for_type_effect.</summary>
    private void PrepareDataForTypeEffect(EngineWorld world)
    {
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            bool usesPre = world.Terminal.Arena[(int)id.Value].UsesInputPreexistingColors;
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.NewScene(false, null, null, "typing", usesPre);
            }

            string[] blockChars = ["▉", "▓", "▒", "░"];
            foreach (string blockChar in blockChars)
            {
                Color color = world.Rng.Choice(_config.CiphertextColors);
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get("typing")!
                    .AddFrame(
                        blockChar,
                        2,
                        new VisualParams { Colors = ColorPair.New(color, null) });
            }

            string symbol = world.Rng.Choice(_encryptedSymbols);
            Color symColor = world.Rng.Choice(_config.CiphertextColors);
            {
                EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
                ch.Animation.Scenes.Get("typing")!
                    .AddFrame(
                        symbol,
                        1,
                        new VisualParams { Colors = ColorPair.New(symColor, null) });
            }

            _typingPendingChars.Add(id);
        }
    }

    /// <summary>DecryptIterator.prepare_data_for_decrypt_effect.</summary>
    private void PrepareDataForDecryptEffect(EngineWorld world)
    {
        List<CharId> characters = world.Terminal.GetCharacters(
            world.Rng,
            CharacterFilter.Default,
            CharacterSort.TopToBottomLeftToRight);
        foreach (CharId id in characters)
        {
            MakeDecryptingAnimationScenes(world, id);
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("fast_decrypt"),
                new EventAction.ActivateScene("slow_decrypt"));
            world.RegisterEvent(
                id,
                Event.SceneComplete,
                new CallerKey.Scene("slow_decrypt"),
                new EventAction.ActivateScene("discovered"));
            world.ActivateScene(this, id, "fast_decrypt");
            _decryptingPendingChars.Add(id);
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
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ColorPair finalColors = dynamic
                ? ColorPair.New(ch.Animation.InputFgColor, ch.Animation.InputBgColor)
                : ColorPair.New(
                    finalGradientMapping.Get(ch.InputCoord)
                        ?? throw new EngineInvariantException("gradient mapping missing"),
                    null);
            _characterFinalColorMap[id] = finalColors;
        }

        PrepareDataForTypeEffect(world);
        PrepareDataForDecryptEffect(world);
    }

    public string? NextFrame(EngineWorld world)
    {
        if (_phase == DecryptPhase.Typing)
        {
            if (_typingPendingChars.Count > 0 || !world.ActiveCharacters.IsEmpty)
            {
                if (_typingPendingChars.Count > 0 && world.Rng.Randint(0, 100) <= 75)
                {
                    for (long i = 0; i < _config.TypingSpeed; i++)
                    {
                        if (_typingPendingChars.Count > 0)
                        {
                            // decrypt.rs:334 — FIFO remove(0)
                            CharId nextCharacter = _typingPendingChars[0];
                            _typingPendingChars.RemoveAt(0);
                            world.Terminal.SetCharacterVisibility(nextCharacter, true);
                            world.ActivateScene(this, nextCharacter, "typing");
                            world.ActiveCharacters.Insert(
                                nextCharacter,
                                world.Terminal.Arena[(int)nextCharacter.Value].CharacterId);
                        }
                    }
                }

                world.Update(this);
                return world.Frame();
            }

            world.ActiveCharacters.Clear();
            foreach (CharId id in _decryptingPendingChars)
            {
                world.ActiveCharacters.Insert(
                    id,
                    world.Terminal.Arena[(int)id.Value].CharacterId);
            }

            foreach (CharId id in world.ActiveCharacters.Snapshot())
            {
                world.ActivateScene(this, id, "fast_decrypt");
            }

            _phase = DecryptPhase.Decrypting;
        }

        if (_phase == DecryptPhase.Decrypting)
        {
            if (!world.ActiveCharacters.IsEmpty)
            {
                world.Update(this);
                return world.Frame();
            }

            return null;
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
