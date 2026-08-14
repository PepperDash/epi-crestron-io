using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Tests;

public class FactoryMetadataTests
{
    private static readonly (string FactoryClass, string[] TypeNames)[] Factories =
    {
        ("C2NIoControllerFactory", new[] { "c2nio" }),
        ("C2nRthsControllerFactory", new[] { "c2nrths" }),
        ("CenCi31ControllerFactory", new[] { "cenci31" }),
        ("CenCi33ControllerFactory", new[] { "cenci33" }),
        ("CenIoCom102ControllerFactory", new[] { "ceniocom102" }),
        ("CenIoDigIn104ControllerFactory", new[] { "ceniodigin104" }),
        ("CenIoIr104ControllerFactory", new[] { "cenioir104" }),
        ("CenIoRy104ControllerFactory", new[] { "ceniory104" }),
        ("CenOdtOccupancySensorBaseControllerFactory", new[] { "cenodtcpoe", "cenodtocc" }),
        ("CenRfgwControllerFactory", new[] { "cenrfgwex", "cenerfgwpoe", "cengwexer", "internal" }),
        ("Din8sw8ControllerFactory", new[] { "din8sw8" }),
        ("DinCenCn2ControllerFactory", new[] { "dincencn2", "dincencn2poe", "din-cencn2", "din-cencn2-poe" }),
        ("DinIo8ControllerFactory", new[] { "DinIo8" }),
        ("GlsOdtOccupancySensorControllerFactory", new[] { "glsodtccn" }),
        ("GlsOccupancySensorBaseControllerFactory", new[] { "glsoirccn" }),
        ("GlsPartitionSensorControllerFactory", new[] { "glspartcn" }),
        ("Hrxx0WirelessRemoteControllerFactory", new[] { "hr100", "hr150", "hr310" }),
        ("InternalCardCageControllerFactory", new[] { "internalcardcage" }),
        ("StatusSignControllerFactory", new[] { "statussign" }),
    };

    [Theory]
    [MemberData(nameof(GetFactoryNames))]
    public void All_Factory_Sources_Set_MinimumEssentialsFrameworkVersion_To_Expected(string factoryClass)
    {
        var source = AssemblyFixture.FindSourceForClass(factoryClass);
        source.Should().NotBeNull($"source for {factoryClass} should exist");
        source.Should().Contain("MinimumEssentialsFrameworkVersion = \"3.0.0-fix-correct-interface-name.1\"");
    }

    [Theory]
    [MemberData(nameof(GetFactoryNames))]
    public void All_Factory_Sources_Set_TypeNames(string factoryClass)
    {
        var source = AssemblyFixture.FindSourceForClass(factoryClass);
        source.Should().NotBeNull();
        source.Should().Contain("TypeNames =");
    }

    public static IEnumerable<object[]> GetFactoryNames() =>
        Factories.Select(f => new object[] { f.FactoryClass });

    public static IEnumerable<object[]> GetFactoryTypeNamePairs() =>
        Factories.SelectMany(f => f.TypeNames.Select(tn => new object[] { f.FactoryClass, tn }));

    [Theory]
    [MemberData(nameof(GetFactoryTypeNamePairs))]
    public void Factory_Source_Contains_TypeName(string factoryClass, string typeName)
    {
        var source = AssemblyFixture.FindSourceForClass(factoryClass);
        source.Should().NotBeNull();
        source.Should().Contain($"\"{typeName}\"");
    }

    [Fact]
    public void No_Duplicate_TypeNames_Across_Factory_Sources()
    {
        var allTypeNames = Factories.SelectMany(f => f.TypeNames).ToList();
        var duplicates = allTypeNames
            .GroupBy(t => t, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        duplicates.Should().BeEmpty();
    }
}
