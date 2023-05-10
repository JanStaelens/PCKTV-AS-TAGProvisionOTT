/*
****************************************************************************
*  Copyright (c) 2023,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

    Skyline Communications NV
    Ambachtenstraat 33
    B-8870 Izegem
    Belgium
    Tel.    : +32 51 31 35 69
    Fax.    : +32 51 31 01 29
    E-mail  : info@skyline.be
    Web     : www.skyline.be
    Contact : Ben Vandenberghe

****************************************************************************
Revision History:

DATE        VERSION     AUTHOR          COMMENTS

dd/mm/2023  1.0.0.1     XXX, Skyline    Initial version
****************************************************************************
*/

namespace Script
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
    using Skyline.DataMiner.ExceptionHelper;
    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
    using Skyline.DataMiner.Net.Sections;
    using TagHelperMethods;

    /// <summary>
    /// DataMiner Script Class.
    /// </summary>
    public class Script
    {
        private readonly int scanChannelsTable = 1310;
        private SharedMethods sharedMethods;

        /// <summary>
        /// The Script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(Engine engine)
        {
            var scriptName = "PA_TAG_Create Scanner and KMS";
            var scanName = String.Empty;

            var helper = new PaProfileLoadDomHelper(engine);
            var domHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

            var exceptionHelper = new ExceptionHelper(engine, domHelper);
            this.sharedMethods = new SharedMethods(helper, domHelper);

            engine.GenerateInformation("START " + scriptName);

            var instanceId = helper.GetParameterValue<string>("InstanceId (TAG Scan)");
            var instance = domHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
            var status = instance.StatusId;

            if (!status.Equals("ready") && !status.Equals("in_progress"))
            {
				engine.GenerateInformation("Failed to create scanner due to incorrect status: " + status);
                helper.SendErrorMessageToTokenHandler();
                return;
            }

            try
            {
                var scanner = new Scanner
                {
                    AssetId = helper.GetParameterValue<string>("Asset ID (TAG Scan)"),
                    InstanceId = instanceId,
                    ScanName = helper.GetParameterValue<string>("Scan Name (TAG Scan)"),
                    SourceElement = helper.TryGetParameterValue("Source Element (TAG Scan)", out string sourceElement) ? sourceElement : String.Empty,
                    SourceId = helper.TryGetParameterValue("Source ID (TAG Scan)", out string sourceId) ? sourceId : String.Empty,
                    TagDevice = helper.GetParameterValue<string>("TAG Device (TAG Scan)"),
                    TagElement = helper.GetParameterValue<string>("TAG Element (TAG Scan)"),
                    TagInterface = helper.GetParameterValue<string>("TAG Interface (TAG Scan)"),
                    ScanType = helper.GetParameterValue<string>("Scan Type (TAG Scan)"),
                    Action = helper.GetParameterValue<string>("Action (TAG Scan)"),
                    Channels = helper.TryGetParameterValue("Channels (TAG Scan)", out List<Guid> channels) ? channels : new List<Guid>(),
                };
                scanName = scanner.ScanName;

                IDms dms = engine.GetDms();
                IDmsElement element = dms.GetElement(scanner.TagElement);
                engine.GenerateInformation("Processing scanner on: " + scanner.TagElement);
                var tagDictionary = new Dictionary<string, TagRequest>();

                var tagRequest = new TagRequest();
                var scanList = this.CreateScanRequestJson(instance, scanner);
                tagRequest.ScanRequests = scanList;
                tagDictionary.Add(scanner.TagDevice, tagRequest);

                element.GetStandaloneParameter<string>(3).SetValue(JsonConvert.SerializeObject(tagDictionary));

                bool VerifyScanCreation()
                {
                    try
                    {
                        var scanRequests = scanList;
                        var requestTitles = this.GetScanRequestTitles(scanRequests);

                        object[][] scanChannelsRows = null;
                        var scanChannelTable = element.GetTable(this.scanChannelsTable);
                        scanChannelsRows = scanChannelTable.GetRows();

                        return scanChannelsRows == null || this.CheckScanRow(requestTitles, scanChannelsRows);
                    }
                    catch (Exception e)
                    {
                        engine.Log("Exception thrown while checking TAG Scan status: " + e);
                        throw;
                    }
                }

                if (this.sharedMethods.Retry(VerifyScanCreation, new TimeSpan(0, 5, 0)))
                {
                    // successfully created filter
                    engine.GenerateInformation("Scan created for " + scanner.TagElement);
                    if (status == "ready")
                    {
                        helper.TransitionState("ready_to_inprogress");
                    }

                    helper.ReturnSuccess();
                }
                else
                {
                    // failed to execute in time
                    var log = new Log
                    {
                        AffectedItem = scriptName,
                        AffectedService = scanner.ScanName,
                        Timestamp = DateTime.Now,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Severity = ErrorCode.SeverityType.Warning,
                            Source = "Retry condition",
                            Description = "Create Scan failed.",
                        },
                    };
                    exceptionHelper.GenerateLog(log);
                    helper.SendErrorMessageToTokenHandler();
                }
            }
            catch (ScriptAbortException)
            {
                // no issue
            }
            catch (Exception ex)
            {
                engine.GenerateInformation("Error in Create Scanner and KMS: " + ex);
                var log = new Log
                {
                    AffectedItem = scriptName,
                    AffectedService = scanName,
                    Timestamp = DateTime.Now,
                    ErrorCode = new ErrorCode
                    {
                        ConfigurationItem = scriptName + " Script",
                        ConfigurationType = ErrorCode.ConfigType.Automation,
                        Severity = ErrorCode.SeverityType.Warning,
                        Source = "Run()",
                    },
                };
                exceptionHelper.ProcessException(ex, log);
                helper.SendErrorMessageToTokenHandler();
            }
        }

        private List<Scan> CreateScanRequestJson(DomInstance instance, Scanner scanner)
        {
            List<Scan> scans = new List<Scan>();
            var nameFormat = "{0} {1} #RES|BAND#";

            var manifests = this.sharedMethods.GetManifests(instance);

            foreach (var manifest in manifests)
            {
                scans.Add(new Scan
                {
                    Action = (int)TagRequest.TAGAction.Add,
                    AssetId = scanner.AssetId,
                    Interface = scanner.TagInterface,
                    Name = String.Format(nameFormat, scanner.ScanName, manifest.Name),
                    Type = scanner.ScanType,
                    Url = manifest.Url,
                });
            }

            return scans;
        }

        private List<string> GetScanRequestTitles(List<Scan> scanRequests)
        {
            var scanTitles = new List<string>();
            foreach (var scan in scanRequests)
            {
                scanTitles.Add(scan.Name);
            }

            return scanTitles;
        }

        private bool CheckScanRow(List<string> titles, object[][] scanChannelsRows)
        {
            foreach (var row in scanChannelsRows)
            {
                if (titles.Contains(Convert.ToString(row[13 /*Title*/])))
                {
                    return true;
                }
            }

            return false;
        }
    }
}