using System.Collections.Generic;
using Newtonsoft.Json;

namespace PepperDash.Essentials.Plugins
{
    public class CenCi33Configuration
    {
        [JsonProperty("cards")]
        public Dictionary<uint, string> Cards { get; set; }
    }
}