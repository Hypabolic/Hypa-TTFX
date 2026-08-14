using System;
using System.Collections.Generic;
using System.Linq;
using Ttfx.Engine;
using Ttfx.Utils;

namespace Ttfx.Tests;

/// <summary>
/// Grouped character queries vs the scan-based reference, including the
/// destructive alternate-pop interleave (terminal.rs:409).
/// Transcribed from <c>tests/terminal_grouping.rs</c>.
/// </summary>
internal static class TerminalGroupingTests
{
    private static readonly CharacterGroup[] Groupings =
    {
        CharacterGroup.ColumnLeftToRight,
        CharacterGroup.ColumnRightToLeft,
        CharacterGroup.RowTopToBottom,
        CharacterGroup.RowBottomToTop,
        CharacterGroup.DiagonalBottomLeftToTopRight,
        CharacterGroup.DiagonalTopRightToBottomLeft,
        CharacterGroup.DiagonalTopLeftToBottomRight,
        CharacterGroup.DiagonalBottomRightToTopLeft,
        CharacterGroup.CenterToOutside,
        CharacterGroup.OutsideToCenter,
    };

    internal static IEnumerable<TestCase> All()
    {
        yield return new TestCase("terminal_grouping buckets match scan", DirectBucketsMatchScan);
        yield return new TestCase("alternate-pop outside/middle interleave", AlternatePopInterleave);
    }

