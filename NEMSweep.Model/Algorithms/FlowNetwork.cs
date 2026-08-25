namespace NEMSweep.Model.Algorithms;

/// <summary>
/// A directed, capacitated graph over contiguous integer node indices.
/// </summary>
/// <remarks>
/// Pure graph structure: no domain concepts, no units, and no notion of transmission
/// loss. Losses are applied by the caller over the decomposed result, never inside the
/// flow computation, which is what keeps this a standard max-flow problem.
/// <para>
/// Parallel edges (two edges sharing the same ordered endpoint pair) are rejected so
/// that adjacency has a total order independent of insertion sequence. That is what
/// makes traversal (and therefore the computed flow) deterministic. Antiparallel
/// edges (both A to B and B to A) are permitted.
/// </para>
/// </remarks>
internal sealed class FlowNetwork
{
    private readonly int[] _from;
    private readonly int[] _to;
    private readonly double[] _capacity;
    private readonly int[][] _outgoing;

    private FlowNetwork(int nodeCount, int[] from, int[] to, double[] capacity)
    {
        NodeCount = nodeCount;
        _from = from;
        _to = to;
        _capacity = capacity;
        _outgoing = BuildAdjacency(nodeCount, from, to);
    }

    /// <summary>Number of nodes; valid node indices are 0 to NodeCount - 1.</summary>
    public int NodeCount { get; }

    /// <summary>Number of edges; valid edge indices are 0 to EdgeCount - 1.</summary>
    public int EdgeCount => _from.Length;

    /// <summary>Tail node of the given edge.</summary>
    public int From(int edge) => _from[RequireEdge(edge)];

    /// <summary>Head node of the given edge.</summary>
    public int To(int edge) => _to[RequireEdge(edge)];

    /// <summary>Capacity of the given edge, metered at the sending end.</summary>
    public double Capacity(int edge) => _capacity[RequireEdge(edge)];

    /// <summary>
    /// Edge indices leaving the given node, ordered by head node. The order does not
    /// depend on the sequence in which edges were added.
    /// </summary>
    public IReadOnlyList<int> OutgoingEdges(int node) => _outgoing[RequireNode(node)];

    /// <summary>
    /// A network with the same topology and new capacities, used to carry residual
    /// capacity from one priority stage to the next.
    /// </summary>
    public FlowNetwork WithCapacities(IReadOnlyList<double> capacities)
    {
        ArgumentNullException.ThrowIfNull(capacities);
        if (capacities.Count != EdgeCount)
        {
            throw new ArgumentException(
                $"Expected {EdgeCount} capacities but received {capacities.Count}.",
                nameof(capacities));
        }

        var replacement = new double[EdgeCount];
        for (int edge = 0; edge < EdgeCount; edge++)
        {
            replacement[edge] = RequireCapacity(capacities[edge], nameof(capacities));
        }

        return new FlowNetwork(NodeCount, _from, _to, replacement);
    }

    /// <summary>Starts building a network with the given number of nodes.</summary>
    public static FlowNetworkBuilder Build(int nodeCount) => new(nodeCount);

    internal static double RequireCapacity(double capacity, string parameterName)
    {
        if (!double.IsFinite(capacity) || capacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                capacity,
                "Edge capacity must be finite and non-negative.");
        }

        return capacity;
    }

    internal static FlowNetwork Create(int nodeCount, int[] from, int[] to, double[] capacity) =>
        new(nodeCount, from, to, capacity);

    private static int[][] BuildAdjacency(int nodeCount, int[] from, int[] to)
    {
        var outgoing = new List<int>[nodeCount];
        for (int node = 0; node < nodeCount; node++)
        {
            outgoing[node] = [];
        }

        for (int edge = 0; edge < from.Length; edge++)
        {
            outgoing[from[edge]].Add(edge);
        }

        var adjacency = new int[nodeCount][];
        for (int node = 0; node < nodeCount; node++)
        {
            adjacency[node] = outgoing[node].OrderBy(edge => to[edge]).ToArray();
        }

        return adjacency;
    }

    private int RequireEdge(int edge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(edge);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge, EdgeCount);
        return edge;
    }

    private int RequireNode(int node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, NodeCount);
        return node;
    }
}

/// <summary>Accumulates edges and produces an immutable <see cref="FlowNetwork"/>.</summary>
internal sealed class FlowNetworkBuilder
{
    private readonly int _nodeCount;
    private readonly List<int> _from = [];
    private readonly List<int> _to = [];
    private readonly List<double> _capacity = [];
    private readonly HashSet<(int From, int To)> _endpoints = [];

    internal FlowNetworkBuilder(int nodeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCount);
        _nodeCount = nodeCount;
    }

    /// <summary>Adds a directed edge. Self-loops and parallel edges are rejected.</summary>
    public FlowNetworkBuilder AddEdge(int from, int to, double capacity)
    {
        RequireNode(from, nameof(from));
        RequireNode(to, nameof(to));
        if (from == to)
        {
            throw new ArgumentException(
                $"A flow network cannot contain a self-loop on node {from}.",
                nameof(to));
        }

        if (!_endpoints.Add((from, to)))
        {
            throw new ArgumentException(
                $"A flow network cannot contain parallel edges; {from} to {to} was added twice.",
                nameof(to));
        }

        _from.Add(from);
        _to.Add(to);
        _capacity.Add(FlowNetwork.RequireCapacity(capacity, nameof(capacity)));
        return this;
    }

    public FlowNetwork ToNetwork() =>
        FlowNetwork.Create(_nodeCount, [.. _from], [.. _to], [.. _capacity]);

    private void RequireNode(int node, string parameterName)
    {
        if (node < 0 || node >= _nodeCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                node,
                $"Node index must be between 0 and {_nodeCount - 1}.");
        }
    }
}
