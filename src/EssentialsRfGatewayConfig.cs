using Newtonsoft.Json;
using PepperDash.Essentials.Core;


namespace PDT.Plugins.Crestron.IO
{
    public class EssentialsRfGatewayConfig
    {
        [JsonProperty("control")]
        public EssentialsControlPropertiesConfig Control { get; set; }

        [JsonProperty("gatewayType")]
        public string GatewayType { get; set; }
     }
}