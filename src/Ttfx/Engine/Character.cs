using Ttfx.Utils;

namespace Ttfx.Engine;

/// <summary>
/// Arena slot index (dense). Never used for ordering.
/// Transcribed from <c>engine/character.rs</c>.
/// </summary>
public readonly record struct CharId(uint Value);

/// <summary>
/// Cardinal neighbor slots, upstream's dict keys north/east/south/west.
/// </summary>
public struct Neighbors
{
    public CharId? North;
    public CharId? East;
    public CharId? South;
    public CharId? West;
}

/// <summary>
/// EffectCharacter, ported from engine/base_character.py, stored in an arena.
///
/// <c>CharId</c> is the arena slot index. <c>character_id</c> is the Python-compatible
/// monotonically allocated id — these are NOT the same thing: the Python parser
/// allocates ids for characters that are later overwritten by cursor movement,
/// popped as trailing whitespace, or cropped by the canvas, so surviving
/// characters have id gaps. All canonical orderings sort by <c>character_id</c>.
/// </summary>
public sealed class EffectCharacter
{
    /// <summary>Python-compatible allocation id; canonical ordering key.</summary>
    public uint CharacterId { get; }

    public string InputSymbol { get; }

    public Coord InputCoord { get; set; }

    /// <summary>Raw input SGR sequences captured at parse time (fg, bg).</summary>
    public string? InputAnsiFgSequence { get; set; }

    public string? InputAnsiBgSequence { get; set; }

    public bool IsVisible { get; set; }

    public Animation Animation { get; }

    public Motion Motion { get; }

    public long Layer { get; set; }

    public bool IsFillCharacter { get; set; }

    public bool UsesInputPreexistingColors { get; set; }

    public Neighbors Neighbors { get; set; }

    public EffectCharacter(uint characterId, string symbol, long inputColumn, long inputRow)
    {
        CharacterId = characterId;
        InputSymbol = symbol;
        InputCoord = Coord.New(inputColumn, inputRow);
        InputAnsiFgSequence = null;
        InputAnsiBgSequence = null;
        IsVisible = false;
        Animation = Animation.New(symbol);
        Motion = Motion.New(InputCoord);
        Layer = 0;
        IsFillCharacter = false;
        UsesInputPreexistingColors = false;
        Neighbors = default;
    }
}
