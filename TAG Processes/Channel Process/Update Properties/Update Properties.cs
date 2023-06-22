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
	using TagHelperMethods;

	public class Script
	{
		private DomHelper innerDomHelper;
		private string channelName = "Pre-Code";

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
		public void Run(Engine engine)
		{
			engine.SetFlag(RunTimeFlags.NoCheckingSets);

			var scriptName = "PA_TAG_Update Properties";
			var tagElementName = "Pre-Code";
			var helper = new PaProfileLoadDomHelper(engine);
			this.innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");
			var exceptionHelper = new ExceptionHelper(engine, this.innerDomHelper);

			var status = String.Empty;

			try
			{
				TagChannelInfo tagInfo = new TagChannelInfo(engine, helper, this.innerDomHelper);
				status = tagInfo.Status;
				channelName = tagInfo.Channel;
				tagElementName = tagInfo.ElementName;
				engine.GenerateInformation("START " + scriptName);

				var successLog = this.ExecuteChannelSets(engine, scriptName, helper, exceptionHelper, tagInfo);
				engine.GenerateInformation($"sets report on {tagInfo.ChannelMatch}: " + successLog);
				if (!successLog.Contains("False"))
				{
					helper.TransitionState("inprogress_to_active");
					engine.GenerateInformation("Successfully executed " + scriptName + " for: " + tagElementName);
				}
				else
				{
					SharedMethods.TransitionToError(helper, status);

					var log = new Log
					{
						AffectedItem = scriptName,
						AffectedService = channelName,
						Timestamp = DateTime.Now,
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = scriptName + " Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = "Run()",
							Code = "FailedChannelSets",
							Severity = ErrorCode.SeverityType.Warning,
							Description = $"Failed to update some channel properties. Log: {successLog}",
						},
					};

					engine.GenerateInformation($"Failed to update some channel properties. Log: {successLog}");
					exceptionHelper.GenerateLog(log);
				}

				helper.ReturnSuccess();
			}
			catch (Exception ex)
			{
				engine.GenerateInformation($"An issue occurred while executing {scriptName} activity for {channelName}: {ex}");
				SharedMethods.TransitionToError(helper, status);
				var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = channelName,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = scriptName + " Script",
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Source = "Run()",
						Severity = ErrorCode.SeverityType.Critical,
						Description = "Exception while processing " + scriptName,
					},
				};

				exceptionHelper.ProcessException(ex, log);
				helper.Log($"An issue occurred while executing {scriptName} activity for {channelName}: {ex}", PaLogLevel.Error);
				helper.SendFinishMessageToTokenHandler();
			}
		}

		private string ExecuteChannelSets(Engine engine, string scriptName, PaProfileLoadDomHelper helper, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo)
		{
			var filterColumn = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = tagInfo.ChannelMatch, Pid = 8010 };
			var channelRows = tagInfo.ChannelProfileTable.QueryData(new List<ColumnFilter> { filterColumn });
			if (channelRows.Any())
			{
				var row = channelRows.First();
				var key = Convert.ToString(row[0]);
				
				tagInfo.ThresholdSetSuccess = tagInfo.TryChannelSetRead(engine, 8004, tagInfo.Threshold, key);
				tagInfo.NotificationSetSuccess = tagInfo.TryChannelSetRead(engine, 8005, tagInfo.Notification, key);
				tagInfo.EncryptionSetSuccess = String.IsNullOrWhiteSpace(tagInfo.Encryption) ? true : tagInfo.TryChannelSetRead(engine, 8018, Convert.ToString(tagInfo.EncryptionValue[tagInfo.Encryption]), key);
				tagInfo.KmsSetSuccess = tagInfo.TryChannelSetRead(engine, 8034, tagInfo.KMS, key);
				tagInfo.MonitoringSetSuccess = tagInfo.TryChannelSet(engine, 8083, Convert.ToString(tagInfo.MonitoringValue[tagInfo.MonitoringMode]), key);

				tagInfo.LayoutSetSuccuess = UpdateLayouts(engine, scriptName, helper, exceptionHelper, tagInfo);

				return $"Successful sets - Monitoring: {tagInfo.MonitoringSetSuccess}|Threshold: {tagInfo.ThresholdSetSuccess}|Notification: {tagInfo.NotificationSetSuccess}|Encryption: {tagInfo.EncryptionSetSuccess}|KMS: {tagInfo.KmsSetSuccess}";
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
						ConfigurationItem = scriptName + " Script",
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Source = "ExecuteChannelSets()",
						Code = "ChannelNotFound",
						Severity = ErrorCode.SeverityType.Warning,
						Description = $"No channels found in All Channel Profiles table.",
					},
				};

				helper.Log($"No channels found in channel status with given name: {tagInfo.ChannelMatch}.", PaLogLevel.Error);
				engine.GenerateInformation("Did not find any channels with match: " + tagInfo.ChannelMatch);
				exceptionHelper.GenerateLog(log);

				return "No channels found: False";
			}
		}

		private bool UpdateLayouts(Engine engine, string scriptName, PaProfileLoadDomHelper helper, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo)
		{
			foreach (var section in tagInfo.Instance.Sections)
			{
				string layout = "Empty";
				try
				{
					Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = this.SetSectionDefinitionById;
					section.Stitch(sectionDefinitionFunc);

					if (!section.GetSectionDefinition().GetName().Equals("Layouts"))
					{
						continue;
					}

					var layoutSectionFields = section.GetSectionDefinition().GetAllFieldDescriptors();

					var layoutMatchField = layoutSectionFields.First(x => x.Name.Contains("Match"));
					var layoutPostionField = layoutSectionFields.First(x => x.Name.Contains("Position"));

					layout = Convert.ToString(section.GetFieldValueById(layoutMatchField.ID).Value.Value);
					var position = Convert.ToString(section.GetFieldValueById(layoutPostionField.ID).Value.Value);
					engine.GenerateInformation("position: " + position);
					if (!String.IsNullOrWhiteSpace(position))
					{
						tagInfo.EngineElement.SetParameterByPrimaryKey(10353, position, tagInfo.ChannelMatch);
					}
					else
					{
						if (String.IsNullOrWhiteSpace(layout))
						{
							return true;
						}

						// layout position wasn't set, so we're using any position in the multiviewer (first available)

						// error log because position wasn't set (but also should be generated on the scan)

						// old code not needed, as space needs to be made for all channels
						// var index = CheckLayoutIndexes(engine, scriptName, exceptionHelper, tagInfo, layout);
						// if (!String.IsNullOrWhiteSpace(index))
						// {
						// 	tagInfo.EngineElement.SetParameterByPrimaryKey(10353, index, tagInfo.ChannelMatch);
						// 	return true;
						// }
					}
				}
				catch (Exception ex)
				{
					engine.GenerateInformation($"Failed to set channel {tagInfo.ChannelMatch} on {layout} layout: " + ex);
					try
					{
						var log = new Log
						{
							AffectedItem = scriptName,
							AffectedService = channelName,
							Timestamp = DateTime.Now,
							ErrorCode = new ErrorCode
							{
								ConfigurationItem = scriptName + " Script",
								ConfigurationType = ErrorCode.ConfigType.Automation,
								Source = "UpdateLayouts()",
								Code = "LayoutNotFound",
								Severity = ErrorCode.SeverityType.Warning,
								Description = $"Failed to set channel on layout.",
							},
						};

						exceptionHelper.GenerateLog(log);
					}
					catch (Exception e)
					{
						engine.Log("QA|failed to generate exception log DOM: " + e);
					}
				}
			}

			return false;
		}

		public string CheckLayoutIndexes(Engine engine, string scriptName, ExceptionHelper exceptionHelper, TagChannelInfo tagInfo, string layout)
		{
			if (String.IsNullOrWhiteSpace(layout))
			{
				return String.Empty;
			}

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
				engine.GenerateInformation("No layouts found to set");
				try
				{
					var log = new Log
					{
						AffectedItem = scriptName,
						AffectedService = channelName,
						Timestamp = DateTime.Now,
						ErrorCode = new ErrorCode
						{
							ConfigurationItem = scriptName + " Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = "CheckLayoutIndexes()",
							Code = "LayoutNotFound",
							Severity = ErrorCode.SeverityType.Warning,
							Description = $"No layouts found to set.",
						},
					};

					exceptionHelper.GenerateLog(log);
				}
				catch (Exception e)
				{
					engine.Log("QA|failed to generate exception log DOM (LayoutNotFound): " + e);
				}
			}

			return String.Empty;
		}

		private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
		{
			return this.innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
		}
	}

	public class TagChannelInfo
	{

		public Dictionary<string, int> EncryptionValue = new Dictionary<string, int>
		{
			{ "None", 0 },
			{ "Axinom, CENC", 196619 },
			{ "BISS-2, AES-128-CBC", 65545 },
			{ "BuyDRM, CENC", 196622 },
			{ "CPIX, CENC", 196616 },
			{ "Generic, AES-128-CBC", 65540 },
			{ "HuaweiPlayReady, AES-128-CTR", 131074 },
			{ "Irdeto, AES-128-CBC", 65543 },
			{ "Irdeto, CENC", 196615 },
			{ "KalturaUDRM, CENC", 196621 },
			{ "Simulcrypt, AES-128-CBC", 65537 },
			{ "Simulcrypt, AES-128-ECB", 524289 },
			{ "Simulcrypt, DVB-CSA", 262145 },
			{ "SKY CKS, CENC", 196614 },
			{ "Static, CENC", 196620 },
			{ "SynMedia, CENC", 196618 },
			{ "VerimatrixMultiRights, CENC", 131077 },
			{ "Verimatrix, AES-128-CBC", 65539 },
		};

		public Dictionary<string, int> MonitoringValue = new Dictionary<string, int>
		{
			{ "Full", 1 },
			{ "Light", 5 },
			{ "ExtraLight", 10 },
		};

		public string ElementName { get; set; }

		public string Channel { get; set; }

		public string ChannelMatch { get; set; }

		public string Threshold { get; set; }

		public bool ThresholdSetSuccess { get; set; }

		public string MonitoringMode { get; set; }

		public bool MonitoringSetSuccess { get; set; }

		public string Notification { get; set; }

		public bool NotificationSetSuccess { get; set; }

		public string Encryption { get; set; }

		public bool EncryptionSetSuccess { get; set; }

		public string KMS { get; set; }

		public bool KmsSetSuccess { get; set; }

		public bool LayoutSetSuccuess { get; set; }

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
			this.KMS = helper.GetParameterValue<string>("KMS (TAG Channel)");

			var instanceId = helper.GetParameterValue<string>("InstanceId (TAG Channel)");
			this.Instance = domHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
			this.Status = this.Instance.StatusId;

			this.MonitorUpdate = TagMonitoring.Yes;
			if (this.Status.Equals("deactivating"))
			{
				this.MonitorUpdate = TagMonitoring.No;
			}
		}

		public TagChannelInfo()
		{
			// necessary for unit test
		}

		public virtual List<object[]> GetLayoutsFromTable(string layout)
		{
			var layoutFilter = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = layout, Pid = 10305 };
			var zeroFilter = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = "0", Pid = 10302 };
			var layoutNoneRows = this.AllLayoutsTable.QueryData(new List<ColumnFilter> { layoutFilter, zeroFilter });
			return layoutNoneRows.ToList();
		}

		public bool TryChannelSet(Engine engine, int columnPid, string updatedValue, string key)
		{
			try
			{
				if (String.IsNullOrWhiteSpace(updatedValue)/* || Convert.ToString(EngineElement.GetParameterByPrimaryKey(columnPid - 50, key)) == updatedValue*/)
				{
					return true;
				}

				EngineElement.SetParameterByPrimaryKey(columnPid, key, updatedValue);

				bool VerifySet()
				{
					try
					{
						var valueToCheck = Convert.ToString(EngineElement.GetParameterByPrimaryKey(columnPid - 50, key));

						if (valueToCheck == updatedValue)
						{
							return true;
						}

						return false;
					}
					catch (Exception e)
					{
						engine.GenerateInformation($"Exception checking channel set: {updatedValue}" + e);
						throw;
					}
				}

				if (SharedMethods.Retry(VerifySet, new TimeSpan(0, 1, 0)))
				{
					return true;
				}
			}
			catch (Exception e)
			{
				engine.GenerateInformation($"Failed to perform set: {updatedValue} on {ChannelMatch}: " + e);
			}

			return false;
		}

		public bool TryChannelSetRead(Engine engine, int columnPid, string updatedValue, string key)
		{
			try
			{
				if (String.IsNullOrWhiteSpace(updatedValue) || Convert.ToString(EngineElement.GetParameterByPrimaryKey(columnPid, key)) == updatedValue)
				{
					return true;
				}

				EngineElement.SetParameterByPrimaryKey(columnPid, key, updatedValue);

				return true;
			}
			catch (Exception e)
			{
				engine.GenerateInformation($"Failed to perform set: {updatedValue} on {ChannelMatch}: " + e);
			}

			return false;
		}
	}
}