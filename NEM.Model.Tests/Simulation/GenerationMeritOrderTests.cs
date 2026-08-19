using AwesomeAssertions;
using NEM.Model.Grid;
using NEM.Model.Simulation;
using NEM.Model.Units;

namespace NEM.Model.Tests.Simulation
{
    public sealed class GenerationMeritOrderTests
    {
        [Fact]
        public void Sort_OrdersByShortRunMarginalCostFirst()
        {
            GeneratingFleet cheap = Fleet(GenerationTechnology.Gas, srmc: 1);
            GeneratingFleet expensive = Fleet(GenerationTechnology.Coal, srmc: 10);

            IOrderedEnumerable<GeneratingFleet> sorted =
                GenerationMeritOrder.Sort([expensive, cheap]);

            sorted.Should().Equal(cheap, expensive);
        }

        [Fact]
        public void Sort_TiesBreakByGenerationTechnology()
        {
            GeneratingFleet gas = Fleet(GenerationTechnology.Gas, srmc: 0);
            GeneratingFleet coal = Fleet(GenerationTechnology.Coal, srmc: 0);

            IOrderedEnumerable<GeneratingFleet> sorted = GenerationMeritOrder.Sort([gas, coal]);

            sorted.Should().Equal(coal, gas);
        }

        private static GeneratingFleet Fleet(GenerationTechnology technology, decimal srmc) =>
            new(
                technology,
                Power.FromMegawatts(100),
                shortRunMarginalCost: GenerationEnergyCost.FromAudPerMwhGenerated(srmc));
    }
}
