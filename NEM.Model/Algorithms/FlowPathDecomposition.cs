namespace NEM.Model.Algorithms;

/// <summary>A single source-to-sink route carrying part of a flow.</summary>
/// <param name="Nodes">Nodes in order, beginning at the source and ending at the sink.</param>
/// <param name="Flow">Quantity sent along this route, metered at the sending end.</param>
internal sealed record FlowPath(IReadOnlyList<int> Nodes, double Flow)
{
    /// <summary>Number of edges traversed. This is the exponent for a per-edge loss factor.</summary>
    public int HopCount => Nodes.Count - 1;
}

/// <summary>
/// Splits a computed flow into source-to-sink paths, so that a per-edge loss factor can
/// be applied over the result according to how many edges each part traversed.
/// </summary>
/// <remarks>
/// A max flow has no unique path decomposition, and different decompositions of the same
/// flow deliver different totals once a per-hop loss is applied. This implementation
/// always extracts the <em>shortest</em> remaining path first, which minimises hop count
/// and therefore maximises delivered energy for a given flow.
/// <para>
/// Note that the sequence of augmenting paths found by <see cref="EdmondsKarp"/> is not
/// itself a valid decomposition: augmentation may push flow along a residual reverse
/// edge, cancelling part of an earlier path. Decomposition must therefore run against
/// the final per-edge flow, not against the search history.
/// </para>
/// <para>
/// Any residual circulation — flow on a cycle that never reaches the sink — is discarded,
/// because it delivers nothing. Edmonds-Karp does not produce circulation, so in practice
/// the decomposition accounts for the whole flow.
/// </para>
/// </remarks>
internal static class FlowPathDecomposition
{
    public static IReadOnlyList<FlowPath> Decompose(
        FlowNetwork network,
        IReadOnlyList<double> flowPerEdge,
        int source,
        int sink)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(flowPerEdge);
        if (flowPerEdge.Count != network.EdgeCount)
        {
            throw new ArgumentException(
                $"Expected {network.EdgeCount} flow values but received {flowPerEdge.Count}.",
                nameof(flowPerEdge));
        }

        if (source == sink)
        {
            throw new ArgumentException(
                "Source and sink must be different nodes.",
                nameof(sink));
        }

        var remaining = new double[network.EdgeCount];
        for (int edge = 0; edge < network.EdgeCount; edge++)
        {
            double flow = flowPerEdge[edge];
            if (!double.IsFinite(flow) || flow < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(flowPerEdge),
                    flow,
                    "Edge flow must be finite and non-negative.");
            }

            remaining[edge] = flow;
        }

        var paths = new List<FlowPath>();
        var parentEdge = new int[network.NodeCount];
        while (TryFindShortestPath(network, remaining, source, sink, parentEdge))
        {
            double bottleneck = double.PositiveInfinity;
            for (int node = sink; node != source; node = network.From(parentEdge[node]))
            {
                bottleneck = Math.Min(bottleneck, remaining[parentEdge[node]]);
            }

            var reversed = new List<int> { sink };
            for (int node = sink; node != source; node = network.From(parentEdge[node]))
            {
                remaining[parentEdge[node]] -= bottleneck;
                reversed.Add(network.From(parentEdge[node]));
            }

            reversed.Reverse();
            paths.Add(new FlowPath(reversed, bottleneck));
        }

        return paths;
    }

    private static bool TryFindShortestPath(
        FlowNetwork network,
        double[] remaining,
        int source,
        int sink,
        int[] parentEdge)
    {
        var visited = new bool[network.NodeCount];
        visited[source] = true;
        var queue = new Queue<int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (int edge in network.OutgoingEdges(node))
            {
                if (remaining[edge] <= EdmondsKarp.Tolerance)
                {
                    continue;
                }

                int next = network.To(edge);
                if (visited[next])
                {
                    continue;
                }

                visited[next] = true;
                parentEdge[next] = edge;
                if (next == sink)
                {
                    return true;
                }

                queue.Enqueue(next);
            }
        }

        return false;
    }
}
