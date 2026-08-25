namespace NEMSweep.Model.Algorithms;

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
/// <param name="DeliveredPerSink">
/// What each sink actually received, in sink order, measured at the receiving end.
/// </param>
/// <param name="Deliveries">Every source-to-sink delivery the solve committed, route by route.</param>
/// <param name="TotalSent">Total flow committed at sending ends across every route.</param>
/// <param name="TotalDelivered">Total flow that arrived across every route.</param>
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
/// and the next sink starts from a fresh network of what is left. No residual reverse
/// edge crosses a stage boundary, so a lower-priority sink can never claw back flow
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

        var state = new SolveState
        {
            Network = network,
            Sources = sources,
            Retention = retention,
            LossFactorPerHop = lossFactorPerHop,
            SuperSource = superSource,
            SuperSink = superSink,
            RemainingSourceFlow = remainingSourceFlow,
            RemainingSourceTotal = remainingSourceFlow.Sum(),
            RemainingCapacity = remainingCapacity,
            SentPerEdge = new double[edgeCount],
            LostPerEdge = new double[edgeCount],
            DeliveredPerSink = new double[sinksInPriorityOrder.Count],
            Deliveries = [],
        };

        for (int sinkIndex = 0; sinkIndex < sinksInPriorityOrder.Count; sinkIndex++)
        {
            SolveForSink(ref state, sinksInPriorityOrder[sinkIndex], sinkIndex);
        }

        return new TransferResult(
            state.SentPerEdge,
            state.LostPerEdge,
            state.DeliveredPerSink,
            state.Deliveries,
            state.TotalSent,
            state.TotalDelivered);
    }

    /// <summary>
    /// Everything one prioritised solve carries across its sinks. A <c>ref struct</c> so that
    /// grouping the state costs no allocation: this runs once per dispatch interval.
    /// </summary>
    private ref struct SolveState
    {
        public FlowNetwork Network;
        public IReadOnlyList<TransferSource> Sources;
        public double Retention;
        public double LossFactorPerHop;
        public int SuperSource;
        public int SuperSink;
        public double[] RemainingSourceFlow;

        /// <summary>
        /// Total of <see cref="RemainingSourceFlow"/>, resummed once per iteration and read by
        /// the two convergence checks. Deliberately a resum of the array rather than a running
        /// subtraction: the checks compare it against a tolerance, and accumulating deductions
        /// instead would reassociate the arithmetic and could move a borderline comparison, which
        /// is the trajectory this change has to leave alone.
        /// </summary>
        public double RemainingSourceTotal;
        public double[] RemainingCapacity;
        public double[] SentPerEdge;
        public double[] LostPerEdge;
        public double[] DeliveredPerSink;
        public List<TransferDelivery> Deliveries;
        public double TotalSent;
        public double TotalDelivered;
    }

    /// <summary>
    /// Serves one sink to its requirement, or to whatever the remaining capacity allows.
    /// See the type remarks for why a sink needs repeated gross-up solves rather than one.
    /// </summary>
    private static void SolveForSink(ref SolveState state, TransferSink sink, int sinkIndex)
    {
        int edgeCount = state.Network.EdgeCount;
        double outstanding = sink.RequiredDelivery;
        int iterationLimit = RequiredIterations(state.Network, state.Retention, sink.RequiredDelivery);
        int iteration;

        for (iteration = 0; iteration < iterationLimit; iteration++)
        {
            if (IsWithinDeliveryTolerance(outstanding, sink.RequiredDelivery)
                || state.RemainingSourceTotal <= EdmondsKarp.Tolerance)
            {
                break;
            }

            FlowNetwork augmented = BuildAugmentedNetwork(
                state.Network,
                state.RemainingCapacity,
                state.Sources,
                state.RemainingSourceFlow,
                sink.Node,
                outstanding,
                state.SuperSource,
                state.SuperSink);

            MaxFlowResult flow = EdmondsKarp.MaxFlow(augmented, state.SuperSource, state.SuperSink);
            if (flow.Value <= EdmondsKarp.Tolerance)
            {
                break;
            }

            IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(
                augmented,
                flow.FlowPerEdge,
                state.SuperSource,
                state.SuperSink);

            foreach (FlowPath path in paths)
            {
                outstanding -= AccountForPath(ref state, path, sink, sinkIndex);
            }

            for (int edge = 0; edge < edgeCount; edge++)
            {
                state.SentPerEdge[edge] += flow.FlowPerEdge[edge];
                state.RemainingCapacity[edge] -= flow.FlowPerEdge[edge];
            }

            for (int index = 0; index < state.Sources.Count; index++)
            {
                state.RemainingSourceFlow[index] -= flow.FlowPerEdge[edgeCount + index];
            }

            state.RemainingSourceTotal = state.RemainingSourceFlow.Sum();
        }

        RequireConverged(ref state, sink, outstanding, iteration, iterationLimit);
    }

    /// <summary>
    /// Books one decomposed route: records the delivery, attributes per-edge loss, and returns
    /// what arrived so the caller can retire it from the sink's outstanding requirement.
    /// </summary>
    private static double AccountForPath(
        ref SolveState state,
        FlowPath path,
        TransferSink sink,
        int sinkIndex)
    {
        // Strip the virtual super-source and super-sink from each end.
        int hopCount = path.Nodes.Count - 3;
        int sourceNode = path.Nodes[1];
        double sent = path.Flow;
        double delivered = sent * Math.Pow(state.Retention, hopCount);

        state.Deliveries.Add(new TransferDelivery(
            sourceNode,
            sink.Node,
            hopCount,
            sent,
            delivered));

        // Loss on each edge is the loss factor times what actually enters it, which decays
        // along the route. Summed over the route this telescopes to sent - delivered.
        for (int step = 0; step < hopCount; step++)
        {
            int edge = state.Network.EdgeBetween(path.Nodes[step + 1], path.Nodes[step + 2]);
            state.LostPerEdge[edge] +=
                sent * Math.Pow(state.Retention, step) * state.LossFactorPerHop;
        }

        state.DeliveredPerSink[sinkIndex] += delivered;
        state.TotalSent += sent;
        state.TotalDelivered += delivered;
        return delivered;
    }

    /// <summary>
    /// Rejects a sink that exhausted its iteration budget while flow could still have reached
    /// it. Stopping short with no route left is a normal outcome; stopping short with one
    /// available is a solver defect and must not be published as a transfer result.
    /// </summary>
    private static void RequireConverged(
        ref SolveState state,
        TransferSink sink,
        double outstanding,
        int iteration,
        int iterationLimit)
    {
        if (iteration == iterationLimit
            && !IsWithinDeliveryTolerance(outstanding, sink.RequiredDelivery)
            && state.RemainingSourceTotal > EdmondsKarp.Tolerance
            && HasRemainingTransferCapacity(
                state.Network,
                state.RemainingCapacity,
                state.Sources,
                state.RemainingSourceFlow,
                sink.Node,
                outstanding,
                state.SuperSource,
                state.SuperSink))
        {
            throw new InvalidOperationException(
                $"Transfer to sink {sink.Node} did not converge within {iterationLimit} "
                + "gross-up solves while capacity remained available.");
        }
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
