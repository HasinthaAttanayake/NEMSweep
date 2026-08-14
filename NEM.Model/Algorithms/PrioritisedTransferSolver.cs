namespace NEM.Model.Algorithms;

/// <summary>A node able to send flow, and how much it has available.</summary>
internal sealed record TransferSource(int Node, double AvailableFlow);

/// <summary>A node needing flow, and how much it needs measured at the receiving end.</summary>
internal sealed record TransferSink(int Node, double RequiredDelivery);

/// <summary>One source-to-sink delivery along a single route.</summary>
internal sealed record TransferDelivery(
    int SourceNode,
    int SinkNode,
    int HopCount,
    double Sent,
    double Delivered)
{
    /// <summary>Flow consumed by losses on this route.</summary>
    public double Lost => Sent - Delivered;
}

/// <summary>The result of a prioritised transfer solve.</summary>
/// <param name="SentPerEdge">
/// Capacity consumed on each edge. Because capacity is metered at the sending end of
/// every edge on a route, this is the scheduled quantity, which is the same on every edge
/// of a route regardless of what has already been lost upstream.
/// </param>
/// <param name="LostPerEdge">
/// Loss attributed to each edge, being the loss factor times the energy actually entering
/// it. Unlike <paramref name="SentPerEdge"/> this decays along a route, so the two differ
/// on any route longer than one hop.
/// </param>
internal sealed record TransferResult(
    IReadOnlyList<double> SentPerEdge,
    IReadOnlyList<double> LostPerEdge,
    IReadOnlyList<double> DeliveredPerSink,
    IReadOnlyList<TransferDelivery> Deliveries,
    double TotalSent,
    double TotalDelivered)
{
    /// <summary>Total flow consumed by losses. Reported, never inferred downstream.</summary>
    public double TotalLost => TotalSent - TotalDelivered;
}

/// <summary>
/// Serves sinks in priority order by maximum flow, applying a per-hop loss factor over
/// the decomposed result.
/// </summary>
/// <remarks>
/// Each sink is served in turn by a full max-flow solve from a virtual super-source over
/// the sources' remaining capacity. Committed flow is then subtracted from edge capacity
/// and the next sink starts from a fresh network of what is left — no residual reverse
/// edges cross a stage boundary, so a lower-priority sink can never claw back flow
/// already committed to a higher-priority one. That is the priority guarantee, and it is
/// also why the outcome is deliberately not a global optimum.
/// <para>
/// A sink's requirement is stated at the receiving end, but max flow caps the sink in
/// sent units, and the hop count is unknown until the flow is decomposed. Each sink is
/// therefore solved iteratively: send what the outstanding requirement allows, measure
/// what actually arrived, and repeat for the shortfall. Delivery converges geometrically
/// and can never overshoot, because delivered never exceeds sent.
/// </para>
/// </remarks>
internal static class PrioritisedTransferSolver
{
    /// <summary>
    /// Absolute delivered-side shortfall tolerated by the system energy ledger, in MW. It avoids
    /// an unbounded sequence of tiny gross-up solves while remaining below the ledger tolerance.
    /// </summary>
    internal const double DeliveryTolerance = 1e-9;

    /// <summary>
    /// Safety ceiling for solves per sink. The normal bound is derived from the loss factor and
    /// longest simple route; this only protects pathological near-total-loss inputs.
    /// </summary>
    internal const int MaxIterationsPerSink = 4_096;

