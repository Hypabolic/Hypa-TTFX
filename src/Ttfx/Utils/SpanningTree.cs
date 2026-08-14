using System.Collections.Generic;
using Ttfx.Engine;

namespace Ttfx.Utils;

/// <summary>
/// Spanning-tree generators, ported from utils/spanningtree/. AldousBroder is
/// deliberately not ported: no shipped effect uses it (plan.md §5 divergences).
///
/// Canonical ordering note (plan.md §4.3): <c>EffectCharacter.links</c> is a Python
/// set; BreadthFirst iterates it. Our canonical order is ascending
/// character_id — links are kept sorted-by-id on insert, and the parity shim
/// patches the Python side to <c>sorted(links, key=character_id)</c>.
/// Transcribed from <c>utils/spanning_tree.rs</c>.
/// </summary>
public static class SpanningTree
{
    /// <summary>EffectCharacter._link: bidirectional set-add (id-sorted, see module note).</summary>
    public static void LinkCharacters(EngineWorld world, CharId a, CharId b)
    {
        InsertSorted(world, a, b);
        InsertSorted(world, b, a);
    }

    private static void InsertSorted(EngineWorld world, CharId owner, CharId link)
    {
        List<CharId> links = world.Terminal.Arena[(int)owner.Value].Links;
        uint linkCharacterId = world.Terminal.Arena[(int)link.Value].CharacterId;
        int lo = 0;
        int hi = links.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            uint midCharacterId = world.Terminal.Arena[(int)links[mid].Value].CharacterId;
            if (midCharacterId < linkCharacterId)
            {
                lo = mid + 1;
            }
            else if (midCharacterId > linkCharacterId)
            {
                hi = mid;
            }
            else
            {
                return;
            }
        }

        links.Insert(lo, link);
    }

    /// <summary>
    /// SpanningTreeGenerator.get_neighbors: neighbors in dict order (north, east,
    /// south, west), optional text-boundary and unlinked filters.
    /// </summary>
    public static List<CharId> GetNeighbors(
        EngineWorld world,
        CharId id,
        bool unlinkedOnly,
        bool limitToTextBoundary)
    {
        Neighbors n = world.Terminal.Arena[(int)id.Value].Neighbors;
        var neighbors = new List<CharId>(4);
        if (n.North is CharId north)
        {
            neighbors.Add(north);
        }

        if (n.East is CharId east)
        {
            neighbors.Add(east);
        }

        if (n.South is CharId south)
        {
            neighbors.Add(south);
        }

        if (n.West is CharId west)
        {
            neighbors.Add(west);
        }

        if (limitToTextBoundary)
        {
            neighbors.RemoveAll(candidate =>
                !world.Terminal.Canvas.CoordIsInText(world.Terminal.Arena[(int)candidate.Value].InputCoord));
        }

        if (unlinkedOnly)
        {
            neighbors.RemoveAll(candidate => world.Terminal.Arena[(int)candidate.Value].Links.Count > 0);
        }

        return neighbors;
    }

    public static CharId DefaultStartingChar(EngineWorld world, bool withinTextBoundary)
    {
        Coord coord = world.Terminal.Canvas.RandomCoord(world.Rng, false, withinTextBoundary);
        return world.Terminal.GetCharacterByInputCoord(coord)
            ?? throw new EngineException("Unable to find a starting character.");
    }
}

/// <summary>algo/primssimple.py PrimsSimple.</summary>
public sealed class PrimsSimple
{
    public bool LimitToTextBoundary { get; }
    private CharId _currentChar;
    public CharId? CharLastLinked { get; private set; }
    public List<CharId> CharLinkOrder { get; } = new List<CharId>();
    public List<CharId> EdgeChars { get; } = new List<CharId>();
    public CharId? EdgeLastAdded { get; private set; }
    public CharId? EdgeLastPopped { get; private set; }
    public bool Complete { get; private set; }

    private PrimsSimple(bool limitToTextBoundary, CharId startingChar)
    {
        LimitToTextBoundary = limitToTextBoundary;
        _currentChar = startingChar;
        CharLastLinked = startingChar;
        CharLinkOrder.Add(startingChar);
        EdgeChars.Add(startingChar);
        EdgeLastAdded = startingChar;
    }

    public static PrimsSimple New(EngineWorld world, CharId? startingChar, bool limitToTextBoundary)
    {
        CharId start = startingChar ?? SpanningTree.DefaultStartingChar(world, limitToTextBoundary);
        return new PrimsSimple(limitToTextBoundary, start);
    }

