using Newtonsoft.Json;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins
{
    public class CrestronRemotePropertiesConfig
    {
        [JsonProperty("control")]
        public EssentialsControlPropertiesConfig Control { get; set; }

        [JsonProperty("gatewayDeviceKey")]
        public string GatewayDeviceKey { get; set; }
    }
}