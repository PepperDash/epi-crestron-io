using Newtonsoft.Json;

namespace PDT.Plugins.Crestron.IO
{
    public class CenCi31Configuration
    {
        [JsonProperty("card")]
        public string Card { get; set; }
    }
}