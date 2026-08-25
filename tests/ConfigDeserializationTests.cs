using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugins.Tests;

public class ConfigDeserializationTests
{
    private static readonly string[] ConfigClassNames =
    {
        "CenCi31Configuration",
        "CenCi33Configuration",
        "GlsOccupancySensorPropertiesConfig",
        "GlsPartitionSensorPropertiesConfig",
        "InternalCardCageConfiguration",
        "EssentialsRfGatewayConfig",
        "CrestronRemotePropertiesConfig",
        "OccupancyAggregatorConfig",
    };

    private static Type GetConfigType(string name) =>
        AssemblyFixture.PluginAssembly.GetTypes().Single(t => t.Name == name);

    [Theory]
    [MemberData(nameof(GetConfigClassNames))]
    public void Config_Class_Exists(string className)
    {
        var type = AssemblyFixture.PluginAssembly.GetTypes().FirstOrDefault(t => t.Name == className);
        type.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(GetConfigClassNames))]
    public void Config_Has_Parameterless_Constructor(string className)
    {
        var type = GetConfigType(className);
        var ctor = type.GetConstructor(Type.EmptyTypes);
        ctor.Should().NotBeNull();
    }

    public static IEnumerable<object[]> GetConfigClassNames() =>
        ConfigClassNames.Select(n => new object[] { n });

    public static IEnumerable<object[]> GetPropertyJsonNamePairs()
    {
        yield return new object[] { "CenCi31Configuration", "Card", "card" };

        yield return new object[] { "CenCi33Configuration", "Cards", "cards" };

        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "EnablePir", "enablePir" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "EnableLedFlash", "enableLedFlash" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "ShortTimeoutState", "shortTimeoutState" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "EnableRawStates", "enableRawStates" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "RemoteTimeout", "remoteTimeout" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "InternalPhotoSensorMinChange", "internalPhotoSensorMinChange" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "ExternalPhotoSensorMinChange", "externalPhotoSensorMinChange" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "EnableUsA", "enableUsA" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "EnableUsB", "enableUsB" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "OrWhenVacatedState", "orWhenVacatedState" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "AndWhenVacatedState", "andWhenVacatedState" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "UsSensitivityOccupied", "usSensitivityOccupied" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "UsSensitivityVacant", "usSensitivityVacant" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "PirSensitivityOccupied", "pirSensitivityOccupied" };
        yield return new object[] { "GlsOccupancySensorPropertiesConfig", "PirSensitivityVacant", "pirSensitivityVacant" };

        yield return new object[] { "GlsPartitionSensorPropertiesConfig", "Sensitivity", "sensitivity" };
        yield return new object[] { "GlsPartitionSensorPropertiesConfig", "EnableSensor", "enable" };

        yield return new object[] { "InternalCardCageConfiguration", "Cards", "cards" };

        yield return new object[] { "EssentialsRfGatewayConfig", "Control", "control" };
        yield return new object[] { "EssentialsRfGatewayConfig", "GatewayType", "gatewayType" };

        yield return new object[] { "CrestronRemotePropertiesConfig", "Control", "control" };
        yield return new object[] { "CrestronRemotePropertiesConfig", "GatewayDeviceKey", "gatewayDeviceKey" };

        yield return new object[] { "OccupancyAggregatorConfig", "DeviceKeys", "deviceKeys" };
    }

    [Theory]
    [MemberData(nameof(GetPropertyJsonNamePairs))]
    public void Config_Property_Has_JsonPropertyAttribute(string className, string propertyName, string jsonName)
    {
        var type = GetConfigType(className);
        var property = type.GetProperty(propertyName);
        property.Should().NotBeNull();

        var hasAttribute = property!.CustomAttributes.Any(a =>
            a.AttributeType.Name == "JsonPropertyAttribute"
            && a.ConstructorArguments.Any(arg =>
                string.Equals(arg.Value?.ToString(), jsonName, StringComparison.Ordinal)));

        hasAttribute.Should().BeTrue($"{className}.{propertyName} should have [JsonProperty(\"{jsonName}\")]");
    }
}
