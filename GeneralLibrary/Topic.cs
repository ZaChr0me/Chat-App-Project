using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace GeneralLibrary
{
    [Serializable]
    public class Topic
    {
        [JsonProperty("Users")]
        public List<string> Users { get; private set; }

        /*[JsonProperty("Messages")]
        public Dictionary<string> Messages { get; private set; }*/

        public string Description { get; set; }

        public Topic()
        {
        }

        public Topic(string description)
        {
            Description = description;
        }
    }
}