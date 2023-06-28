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

			var status = String.Empty;
			var channelMatch = String.Empty;

			try
			{
				TagChannelInfo tagInfo = new TagChannelInfo(engine, this.helper, innerDomHelper);
				status = tagInfo.Status;
				channelMatch = tagInfo.ChannelMatch;
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
					SharedMethods.TransitionToError(helper, status);
					engine.GenerateInformation("Did not find any channels with match: " + tagInfo.ChannelMatch);
					var log = new Log
					{
						AffectedItem = channelMatch,
						AffectedService = this.channelName,
						Timestamp = DateTime.Now,
						LogNotes = $"{channelMatch} not found in the Channel Status table in {tagInfo.ElementName}",
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = this.scriptName + " Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = "Channel Status condition",
							Code = "ChannelNotFound",
							Severity = ErrorCode.SeverityType.Warning,
							Description = $"No matching channels found in Channel Status Overview Table.",
						},
					};

					this.exceptionHelper.GenerateLog(log);
					this.helper.SendFinishMessageToTokenHandler();
					return;
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

				if (SharedMethods.Retry(VerifyMonitoredChannels, new TimeSpan(0, 3, 0)))
				{
					this.ExecuteDoneTransition(tagInfo.Status, tagElementName, channelMatch);
				}
				else
				{
					SharedMethods.TransitionToError(this.helper, status);
					var log = new Log
					{
						AffectedItem = channelMatch,
						AffectedService = this.channelName,
						Timestamp = DateTime.Now,
						LogNotes = $"Missing channels to finish: {JsonConvert.SerializeObject(missingChannelsData)}",
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = this.scriptName + " Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = "Retry condition",
							Code = "RetryTimeout",
							Severity = ErrorCode.SeverityType.Warning,
							Description = $"Monitor Channel did not finish due to timeout. Must be needed both values (Monitored and ResponseData) to execute next activity (channel sets).",
						},
					};

					this.exceptionHelper.GenerateLog(log);
				}

				this.helper.ReturnSuccess();
			}
			catch (Exception ex)
			{
				engine.GenerateInformation($"An issue occurred while executing {this.scriptName} activity for {this.channelName}: {ex}");
				SharedMethods.TransitionToError(this.helper, status);
				var log = new Log
				{
					AffectedItem = channelMatch,
					AffectedService = this.channelName,
					Timestamp = DateTime.Now,
					LogNotes = ex.ToString(),
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
				this.helper.SendFinishMessageToTokenHandler();
			}
		}

		private void ExecuteDoneTransition(string status, string tagElementName, string channelMatch)
		{
			if (status.Equals("deactivating"))
			{
				this.helper.TransitionState("deactivating_to_complete");
				this.engine.GenerateInformation("Successfully executed " + this.scriptName + " for: " + tagElementName);
				this.helper.SendFinishMessageToTokenHandler();
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
				SharedMethods.TransitionToError(this.helper, status);
				var log = new Log
				{
					AffectedItem = channelMatch,
					AffectedService = this.channelName,
					Timestamp = DateTime.Now,
					LogNotes = $"Expected deactivating, ready, or in_progress statuses to transition, but current status is: {status}.",
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

				this.exceptionHelper.GenerateLog(log);
			}
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