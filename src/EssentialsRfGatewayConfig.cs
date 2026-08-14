using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Core;
using static PepperDash.Essentials.Plugins.CenRfgwController;


namespace PepperDash.Essentials.Plugins
{
    public class EssentialsRfGatewayConfig
    {
        [JsonProperty("control")]
        public EssentialsControlPropertiesConfig Control { get; set; }

        [JsonProperty("gatewayType")]
        [JsonConverter(typeof(StringEnumConverter))]
        public EExGatewayType GatewayType { get; set; }
     }
}