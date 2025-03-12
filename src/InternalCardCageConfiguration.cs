using System.Collections.Generic;
using Newtonsoft.Json;

namespace PDT.Plugins.Crestron.IO
{
    public class InternalCardCageConfiguration
    {
        [JsonProperty("cards")]
        public Dictionary<uint, string> Cards { get; set; }
    }
}