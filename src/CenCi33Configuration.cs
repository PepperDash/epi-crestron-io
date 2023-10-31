using System.Collections.Generic;
using Newtonsoft.Json;

namespace PDT.Plugins.Crestron.IO
{
    public class CenCi33Configuration
    {
        [JsonProperty("cards")]
        public Dictionary<uint, string> Cards { get; set; }
    }
}