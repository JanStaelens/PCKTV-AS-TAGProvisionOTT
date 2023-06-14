/*
****************************************************************************
*  Copyright (c) 2022,  Skyline Communications NV  All Rights Reserved.    *
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

10/01/2023  1.0.0.1     BSM, Skyline    Initial Version

****************************************************************************
*/

namespace Script
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Newtonsoft.Json;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
    using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
    using Skyline.DataMiner.ExceptionHelper;
    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
    using Skyline.DataMiner.Net.Sections;

    internal class Script
    {
        private readonly string scriptName = "PA_TAG_Update Monitoring State";
        private PaProfileLoadDomHelper helper;
        private Engine engine;
        private ExceptionHelper exceptionHelper;
        private string channelName = "Pre-Code";

        /// <summary>
        /// The Script entry point.
        /// </summary>
        /// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
        public void Run(Engine engine)
        {
            this.engine = engine;
            engine.SetFlag(RunTimeFlags.NoCheckingSets);

            var tagElementName = "Pre-Code";
            this.helper = new PaProfileLoadDomHelper(engine);
            var innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");
            this.exceptionHelper = new ExceptionHelper(engine, innerDomHelper);

            try
            {
                TagChannelInfo tagInfo = new TagChannelInfo(engine, this.helper, innerDomHelper);
                this.channelName = tagInfo.Channel;
                tagElementName = tagInfo.ElementName;
                engine.GenerateInformation("START " + this.scriptName);

                var filterColumn = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = tagInfo.ChannelMatch, Pid = 248 };
                var channelStatusRows = tagInfo.ChannelStatusTable.QueryData(new List<ColumnFilter> { filterColumn });
                if (channelStatusRows.Any())
                {
                    foreach (var row in channelStatusRows)
                    {
                        tagInfo.EngineElement.SetParameterByPrimaryKey(356, Convert.ToString(row[0]), (int)tagInfo.MonitorUpdate);
                    }
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
                            Source = "Channel Status condition",
                            Code = "ChannelNotFound",
                            Severity = ErrorCode.SeverityType.Warning,
                            Description = $"No channels found in Channel Status Overview Table.",
                        },
                    };

                    this.helper.Log($"No channels found in channel status with given name: {this.channelName}.", PaLogLevel.Error);
                    engine.GenerateInformation("Did not find any channels with match: " + tagInfo.ChannelMatch);
                    this.exceptionHelper.GenerateLog(log);
                }

                var missingChannelsData = new List<string>();
                bool VerifyMonitoredChannels()
                {
                    missingChannelsData = new List<string>();
                    var finishedChannels = 0;
                    var totalChannels = channelStatusRows.Count();
                    if (channelStatusRows.Any())
                    {
                        foreach (var row in channelStatusRows)
                        {
                            var ismonitored = Convert.ToInt32(row[14 /*Monitored*/]) == (int)tagInfo.MonitorUpdate;
                            var isresponseDataFilled = !string.IsNullOrWhiteSpace(Convert.ToString(row[27 /*ResponseData*/]));
                            if (tagInfo.Status.Equals("deactivating"))
                            {
                                isresponseDataFilled = true;
                            }

                            if (ismonitored && isresponseDataFilled)
                            {
                                finishedChannels++;
                            }
                            else
                            {
                                missingChannelsData.Add(Convert.ToString(row[12 /*Name*/]));
                            }
                        }

                        return finishedChannels == totalChannels;
                    }
                    else
                    {
                        engine.Log("No monitored channels to evaluate");
                        return true;
                    }
                }

                if (Retry(VerifyMonitoredChannels, new TimeSpan(0, 3, 0)))
                {
                    this.ExecuteDoneTransition(tagInfo.Status, tagElementName);
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
                            Source = "Retry condition",
                            Code = "RetryTimeout",
                            Severity = ErrorCode.SeverityType.Warning,
                            Description = $"Monitor Channel did not finish due to timeout. Must be needed both values (Monitored and ResponseData) to execute next activity (channel sets).\n Missing channels to finish: {JsonConvert.SerializeObject(missingChannelsData)}",
                        },
                    };

                    this.helper.Log($"Monitor Channel did not finish due to timeout. Must be needed both values (Monitored and ResponseData) to execute next activity (channel sets).\n Missing channels to finish: {JsonConvert.SerializeObject(missingChannelsData)}", PaLogLevel.Error);
                    this.exceptionHelper.GenerateLog(log);

                    this.ExecuteErrorTransition(tagInfo.Status);
                }

                this.helper.ReturnSuccess();
            }
            catch (Exception ex)
            {
                engine.GenerateInformation($"An issue occurred while executing {this.scriptName} activity for {this.channelName}: {ex}");
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
                        Description = "Exception while processing " + this.scriptName,
                    },
                };

                this.exceptionHelper.ProcessException(ex, log);
                this.helper.Log($"An issue occurred while executing {this.scriptName} activity for {this.channelName}: {ex}", PaLogLevel.Error);
                this.helper.SendErrorMessageToTokenHandler();
            }
        }

        private void ExecuteErrorTransition(string status)
        {
            if (status.Equals("deactivating"))
            {
                this.helper.TransitionState("deactivating_to_error");
            }
            else if (status.Equals("ready"))
            {
                this.helper.TransitionState("ready_to_inprogress");
                this.helper.TransitionState("inprogress_to_error");
            }
            else if (status.Equals("in_progress"))
            {
                this.helper.TransitionState("inprogress_to_error");
            }
        }

        private void ExecuteDoneTransition(string status, string tagElementName)
        {
            if (status.Equals("deactivating"))
            {
                this.helper.TransitionState("deactivating_to_complete");
                this.engine.GenerateInformation("Successfully executed " + this.scriptName + " for: " + tagElementName);
                this.helper.SendFinishMessageToTokenHandler();
                return;
            }
            else if (status.Equals("ready"))
            {
                this.helper.TransitionState("ready_to_inprogress");
            }
            else if (status.Equals("in_progress"))
            {
                // no update
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
                        Source = "Status transition condition",
                        Code = "InvalidStatusForTransition",
                        Severity = ErrorCode.SeverityType.Warning,
                        Description = $"Cannot execute the transition as the current status is unexpected.",
                    },
                };

                this.helper.Log($"Cannot execute the transition as the status. Current status: {status}", PaLogLevel.Error);
                this.exceptionHelper.GenerateLog(log);
            }

            this.engine.GenerateInformation("Successfully executed " + this.scriptName + " for: " + tagElementName);
        }

        /// <summary>
        /// Retry until success or until timeout.
        /// </summary>
        /// <param name="func">Operation to retry.</param>
        /// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
        /// <returns><c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise <c>false</c>.</returns>
        public static bool Retry(Func<bool> func, TimeSpan timeout)
        {
            bool success = false;

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

    public class TagChannelInfo
    {
        public string ElementName { get; set; }

        public string Channel { get; set; }

        public string ChannelMatch { get; set; }

        public string Threshold { get; set; }

        public string MonitoringMode { get; set; }

        public string Notification { get; set; }

        public string Encryption { get; set; }

        public string Kms { get; set; }

        public DomInstance Instance { get; set; }

        public Element EngineElement { get; set; }

        public IDmsElement Element { get; set; }

        public IDmsTable ChannelProfileTable { get; set; }

        public IDmsTable ChannelStatusTable { get; set; }

        public IDmsTable AllLayoutsTable { get; set; }

        public TagMonitoring MonitorUpdate { get; set; }

        public string Status { get; set; }

        public enum TagMonitoring
        {
            No = 0,
            Yes = 1,
        }

        public TagChannelInfo(Engine engine, PaProfileLoadDomHelper helper, DomHelper domHelper)
        {
            this.ElementName = helper.GetParameterValue<string>("TAG Element (TAG Channel)");
            this.Channel = helper.GetParameterValue<string>("Channel Name (TAG Channel)");
            this.ChannelMatch = helper.GetParameterValue<string>("Channel Match (TAG Channel)");

            IDms thisDms = engine.GetDms();
            this.Element = thisDms.GetElement(this.ElementName);
            this.EngineElement = engine.FindElement(this.Element.Name);
            this.ChannelProfileTable = this.Element.GetTable(8000);
            this.AllLayoutsTable = this.Element.GetTable(10300);
            this.ChannelStatusTable = this.Element.GetTable(240);

            this.MonitoringMode = helper.GetParameterValue<string>("Monitoring Mode (TAG Channel)");
            this.Threshold = helper.GetParameterValue<string>("Threshold (TAG Channel)");
            this.Notification = helper.GetParameterValue<string>("Notification (TAG Channel)");
            this.Encryption = helper.GetParameterValue<string>("Encryption (TAG Channel)");
            this.Kms = helper.GetParameterValue<string>("KMS (TAG Channel)");

            var instanceId = helper.GetParameterValue<string>("InstanceId (TAG Channel)");
            this.Instance = domHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
            this.Status = this.Instance.StatusId;

            this.MonitorUpdate = TagMonitoring.Yes;
            if (this.Status.Equals("deactivating"))
            {
                this.MonitorUpdate = TagMonitoring.No;
            }
        }
    }
}