using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Tests;

public class FactoryDiscoveryTests
{
    private static readonly string[] ExpectedFactoryNames =
    {
        "C2NIoControllerFactory",
        "C2nRthsControllerFactory",
        "CenCi31ControllerFactory",
        "CenCi33ControllerFactory",
        "CenIoCom102ControllerFactory",
        "CenIoDigIn104ControllerFactory",
        "CenIoIr104ControllerFactory",
        "CenIoRy104ControllerFactory",
        "CenOdtOccupancySensorBaseControllerFactory",
        "CenRfgwControllerFactory",
        "Din8sw8ControllerFactory",
        "DinCenCn2ControllerFactory",
        "DinIo8ControllerFactory",
        "GlsOdtOccupancySensorControllerFactory",
        "GlsOccupancySensorBaseControllerFactory",
        "GlsPartitionSensorControllerFactory",
        "Hrxx0WirelessRemoteControllerFactory",
        "InternalCardCageControllerFactory",
        "StatusSignControllerFactory",
    };

    [Fact]
    public void Assembly_Loads_Successfully()
    {
        AssemblyFixture.PluginAssembly.Should().NotBeNull();
    }

    [Fact]
    public void Assembly_Name_Matches_Expected()
    {
        AssemblyFixture.PluginAssembly.GetName().Name.Should().Be("PepperDash.Essentials.Plugins.Crestron.Io");
    }

    [Fact]
    public void Factory_Count_Matches_Expected()
    {
        var factories = AssemblyFixture.FindFactoryTypes();
        factories.Should().HaveCount(ExpectedFactoryNames.Length);
    }

    [Theory]
    [MemberData(nameof(GetFactoryNames))]
    public void Factory_Exists_ByName(string factoryName)
    {
        var factories = AssemblyFixture.FindFactoryTypes();
        factories.Should().Contain(t => t.Name == factoryName);
    }

    [Fact]
    public void All_Factories_Have_Parameterless_Constructor()
    {
        var factories = AssemblyFixture.FindFactoryTypes();
        foreach (var factory in factories)
        {
            var ctor = factory.GetConstructor(Type.EmptyTypes);
            ctor.Should().NotBeNull($"factory {factory.Name} must have a parameterless constructor for discovery");
        }
    }

    public static IEnumerable<object[]> GetFactoryNames() =>
        ExpectedFactoryNames.Select(n => new object[] { n });
}
