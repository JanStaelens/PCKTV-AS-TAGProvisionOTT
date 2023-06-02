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
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Helper;
    using Newtonsoft.Json;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
    using Skyline.DataMiner.ExceptionHelper;
    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
    using Skyline.DataMiner.Net.Messages.SLDataGateway;
    using Skyline.DataMiner.Net.Sections;

    /// <summary>
    /// DataMiner Script Class.
    /// </summary>
    public class Script
    {
        private DomHelper innerDomHelper;
        private ExceptionHelper exceptionHelper;
        private string scriptName;
        private string channelName;

        /// <summary>
        /// The Script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(Engine engine)
        {
            this.scriptName = "PA_TAG_Monitor Scanners";

            var helper = new PaProfileLoadDomHelper(engine);
            this.innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

            this.exceptionHelper = new ExceptionHelper(engine, this.innerDomHelper);

            engine.GenerateInformation("START " + this.scriptName);

            this.channelName = helper.GetParameterValue<string>("Provision Name (TAG Provision)");
            var tagInstanceId = helper.GetParameterValue<string>("InstanceId (TAG Provision)");

            try
            {
                var action = helper.GetParameterValue<string>("Action (TAG Provision)");

                var scanners = helper.GetParameterValue<List<Guid>>("TAG Scanners (TAG Provision)");
                Dictionary<Guid, bool> scannersComplete = new Dictionary<Guid, bool>();

                bool CheckScanners()
                {
                    try
                    {
                        foreach (var scanner in scanners)
                        {
                            var scannerFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(scanner));
                            var scannerInstance = this.innerDomHelper.DomInstances.Read(scannerFilter).First();

                            if (scannerInstance.StatusId == "active" || scannerInstance.StatusId == "complete")
                            {
                                scannersComplete[scannerInstance.ID.Id] = true;
                            }
                            else
                            {
                                scannersComplete[scannerInstance.ID.Id] = false;
                            }
                        }

                        if (scannersComplete.Count(x => x.Value) == scanners.Count)
                        {
                            return true;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        engine.Log("Exception thrown while verifying the scan subprocess: " + ex);
                        var log = new Log
                        {
                            AffectedItem = this.scriptName,
                            AffectedService = this.channelName,
                            Timestamp = DateTime.Now,
                            ErrorCode = new ErrorCode
                            {
                                ConfigurationItem = this.scriptName + " Script",
                                ConfigurationType = ErrorCode.ConfigType.Automation,
                                Source = "CheckScanners()",
                                Severity = ErrorCode.SeverityType.Critical,
                            },
                        };
                        this.exceptionHelper.ProcessException(ex, log);
                        throw;
                    }
                }

                if (Retry(CheckScanners, new TimeSpan(0, 10, 0)))
                {
                    this.PostActions(engine, helper, tagInstanceId, action, scannersComplete);

                    helper.SendFinishMessageToTokenHandler();
                }
                else
                {
                    var log = new Log
                    {
                        AffectedItem = this.scriptName,
                        AffectedService = this.channelName,
                        Timestamp = DateTime.Now,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = this.scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Code = "PAActivityFailed",
                            Source = "Retry condition",
                            Severity = ErrorCode.SeverityType.Major,
                            Description = "Scanners did not complete in time.",
                        },
                    };
                    this.exceptionHelper.GenerateLog(log);
                }
            }
            catch (Exception ex)
            {
                engine.GenerateInformation($"ERROR in {this.scriptName} " + ex);
                var log = new Log
                {
                    AffectedItem = this.scriptName,
                    AffectedService = this.channelName,
                    Timestamp = DateTime.Now,
                    ErrorCode = new ErrorCode
                    {
                        ConfigurationItem = this.scriptName + " Script",
                        ConfigurationType = ErrorCode.ConfigType.Automation,
                        Source = "Run()",
                        Severity = ErrorCode.SeverityType.Critical,
                    },
                };
                this.exceptionHelper.ProcessException(ex, log);

                helper.Log($"An issue occurred while executing {this.scriptName} activity for {this.channelName}: {ex}", PaLogLevel.Error);
                helper.SendErrorMessageToTokenHandler();
            }
        }

        private void PostActions(Engine engine, PaProfileLoadDomHelper helper, string tagInstanceId, string action, Dictionary<Guid, bool> scannersComplete)
        {
            var scansNotCompleted = scannersComplete.Select(x => !x.Value).ToList().Count;
            var allScannersCount = scannersComplete.Count;

            if (action == "provision" || action == "reprovision" || action == "complete-provision")
            {
                if (scansNotCompleted == allScannersCount)
                {
                    helper.TransitionState("inprogress_to_error");

                    var log = new Log
                    {
                        AffectedItem = this.scriptName,
                        AffectedService = this.channelName,
                        Timestamp = DateTime.Now,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = this.scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Code = "PAErrorState",
                            Source = "PostActions method",
                            Severity = ErrorCode.SeverityType.Major,
                            Description = "All Scans were not provisioned.",
                        },
                    };
                    this.exceptionHelper.GenerateLog(log);
                }
                else if (scansNotCompleted > 0)
                {
                    helper.TransitionState("inprogress_to_activewitherrors");

                    var log = new Log
                    {
                        AffectedItem = this.scriptName,
                        AffectedService = this.channelName,
                        Timestamp = DateTime.Now,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = this.scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Code = "PAActiveWithErrorState",
                            Source = "PostActions method",
                            Severity = ErrorCode.SeverityType.Major,
                            Description = "Some Scans were not provisioned.",
                        },
                    };
                    this.exceptionHelper.GenerateLog(log);
                }
                else
                {
                    helper.TransitionState("inprogress_to_active");
                }
            }
            else if (action == "deactivate")
            {
                if (scansNotCompleted == allScannersCount)
                {
                    helper.TransitionState("inprogress_to_error");

                    var log = new Log
                    {
                        AffectedItem = this.scriptName,
                        AffectedService = this.channelName,
                        Timestamp = DateTime.Now,
                        SummaryFlag = false,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = this.scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Code = "PAErrorState",
                            Source = "PostActions method",
                            Severity = ErrorCode.SeverityType.Major,
                            Description = "All Scans were not deactivated.",
                        },
                    };
                    this.exceptionHelper.GenerateLog(log);
                }
                else if (scansNotCompleted > 0)
                {
                    helper.TransitionState("deactivating_to_activewitherrors");

                    var log = new Log
                    {
                        AffectedItem = this.scriptName,
                        AffectedService = this.channelName,
                        Timestamp = DateTime.Now,
                        SummaryFlag = false,
                        ErrorCode = new ErrorCode
                        {
                            ConfigurationItem = this.scriptName + " Script",
                            ConfigurationType = ErrorCode.ConfigType.Automation,
                            Code = "PAActiveWithErrorState",
                            Source = "PostActions method",
                            Severity = ErrorCode.SeverityType.Major,
                            Description = "Some Scans were not deactivated.",
                        },
                    };
                    this.exceptionHelper.GenerateLog(log);
                }
                else
                {
                    helper.TransitionState("deactivating_to_complete");
                }
            }

            var filter = DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(tagInstanceId)));
            var tagInstances = this.innerDomHelper.DomInstances.Read(filter);
            var tagInstance = tagInstances.First();

            // successfully created filter
            var sourceElement = helper.GetParameterValue<string>("Source Element (TAG Provision)");
            var provisionName = helper.GetParameterValue<string>("Source ID (TAG Provision)");

            if (!string.IsNullOrWhiteSpace(sourceElement))
            {
                ExternalRequest evtmgrUpdate = new ExternalRequest
                {
                    Type = "Process Automation",
                    ProcessResponse = new ProcessResponse
                    {
                        EventName = provisionName,
                        Tag = new TagResponse
                        {
                            Status = tagInstance.StatusId == "active" ? "Active" : "Complete",
                        },
                    },
                };

                var elementSplit = sourceElement.Split('/');
                var eventManager = engine.FindElement(Convert.ToInt32(elementSplit[0]), Convert.ToInt32(elementSplit[1]));
                eventManager.SetParameter(Convert.ToInt32(elementSplit[2]), JsonConvert.SerializeObject(evtmgrUpdate));
            }
        }

        /// <summary>
        /// Retry until success or until timeout.
        /// </summary>
        /// <param name="func">Operation to retry.</param>
        /// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
        /// <returns><c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise <c>false</c>.</returns>
#pragma warning disable SA1204 // Static elements should appear before instance elements
        public static bool Retry(Func<bool> func, TimeSpan timeout)
#pragma warning restore SA1204 // Static elements should appear before instance elements
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
    }
}