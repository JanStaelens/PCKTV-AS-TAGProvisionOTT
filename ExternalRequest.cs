#pragma warning disable SA1633 // File should have header
namespace Helper
#pragma warning restore SA1633 // File should have header
{
    using Newtonsoft.Json;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class ExternalRequest
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("processResponse", NullValueHandling = NullValueHandling.Ignore)]
        public ProcessResponse ProcessResponse { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class ProcessResponse
    {
        [JsonProperty("conviva", NullValueHandling = NullValueHandling.Ignore)]
        public ConvivaResponse Conviva { get; set; }

        [JsonProperty("peacock", NullValueHandling = NullValueHandling.Ignore)]
        public PeacockResponse Peacock { get; set; }

        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public TagResponse Tag { get; set; }

        [JsonProperty("touchstream", NullValueHandling = NullValueHandling.Ignore)]
        public TouchstreamResponse Touchstream { get; set; }

        [JsonProperty("eventName")]
        public string EventName { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class ConvivaResponse
    {
        public string Status { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class TagResponse
    {
        public string Status { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class TouchstreamResponse
    {
        public string Status { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "ignored")]
    public class PeacockResponse
    {
        public string Status { get; set; }
    }
}