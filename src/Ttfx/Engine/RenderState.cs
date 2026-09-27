using System;
using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// What the renderer reads per character — coordinate, layer, id, visual —
/// kept in dense arrays by arena slot. <see cref="Motion.CurrentCoord"/>,
/// <see cref="EffectCharacter.Layer"/> and
/// <see cref="Animation.CurrentCharacterVisual"/> write through to here, so
/// painting a frame walks a few small arrays instead of three objects per
/// visible character scattered over the heap.
/// Owned by the <see cref="Terminal"/>.
/// </summary>
internal sealed class RenderState
{
    public Coord[] Coords = [];
    public long[] Layers = [];
    public uint[] CharacterIds = [];
    public CharacterVisual?[] Visuals = [];

    /// <summary>Mirror <paramref name="ch"/> at <paramref name="slot"/> from now on.</summary>
    public void Attach(EffectCharacter ch, int slot)
    {
        if (slot >= Coords.Length)
        {
            int length = Math.Max(slot + 1, Coords.Length * 2);
            Array.Resize(ref Coords, length);
            Array.Resize(ref Layers, length);
            Array.Resize(ref CharacterIds, length);
            Array.Resize(ref Visuals, length);
        }

        Coords[slot] = ch.Motion.CurrentCoord;
        Layers[slot] = ch.Layer;
        CharacterIds[slot] = ch.CharacterId;
        Visuals[slot] = ch.Animation.CurrentCharacterVisual;
        ch.AttachRender(this, slot);
    }
}
