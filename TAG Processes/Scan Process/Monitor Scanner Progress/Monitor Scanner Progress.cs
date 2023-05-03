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
    using System.Web;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
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
        private PaProfileLoadDomHelper innerHelper;
#pragma warning disable S1450 // Private fields only used as local variables in methods should become local variables
        private DomHelper innerDomHelper;
#pragma warning restore S1450 // Private fields only used as local variables in methods should become local variables

        /// <summary>
        /// The Script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(Engine engine)
        {
            var scriptName = "Monitor Scanner Progress";

            innerHelper = new PaProfileLoadDomHelper(engine);
            this.innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

            var exceptionHelper = new ExceptionHelper(engine, this.innerDomHelper);
            var sharedMethods = new SharedMethods(innerHelper, this.innerDomHelper);

            engine.GenerateInformation("START " + scriptName);

            var instanceId = innerHelper.GetParameterValue<string>("InstanceId (TAG Scan)");
            var instance = this.innerDomHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
            var status = instance.StatusId;

            if (!status.Equals("in_progress"))
            {
                innerHelper.SendErrorMessageToTokenHandler();
                return;
            }

            var scanner = new Scanner
            {
                AssetId = innerHelper.GetParameterValue<string>("Asset ID (TAG Scan)"),
                InstanceId = instanceId,
                ScanName = innerHelper.GetParameterValue<string>("Scan Name (TAG Scan)"),
                SourceElement = innerHelper.TryGetParameterValue("Source Element (TAG Scan)", out string sourceElement) ? sourceElement : String.Empty,
                SourceId = innerHelper.TryGetParameterValue("Source ID (TAG Scan)", out string sourceId) ? sourceId : String.Empty,
                TagDevice = innerHelper.GetParameterValue<string>("TAG Device (TAG Scan)"),
                TagElement = innerHelper.GetParameterValue<string>("TAG Element (TAG Scan)"),
                TagInterface = innerHelper.GetParameterValue<string>("TAG Interface (TAG Scan)"),
                ScanType = innerHelper.GetParameterValue<string>("Scan Type (TAG Scan)"),
                Action = innerHelper.GetParameterValue<string>("Action (TAG Scan)"),
                Channels = innerHelper.TryGetParameterValue("Channels (TAG Scan)", out List<Guid> channels) ? channels : new List<Guid>(),
            };

            try
            {
                IDms dms = engine.GetDms();
                IDmsElement element = dms.GetElement(scanner.TagElement);

                var manifests = sharedMethods.GetManifests(instance);

                bool VerifyScan()
                {
                    try
                    {
                        int iTotalExpected = 0;
                        int iScanRequestChecked = 0;

                        iTotalExpected = manifests.Count;

                        object[][] scanChannelsRows = null;
                        var scanChannelTable = element.GetTable(1310);
                        scanChannelsRows = scanChannelTable.GetRows();

                        if (scanChannelsRows == null)
                        {
                            innerHelper.Log("No Scan Channel Rows found", PaLogLevel.Information);
                            return false;
                        }

                        foreach (var manifest in manifests)
                        {
                            foreach (var row in scanChannelsRows)
                            {
                                // Tried to refactor, but QueryData can't check for contains or a column equals two different values
                                // Though ideally we can get around getting all rows in the table
                                string[] urls = Convert.ToString(row[14]).Split('|');
                                string title = HttpUtility.HtmlDecode(Convert.ToString(row[13]));
                                var mode = (ModeState)Convert.ToInt32(row[2]);

                                bool isScanFinished = mode == ModeState.Finished || mode == ModeState.FinishedRemoved;
                                if (title.Contains(scanner.ScanName.Split(' ')[0]) && urls.Contains(manifest.Url) && isScanFinished)
                                {
                                    iScanRequestChecked++;
                                    break;
                                }
                            }
                        }

                        if (iTotalExpected == iScanRequestChecked)
                        {
                            // done
                            return true;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        engine.Log("Exception thrown while checking TAG Scan status: " + ex);
                        throw;
                    }
                }

                if (sharedMethods.Retry(VerifyScan, new TimeSpan(0, 5, 0)))
                {
                    sharedMethods.StartTAGChannelsProcess(scanner);
                    innerHelper.ReturnSuccess();
                }
                else
                {
                    // failed to execute in time
                    var log = new Log
                    {
                        AffectedItem = scanner.TagElement,
                        AffectedService = "TAG Scan Subprocess",
                        Timestamp = DateTime.Now,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = scriptName + "Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
                            Severity = ErrorCode.SeverityType.Warning,
                            Source = "Retry condition",
							Description = "Scan did not finish due to verify timeout.",
                        },
                    };
                    exceptionHelper.GenerateLog(log);
                    innerHelper.SendErrorMessageToTokenHandler();
                }
            }
            catch (ScriptAbortException)
            {
                // no issue
            }
            catch (Exception ex)
            {
                var log = new Log
                {
                    AffectedItem = scanner.TagElement,
                    AffectedService = "TAG Scan Subprocess",
                    Timestamp = DateTime.Now,
                    ErrorCode = new ErrorCode
                    {
                        ConfigurationItem = scriptName + "Script",
                        ConfigurationType = ErrorCode.ConfigType.Automation,
                        Severity = ErrorCode.SeverityType.Warning,
                        Source = "Run() method - exception",
                    },
                };
                exceptionHelper.ProcessException(ex, log);
                innerHelper.SendErrorMessageToTokenHandler();
                throw;
            }
        }
    }
}