    /// <summary>
    /// Faithful quirk: <c>complete</c> flips only when edge_chars is already empty
    /// at call entry.
    /// </summary>
    public void Step(EngineWorld world)
    {
        if (EdgeChars.Count > 0)
        {
            int idx = (int)world.Rng.Randrange(0, EdgeChars.Count);
            _currentChar = EdgeChars[idx];
            EdgeChars.RemoveAt(idx);
            EdgeLastPopped = _currentChar;
            List<CharId> unlinkedNeighbors = SpanningTree.GetNeighbors(world, _currentChar, true, LimitToTextBoundary);
            if (unlinkedNeighbors.Count > 0)
            {
                int neighborIdx = (int)world.Rng.Randrange(0, unlinkedNeighbors.Count);
                CharId nextChar = unlinkedNeighbors[neighborIdx];
                unlinkedNeighbors.RemoveAt(neighborIdx);
                SpanningTree.LinkCharacters(world, _currentChar, nextChar);
                CharLinkOrder.Add(nextChar);
                CharLastLinked = nextChar;
                if (unlinkedNeighbors.Count > 0)
                {
                    EdgeChars.Add(_currentChar);
                }

                List<CharId> nextNeighbors = SpanningTree.GetNeighbors(world, nextChar, true, LimitToTextBoundary);
                if (nextNeighbors.Count > 0)
                {
                    EdgeChars.Add(nextChar);
                    EdgeLastAdded = nextChar;
                }
            }
        }
        else
        {
            Complete = true;
        }
    }
}

/// <summary>algo/primsweighted.py WeightedLink.</summary>
public readonly record struct WeightedLink(CharId CharA, CharId CharB, long Weight);

/// <summary>algo/primsweighted.py PrimsWeighted.</summary>
public sealed class PrimsWeighted
{
    public bool LimitToTextBoundary { get; }
    private readonly Dictionary<CharId, long> _charWeights = new Dictionary<CharId, long>();
    public CharId? CharLastLinked { get; private set; }
    public List<CharId> CharLinkOrder { get; } = new List<CharId>();
    public List<CharId> NeighborsLastAdded { get; } = new List<CharId>();
    public bool Complete { get; private set; }
    private readonly SortedDictionary<long, List<WeightedLink>> _pendingWeightedLinks = new SortedDictionary<long, List<WeightedLink>>();

    private PrimsWeighted(bool limitToTextBoundary)
    {
        LimitToTextBoundary = limitToTextBoundary;
    }

    public static PrimsWeighted New(EngineWorld world, CharId? startingChar, bool limitToTextBoundary)
    {
        CharId start = startingChar ?? SpanningTree.DefaultStartingChar(world, limitToTextBoundary);
        var generator = new PrimsWeighted(limitToTextBoundary);
        // weights assigned over get_characters(inner+outer fill, default sort)
        // — that ordering is the RNG consumption order, faithfully
        var filter = new CharacterFilter(
            InputChars: true,
            InnerFillChars: true,
            OuterFillChars: true,
            AddedChars: false);
        List<CharId> ordered = world.Terminal.CollectCharacters(filter);
        ordered = System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.OrderBy(ordered, id =>
            {
                Coord c = world.Terminal.Arena[(int)id.Value].InputCoord;
                return (-c.Row, c.Column);
            }));
        int orderedCount = ordered.Count;
        for (int i = 0; i < orderedCount; i++)
        {
            generator._charWeights[ordered[i]] = world.Rng.Randint(0, 99);
        }

        generator.CharLastLinked = start;
        generator.CharLinkOrder.Add(start);
        generator.AddWeightedLinks(world, start);
        return generator;
    }

    private void AddWeightedLinks(EngineWorld world, CharId charId)
    {
        NeighborsLastAdded.Clear();
        List<CharId> neighbors = SpanningTree.GetNeighbors(world, charId, true, LimitToTextBoundary);
        int count = neighbors.Count;
        for (int i = 0; i < count; i++)
        {
            CharId neighbor = neighbors[i];
            NeighborsLastAdded.Add(neighbor);
            long weight = _charWeights[neighbor];
            if (!_pendingWeightedLinks.TryGetValue(weight, out List<WeightedLink>? links))
            {
                links = new List<WeightedLink>();
                _pendingWeightedLinks[weight] = links;
            }

            links.Add(new WeightedLink(charId, neighbor, weight));
        }
    }

    private WeightedLink? GetLowestWeightLink(EngineWorld world)
    {
        while (_pendingWeightedLinks.Count > 0)
        {
            long lowestWeight = 0;
            foreach (long key in _pendingWeightedLinks.Keys)
            {
                lowestWeight = key;
                break;
            }

            List<WeightedLink> linksAtWeight = _pendingWeightedLinks[lowestWeight];
            int idx = (int)world.Rng.Randrange(0, linksAtWeight.Count);
            WeightedLink link = linksAtWeight[idx];
            linksAtWeight.RemoveAt(idx);
            if (linksAtWeight.Count == 0)
            {
                _pendingWeightedLinks.Remove(lowestWeight);
            }

            if (world.Terminal.Arena[(int)link.CharB.Value].Links.Count == 0)
            {
                return link;
            }
        }

        return null;
    }

    public void Step(EngineWorld world)
    {
        if (_pendingWeightedLinks.Count > 0)
        {
            WeightedLink? next = GetLowestWeightLink(world);
            if (next is null)
            {
                Complete = true;
                return;
            }

            WeightedLink nextLink = next.Value;
            SpanningTree.LinkCharacters(world, nextLink.CharA, nextLink.CharB);
            CharLastLinked = nextLink.CharB;
            CharLinkOrder.Add(nextLink.CharB);
            AddWeightedLinks(world, nextLink.CharB);
        }
        else
        {
            Complete = true;
            CharLastLinked = null;
            NeighborsLastAdded.Clear();
        }
    }
}

