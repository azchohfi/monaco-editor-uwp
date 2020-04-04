﻿using Newtonsoft.Json;

namespace Monaco.Languages
{
    public sealed class CompletionList
    {
        [JsonProperty("incomplete", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Incomplete { get; set; }

        [JsonProperty("suggestions")]
        public CompletionItem[] Suggestions { get; set; }
    }
}
