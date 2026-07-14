using FluentAssertions;
using Newtonsoft.Json;
using PDT.Plugins.Crestron.IO;
using Xunit;

namespace EpiCrestronIo.Tests
{
    /// <summary>
    /// Validates the GLS occupancy sensor config contract against the JSON shape Essentials
    /// deserializes at runtime. This is a true unit test: the config is a pure POCO with no
    /// Crestron SDK dependency.
    /// </summary>
    public class GlsOccupancySensorPropertiesConfigTests
    {
        // The ConfigSnippet from GlsOdtOccupancySensorController - the documented dual-tech shape.
        private const string OdtSnippet =
            "{\"enablePir\": true,\"enableLedFlash\": true,\"enableRawStates\":true,\"remoteTimeout\": 30," +
            "\"internalPhotoSensorMinChange\": 0,\"externalPhotoSensorMinChange\": 0," +
            "\"enableUsA\": true,\"enableUsB\": true,\"orWhenVacatedState\": true}";

        [Fact]
        public void Deserializes_OdtConfigSnippet_WithoutError()
        {
            var config = JsonConvert.DeserializeObject<GlsOccupancySensorPropertiesConfig>(OdtSnippet);

            config.Should().NotBeNull();
            config!.EnablePir.Should().BeTrue();
            config.EnableLedFlash.Should().BeTrue();
            config.EnableRawStates.Should().BeTrue();
            config.RemoteTimeout.Should().Be(30);
            config.InternalPhotoSensorMinChange.Should().Be(0);
            config.ExternalPhotoSensorMinChange.Should().Be(0);
            config.EnableUsA.Should().BeTrue();
            config.EnableUsB.Should().BeTrue();
            config.OrWhenVacatedState.Should().BeTrue();
        }

        [Fact]
        public void OmittedFields_RemainNull_SoExistingSensorSettingsAreNotOverwritten()
        {
            // Only PIR specified - every other setting must stay null so ApplySettingsToSensorFromConfig
            // skips it (the controller treats null as "leave as-is").
            var config = JsonConvert.DeserializeObject<GlsOccupancySensorPropertiesConfig>("{\"enablePir\": true}");

            config.Should().NotBeNull();
            config!.EnablePir.Should().BeTrue();
            config.EnableLedFlash.Should().BeNull();
            config.RemoteTimeout.Should().BeNull();
            config.EnableUsA.Should().BeNull();
            config.OrWhenVacatedState.Should().BeNull();
            config.UsSensitivityOccupied.Should().BeNull();
        }

        [Fact]
        public void EmptyObject_DeserializesToAllNulls()
        {
            var config = JsonConvert.DeserializeObject<GlsOccupancySensorPropertiesConfig>("{}");

            config.Should().NotBeNull();
            config!.EnablePir.Should().BeNull();
            config.RemoteTimeout.Should().BeNull();
        }

        [Fact]
        public void Sensitivities_ParseAsUshort()
        {
            var config = JsonConvert.DeserializeObject<GlsOccupancySensorPropertiesConfig>(
                "{\"usSensitivityOccupied\":3,\"usSensitivityVacant\":2,\"pirSensitivityOccupied\":3,\"pirSensitivityVacant\":1}");

            config.Should().NotBeNull();
            config!.UsSensitivityOccupied.Should().Be(3);
            config.UsSensitivityVacant.Should().Be(2);
            config.PirSensitivityOccupied.Should().Be(3);
            config.PirSensitivityVacant.Should().Be(1);
        }
    }
}
