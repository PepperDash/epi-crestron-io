using System.Collections.Generic;
using Newtonsoft.Json;

namespace PDT.Plugins.Crestron.IO
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