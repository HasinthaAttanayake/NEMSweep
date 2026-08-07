using FluentAssertions;
using NEM.Model.Grid;
using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Tests.Grid
{
    public sealed class RegionTests
    {
        private static readonly DateTimeOffset NemStart =
            new(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(10));

        [Fact]
        public void Construction_RejectsNullGeneratingFleetCollection()
        {
            var act = () => new Region("NSW1", null!, HourlyFlow(100));

            act.Should().Throw<ArgumentNullException>().WithParameterName("generatingFleets");
        }

        [Fact]
        public void Construction_RejectsEmptyGeneratingFleetCollection()
        {
            var act = () => new Region("NSW1", [], HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("generatingFleets");
        }

        [Fact]
        public void Construction_RejectsNullGeneratingFleetEntry()
        {
            var act = () => new Region("NSW1", [null!], HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("generatingFleets");
        }

        [Fact]
        public void Construction_RejectsDuplicateGenerationTechnologyAggregates()
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal), Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100));

            act.Should().Throw<ArgumentException>().WithParameterName("generatingFleets");
        }

        [Fact]
        public void Construction_CopiesAndExposesReadOnlyGeneratingFleetCollection()
        {
            GeneratingFleet coal = Fleet(GenerationTechnology.Coal);
            GeneratingFleet[] generatingFleets = [coal];
            var region = new Region("NSW1", generatingFleets, HourlyFlow(100));

            generatingFleets[0] = Fleet(GenerationTechnology.Gas);
            var mutableView = (IList<GeneratingFleet>)region.GeneratingFleets;
            var act = () => mutableView[0] = Fleet(GenerationTechnology.Gas);

            region.GeneratingFleets.Should().ContainSingle().Which.Should().BeSameAs(coal);
            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void Construction_AllowsNoStorageFleets()
        {
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100));

            region.StorageFleets.Should().BeEmpty();
        }

        [Fact]
        public void Construction_RejectsNullStorageFleetEntry()
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100),
                storageFleets: [null!]);

            act.Should().Throw<ArgumentException>()
                .WithParameterName("storageFleets");
        }

        [Fact]
        public void Construction_RejectsDuplicateStorageTechnologyAggregates()
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100),
                storageFleets: [
                    Storage(StorageTechnology.Battery),
                    Storage(StorageTechnology.Battery),
                ]);

            act.Should().Throw<ArgumentException>().WithParameterName("storageFleets");
        }

        [Theory]
        [InlineData(GenerationTechnology.Solar)]
        [InlineData(GenerationTechnology.Wind)]
        public void Construction_RejectsRenewableFleetWithoutResourceProfile(
            GenerationTechnology technology)
        {
            var act = () => new Region(
                "NSW1",
                [Fleet(technology)],
                HourlyFlow(100));

            act.Should().Throw<ArgumentException>()
                .WithParameterName("resourceProfile")
                .WithMessage("*wind or solar generating fleets require a resource profile*");
        }

        [Fact]
        public void Construction_RejectsResourceProfileMisalignedWithDemand()
        {
            FlowSeries demand = HourlyFlow(100);
            RegionalResourceProfile resources = ResourceProfile(NemStart.AddHours(1));

            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Wind)],
                demand,
                resourceProfile: resources);

            act.Should().Throw<ArgumentException>().WithMessage("*misaligned on start*");
        }

        [Fact]
        public void WithBatteryStorage_IntroducesBatteryAndPreservesDemandAndFixedStorage()
        {
            FlowSeries additiveDemand = HourlyFlow(20);
            var pumpedHydro = Storage(StorageTechnology.PumpedHydro);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100),
                new Dictionary<string, FlowSeries> { ["DataCentre"] = additiveDemand },
                storageFleets: [pumpedHydro],
                storageTechnologyProfiles: BatteryProfiles());

            Region sized = region.WithBatteryStorage(
                Energy.FromMegawattHours(120),
                Power.FromMegawatts(30));

            region.StorageFleets.Should().ContainSingle().Which.Should().BeSameAs(pumpedHydro);
            sized.StorageFleets.Should().HaveCount(2).And.Contain(pumpedHydro);
            StorageFleet battery = sized.StorageFleets.Single(
                fleet => fleet.StorageTechnology == StorageTechnology.Battery);
            battery.StorageCapacity.Should().Be(Energy.FromMegawattHours(120));
            battery.PowerCapacity.Should().Be(Power.FromMegawatts(30));
            sized.Demand.BaseDemand.Should().BeSameAs(region.Demand.BaseDemand);
            sized.Demand.AdditiveComponents["DataCentre"].Should().BeSameAs(additiveDemand);
        }

        [Fact]
        public void WithBatteryStorage_ReplacesExistingBatteryAsTotalSizing()
        {
            var existingBattery = Storage(StorageTechnology.Battery);
            var region = new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100),
                storageFleets: [existingBattery]);

            Region sized = region.WithBatteryStorage(
                Energy.FromMegawattHours(240),
                Power.FromMegawatts(60));

            sized.StorageFleets.Should().ContainSingle();
            sized.StorageFleets[0].Should().NotBeSameAs(existingBattery);
            sized.StorageFleets[0].StorageCapacity.Should().Be(Energy.FromMegawattHours(240));
            sized.StorageFleets[0].PowerCapacity.Should().Be(Power.FromMegawatts(60));
        }

        [Fact]
        public void Constructor_RejectsFleetThatDisagreesWithConfiguredStorageProfile()
        {
            StorageFleet battery = Storage(StorageTechnology.Battery);
            var profiles = new Dictionary<StorageTechnology, StorageTechnologyProfile>
            {
                [StorageTechnology.Battery] = new(20u, 0.5),
            };

            var act = () => new Region(
                "NSW1",
                [Fleet(GenerationTechnology.Coal)],
                HourlyFlow(100),
                storageFleets: [battery],
                storageTechnologyProfiles: profiles);

            act.Should().Throw<ArgumentException>()
                .WithParameterName("storageFleets");
        }

        private static GeneratingFleet Fleet(GenerationTechnology technology) =>
            new(technology, Power.FromMegawatts(100));

        private static StorageFleet Storage(StorageTechnology technology) =>
            new(
                technology,
                Energy.FromMegawattHours(100),
                Power.FromMegawatts(50),
                BatteryProfile());

        private static StorageTechnologyProfile BatteryProfile() => new(15u, 0.87);

        private static IReadOnlyDictionary<StorageTechnology, StorageTechnologyProfile>
            BatteryProfiles() => new Dictionary<StorageTechnology, StorageTechnologyProfile>
            {
                [StorageTechnology.Battery] = BatteryProfile(),
            };

        private static FlowSeries HourlyFlow(params double[] megawatts) =>
            new(NemStart, TimeSpan.FromHours(1), megawatts);

        private static RegionalResourceProfile ResourceProfile(DateTimeOffset start)
        {
            var values = new[] { 0.0 };
            return new RegionalResourceProfile(
                TraceSeries.GlobalHorizontalRadiation(start, TimeSpan.FromHours(1), values),
                TraceSeries.DirectNormalRadiation(start, TimeSpan.FromHours(1), values),
                TraceSeries.DiffuseHorizontalRadiation(start, TimeSpan.FromHours(1), values),
                SolarZenithSeries.Calculate(
                    start,
                    TimeSpan.FromHours(1),
                    1,
                    latitude: -33.8688,
                    longitude: 151.2093),
                TraceSeries.DryBulbTemperature(start, TimeSpan.FromHours(1), values),
                TraceSeries.WindSpeed(start, TimeSpan.FromHours(1), values, 10));
        }
    }
}