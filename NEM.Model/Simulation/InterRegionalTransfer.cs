using NEM.Model.Algorithms;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;

namespace NEM.Model.Simulation;

/// <summary>
/// Realised directed flow and loss on one interconnector over the run. One of these exists per
/// directed link, so a reciprocal corridor produces two.
/// </summary>
public sealed record InterconnectorFlow
{
    /// <summary>Validates and creates directional evidence for one link.</summary>
    /// <param name="interconnector">The link these series belong to.</param>
    /// <param name="flow">
    /// Non-negative scheduled transfer in MW from the link's sending region to its receiving
    /// region, metered at the sending end.
    /// </param>
    /// <param name="losses">
    /// Energy consumed by losses on this link, in MW. Must be aligned with
    /// <paramref name="flow"/> and never greater than it.
    /// </param>
    public InterconnectorFlow(
        Interconnector interconnector,
        FlowSeries flow,
        FlowSeries losses)
    {
        ArgumentNullException.ThrowIfNull(interconnector);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(losses);
        flow.RequireAligned(losses);

        Interconnector = interconnector;
        Flow = flow;
        Losses = losses;
    }

    /// <summary>The interconnector whose directional evidence this object records.</summary>
    public Interconnector Interconnector { get; }

    /// <summary>Non-negative scheduled flow from the interconnector's sending region.</summary>
    public FlowSeries Flow { get; }

    /// <summary>Non-negative loss attributed to this interconnector.</summary>
    public FlowSeries Losses { get; }
}

/// <summary>
/// Adapts regional surplus and deficit onto the interconnector graph, solves the
/// prioritised transfer for one interval, and books the outcome back onto the regions.
/// </summary>
/// <remarks>
/// This is the only place the domain meets the graph. The algorithm layer knows nothing
/// about regions, power, or losses; this type knows nothing about how max flow is found.
/// </remarks>
internal sealed class InterRegionalTransfer
{
    /// <summary>
    /// Fraction of a transfer lost on each edge it traverses, so a two-hop route delivers
    /// 0.95 squared. Applied over the max-flow result rather than inside it: capacity is
    /// metered at the sending end of every edge, so flow is conserved in the capacity
    /// graph and the search stays a standard max-flow problem.
    /// </summary>
    /// <remarks>
    /// TODO(NEM-053): this flat figure is a placeholder and is not yet sourced. AEMO
    /// publishes marginal loss factors per interconnector; a cited value should replace
    /// it. Defined here, once, so there is a single place to change.
    /// </remarks>
    internal const double LossFactorPerHop = 0.05;

    private readonly FlowNetwork _network;
    private readonly IReadOnlyList<Interconnector> _interconnectors;
    private readonly double[][] _sentPerEdge;
    private readonly double[][] _lostPerEdge;

    private InterRegionalTransfer(
        FlowNetwork network,
        IReadOnlyList<Interconnector> interconnectors,
        int intervalCount)
    {
        _network = network;
        _interconnectors = interconnectors;
        _sentPerEdge = Enumerable.Range(0, network.EdgeCount)
            .Select(_ => new double[intervalCount])
            .ToArray();
        _lostPerEdge = Enumerable.Range(0, network.EdgeCount)
            .Select(_ => new double[intervalCount])
            .ToArray();
    }

    /// <summary>
    /// Builds the directed transfer graph. Node indices match region order, and each
    /// interconnector contributes exactly one edge from its sending to its receiving region.
    /// </summary>
    public static InterRegionalTransfer Create(
        PowerSystem powerSystem,
        IReadOnlyList<RegionalDispatchRun> runs,
        int intervalCount)
    {
        var nodeByRegion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int node = 0; node < runs.Count; node++)
        {
            nodeByRegion[runs[node].RegionId] = node;
        }

        FlowNetworkBuilder builder = FlowNetwork.Build(runs.Count);
        foreach (Interconnector interconnector in powerSystem.Interconnectors)
        {
            int from = nodeByRegion[interconnector.FromRegionId];
            int to = nodeByRegion[interconnector.ToRegionId];
            builder.AddEdge(from, to, interconnector.Capacity.Megawatts);
        }

        return new InterRegionalTransfer(
            builder.ToNetwork(),
            powerSystem.Interconnectors,
            intervalCount);
    }

    /// <summary>
    /// Transfers surplus to deficit for one interval. Regions in deficit are served
    /// largest first, ties broken by region identity so the outcome is deterministic.
    /// </summary>
    public void Execute(IReadOnlyList<RegionalDispatchRun> runs, int index)
    {
        var sources = new List<TransferSource>();
        var sinks = new List<TransferSink>();
        for (int node = 0; node < runs.Count; node++)
        {
            RegionalDispatchRun run = runs[node];
            Power deficit = run.CurrentDeficit;
            if (deficit > Power.Zero)
            {
                sinks.Add(new TransferSink(node, deficit.Megawatts));
                continue;
            }

            Power surplus = run.ExportableSurplus();
            if (surplus > Power.Zero)
            {
                sources.Add(new TransferSource(node, surplus.Megawatts));
            }
        }

        if (sources.Count == 0 || sinks.Count == 0)
        {
            return;
        }

        TransferSink[] prioritised = sinks
            .OrderByDescending(sink => sink.RequiredDelivery)
            .ThenBy(sink => runs[sink.Node].RegionId, StringComparer.Ordinal)
            .ToArray();

        TransferResult result = PrioritisedTransferSolver.Solve(
            _network,
            sources,
            prioritised,
            LossFactorPerHop);

        for (int edge = 0; edge < _network.EdgeCount; edge++)
        {
            _sentPerEdge[edge][index] = result.SentPerEdge[edge];
            _lostPerEdge[edge][index] = result.LostPerEdge[edge];
        }

        foreach (IGrouping<int, TransferDelivery> exporter in
            result.Deliveries.GroupBy(delivery => delivery.SourceNode))
        {
            runs[exporter.Key].ApplyExport(
                Power.FromMegawatts(exporter.Sum(delivery => delivery.Sent)));
        }

        foreach (IGrouping<int, TransferDelivery> importer in
            result.Deliveries.GroupBy(delivery => delivery.SinkNode))
        {
            runs[importer.Key].ApplyImport(
                Power.FromMegawatts(importer.Sum(delivery => delivery.Delivered)));
        }
    }

    /// <summary>
    /// Per-interconnector flow and loss series.
    /// </summary>
    /// <remarks>
    /// Flow is the scheduled quantity at the sending end, so a wheeled transfer occupies
    /// its full size on every link it crosses. Loss is attributed separately by the
    /// solver, because what actually enters a link decays with each hop already travelled
    /// and so cannot be recovered from the scheduled figure alone.
    /// </remarks>
    public IReadOnlyList<InterconnectorFlow> BuildFlows(DateTimeOffset start, TimeSpan resolution)
    {
        var flows = new List<InterconnectorFlow>(_interconnectors.Count);
        for (int link = 0; link < _interconnectors.Count; link++)
        {
            flows.Add(new InterconnectorFlow(
                _interconnectors[link],
                new FlowSeries(start, resolution, _sentPerEdge[link]),
                new FlowSeries(start, resolution, _lostPerEdge[link])));
        }

        return flows;
    }
}
