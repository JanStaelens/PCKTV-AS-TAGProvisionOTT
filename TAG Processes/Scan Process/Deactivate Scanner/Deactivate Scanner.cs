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
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
	using Skyline.DataMiner.ExceptionHelper;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Messages.SLDataGateway;
	using Skyline.DataMiner.Net.Sections;
	using TagHelperMethods;

	/// <summary>
	/// DataMiner Script Class.
	/// </summary>
	public class Script
	{
		private readonly int scanChannelsTable = 1310;
		private DomHelper innerDomHelper;
		private SharedMethods sharedMethods;

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
		public void Run(Engine engine)
		{
			var scriptName = "PA_TAG_Deactivate Scanner";
			var scanName = String.Empty;

			var helper = new PaProfileLoadDomHelper(engine);
			this.innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

			var exceptionHelper = new ExceptionHelper(engine, this.innerDomHelper);
			this.sharedMethods = new SharedMethods(helper, this.innerDomHelper);
			var instanceId = helper.GetParameterValue<string>("InstanceId (TAG Scan)");

			try
			{
				engine.GenerateInformation("START " + scriptName);

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
				var instanceFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)));
				var scanInstances = this.innerDomHelper.DomInstances.Read(instanceFilter);
				if (!scanInstances.Any())
				{
					engine.GenerateInformation("No TAG Scan Instance found with instanceId: " + instanceId);
					helper.ReturnSuccess();
					return;
				}

				var instance = scanInstances.First();

				var status = instance.StatusId;

				if (!status.Equals("deactivate") && !status.Equals("reprovision"))
				{
					helper.ReturnSuccess();
					return;
				}

				if (status.Equals("deactivate"))
				{
					helper.TransitionState("deactivate_to_deactivating");

					// need to get instance again after a transition is executed
					instance = this.innerDomHelper.DomInstances.Read(instanceFilter).First();
					status = instance.StatusId;
				}

				IDmsElement element;
				List<Scan> scanList;
				DeactivateScans(engine, scanner, instance, status, out element, out scanList);

				bool VerifyScanDeleted()
				{
					try
					{
						var scanRequests = scanList;
						var requestTitles = this.GetScanRequestTitles(scanRequests);

						object[][] scanChannelsRows = null;
						var scanChannelTable = element.GetTable(this.scanChannelsTable);
						scanChannelsRows = scanChannelTable.GetRows();

						var scanCompleted = scanChannelsRows == null || this.CheckScanDelete(requestTitles, scanChannelsRows);

						var channelsUpdated = this.ValidateChannelsStatus(scanner.Channels, status);

						return scanCompleted && channelsUpdated;
					}
					catch (Exception e)
					{
						engine.Log("Exception thrown while checking TAG Scan status: " + e);
						throw;
					}
				}

				if (this.sharedMethods.Retry(VerifyScanDeleted, new TimeSpan(0, 5, 0)))
				{
					// successfully deleted
					helper.Log($"Scanner {scanner.ScanName} Deactivated", PaLogLevel.Information);
					PostActions(scriptName, helper, exceptionHelper, scanner, status);
				}
				else
				{
					// failed to execute in time
					engine.GenerateInformation("Failed to verify the scan was deleted in time");
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
							Code = "ActivityNotFinished",
							Source = "Retry condition",
							Description = "Failed to deactivate the scanners within the timeout time.",
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
				engine.GenerateInformation("Exception caught in Deactivate Scanner: " + ex);
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

		private void DeactivateScans(Engine engine, Scanner scanner, DomInstance instance, string status, out IDmsElement element, out List<Scan> scanList)
		{
			IDms dms = engine.GetDms();
			element = dms.GetElement(scanner.TagElement);
			var tagDictionary = new Dictionary<string, TagRequest>();

			var tagRequest = new TagRequest();
			scanList = this.CreateScanRequestJson(instance, scanner);
			tagRequest.ScanRequests = scanList;
			tagDictionary.Add(scanner.TagDevice, tagRequest);

			element.GetStandaloneParameter<string>(3).SetValue(JsonConvert.SerializeObject(tagDictionary));

			this.ExecuteChannelsTransition(scanner.Channels, status);
		}

		private static void PostActions(string scriptName, PaProfileLoadDomHelper helper, ExceptionHelper exceptionHelper, Scanner scanner, string status)
		{
			if (status == "deactivating")
			{
				helper.TransitionState("deactivating_to_complete");
				helper.SendFinishMessageToTokenHandler();
			}
			else if (scanner.Action == "reprovision")
			{
				helper.TransitionState("reprovision_to_inprogress");
				helper.ReturnSuccess();
			}
			else
			{
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
						Code = "InvalidStatusForTransition",
						Source = "Status Transition condition",
						Description = $"Failed to execute transition status. Current status: {status}",
					},
				};
				exceptionHelper.GenerateLog(log);
				helper.SendErrorMessageToTokenHandler();
			}
		}

		private bool ValidateChannelsStatus(List<Guid> channels, string status)
		{
			var expectedChannels = channels.Count;
			var updatedChannels = 0;

			var newStatus = status.Equals("deactivating") ? "complete" : "draft";

			foreach (var channel in channels)
			{
				var subFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(channel));
				var subInstance = this.innerDomHelper.DomInstances.Read(subFilter).First();

				subInstance.Stitch(this.SetSectionDefinitionById, this.SetDomDefinitionById);
				if (subInstance.StatusId == newStatus)
				{
					updatedChannels++;
				}

				if (updatedChannels == expectedChannels)
				{
					return true;
				}
			}

			return false;
		}

		private void ExecuteChannelsTransition(List<Guid> channels, string status)
		{
			var transition = status.Equals("deactivating") ? "active_to_complete" : "active_to_draft";

			foreach (var channel in channels)
			{
				var subFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(channel));
				var subInstance = this.innerDomHelper.DomInstances.Read(subFilter).First();

				this.innerDomHelper.DomInstances.DoStatusTransition(subInstance.ID, transition);
			}
		}

		private bool CheckScanDelete(List<string> titles, object[][] scanChannelsRows)
		{
			foreach (var row in scanChannelsRows)
			{
				if (titles.Contains(Convert.ToString(row[13 /*Title*/])))
				{
					return false;
				}
			}

			return true;
		}

		private List<string> GetScanRequestTitles(List<Scan> scanRequests)
		{
			return scanRequests.Select(x => x.Name).ToList();
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
					Action = (int)TagRequest.TAGAction.Delete,
					AssetId = scanner.AssetId,
					Interface = scanner.TagInterface,
					Name = String.Format(nameFormat, scanner.ScanName, manifest.Name),
					Type = scanner.ScanType,
					Url = manifest.Url,
				});
			}

			return scans;
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