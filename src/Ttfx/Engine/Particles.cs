using System;
using System.Collections.Generic;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// ParticlePool / ParticleReset, ported from engine/effect_support/particles.py.
/// Transcribed from <c>engine/particles.rs</c>.
/// </summary>
public readonly record struct ParticleReset(
    bool ClearPaths,
    bool ClearScenes,
    bool ClearEvents,
    bool DeactivatePath,
    bool DeactivateScene,
    bool ResetAppearance)
{
    public static ParticleReset Default { get; } = new ParticleReset(
        ClearPaths: true,
        ClearScenes: false,
        ClearEvents: false,
        DeactivatePath: true,
        DeactivateScene: true,
        ResetAppearance: false);
}

public sealed class ParticlePool
{
    public List<string> Symbols { get; }
    public int? MaxSize { get; }
    public Coord Coord { get; }

    /// <summary>
    /// Available queue: pop from the RIGHT (Python deque.pop), push right.
    /// C# <see cref="Stack{T}"/> is LIFO (push_back + pop_back).
    /// </summary>
    public Stack<CharId> Available { get; } = new Stack<CharId>();

    /// <summary>All owned particles, active and available.</summary>
    public List<CharId> Particles { get; } = new List<CharId>();

    private ParticlePool(List<string> symbols, int? maxSize, Coord coord)
    {
        Symbols = symbols;
        MaxSize = maxSize;
        Coord = coord;
    }

    /// <summary>
    /// ParticlePool.__init__ (without preallocation — call <c>Preallocate</c> after
    /// construction so the initializer closure can run against the world).
    /// </summary>
    public static ParticlePool New(List<string> symbols, int? maxSize, Coord? coord)
    {
        if (symbols.Count == 0)
        {
            throw new EngineException("ParticlePool requires at least one symbol.");
        }

        return new ParticlePool(symbols, maxSize, coord ?? Coord.New(0, 0));
    }

    /// <summary>The <c>initial_count</c> loop from __init__.</summary>
    public void Preallocate(EngineWorld world, int initialCount, Action<EngineWorld, CharId> initializer)
    {
        if (MaxSize is int max && max < initialCount)
        {
            throw new EngineException("max_size must be greater than or equal to initial_count.");
        }

        for (int i = 0; i < initialCount; i++)
        {
            CharId particle = CreateParticle(world, null, initializer);
            Available.Push(particle);
        }
    }

    public int Count => Particles.Count;

    public bool IsEmpty => Particles.Count == 0;

    /// <summary>ParticlePool._create_particle.</summary>
    private CharId CreateParticle(EngineWorld world, string? symbol, Action<EngineWorld, CharId> initializer)
    {
        string resolved = symbol ?? world.Rng.Choice(Symbols);
        CharId particle = world.Terminal.AddCharacter(resolved, Coord);
        initializer(world, particle);
        Particles.Add(particle);
        return particle;
    }

    /// <summary>ParticlePool._reset_particle.</summary>
    private static void ResetParticle(EngineWorld world, CharId id, ParticleReset reset)
    {
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        if (reset.DeactivatePath)
        {
            ch.Motion.DeactivatePath(null);
        }

        if (reset.DeactivateScene)
        {
            ch.Animation.ActiveScene = null;
        }

        if (reset.ClearPaths)
        {
            ch.Motion.Paths.Clear();
        }

        if (reset.ClearScenes)
        {
            ch.Animation.Scenes.Clear();
        }

        if (reset.ClearEvents)
        {
            ch.EventHandler.Clear();
        }

        if (reset.ResetAppearance)
        {
            string inputSymbol = ch.InputSymbol;
            bool uses = ch.UsesInputPreexistingColors;
            ch.Animation.SetAppearance(inputSymbol, uses, inputSymbol, null);
        }
    }

    /// <summary>ParticlePool.acquire.</summary>
    public CharId? Acquire(
        EngineWorld world,
        string? symbol,
        ParticleReset reset,
        Action<EngineWorld, CharId> initializer)
    {
        if (Available.Count > 0)
        {
            CharId particle = Available.Pop();
            ResetParticle(world, particle, reset);
            if (symbol is not null)
            {
                EffectCharacter ch = world.Terminal.Arena[(int)particle.Value];
                ch.InputSymbol = symbol;
                bool uses = ch.UsesInputPreexistingColors;
                ch.Animation.SetAppearance(symbol, uses, symbol, null);
            }

            return particle;
        }

        if (MaxSize is int max && Particles.Count >= max)
        {
            return null;
        }

        CharId created = CreateParticle(world, symbol, initializer);
        ResetParticle(world, created, reset);
        return created;
    }

    /// <summary>ParticlePool.emit: acquire -&gt; position -&gt; on_emit -&gt; visibility -&gt; activate.</summary>
    public CharId? Emit(
        EngineWorld world,
        Coord origin,
        string? symbol,
        bool visible,
        ParticleReset reset,
        Action<EngineWorld, CharId> initializer,
        Action<EngineWorld, CharId> onEmit)
    {
        CharId? particle = Acquire(world, symbol, reset, initializer);
        if (particle is null)
        {
            return null;
        }

        CharId id = particle.Value;
        world.Terminal.Arena[(int)id.Value].Motion.SetCoordinate(origin);
        onEmit(world, id);
        world.Terminal.SetCharacterVisibility(id, visible);
        EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
        world.ActiveCharacters.Insert(id, ch.CharacterId);
        return id;
    }

    /// <summary>ParticlePool.reclaim (idempotent: no duplicate queue entries).</summary>
    public void Reclaim(EngineWorld world, CharId id, bool hide, bool deactivate)
    {
        if (hide)
        {
            world.Terminal.SetCharacterVisibility(id, false);
        }

        if (deactivate)
        {
            EffectCharacter ch = world.Terminal.Arena[(int)id.Value];
            ch.Motion.DeactivatePath(null);
            ch.Animation.ActiveScene = null;
        }

        world.ActiveCharacters.Remove(id);
        if (!Available.Contains(id))
        {
            Available.Push(id);
        }
    }

    /// <summary>ParticlePool.extend: adopt externally created characters, no reset.</summary>
    public void Extend(IEnumerable<CharId> particles)
    {
        foreach (CharId particle in particles)
        {
            Particles.Add(particle);
            Available.Push(particle);
        }
    }
}
