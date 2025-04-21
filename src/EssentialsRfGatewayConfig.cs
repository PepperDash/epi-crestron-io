using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Core;
using static PDT.Plugins.Crestron.IO.CenRfgwController;


namespace PDT.Plugins.Crestron.IO
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