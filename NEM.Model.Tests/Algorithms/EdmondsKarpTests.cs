using AwesomeAssertions;
using NEM.Model.Algorithms;

namespace NEM.Model.Tests.Algorithms;

public sealed class EdmondsKarpTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void MaxFlow_CormenFigure26_1_Returns23()
    {
        FlowNetwork network = FlowNetwork.Build(6)
            .AddEdge(0, 1, 16)
            .AddEdge(0, 2, 13)
            .AddEdge(1, 2, 10)
            .AddEdge(2, 1, 4)
            .AddEdge(1, 3, 12)
            .AddEdge(3, 2, 9)
            .AddEdge(2, 4, 14)
            .AddEdge(4, 3, 7)
            .AddEdge(3, 5, 20)
            .AddEdge(4, 5, 4)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 5);

        result.Value.Should().BeApproximately(23, Tolerance);
        result.Value.Should().BeApproximately(
            MinCut(network, 0, 5),
            Tolerance,
            "max flow must equal min cut");
    }

    [Fact]
    public void MaxFlow_SeriesBottleneck_IsLimitedByTheNarrowestEdge()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 200)
            .AddEdge(1, 2, 50)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 2);

        result.Value.Should().BeApproximately(50, Tolerance);
        result.FlowPerEdge[0].Should().BeApproximately(50, Tolerance);
        result.FlowPerEdge[1].Should().BeApproximately(50, Tolerance);
    }

    [Fact]
    public void MaxFlow_ParallelRoutes_SaturatesBothAndMatchesMinCut()
    {
        FlowNetwork network = FlowNetwork.Build(4)
            .AddEdge(0, 1, 3)
            .AddEdge(0, 2, 2)
            .AddEdge(1, 3, 2)
            .AddEdge(2, 3, 3)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 3);

        result.Value.Should().BeApproximately(4, Tolerance);
        result.Value.Should().BeApproximately(MinCut(network, 0, 3), Tolerance);
    }

    [Fact]
    public void MaxFlow_AntiparallelEdges_AreTreatedIndependently()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 10)
            .AddEdge(1, 0, 10)
            .AddEdge(1, 2, 6)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 2);

        result.Value.Should().BeApproximately(6, Tolerance);
        result.FlowPerEdge[1].Should().BeApproximately(0, Tolerance, "the reverse edge carries nothing");
    }

    [Fact]
    public void MaxFlow_DisconnectedSink_ReturnsZero()
    {
        FlowNetwork network = FlowNetwork.Build(4)
            .AddEdge(0, 1, 10)
            .AddEdge(2, 3, 10)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 3);

        result.Value.Should().Be(0);
        result.FlowPerEdge.Should().OnlyContain(flow => flow == 0);
    }

    [Fact]
    public void MaxFlow_ZeroCapacity_ReturnsZero()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 0).ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 1);

        result.Value.Should().Be(0);
    }

    [Fact]
    public void MaxFlow_CycleInGraph_DoesNotPreventTermination()
    {
        FlowNetwork network = FlowNetwork.Build(4)
            .AddEdge(0, 1, 10)
            .AddEdge(1, 2, 10)
            .AddEdge(2, 1, 10)
            .AddEdge(2, 3, 5)
            .ToNetwork();

        MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, 3);

        result.Value.Should().BeApproximately(5, Tolerance);
    }

    [Fact]
    public void MaxFlow_PermutedEdgeInsertionOrder_ProducesIdenticalFlow()
    {
        (int From, int To, double Capacity)[] edges =
        [
            (0, 1, 16), (0, 2, 13), (1, 2, 10), (2, 1, 4), (1, 3, 12),
            (3, 2, 9), (2, 4, 14), (4, 3, 7), (3, 5, 20), (4, 5, 4),
        ];

        Dictionary<(int, int), double> first = FlowByEndpoints(edges, 0, 5);
        Dictionary<(int, int), double> reversed = FlowByEndpoints(edges.Reverse().ToArray(), 0, 5);
        Dictionary<(int, int), double> shuffled = FlowByEndpoints(
            Shuffle(edges, new Random(2026)),
            0,
            5);

        reversed.Should().BeEquivalentTo(first);
        shuffled.Should().BeEquivalentTo(first);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(2026)]
    public void MaxFlow_RandomNetwork_RespectsCapacityConservationAndMinCut(int seed)
    {
        var random = new Random(seed);

        for (int trial = 0; trial < 40; trial++)
        {
            int nodeCount = random.Next(3, 8);
            FlowNetworkBuilder builder = FlowNetwork.Build(nodeCount);
            for (int from = 0; from < nodeCount; from++)
            {
                for (int to = 0; to < nodeCount; to++)
                {
                    if (from != to && random.NextDouble() < 0.45)
                    {
                        builder.AddEdge(from, to, Math.Round(random.NextDouble() * 20, 6));
                    }
                }
            }

            FlowNetwork network = builder.ToNetwork();
            int sink = nodeCount - 1;

            MaxFlowResult result = EdmondsKarp.MaxFlow(network, 0, sink);

            for (int edge = 0; edge < network.EdgeCount; edge++)
            {
                result.FlowPerEdge[edge].Should().BeGreaterThanOrEqualTo(
                    -Tolerance,
                    "flow on edge {0} must be non-negative",
                    edge);
                result.FlowPerEdge[edge].Should().BeLessThanOrEqualTo(
                    network.Capacity(edge) + Tolerance,
                    "flow on edge {0} must not exceed its capacity",
                    edge);
            }

            for (int node = 0; node < nodeCount; node++)
            {
                if (node == 0 || node == sink)
                {
                    continue;
                }

                double inflow = 0;
                double outflow = 0;
                for (int edge = 0; edge < network.EdgeCount; edge++)
                {
                    if (network.To(edge) == node)
                    {
                        inflow += result.FlowPerEdge[edge];
                    }

                    if (network.From(edge) == node)
                    {
                        outflow += result.FlowPerEdge[edge];
                    }
                }

                inflow.Should().BeApproximately(
                    outflow,
                    1e-7,
                    "flow must be conserved at intermediate node {0}",
                    node);
            }

            result.Value.Should().BeApproximately(
                MinCut(network, 0, sink),
                1e-7,
                "max flow must equal min cut");
        }
    }

    [Fact]
    public void MaxFlow_SourceEqualsSink_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 5).ToNetwork();

        var act = () => EdmondsKarp.MaxFlow(network, 1, 1);

        act.Should().Throw<ArgumentException>().WithParameterName("sink");
    }

    [Fact]
    public void MaxFlow_NodeOutsideNetwork_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 5).ToNetwork();

        var act = () => EdmondsKarp.MaxFlow(network, 0, 9);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("sink");
    }

    private static Dictionary<(int, int), double> FlowByEndpoints(
        (int From, int To, double Capacity)[] edges,
        int source,
        int sink)
    {
        FlowNetworkBuilder builder = FlowNetwork.Build(6);
        foreach ((int from, int to, double capacity) in edges)
        {
            builder.AddEdge(from, to, capacity);
        }

        FlowNetwork network = builder.ToNetwork();
        MaxFlowResult result = EdmondsKarp.MaxFlow(network, source, sink);
        return Enumerable.Range(0, network.EdgeCount).ToDictionary(
            edge => (network.From(edge), network.To(edge)),
            edge => result.FlowPerEdge[edge]);
    }

    private static (int From, int To, double Capacity)[] Shuffle(
        (int From, int To, double Capacity)[] edges,
        Random random)
    {
        var copy = edges.ToArray();
        for (int index = copy.Length - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    /// <summary>
    /// Minimum cut by exhaustive enumeration of source-side subsets. Deliberately naive:
    /// it shares no code with the algorithm under test, so it is an independent oracle.
    /// </summary>
    private static double MinCut(FlowNetwork network, int source, int sink)
    {
        double best = double.PositiveInfinity;
        for (int mask = 0; mask < 1 << network.NodeCount; mask++)
        {
            bool sourceSide(int node) => (mask & (1 << node)) != 0;
            if (!sourceSide(source) || sourceSide(sink))
            {
                continue;
            }

            double cut = 0;
            for (int edge = 0; edge < network.EdgeCount; edge++)
            {
                if (sourceSide(network.From(edge)) && !sourceSide(network.To(edge)))
                {
                    cut += network.Capacity(edge);
                }
            }

            best = Math.Min(best, cut);
        }

        return best;
    }
}
