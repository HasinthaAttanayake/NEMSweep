using AwesomeAssertions;
using NEM.Model.Algorithms;

namespace NEM.Model.Tests.Algorithms;

public sealed class PrioritisedTransferSolverTests
{
    private const double LossFactor = 0.05;
    private const double Tolerance = 1e-9;

    [Fact]
    public void Solve_SingleHop_DeliversNinetyFivePercentOfWhatWasSent()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 1_000).ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 100)], [new TransferSink(1, 1_000)]);

        result.TotalSent.Should().BeApproximately(100, Tolerance);
        result.TotalDelivered.Should().BeApproximately(95, Tolerance);
        result.TotalLost.Should().BeApproximately(5, Tolerance);
        result.Deliveries.Should().ContainSingle().Which.HopCount.Should().Be(1);
    }

    [Fact]
    public void Solve_TwoHops_CompoundsTheLossOverBothEdges()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 1_000)
            .AddEdge(1, 2, 1_000)
            .ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 100)], [new TransferSink(2, 1_000)]);

        result.TotalSent.Should().BeApproximately(100, Tolerance);
        result.TotalDelivered.Should().BeApproximately(90.25, Tolerance, "100 x 0.95^2 = 90.25");
        result.TotalLost.Should().BeApproximately(9.75, Tolerance);
        result.Deliveries.Should().ContainSingle().Which.HopCount.Should().Be(2);
    }

    [Fact]
    public void Solve_TwoHops_ConsumesFullSentQuantityOnEveryEdgeOfThePath()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 200)
            .AddEdge(1, 2, 50)
            .ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 500)], [new TransferSink(2, 1_000)]);

        result.SentPerEdge[0].Should().BeApproximately(50, Tolerance);
        result.SentPerEdge[1].Should().BeApproximately(50, Tolerance);
        result.TotalDelivered.Should().BeApproximately(45.125, Tolerance, "50 x 0.95^2");
    }

    [Fact]
    public void Solve_TwoHops_AttributesLossByWhatActuallyEntersEachEdge()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 1_000)
            .AddEdge(1, 2, 1_000)
            .ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 50)], [new TransferSink(2, 1_000)]);

        result.SentPerEdge[0].Should().BeApproximately(50, Tolerance);
        result.SentPerEdge[1].Should().BeApproximately(
            50,
            Tolerance,
            "capacity is metered at the sending end, so the full scheduled quantity occupies both edges");
        result.LostPerEdge[0].Should().BeApproximately(2.5, Tolerance, "50 x 0.05");
        result.LostPerEdge[1].Should().BeApproximately(
            2.375,
            Tolerance,
            "only 47.5 reaches the second edge, so 47.5 x 0.05 is lost there");
        result.LostPerEdge.Sum().Should().BeApproximately(result.TotalLost, Tolerance);
    }

    [Fact]
    public void Solve_ConstrainedSource_ServesHigherPrioritySinkInFullBeforeTheNext()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 1_000)
            .AddEdge(0, 2, 1_000)
            .ToNetwork();

        TransferResult result = Solve(
            network,
            [new TransferSource(0, 100)],
            [new TransferSink(1, 80), new TransferSink(2, 80)]);

        result.DeliveredPerSink[0].Should().BeApproximately(
            80,
            1e-5,
            "the higher-priority sink is served in full before the second is considered");
        result.DeliveredPerSink[1].Should().BeApproximately(
            15,
            1e-5,
            "80/0.95 = 84.21 is sent to the first sink, leaving 15.79 which delivers 15");
    }

    [Fact]
    public void Solve_DeficitRequiringGrossUp_ConvergesToFullDelivery()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 1_000)
            .AddEdge(1, 2, 1_000)
            .ToNetwork();

        TransferResult result = Solve(
            network,
            [new TransferSource(0, 1_000)],
            [new TransferSink(2, 90.25)]);

        result.TotalDelivered.Should().BeApproximately(
            90.25,
            PrioritisedTransferSolver.DeliveryTolerance,
            "the solver must gross up the sent quantity until the delivered deficit closes "
            + "at the configured relative tolerance");
        result.TotalSent.Should().BeApproximately(
            100,
            PrioritisedTransferSolver.DeliveryTolerance);
    }

    [Fact]
    public void Solve_NeverOverServesASink()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10_000).ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 10_000)], [new TransferSink(1, 40)]);

        result.TotalDelivered.Should().BeLessThanOrEqualTo(40 + Tolerance);
        result.DeliveredPerSink[0].Should().BeApproximately(40, 1e-5);
    }

    [Fact]
    public void Solve_ZeroCapacityLink_TransfersNothing()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 0).ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 500)], [new TransferSink(1, 500)]);

        result.TotalSent.Should().Be(0);
        result.TotalDelivered.Should().Be(0);
        result.Deliveries.Should().BeEmpty();
        result.SentPerEdge.Should().OnlyContain(sent => sent == 0);
    }

    [Fact]
    public void Solve_NoSurplus_TransfersNothing()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 500).ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 0)], [new TransferSink(1, 500)]);

        result.TotalSent.Should().Be(0);
        result.TotalDelivered.Should().Be(0);
    }

    [Fact]
    public void Solve_WheelingThroughAnIntermediateNode_AttributesTheOriginalSource()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 1_000)
            .AddEdge(1, 2, 1_000)
            .ToNetwork();

        TransferResult result = Solve(network, [new TransferSource(0, 50)], [new TransferSink(2, 1_000)]);

        TransferDelivery delivery = result.Deliveries.Should().ContainSingle().Subject;
        delivery.SourceNode.Should().Be(0);
        delivery.SinkNode.Should().Be(2);
        delivery.HopCount.Should().Be(2);
        delivery.Sent.Should().BeApproximately(50, Tolerance);
        delivery.Delivered.Should().BeApproximately(45.125, Tolerance);
        delivery.Lost.Should().BeApproximately(4.875, Tolerance);
    }

    [Fact]
    public void Solve_LossFactorOutOfRange_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        var act = () => PrioritisedTransferSolver.Solve(
            network,
            [new TransferSource(0, 10)],
            [new TransferSink(1, 10)],
            1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lossFactorPerHop");
    }

    [Fact]
    public void Solve_NodeThatIsBothSourceAndSink_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        var act = () => Solve(network, [new TransferSource(0, 10)], [new TransferSink(0, 10)]);

        act.Should().Throw<ArgumentException>().WithParameterName("sinks");
    }

    [Fact]
    public void Solve_DuplicateSource_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        var act = () => Solve(
            network,
            [new TransferSource(0, 10), new TransferSource(0, 5)],
            [new TransferSink(1, 10)]);

        act.Should().Throw<ArgumentException>().WithParameterName("sources");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(2026)]
    public void Solve_RandomNetwork_RespectsCapacityAndSourceLimits(int seed)
    {
        var random = new Random(seed);

        for (int trial = 0; trial < 30; trial++)
        {
            int nodeCount = random.Next(3, 7);
            FlowNetworkBuilder builder = FlowNetwork.Build(nodeCount);
            for (int from = 0; from < nodeCount; from++)
            {
                for (int to = 0; to < nodeCount; to++)
                {
                    if (from != to && random.NextDouble() < 0.5)
                    {
                        builder.AddEdge(from, to, Math.Round(random.NextDouble() * 50, 6));
                    }
                }
            }

            FlowNetwork network = builder.ToNetwork();
            var source = new TransferSource(0, Math.Round(random.NextDouble() * 100, 6));
            TransferSink[] sinks = Enumerable.Range(1, nodeCount - 1)
                .Select(node => new TransferSink(node, Math.Round(random.NextDouble() * 60, 6)))
                .ToArray();

            TransferResult result = Solve(network, [source], sinks);

            for (int edge = 0; edge < network.EdgeCount; edge++)
            {
                result.SentPerEdge[edge].Should().BeLessThanOrEqualTo(
                    network.Capacity(edge) + 1e-7,
                    "edge {0} must never carry more than its capacity",
                    edge);
            }

            result.TotalSent.Should().BeLessThanOrEqualTo(
                source.AvailableFlow + 1e-7,
                "a source cannot send more than it has available");
            result.TotalDelivered.Should().BeLessThanOrEqualTo(result.TotalSent + 1e-7);
            result.TotalLost.Should().BeGreaterThanOrEqualTo(-1e-7);

            for (int index = 0; index < sinks.Length; index++)
            {
                result.DeliveredPerSink[index].Should().BeLessThanOrEqualTo(
                    sinks[index].RequiredDelivery + 1e-7,
                    "sink {0} must never be over-served",
                    sinks[index].Node);
            }

            result.Deliveries.Sum(delivery => delivery.Delivered).Should().BeApproximately(
                result.TotalDelivered,
                1e-7);
            result.LostPerEdge.Sum().Should().BeApproximately(
                result.TotalLost,
                1e-7,
                "per-edge loss attribution must reconcile with sent less delivered");
        }
    }

    private static TransferResult Solve(
        FlowNetwork network,
        IReadOnlyList<TransferSource> sources,
        IReadOnlyList<TransferSink> sinks) =>
        PrioritisedTransferSolver.Solve(network, sources, sinks, LossFactor);
}
