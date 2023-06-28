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

namespace TagHelperMethods
{
	using Skyline.DataMiner.Automation;
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

	[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "ignored")]
	public class SharedMethods
	{
		private readonly PaProfileLoadDomHelper innerHelper;
		private readonly DomHelper innerDomHelper;
		private readonly Engine engine;

		public SharedMethods(Engine engine, PaProfileLoadDomHelper helper, DomHelper domHelper)
		{
			this.engine = engine;
			this.innerHelper = helper;
			this.innerDomHelper = domHelper;
		}

		/// <summary>
		/// Retry until success or until timeout.
		/// </summary>
		/// <param name="func">Operation to retry.</param>
		/// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
		/// <returns><c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise <c>false</c>.</returns>
		public static bool Retry(Func<bool> func, TimeSpan timeout)
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

		public Dictionary<Guid, string> GetScanNames(List<Guid> scanners)
		{
			var scanNamesById = new Dictionary<Guid, string>();
			foreach (var scanner in scanners)
			{
				var scannerFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(scanner));
				var scannerInstances = innerDomHelper.DomInstances.Read(scannerFilter);
				if (scannerInstances.Count > 0)
				{
					// scan not found
					continue;
				}

				var scannerInstance = scannerInstances.First();
				foreach (var section in scannerInstance.Sections)
				{
					Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = SetSectionDefinitionById;
					section.Stitch(sectionDefinitionFunc);

					var sectionDefinition = section.GetSectionDefinition();
					if (!sectionDefinition.GetName().Equals("Scanner"))
					{
						continue;
					}

					var fields = sectionDefinition.GetAllFieldDescriptors();
					var scanName = section.GetFieldValueById(fields.First(x => x.Name.Contains("Scan Name")).ID);
					var scanElement = section.GetFieldValueById(fields.First(x => x.Name.Contains("TAG Element")).ID);
					var scanFullName = $"{scanName.Value.Value}/{scanElement.Value.Value}";

					scanNamesById[scanner] = scanFullName;
				}
			}

			return scanNamesById;
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
				var action = subInstance.StatusId.Equals("draft") ? "provision" : scanner.Action;
				this.innerDomHelper.DomInstances.ExecuteAction(subInstance.ID, actionPrefix + action);
			}
		}

		private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
		{
			return this.innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
		}
	}
}