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
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

10/01/2023	1.0.0.1		BSM, Skyline	Initial Version

****************************************************************************
*/

namespace Script
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Common;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
	using Skyline.DataMiner.ExceptionHelper;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Sections;

	public class Script
	{
		private DomHelper innerDomHelper;

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
		public void Run(Engine engine)
		{
			engine.SetFlag(RunTimeFlags.NoCheckingSets);

			var scriptName = "Update Properties";
			var tagElementName = "Pre-Code";
			var channelName = "Pre-Code";
			var helper = new PaProfileLoadDomHelper(engine);
			innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");
			var exceptionHelper = new ExceptionHelper(engine, innerDomHelper);

			try
			{
				TagChannelInfo tagInfo = new TagChannelInfo(engine, helper, innerDomHelper);
				channelName = tagInfo.Channel;
				tagElementName = tagInfo.ElementName;
				engine.GenerateInformation("START " + scriptName);

				ExecuteChannelSets(engine, scriptName, helper, exceptionHelper, tagInfo);

				if (tagInfo.Status.Equals("in_progress"))
				{
					helper.TransitionState("inprogress_to_active");
				}
				else
				{
					var log = new Log
					{
						AffectedItem = scriptName,
						AffectedService = channelName,
						Timestamp = DateTime.Now,
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = channelName,
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = scriptName,
							Code = "InvalidStatusForTransition",
							Severity = ErrorCode.SeverityType.Warning,
							Description = $"Cannot execute the transition as the current status is unexpected. Current status: {tagInfo.Status}",
						},
					};

					helper.Log($"Cannot execute the transition as the status. Current status: {tagInfo.ChannelMatch}", PaLogLevel.Error);
					exceptionHelper.GenerateLog(log);
				}

				engine.GenerateInformation("Successfully executed " + scriptName + " for: " + tagElementName);
				helper.ReturnSuccess();
			}
			catch (Exception ex)
			{
				engine.GenerateInformation($"An issue occurred while executing {scriptName} activity for {channelName}: {ex}");
				var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = channelName,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = channelName,
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Source = scriptName,
						Severity = ErrorCode.SeverityType.Critical,
						Description = "Exception while processing " + scriptName,
					},
				};

				exceptionHelper.ProcessException(ex, log);
				helper.Log($"An issue occurred while executing {scriptName} activity for {channelName}: {ex}", PaLogLevel.Error);
				helper.SendErrorMessageToTokenHandler();
			}
		}

		private void ExecuteChannelSets(Engine engine, string scriptName, PaProfileLoadDomHelper helper, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo)
		{
			var filterColumn = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = tagInfo.ChannelMatch, Pid = 8010 };
			var channelRows = tagInfo.ChannelProfileTable.QueryData(new List<ColumnFilter> { filterColumn });
			if (channelRows.Any())
			{
				var row = channelRows.First();
				var key = Convert.ToString(row[0]);
				tagInfo.EngineElement.SetParameterByPrimaryKey(8083, key, tagInfo.MonitoringMode);
				Thread.Sleep(5000);
				tagInfo.EngineElement.SetParameterByPrimaryKey(8054, key, tagInfo.Threshold);
				Thread.Sleep(5000);
				tagInfo.EngineElement.SetParameterByPrimaryKey(8055, key, tagInfo.Notification);
				Thread.Sleep(5000);
				// pending features to set channel KMS/encryption on TAG
				// engineTag.SetParameterByPrimaryKey(356, key, encryption);
				// engineTag.SetParameterByPrimaryKey(356, key, kms);

				UpdateLayouts(engine, scriptName, helper, exceptionHelper, tagInfo);
			}
			else
			{
				var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = tagInfo.ChannelMatch,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = tagInfo.ChannelMatch,
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Source = scriptName,
						Code = "ChannelNotFound",
						Severity = ErrorCode.SeverityType.Warning,
						Description = $"No channels found in channel status with given name: {tagInfo.ChannelMatch}.",
					},
				};

				helper.Log($"No channels found in channel status with given name: {tagInfo.ChannelMatch}.", PaLogLevel.Error);
				engine.GenerateInformation("Did not find any channels with match: " + tagInfo.ChannelMatch);
				exceptionHelper.GenerateLog(log);
			}
		}

		private void UpdateLayouts(Engine engine, string scriptName, PaProfileLoadDomHelper helper, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo)
		{
			foreach (var section in tagInfo.Instance.Sections)
			{
				Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = SetSectionDefinitionById;
				section.Stitch(sectionDefinitionFunc);

				if (!section.GetSectionDefinition().GetName().Equals("Layouts"))
				{
					continue;
				}

				var layoutMatchField = section.FieldValues.First();
				var layout = Convert.ToString(layoutMatchField.Value.Value);
				var index = CheckAndUpdateLayout(engine, scriptName, exceptionHelper, tagInfo, layout);
				if (!String.IsNullOrWhiteSpace(index))
				{
					tagInfo.EngineElement.SetParameterByPrimaryKey(10353, index, tagInfo.ChannelMatch);
				}
			}
		}

		public static string CheckAndUpdateLayout(Engine engine, string scriptName, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo, string layout)
		{
			IEnumerable<object[]> layoutNoneRows = tagInfo.GetLayoutsFromTable(layout);
			if (layoutNoneRows.Any())
			{
				// Get first available layout row (index 0 => primary key which looks like '5/7')
				var minimumIndex = layoutNoneRows.Select(row => Convert.ToInt32(Convert.ToString(row[0]).Split('/')[1])).Min();
				var index = Convert.ToString(layoutNoneRows.First()[0]).Split('/')[0] + "/" + minimumIndex;

				return index;
			}
			else
			{
				var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = tagInfo.ChannelMatch,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = tagInfo.ChannelMatch,
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Source = scriptName,
						Code = "LayoutFull",
						Severity = ErrorCode.SeverityType.Critical,
						Description = $"Did not find any channels with match: " + tagInfo.ChannelMatch,
					},
				};

				engine.GenerateInformation("Did not find any channels with match: " + tagInfo.ChannelMatch);
				exceptionHelper.GenerateLog(log);
			}

			return String.Empty;
		}

		private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
		{
			return innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
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

		public string KMS { get; set; }

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
			ElementName = helper.GetParameterValue<string>("TAG Element");
			Channel = helper.GetParameterValue<string>("Channel Name");
			ChannelMatch = helper.GetParameterValue<string>("Channel Match");

			IDms thisDms = engine.GetDms();
			Element = thisDms.GetElement(ElementName);
			EngineElement = engine.FindElement(Element.Name);
			ChannelProfileTable = Element.GetTable(8000);
			AllLayoutsTable = Element.GetTable(10300);
			ChannelStatusTable = Element.GetTable(240);

			MonitoringMode = helper.GetParameterValue<string>("Monitoring Mode");
			Threshold = helper.GetParameterValue<string>("Threshold");
			Notification = helper.GetParameterValue<string>("Notification");
			Encryption = helper.GetParameterValue<string>("Encryption");
			KMS = helper.GetParameterValue<string>("KMS");

			var instanceId = helper.GetParameterValue<string>("InstanceId");
			Instance = domHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
			Status = Instance.StatusId;

			MonitorUpdate = TagMonitoring.Yes;
			if (Status.Equals("deactivating"))
			{
				MonitorUpdate = TagMonitoring.No;
			}
		}

		public TagChannelInfo()
		{
			// unit test
		}

		public virtual List<object[]> GetLayoutsFromTable(string layout)
		{
			var layoutFilter = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = layout, Pid = 10305 };
			var noneFilter = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = "None", Pid = 10303 };
			var layoutNoneRows = AllLayoutsTable.QueryData(new List<ColumnFilter> { layoutFilter, noneFilter });
			return layoutNoneRows.ToList();
		}
	}
}