/// <summary>algo/recursivebacktracker.py RecursiveBacktracker.</summary>
public sealed class RecursiveBacktracker
{
    public bool LimitToTextBoundary { get; }
    private CharId _currentChar;
    public CharId? CharLastLinked { get; private set; }
    public List<CharId> CharLinkOrder { get; } = new List<CharId>();
    public List<CharId> Stack { get; } = new List<CharId>();
    public CharId? StackLastPopped { get; private set; }
    public bool Complete { get; private set; }

    private RecursiveBacktracker(bool limitToTextBoundary, CharId startingChar)
    {
        LimitToTextBoundary = limitToTextBoundary;
        _currentChar = startingChar;
        CharLastLinked = startingChar;
        CharLinkOrder.Add(startingChar);
        Stack.Add(startingChar);
    }

    public static RecursiveBacktracker New(EngineWorld world, CharId? startingChar, bool limitToTextBoundary)
    {
        CharId start = startingChar ?? SpanningTree.DefaultStartingChar(world, limitToTextBoundary);
        return new RecursiveBacktracker(limitToTextBoundary, start);
    }

    public void Step(EngineWorld world)
    {
        CharLastLinked = null;
        StackLastPopped = null;
        if (Stack.Count > 0)
        {
            List<CharId> unvisited = SpanningTree.GetNeighbors(world, _currentChar, true, LimitToTextBoundary);
            if (unvisited.Count > 0)
            {
                CharId nextChar = world.Rng.Choice(unvisited);
                SpanningTree.LinkCharacters(world, _currentChar, nextChar);
                CharLinkOrder.Add(nextChar);
                CharLastLinked = nextChar;
                Stack.Add(nextChar);
                _currentChar = nextChar;
            }
            else
            {
                StackLastPopped = Stack[Stack.Count - 1];
                Stack.RemoveAt(Stack.Count - 1);
                if (Stack.Count > 0)
                {
                    _currentChar = Stack[Stack.Count - 1];
                }
            }
        }
        else
        {
            Complete = true;
        }
    }
}

/// <summary>
/// algo/breadthfirst.py BreadthFirst: traverses the linked graph layer by
/// layer. No randomness of its own; <c>links</c> iteration is ascending id (the
/// canonical order; shim-matched on the Python side).
/// </summary>
public sealed class BreadthFirst
{
    public CharId StartingChar { get; }
    private List<CharId> _frontier = new List<CharId>();
    private readonly HashSet<CharId> _explored = new HashSet<CharId>();
    public List<CharId> ExploredLastStep { get; } = new List<CharId>();
    public List<CharId> CharExploreOrder { get; } = new List<CharId>();
    public bool Complete { get; private set; }

    private BreadthFirst(CharId startingChar)
    {
        StartingChar = startingChar;
        _frontier.Add(startingChar);
        _explored.Add(startingChar);
    }

    public static BreadthFirst New(EngineWorld world, CharId? startingChar, bool limitToTextBoundary)
    {
        CharId start = startingChar ?? SpanningTree.DefaultStartingChar(world, limitToTextBoundary);
        return new BreadthFirst(start);
    }

    public void Step(EngineWorld world)
    {
        ExploredLastStep.Clear();
        if (_frontier.Count == 0)
        {
            Complete = true;
            return;
        }

        var newEdges = new List<CharId>();
        while (_frontier.Count > 0)
        {
            CharId position = _frontier[0];
            _frontier.RemoveAt(0);
            List<CharId> links = world.Terminal.Arena[(int)position.Value].Links;
            var positionNewEdges = new List<CharId>();
            // Length captured once: links snapshot for this node (spanning_tree.rs:318).
            int linkCount = links.Count;
            for (int i = 0; i < linkCount; i++)
            {
                CharId n = links[i];
                if (!_explored.Contains(n) && !_frontier.Contains(n) && !newEdges.Contains(n))
                {
                    positionNewEdges.Add(n);
                }
            }

            int newCount = positionNewEdges.Count;
            for (int i = 0; i < newCount; i++)
            {
                CharId character = positionNewEdges[i];
                _explored.Add(character);
                ExploredLastStep.Add(character);
                CharExploreOrder.Add(character);
            }

            newEdges.AddRange(positionNewEdges);
        }

        _frontier = newEdges;
    }
}
