// <copyright file="TAG.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace TagHelperMethods
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "<Pending>")]
    public class TagRequest
    {
        public TagRequest()
        {
            this.ScanRequests = new List<Scan>();
            this.MonitorRequests = new List<Monitor>();
            this.MultiViewerRequests = new List<Multiviewer>();
            this.ChannelSets = new List<Channel>();
        }

        public enum TAGAction
        {
            Add = 1,
            Delete = 2,
        }

        [JsonProperty("channelSets", NullValueHandling = NullValueHandling.Ignore)]
        public List<Channel> ChannelSets { get; set; }

        [JsonProperty("monitorRequest", NullValueHandling = NullValueHandling.Ignore)]
        public List<Monitor> MonitorRequests { get; set; }

        [JsonProperty("multiviewerRequest", NullValueHandling = NullValueHandling.Ignore)]
        public List<Multiviewer> MultiViewerRequests { get; set; }

        [JsonProperty("scanRequest", NullValueHandling = NullValueHandling.Ignore)]
        public List<Scan> ScanRequests { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
    public class Scan
    {
        public enum ModeState
        {
            Starting = 1,
            Running = 2,
            Canceling = 3,
            Finishing = 4,
            Finished = 5,
            Failed = 6,
            FinishedRemoved = 7,
        }

        [JsonProperty("assetId", NullValueHandling = NullValueHandling.Ignore)]
        public string AssetId { get; set; }

        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
        public long? Action { get; set; }

        [JsonProperty("interface", NullValueHandling = NullValueHandling.Ignore)]
        public string Interface { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }

        [JsonProperty("url", NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }
    }

    public class Monitor
    {
        [JsonProperty("template", NullValueHandling = NullValueHandling.Ignore)]
        public string Template { get; set; }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long? Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    public class Multiviewer
    {
        [JsonProperty("event", NullValueHandling = NullValueHandling.Ignore)]
        public string Event { get; set; }

        [JsonProperty("layout", NullValueHandling = NullValueHandling.Ignore)]
        public List<Layout> Layout { get; set; }
    }

    public class Layout
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long? Id { get; set; }

        [JsonProperty("layout", NullValueHandling = NullValueHandling.Ignore)]
        public string LayoutLayout { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("template", NullValueHandling = NullValueHandling.Ignore)]
        public string Template { get; set; }
    }

    public class Channel
    {
        [JsonProperty("template", NullValueHandling = NullValueHandling.Ignore)]
        public string Template { get; set; }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long? Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("thresholdSet", NullValueHandling = NullValueHandling.Ignore)]
        public string ThresholdSet { get; set; }

        [JsonProperty("notificationSet", NullValueHandling = NullValueHandling.Ignore)]
        public string NotificationSet { get; set; }

        [JsonProperty("delay", NullValueHandling = NullValueHandling.Ignore)]
        public int? Delay { get; set; }

        [JsonProperty("monitoringLevel", NullValueHandling = NullValueHandling.Ignore)]
        public int? MonitoringLevel { get; set; }

        [JsonProperty("descrambling", NullValueHandling = NullValueHandling.Ignore)]
        public int? Descrambling { get; set; }

        [JsonProperty("encryption", NullValueHandling = NullValueHandling.Ignore)]
        public string Encryption { get; set; }

        [JsonProperty("kms", NullValueHandling = NullValueHandling.Ignore)]
        public string Kms { get; set; }
    }
}