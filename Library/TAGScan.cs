namespace TagHelperMethods
{
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
    using Skyline.DataMiner.Net.Sections;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;

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

    public class Scanner
    {
        public string Action { get; set; }

        public string AssetId { get; set; }

        public List<Guid> Channels { get; set; }

        public string InstanceId { get; set; }

        public string ScanName { get; set; }

        public string ScanType { get; set; }

        public string SourceElement { get; set; }

        public string SourceId { get; set; }

        public string TagDevice { get; set; }

        public string TagElement { get; set; }

        public string TagInterface { get; set; }
    }

    public class Manifest
    {
        public string Name { get; set; }

        public string Url { get; set; }
    }

    public class SharedMethods
    {
        private PaProfileLoadDomHelper innerHelper;
        private DomHelper innerDomHelper;

        public SharedMethods(PaProfileLoadDomHelper helper, DomHelper domHelper)
        {
            this.innerHelper = helper;
            this.innerDomHelper = domHelper;
        }

        /// <summary>
        /// Retry until success or until timeout.
        /// </summary>
        /// <param name="func">Operation to retry.</param>
        /// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
        /// <returns><c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise <c>false</c>.</returns>
        public bool Retry(Func<bool> func, TimeSpan timeout)
        {
            bool success;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            do
            {
                success = func();
                if (!success)
                {
                    Thread.Sleep(3000);
                }
            }
            while (!success && sw.Elapsed <= timeout);
            return success;
        }

        public static void TransitionToError(PaProfileLoadDomHelper helper, string status)
        {
            switch (status)
            {
                case "draft":
                    helper.TransitionState("draft_to_ready");
                    helper.TransitionState("ready_to_inprogress");
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "ready":
                    helper.TransitionState("ready_to_inprogress");
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "in_progress":
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "active":
                    helper.TransitionState("active_to_reprovision");
                    helper.TransitionState("reprovision_to_inprogress");
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "deactivate":
                    helper.TransitionState("deactivate_to_deactivating");
                    helper.TransitionState("deactivating_to_error");
                    break;

                case "deactivating":
                    helper.TransitionState("deactivating_to_error");
                    break;

                case "reprovision":
                    helper.TransitionState("reprovision_to_inprogress");
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "complete":
                    helper.TransitionState("complete_to_ready");
                    helper.TransitionState("ready_to_inprogress");
                    helper.TransitionState("inprogress_to_error");
                    break;

                case "active_with_errors":
                    helper.TransitionState("activewitherrors_to_deactivate");
                    helper.TransitionState("deactivate_to_deactivating");
                    helper.TransitionState("deactivating_to_error");
                    break;
            }
        }

        public List<Manifest> GetManifests(DomInstance instance)
        {
            List<Manifest> manifests = new List<Manifest>();

            foreach (var section in instance.Sections)
            {
                Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = this.SetSectionDefinitionById;
                section.Stitch(sectionDefinitionFunc);

                if (!section.GetSectionDefinition().GetName().Equals("Manifests"))
                {
                    continue;
                }

                var manifest = new Manifest();
                foreach (var field in section.FieldValues)
                {
                    switch (field.GetFieldDescriptor().Name)
                    {
                        case "Manifest Name (TAG Scan)":
                            manifest.Name = field.Value.ToString();
                            break;

                        case "Manifest URL (TAG Scan)":
                            manifest.Url = field.Value.ToString();
                            break;

                        default:
                            this.innerHelper.Log($"fieldName not found: {field.GetFieldDescriptor().Name}", PaLogLevel.Error);
                            break;
                    }
                }

                manifests.Add(manifest);
            }

            return manifests;
        }

        public List<string> GetScanRequestTitles(List<Scan> scanRequests)
        {
            var scanTitles = new List<string>();
            foreach (var scan in scanRequests)
            {
                scanTitles.Add(scan.Name);
            }

            return scanTitles;
        }

        public void StartTAGChannelsProcess(Scanner scanner)
        {
            foreach (var channel in scanner.Channels)
            {
                var subFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(channel));
                var subInstance = this.innerDomHelper.DomInstances.Read(subFilter).First();

                var actionPrefix = subInstance.StatusId.Equals("error") ? "error-" : String.Empty;
                this.innerDomHelper.DomInstances.ExecuteAction(subInstance.ID, actionPrefix + scanner.Action);
            }
        }

        private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
        {
            return this.innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
        }

        private DomDefinition SetDomDefinitionById(DomDefinitionId domDefinitionId)
        {
            return this.innerDomHelper.DomDefinitions.Read(DomDefinitionExposers.Id.Equal(domDefinitionId)).First();
        }
    }
}