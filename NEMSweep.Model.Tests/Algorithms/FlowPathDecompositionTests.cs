using AwesomeAssertions;
using NEMSweep.Model.Algorithms;

namespace NEMSweep.Model.Tests.Algorithms;

public sealed class FlowPathDecompositionTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Decompose_TwoHopRoute_ReportsOnePathOfTwoHops()
    {
        FlowNetwork network = FlowNetwork.Build(3)
            .AddEdge(0, 1, 200)
            .AddEdge(1, 2, 50)
            .ToNetwork();
        MaxFlowResult flow = EdmondsKarp.MaxFlow(network, 0, 2);

        IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(
            network,
            flow.FlowPerEdge,
            0,
            2);

        paths.Should().ContainSingle();
        paths[0].Nodes.Should().Equal(0, 1, 2);
        paths[0].Flow.Should().BeApproximately(50, Tolerance);
        paths[0].HopCount.Should().Be(2);
        (network.Capacity(0) - flow.FlowPerEdge[0]).Should().BeApproximately(
            150,
            Tolerance,
            "sending 50 consumes 50 of the 200 available on the first edge");
        (network.Capacity(1) - flow.FlowPerEdge[1]).Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void Decompose_ShortAndLongRoute_ExtractsTheShortRouteFirst()
    {
        FlowNetwork network = FlowNetwork.Build(4)
            .AddEdge(0, 3, 5)
            .AddEdge(0, 1, 10)
            .AddEdge(1, 2, 10)
            .AddEdge(2, 3, 10)
            .ToNetwork();
        MaxFlowResult flow = EdmondsKarp.MaxFlow(network, 0, 3);

        IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(
            network,
            flow.FlowPerEdge,
            0,
            3);

        flow.Value.Should().BeApproximately(15, Tolerance);
        paths.Should().HaveCount(2);
        paths[0].HopCount.Should().Be(1, "the direct route must be taken before the long one");
        paths[0].Nodes.Should().Equal(0, 3);
        paths[0].Flow.Should().BeApproximately(5, Tolerance);
        paths[1].HopCount.Should().Be(3);
        paths[1].Nodes.Should().Equal(0, 1, 2, 3);
        paths[1].Flow.Should().BeApproximately(10, Tolerance);
    }

    [Fact]
    public void Decompose_NoFlow_ReturnsNoPaths()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(network, [0], 0, 1);

        paths.Should().BeEmpty();
    }

    [Fact]
    public void Decompose_WrongNumberOfFlowValues_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        var act = () => FlowPathDecomposition.Decompose(network, [1, 2], 0, 1);

        act.Should().Throw<ArgumentException>().WithParameterName("flowPerEdge");
    }

    [Fact]
    public void Decompose_NegativeFlow_Throws()
    {
        FlowNetwork network = FlowNetwork.Build(2).AddEdge(0, 1, 10).ToNetwork();

        var act = () => FlowPathDecomposition.Decompose(network, [-1], 0, 1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("flowPerEdge");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(2026)]
    public void Decompose_RandomNetwork_RecomposesToTheOriginalFlow(int seed)
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
            MaxFlowResult flow = EdmondsKarp.MaxFlow(network, 0, sink);

            IReadOnlyList<FlowPath> paths = FlowPathDecomposition.Decompose(
                network,
                flow.FlowPerEdge,
                0,
                sink);

            var recomposed = new double[network.EdgeCount];
            foreach (FlowPath path in paths)
            {
                for (int step = 0; step < path.HopCount; step++)
                {
                    recomposed[EdgeBetween(network, path.Nodes[step], path.Nodes[step + 1])] +=
                        path.Flow;
                }
            }

            for (int edge = 0; edge < network.EdgeCount; edge++)
            {
                recomposed[edge].Should().BeApproximately(
                    flow.FlowPerEdge[edge],
                    1e-7,
                    "decomposed paths must recompose to the original flow on edge {0}",
                    edge);
            }

            paths.Sum(path => path.Flow).Should().BeApproximately(
                flow.Value,
                1e-7,
                "every unit of flow must belong to exactly one path");
        }
    }

    private static int EdgeBetween(FlowNetwork network, int from, int to)
    {
        for (int edge = 0; edge < network.EdgeCount; edge++)
        {
            if (network.From(edge) == from && network.To(edge) == to)
            {
                return edge;
            }
        }

        throw new InvalidOperationException($"No edge from {from} to {to}.");
    }
}
