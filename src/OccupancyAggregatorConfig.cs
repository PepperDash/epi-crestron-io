using System.Collections.Generic;
using Newtonsoft.Json;

namespace PepperDash.Essentials.Plugins
{
    public class OccupancyAggregatorConfig
    {
        [JsonProperty("deviceKeys")] public List<string> DeviceKeys { get; set; }

        public OccupancyAggregatorConfig()
        {
            DeviceKeys = new List<string>();
        }
    }
}