    public static TransferResult Solve(
        FlowNetwork network,
        IReadOnlyList<TransferSource> sources,
        IReadOnlyList<TransferSink> sinksInPriorityOrder,
        double lossFactorPerHop)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sinksInPriorityOrder);
        Validate(network, sources, sinksInPriorityOrder, lossFactorPerHop);

        int edgeCount = network.EdgeCount;
        int superSource = network.NodeCount;
        int superSink = network.NodeCount + 1;
        double retention = 1 - lossFactorPerHop;

        var remainingSourceFlow = sources.Select(source => source.AvailableFlow).ToArray();
        var remainingCapacity = new double[edgeCount];
        for (int edge = 0; edge < edgeCount; edge++)
        {
            remainingCapacity[edge] = network.Capacity(edge);
        }

        var edgeByEndpoints = new Dictionary<(int From, int To), int>();
        for (int edge = 0; edge < edgeCount; edge++)
        {
            edgeByEndpoints[(network.From(edge), network.To(edge))] = edge;
        }

        var sentPerEdge = new double[edgeCount];
        var lostPerEdge = new double[edgeCount];
        var deliveredPerSink = new double[sinksInPriorityOrder.Count];
        var deliveries = new List<TransferDelivery>();
        double totalSent = 0;
        double totalDelivered = 0;

        for (int sinkIndex = 0; sinkIndex < sinksInPriorityOrder.Count; sinkIndex++)
        {
            TransferSink sink = sinksInPriorityOrder[sinkIndex];
            double outstanding = sink.RequiredDelivery;
            int iterationLimit = RequiredIterations(network, retention, sink.RequiredDelivery);
            int iteration;

            for (iteration = 0; iteration < iterationLimit; iteration++)
            {
                if (IsWithinDeliveryTolerance(outstanding, sink.RequiredDelivery)
                    || remainingSourceFlow.Sum() <= EdmondsKarp.Tolerance)
                {
                    break;
                }

                FlowNetwork augmented = BuildAugmentedNetwork(
                    network,
                    remainingCapacity,
                    sources,
                    remainingSourceFlow,
                    sink.Node,
                    outstanding,
                    superSource,
                    superSink);

                MaxFlowResult flow = EdmondsKarp.MaxFlow(augmented, superSource, superSink);
                if (flow.Value <= EdmondsKarp.Tolerance)
                {
                    break;
                }

                IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(
                    augmented,
                    flow.FlowPerEdge,
                    superSource,
                    superSink);

                foreach (FlowPath path in paths)
                {
                    // Strip the virtual super-source and super-sink from each end.
                    int hopCount = path.Nodes.Count - 3;
                    int sourceNode = path.Nodes[1];
                    double sent = path.Flow;
                    double delivered = sent * Math.Pow(retention, hopCount);

                    deliveries.Add(new TransferDelivery(
                        sourceNode,
                        sink.Node,
                        hopCount,
                        sent,
                        delivered));

                    // Loss on each edge is the loss factor times what actually enters it,
                    // which decays along the route. Summed over the route this telescopes
                    // to sent - delivered.
                    for (int step = 0; step < hopCount; step++)
                    {
                        int edge = edgeByEndpoints[
                            (path.Nodes[step + 1], path.Nodes[step + 2])];
                        lostPerEdge[edge] +=
                            sent * Math.Pow(retention, step) * lossFactorPerHop;
                    }

                    outstanding -= delivered;
                    deliveredPerSink[sinkIndex] += delivered;
                    totalSent += sent;
                    totalDelivered += delivered;
                }

                for (int edge = 0; edge < edgeCount; edge++)
                {
                    sentPerEdge[edge] += flow.FlowPerEdge[edge];
                    remainingCapacity[edge] -= flow.FlowPerEdge[edge];
                }

                for (int index = 0; index < sources.Count; index++)
                {
                    remainingSourceFlow[index] -= flow.FlowPerEdge[edgeCount + index];
                }
            }

            if (iteration == iterationLimit
                && !IsWithinDeliveryTolerance(outstanding, sink.RequiredDelivery)
                && remainingSourceFlow.Sum() > EdmondsKarp.Tolerance
                && HasRemainingTransferCapacity(
                    network,
                    remainingCapacity,
                    sources,
                    remainingSourceFlow,
                    sink.Node,
                    outstanding,
                    superSource,
                    superSink))
            {
                throw new InvalidOperationException(
                    $"Transfer to sink {sink.Node} did not converge within {iterationLimit} "
                    + "gross-up solves while capacity remained available.");
            }
        }

        return new TransferResult(
            sentPerEdge,
            lostPerEdge,
            deliveredPerSink,
            deliveries,
            totalSent,
            totalDelivered);
    }

    private static int RequiredIterations(
        FlowNetwork network,
        double retention,
        double requiredDelivery)
    {
        double threshold = DeliveryTolerance;
        if (requiredDelivery <= threshold || retention >= 1)
        {
            return 1;
        }

        // A decomposed route is simple, so it has at most nodeCount - 1 real edges.
        double minimumDeliveryFraction = Math.Pow(retention, Math.Max(1, network.NodeCount - 1));
        if (minimumDeliveryFraction <= 0 || 1 - minimumDeliveryFraction >= 1)
        {
            return MaxIterationsPerSink;
        }

        if (minimumDeliveryFraction >= 1)
        {
            return 1;
        }

        double logarithmicDecay = Math.Log(1 - minimumDeliveryFraction);
        if (!double.IsFinite(logarithmicDecay) || logarithmicDecay >= 0)
        {
            return MaxIterationsPerSink;
        }

        double iterations = Math.Ceiling(
            Math.Log(threshold / requiredDelivery) / logarithmicDecay);
        if (!double.IsFinite(iterations))
        {
            return MaxIterationsPerSink;
        }

        return (int)Math.Clamp(iterations, 1, MaxIterationsPerSink);
    }

    private static bool IsWithinDeliveryTolerance(double outstanding, double requiredDelivery) =>
        outstanding <= DeliveryTolerance;

    private static bool HasRemainingTransferCapacity(
        FlowNetwork network,
        double[] remainingCapacity,
        IReadOnlyList<TransferSource> sources,
        double[] remainingSourceFlow,
        int sinkNode,
        double outstanding,
        int superSource,
        int superSink) => EdmondsKarp.MaxFlow(
            BuildAugmentedNetwork(
                network,
                remainingCapacity,
                sources,
                remainingSourceFlow,
                sinkNode,
                outstanding,
                superSource,
                superSink),
            superSource,
            superSink).Value > EdmondsKarp.Tolerance;

    /// <summary>
    /// The transfer network plus a super-source feeding every source node and a
    /// super-sink drawing from the sink under consideration. Source edges are always
    /// present, even at zero remaining capacity, so that edge indices stay stable across
    /// iterations and flow can be attributed back by index.
    /// </summary>
    private static FlowNetwork BuildAugmentedNetwork(
        FlowNetwork network,
        double[] remainingCapacity,
        IReadOnlyList<TransferSource> sources,
        double[] remainingSourceFlow,
        int sinkNode,
        double outstanding,
        int superSource,
        int superSink)
    {
        FlowNetworkBuilder builder = FlowNetwork.Build(network.NodeCount + 2);
        for (int edge = 0; edge < network.EdgeCount; edge++)
        {
            builder.AddEdge(
                network.From(edge),
                network.To(edge),
                Math.Max(0, remainingCapacity[edge]));
        }

        for (int index = 0; index < sources.Count; index++)
        {
            builder.AddEdge(
                superSource,
                sources[index].Node,
                Math.Max(0, remainingSourceFlow[index]));
        }

        builder.AddEdge(sinkNode, superSink, outstanding);
        return builder.ToNetwork();
    }

    private static void Validate(
        FlowNetwork network,
        IReadOnlyList<TransferSource> sources,
        IReadOnlyList<TransferSink> sinks,
        double lossFactorPerHop)
    {
        if (!double.IsFinite(lossFactorPerHop) || lossFactorPerHop < 0 || lossFactorPerHop >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lossFactorPerHop),
                lossFactorPerHop,
                "Loss factor per hop must be finite and in the range [0, 1).");
        }

        var sourceNodes = new HashSet<int>();
        foreach (TransferSource source in sources)
        {
            RequireNode(network, source.Node, nameof(sources));
            RequireQuantity(source.AvailableFlow, nameof(sources), "Available flow");
            if (!sourceNodes.Add(source.Node))
            {
                throw new ArgumentException(
                    $"Node {source.Node} appears more than once as a source.",
                    nameof(sources));
            }
        }

        var sinkNodes = new HashSet<int>();
        foreach (TransferSink sink in sinks)
        {
            RequireNode(network, sink.Node, nameof(sinks));
            RequireQuantity(sink.RequiredDelivery, nameof(sinks), "Required delivery");
            if (!sinkNodes.Add(sink.Node))
            {
                throw new ArgumentException(
                    $"Node {sink.Node} appears more than once as a sink.",
                    nameof(sinks));
            }

            if (sourceNodes.Contains(sink.Node))
            {
                throw new ArgumentException(
                    $"Node {sink.Node} cannot be both a source and a sink.",
                    nameof(sinks));
            }
        }
    }

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

    private static void RequireQuantity(double value, string parameterName, string description)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{description} must be finite and non-negative.");
        }
    }
}
