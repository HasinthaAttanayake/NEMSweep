namespace NEMSweep.Model.Algorithms;

/// <summary>Maximum flow and the per-edge flow that realises it.</summary>
/// <param name="Value">Total flow from source to sink.</param>
/// <param name="FlowPerEdge">Flow on each edge, indexed as in the source network.</param>
internal sealed record MaxFlowResult(double Value, IReadOnlyList<double> FlowPerEdge);

/// <summary>
/// Maximum flow by the Edmonds-Karp refinement of Ford-Fulkerson: repeatedly augment
/// along a breadth-first (shortest) path in the residual graph.
/// </summary>
/// <remarks>
/// This is textbook max flow. Flow is conserved at every intermediate node, so the
/// max-flow–min-cut theorem holds and can be used as a test oracle. Transmission loss
/// is deliberately absent here: applying a per-edge loss inside the search would break
/// conservation and turn this into a generalized-flow problem that this algorithm does
/// not solve. Losses belong over the decomposed result; see
/// <see cref="FlowPathDecomposition"/>.
/// </remarks>
internal static class EdmondsKarp
{
    /// <summary>
    /// Residual capacities below this are treated as zero, so that floating-point dust
    /// cannot produce an unbounded sequence of vanishing augmentations.
    /// </summary>
    internal const double Tolerance = 1e-12;

    public static MaxFlowResult MaxFlow(FlowNetwork network, int source, int sink)
    {
        ArgumentNullException.ThrowIfNull(network);
        RequireNode(network, source, nameof(source));
        RequireNode(network, sink, nameof(sink));
        if (source == sink)
        {
            throw new ArgumentException(
                "Source and sink must be different nodes.",
                nameof(sink));
        }

        int edgeCount = network.EdgeCount;
        var residual = new double[edgeCount * 2];
        for (int edge = 0; edge < edgeCount; edge++)
        {
            residual[edge * 2] = network.Capacity(edge);
            residual[(edge * 2) + 1] = 0;
        }

        int[][] adjacency = BuildResidualAdjacency(network);
        var parentEdge = new int[network.NodeCount];
        double value = 0;

        while (TryFindAugmentingPath(network, adjacency, residual, source, sink, parentEdge))
        {
            double bottleneck = double.PositiveInfinity;
            for (int node = sink; node != source; node = Tail(network, parentEdge[node]))
            {
                bottleneck = Math.Min(bottleneck, residual[parentEdge[node]]);
            }

            for (int node = sink; node != source; node = Tail(network, parentEdge[node]))
            {
                int residualEdge = parentEdge[node];
                residual[residualEdge] -= bottleneck;
                residual[residualEdge ^ 1] += bottleneck;
            }

            value += bottleneck;
        }

        var flowPerEdge = new double[edgeCount];
        for (int edge = 0; edge < edgeCount; edge++)
        {
            flowPerEdge[edge] = network.Capacity(edge) - residual[edge * 2];
        }

        return new MaxFlowResult(value, flowPerEdge);
    }

    private static bool TryFindAugmentingPath(
        FlowNetwork network,
        int[][] adjacency,
        double[] residual,
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
            foreach (int residualEdge in adjacency[node])
            {
                if (residual[residualEdge] <= Tolerance)
                {
                    continue;
                }

                int next = Head(network, residualEdge);
                if (visited[next])
                {
                    continue;
                }

                visited[next] = true;
                parentEdge[next] = residualEdge;
                if (next == sink)
                {
                    return true;
                }

                queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>
    /// Residual edges leaving each node, ordered by head node and then by forward
    /// before reverse. The ordering is independent of the sequence in which edges were
    /// added, which is what makes the computed flow deterministic.
    /// </summary>
    private static int[][] BuildResidualAdjacency(FlowNetwork network)
    {
        var lists = new List<int>[network.NodeCount];
        for (int node = 0; node < network.NodeCount; node++)
        {
            lists[node] = [];
        }

        for (int edge = 0; edge < network.EdgeCount; edge++)
        {
            lists[network.From(edge)].Add(edge * 2);
            lists[network.To(edge)].Add((edge * 2) + 1);
        }

        var adjacency = new int[network.NodeCount][];
        for (int node = 0; node < network.NodeCount; node++)
        {
            adjacency[node] = lists[node]
                .OrderBy(residualEdge => Head(network, residualEdge))
                .ThenBy(residualEdge => residualEdge & 1)
                .ToArray();
        }

        return adjacency;
    }

    private static int Head(FlowNetwork network, int residualEdge) =>
        (residualEdge & 1) == 0
            ? network.To(residualEdge / 2)
            : network.From(residualEdge / 2);

    private static int Tail(FlowNetwork network, int residualEdge) =>
        (residualEdge & 1) == 0
            ? network.From(residualEdge / 2)
            : network.To(residualEdge / 2);

    private static void RequireNode(FlowNetwork network, int node, string parameterName)
    {
        if (node < 0 || node >= network.NodeCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                node,
                $"Node index must be between 0 and {network.NodeCount - 1}.");
        }
    }
}