    private static List<List<CharId>> ReferenceGrouping(Terminal terminal, CharacterFilter filter, CharacterGroup grouping)
    {
        List<CharId> all = terminal.CollectCharacters(filter);
        all = all.OrderBy(id =>
        {
            Coord c = terminal.Arena[(int)id.Value].InputCoord;
            return (c.Row, c.Column);
        }).ToList();
        Coord CoordOf(CharId id) => terminal.Arena[(int)id.Value].InputCoord;
        switch (grouping)
        {
            case CharacterGroup.ColumnLeftToRight:
            case CharacterGroup.ColumnRightToLeft:
            {
                var groups = new List<List<CharId>>();
                for (long key = 0; key <= terminal.Canvas.Right; key++)
                {
                    List<CharId> group = all.Where(id => CoordOf(id).Column == key).ToList();
                    if (group.Count > 0)
                    {
                        groups.Add(group);
                    }
                }

                if (grouping == CharacterGroup.ColumnRightToLeft)
                {
                    groups.Reverse();
                }

                return groups;
            }
            case CharacterGroup.RowBottomToTop:
            case CharacterGroup.RowTopToBottom:
            {
                var groups = new List<List<CharId>>();
                for (long key = 0; key <= terminal.Canvas.Top; key++)
                {
                    List<CharId> group = all.Where(id => CoordOf(id).Row == key).ToList();
                    if (group.Count > 0)
                    {
                        groups.Add(group);
                    }
                }

                if (grouping == CharacterGroup.RowTopToBottom)
                {
                    groups.Reverse();
                }

                return groups;
            }
            case CharacterGroup.DiagonalBottomLeftToTopRight:
            case CharacterGroup.DiagonalTopRightToBottomLeft:
            {
                var groups = new List<List<CharId>>();
                for (long key = 0; key <= terminal.Canvas.Top + terminal.Canvas.Right; key++)
                {
                    List<CharId> group = all.Where(id =>
                    {
                        Coord c = CoordOf(id);
                        return c.Row + c.Column == key;
                    }).ToList();
                    if (group.Count > 0)
                    {
                        groups.Add(group);
                    }
                }

                if (grouping == CharacterGroup.DiagonalTopRightToBottomLeft)
                {
                    groups.Reverse();
                }

                return groups;
            }
            case CharacterGroup.DiagonalTopLeftToBottomRight:
            case CharacterGroup.DiagonalBottomRightToTopLeft:
            {
                var groups = new List<List<CharId>>();
                for (long key = terminal.Canvas.Left - terminal.Canvas.Top;
                     key <= terminal.Canvas.Right - terminal.Canvas.Bottom;
                     key++)
                {
                    List<CharId> group = all.Where(id =>
                    {
                        Coord c = CoordOf(id);
                        return c.Column - c.Row == key;
                    }).ToList();
                    if (group.Count > 0)
                    {
                        groups.Add(group);
                    }
                }

                if (grouping == CharacterGroup.DiagonalBottomRightToTopLeft)
                {
                    groups.Reverse();
                }

                return groups;
            }
            case CharacterGroup.CenterToOutside:
            case CharacterGroup.OutsideToCenter:
            {
                var distances = new List<(long Distance, List<CharId> Group)>();
                int allCount = all.Count;
                for (int i = 0; i < allCount; i++)
                {
                    CharId id = all[i];
                    Coord c = CoordOf(id);
                    long distance = Math.Abs(c.Column - terminal.Canvas.TextCenter.Column)
                        + Math.Abs(c.Row - terminal.Canvas.TextCenter.Row);
                    bool found = false;
                    int distCount = distances.Count;
                    for (int d = 0; d < distCount; d++)
                    {
                        if (distances[d].Distance == distance)
                        {
                            distances[d].Group.Add(id);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        distances.Add((distance, new List<CharId> { id }));
                    }
                }

                distances.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                if (grouping == CharacterGroup.OutsideToCenter)
                {
                    distances.Reverse();
                }

                return distances.Select(pair => pair.Group).ToList();
            }
            default:
                throw new InvalidOperationException($"unknown grouping {grouping}");
        }
    }

    private static bool GroupsEqual(List<List<CharId>> a, List<List<CharId>> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Count != b[i].Count)
            {
                return false;
            }

            for (int j = 0; j < a[i].Count; j++)
            {
                if (a[i][j] != b[i][j])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void DirectBucketsMatchScan()
    {
        Terminal terminal = Terminal.New(
            "A C\n DE\nF G",
            new TerminalConfig
            {
                CanvasWidth = 8,
                CanvasHeight = 6,
                IgnoreTerminalDimensions = true,
            });

        Coord[] extras =
        {
            Coord.New(0, 0),
            Coord.New(-5, 2),
            Coord.New(2, 99),
            Coord.New(1, 3),
            Coord.New(1, 3),
            Coord.New(99, 99),
        };
        for (int i = 0; i < extras.Length; i++)
        {
            terminal.AddCharacter("+", extras[i]);
        }

        CharacterFilter[] filters =
        {
            CharacterFilter.Default,
            new CharacterFilter(true, true, true, true),
            new CharacterFilter(false, false, false, true),
            new CharacterFilter(false, false, false, false),
        };

        int mismatches = 0;
        for (int f = 0; f < filters.Length; f++)
        {
            for (int g = 0; g < Groupings.Length; g++)
            {
                List<List<CharId>> actual = terminal.GetCharactersGrouped(filters[f], Groupings[g]);
                List<List<CharId>> expected = ReferenceGrouping(terminal, filters[f], Groupings[g]);
                if (!GroupsEqual(actual, expected))
                {
                    mismatches++;
                    if (mismatches <= 3)
                    {
                        Console.Error.WriteLine($"grouping {Groupings[g]} filter {f} diverged");
                    }
                }
            }
        }

        Harness.AssertEqual("grouping mismatches", 0, mismatches);
    }

    private static void AlternatePopInterleave()
    {
        Terminal terminal = Terminal.New(
            "ABC\nDEF",
            new TerminalConfig
            {
                CanvasWidth = 6,
                CanvasHeight = 4,
                IgnoreTerminalDimensions = true,
            });
        Rng rng = Rng.Seeded(0);
        List<CharId> outside = terminal.GetCharacters(
            rng,
            CharacterFilter.Default,
            CharacterSort.OutsideRowToMiddle);
        List<CharId> middle = terminal.GetCharacters(
            rng,
            CharacterFilter.Default,
            CharacterSort.MiddleRowToOutside);

        List<CharId> baseOrder = terminal.CollectCharacters(CharacterFilter.Default)
            .OrderBy(id =>
            {
                Coord c = terminal.Arena[(int)id.Value].InputCoord;
                return (-c.Row, c.Column);
            })
            .ToList();
        var deque = new LinkedList<CharId>(baseOrder);
        var interleaved = new List<CharId>();
        bool fromFront = true;
        while (deque.Count > 0)
        {
            if (fromFront)
            {
                interleaved.Add(deque.First!.Value);
                deque.RemoveFirst();
            }
            else
            {
                interleaved.Add(deque.Last!.Value);
                deque.RemoveLast();
            }

            fromFront = !fromFront;
        }

        Harness.AssertTrue("outside_row_to_middle matches alternate-pop", IdsEqual(outside, interleaved));
        interleaved.Reverse();
        Harness.AssertTrue("middle_row_to_outside is reversed alternate-pop", IdsEqual(middle, interleaved));
        Harness.AssertTrue("alternate-pop is not a FIFO drain", outside.Count > 1 && outside[0] != outside[1]);
    }

    private static bool IdsEqual(List<CharId> a, List<CharId> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
