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
	using System.Linq;
	using System.Threading;
	using Newtonsoft.Json;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Actions;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Buttons;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Concatenation;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Conditions;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Status;
	using Skyline.DataMiner.Net.Apps.Sections.SectionDefinitions;
	using Skyline.DataMiner.Net.GenericEnums;
	using Skyline.DataMiner.Net.Messages.SLDataGateway;
	using Skyline.DataMiner.Net.Sections;

	internal class Script
	{
		private const string ModuleId = "process_automation";
		private static Engine internalEngine;
		private DomHelper domHelper;

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
		public void Run(Engine engine)
		{
			internalEngine = engine;
			var scriptName = "Create TAG DOM";
			engine.GenerateInformation("START " + scriptName);
			try
			{
				domHelper = new DomHelper(engine.SendSLNetMessages, ModuleId);

				var channelDomDefinition = CreateTagChannelDomDefinition();
				CreateOrUpdateDomDefinition(channelDomDefinition);
				engine.GenerateInformation("TAG Channel DOM definition created");
				Thread.Sleep(2000);

				var scanDomDefinition = CreateTagScanDomDefinition();
				CreateOrUpdateDomDefinition(scanDomDefinition);
				engine.GenerateInformation("TAG Scan DOM definition created");
				Thread.Sleep(2000);

				var provisionDomDefinition = CreateTagProvisionDomDefinition();
				CreateOrUpdateDomDefinition(provisionDomDefinition);
				engine.GenerateInformation("TAG DOM definition created");
			}
			catch (Exception ex)
			{
				engine.Log(scriptName + $"|Failed to create the TAG DOM due to exception: " + ex);
			}
		}

		private void CreateOrUpdateDomDefinition(DomDefinition newDomDefinition)
		{
			if (newDomDefinition != null)
			{
				var domDefinition = domHelper.DomDefinitions.Read(DomDefinitionExposers.Name.Equal(newDomDefinition.Name));
				if (domDefinition.Any())
				{
					newDomDefinition.ID = domDefinition.FirstOrDefault()?.ID;
					domHelper.DomDefinitions.Update(newDomDefinition);
				}
				else
				{
					domHelper.DomDefinitions.Create(newDomDefinition);
				}
			}
		}

		private void CreateOrUpdateDomBehaviorDefinition(DomBehaviorDefinition newDomBehaviorDefinition)
		{
			if (newDomBehaviorDefinition != null)
			{
				var domBehaviorDefinition = domHelper.DomBehaviorDefinitions.Read(DomBehaviorDefinitionExposers.Name.Equal(newDomBehaviorDefinition.Name));
				if (domBehaviorDefinition.Any())
				{
					newDomBehaviorDefinition.ID = domBehaviorDefinition.FirstOrDefault()?.ID;
					domHelper.DomBehaviorDefinitions.Update(newDomBehaviorDefinition);
				}
				else
				{
					domHelper.DomBehaviorDefinitions.Create(newDomBehaviorDefinition);
				}
			}
		}

		private DomDefinition CreateTagProvisionDomDefinition()
		{
			// Create SectionDefinitions
			var nameDescriptor = new FieldDescriptorID();
			var configSectionDefinition = SectionDefinitions.CreateConfigSection(domHelper, ref nameDescriptor);

			var sections = new List<SectionDefinition> { configSectionDefinition };

			// Create DomBehaviorDefinition
			var behaviorName = "TAG Behavior";
			var domBehaviorDefinition = BehaviorDefinitions.CreateTagProvisionBehaviorDefinition(sections, behaviorName);
			CreateOrUpdateDomBehaviorDefinition(domBehaviorDefinition);

			var sectionLink = new SectionDefinitionLink(configSectionDefinition.GetID())
			{
				AllowMultipleSections = false,
			};

			var nameDefinition = new ModuleSettingsOverrides
			{
				NameDefinition = new DomInstanceNameDefinition
				{
					ConcatenationItems = new List<IDomInstanceConcatenationItem>
					{
						new FieldValueConcatenationItem
						{
							FieldDescriptorId = nameDescriptor,
						},
					},
				},
			};

			return new DomDefinition
			{
				Name = "TAG",
				SectionDefinitionLinks = new List<SectionDefinitionLink> { sectionLink },
				DomBehaviorDefinitionId = domBehaviorDefinition.ID,
				ModuleSettingsOverrides = nameDefinition,
			};
		}

		private DomDefinition CreateTagScanDomDefinition()
		{
			// Create SectionDefinitions
			var nameDescriptor = new FieldDescriptorID();
			var scannerSectionDefinition = SectionDefinitions.CreateScannerSection(domHelper, ref nameDescriptor);
			var manifestsSectionDefinition = SectionDefinitions.CreateManifestsSection(domHelper);

			var sections = new List<SectionDefinition> { scannerSectionDefinition, manifestsSectionDefinition };

			// Create DomBehaviorDefinition
			var behaviorName = "TAG Scan Behavior";
			var domBehaviorDefinition = BehaviorDefinitions.CreateTagScanBehaviorDefinition(sections, behaviorName);
			CreateOrUpdateDomBehaviorDefinition(domBehaviorDefinition);

			var manifestSectionLink = new SectionDefinitionLink(manifestsSectionDefinition.GetID())
			{
				AllowMultipleSections = true,
			};

			var nameDefinition = new ModuleSettingsOverrides
			{
				NameDefinition = new DomInstanceNameDefinition
				{
					ConcatenationItems = new List<IDomInstanceConcatenationItem>
					{
						new FieldValueConcatenationItem
						{
							FieldDescriptorId = nameDescriptor,
						},
					},
				},
			};

			return new DomDefinition
			{
				Name = "TAG Scan",
				SectionDefinitionLinks = new List<SectionDefinitionLink> { new SectionDefinitionLink(scannerSectionDefinition.GetID()), manifestSectionLink },
				DomBehaviorDefinitionId = domBehaviorDefinition.ID,
				ModuleSettingsOverrides = nameDefinition,
			};
		}

		private DomDefinition CreateTagChannelDomDefinition()
		{
			// Create SectionDefinitions
			var nameDescriptor = new FieldDescriptorID();
			var channelSectionDefinition = SectionDefinitions.CreateChannelSection(domHelper, ref nameDescriptor);
			var layoutsSectionDefinition = SectionDefinitions.CreateLayoutsSection(domHelper);

			var sections = new List<SectionDefinition> { channelSectionDefinition, layoutsSectionDefinition };

			// Create DomBehaviorDefinition
			var behaviorName = "TAG Channel Behavior";
			var domBehaviorDefinition = BehaviorDefinitions.CreateTagChannelBehaviorDefinition(sections, behaviorName);
			CreateOrUpdateDomBehaviorDefinition(domBehaviorDefinition);

			var layoutSectionLink = new SectionDefinitionLink(layoutsSectionDefinition.GetID())
			{
				AllowMultipleSections = true,
			};

			var nameDefinition = new ModuleSettingsOverrides
			{
				NameDefinition = new DomInstanceNameDefinition
				{
					ConcatenationItems = new List<IDomInstanceConcatenationItem>
					{
						new FieldValueConcatenationItem
						{
							FieldDescriptorId = nameDescriptor,
						},
					},
				},
			};

			return new DomDefinition
			{
				Name = "TAG Channel",
				SectionDefinitionLinks = new List<SectionDefinitionLink> { new SectionDefinitionLink(channelSectionDefinition.GetID()), layoutSectionLink },
				DomBehaviorDefinitionId = domBehaviorDefinition.ID,
				ModuleSettingsOverrides = nameDefinition,
			};
		}

		public class SectionDefinitions
		{
			public static SectionDefinition CreateConfigSection(DomHelper domHelper, ref FieldDescriptorID nameDescriptor)
			{
				var tagScanDef = domHelper.DomDefinitions.Read(DomDefinitionExposers.Name.Equal("TAG Scan")).First().ID;

				var sourceElement = CreateFieldDescriptorObject<string>("Source Element (TAG Provision)", "A DMAID/ELID/PID to allow the process to report back to the source element that created this DOM.", true);
				var sourceId = CreateFieldDescriptorObject<string>("Source ID (TAG Provision)", "An ID that can be used when reporting back to the source element to identify the response.", true);
				var provisionName = CreateFieldDescriptorObject<string>("Provision Name (TAG Provision)", "A name for the scan, such as an event name or channel name, that can help identify this scan to users.", false);
				var tagScanners = CreateDomInstanceFieldDescriptorObject<List<Guid>>("TAG Scanners (TAG Provision)", "Links to the TAG Scanner Instances for this event. ", tagScanDef, false);
				var instanceId = CreateFieldDescriptorObject<string>("InstanceId (TAG Provision)", "The instanceId of the current DOM Instance.", false);
				var action = CreateFieldDescriptorObject<string>("Action (TAG Provision)", "The main provision action to execute.", false);

				nameDescriptor = provisionName.ID;

				List<FieldDescriptor> fieldDescriptors = new List<FieldDescriptor>
				{
					sourceElement,
					sourceId,
					provisionName,
					tagScanners,
					instanceId,
					action,
				};

				var sectionDefinition = CreateOrUpdateSection("Config", domHelper, fieldDescriptors);

				return sectionDefinition;
			}

			public static SectionDefinition CreateScannerSection(DomHelper domHelper, ref FieldDescriptorID nameDescriptor)
			{
				var typeEnum = new GenericEnum<string>();
				typeEnum.AddEntry("OTT", "OTT");

				var tagChannelDef = domHelper.DomDefinitions.Read(DomDefinitionExposers.Name.Equal("TAG Channel")).First().ID;

				var sourceElement = CreateFieldDescriptorObject<string>("Source Element (TAG Scan)", "A DMAID/ELID/PID to allow the process to report back to the source element that created this DOM.", true);
				var sourceId = CreateFieldDescriptorObject<string>("Source ID (TAG Scan)", "An ID that can be used when reporting back to the source element to identify the response.", true);
				var element = CreateFieldDescriptorObject<string>("TAG Element (TAG Scan)", "The name of the TAG element to run the scan.", false);
				var device = CreateFieldDescriptorObject<string>("TAG Device (TAG Scan)", "The name of the TAG Device to run the scan.", false);
				var tagInterface = CreateFieldDescriptorObject<string>("TAG Interface (TAG Scan)", "Name of the TAG Interface to perform the scan.", false);
				var scanName = CreateFieldDescriptorObject<string>("Scan Name (TAG Scan)", "The name of the scanner that will be created.", false);
				var assetId = CreateFieldDescriptorObject<string>("Asset ID (TAG Scan)", "The asset ID for the scan which will be used in the KMS creation.", false);
				var type = CreateEnumFieldDescriptorObject("Scan Type (TAG Scan)", "Define the type of the scan (NOTE: currently only OTT scans are supported)", typeEnum);
				var channels = CreateDomInstanceFieldDescriptorObject<List<Guid>>("Channels (TAG Scan)", "Links to the Channel Instances for this Scanner.", tagChannelDef, true);
				var instanceId = CreateFieldDescriptorObject<string>("InstanceId (TAG Scan)", "The instanceId of the current DOM Instance.", false);
				var action = CreateFieldDescriptorObject<string>("Action (TAG Scan)", "The main provision action to execute.", false);

				nameDescriptor = scanName.ID;

				List<FieldDescriptor> fieldDescriptors = new List<FieldDescriptor>
				{
					sourceElement,
					sourceId,
					element,
					device,
					tagInterface,
					scanName,
					assetId,
					type,
					channels,
					instanceId,
					action,
				};

				var sectionDefinition = CreateOrUpdateSection("Scanner", domHelper, fieldDescriptors);

				return sectionDefinition;
			}

			public static SectionDefinition CreateManifestsSection(DomHelper domHelper)
			{
				var manifestName = CreateFieldDescriptorObject<string>("Manifest Name (TAG Scan)", "The Name that will be used when scanning the manifest URL to be scanned.", false);
				var manifestURL = CreateFieldDescriptorObject<string>("Manifest URL (TAG Scan)", "The manifest URL to be scanned. Multiple manifests can be included for a scanner.", false);

				List<FieldDescriptor> fieldDescriptors = new List<FieldDescriptor>
				{
					manifestName,
					manifestURL,
				};

				var sectionDefinition = CreateOrUpdateSection("Manifests", domHelper, fieldDescriptors);

				return sectionDefinition;
			}

			public static SectionDefinition CreateChannelSection(DomHelper domHelper, ref FieldDescriptorID nameDescriptor)
			{
				var channelName = CreateFieldDescriptorObject<string>("Channel Name (TAG Channel)", "A name for the channel that can help users identify this record. This value is for reference only and is not used for channel matching.", true);
				var element = CreateFieldDescriptorObject<string>("TAG Element (TAG Channel)", "The name of the TAG element to run the scan.", false);
				var channelMatch = CreateFieldDescriptorObject<string>("Channel Match (TAG Channel)", "The string that will be used to find and match a channel so it can be configured.", false);
				var monitoringMode = CreateFieldDescriptorObject<string>("Monitoring Mode (TAG Channel)", "The string value that will be used to set the TAG Monitor mode.", false);
				var threshold = CreateFieldDescriptorObject<string>("Threshold (TAG Channel)", "The string value that will be used to set the TAG Threshold.", false);
				var notification = CreateFieldDescriptorObject<string>("Notification (TAG Channel)", "The string value that will be used to set the TAG Notification level.", false);
				var encryption = CreateFieldDescriptorObject<string>("Encryption (TAG Channel)", "The string value that will be used to set the TAG Encryption used for the channel.", false);
				var kms = CreateFieldDescriptorObject<string>("KMS (TAG Channel)", "The string value that will be used to set the KMS value.", false);
				var instanceId = CreateFieldDescriptorObject<string>("InstanceId (TAG Channel)", "The instanceId of the current DOM Instance.", false);

				nameDescriptor = channelName.ID;

				List<FieldDescriptor> fieldDescriptors = new List<FieldDescriptor>
				{
					channelName,
					element,
					channelMatch,
					monitoringMode,
					threshold,
					notification,
					encryption,
					kms,
					instanceId,
				};

				var sectionInfo = CreateOrUpdateSection("Channel", domHelper, fieldDescriptors);

				return sectionInfo;
			}

			public static SectionDefinition CreateLayoutsSection(DomHelper domHelper)
			{
				var layoutMatch = CreateFieldDescriptorObject<string>("Layout Match (TAG Channel)", "The Name that will be used when scanning the manifest URL to be scanned.", false);
				var layoutPosition = CreateFieldDescriptorObject<string>("Layout Position (TAG Channel)", "The position in the layouts that is reserved for this channel.", false);

				List<FieldDescriptor> fieldDescriptors = new List<FieldDescriptor>
				{
					layoutMatch,
					layoutPosition,
				};

				var sectionDefinition = CreateOrUpdateSection("Layouts", domHelper, fieldDescriptors);

				return sectionDefinition;
			}

			private static SectionDefinition CreateOrUpdateSection(string name, DomHelper domHelper, List<FieldDescriptor> fieldDescriptors)
			{
				var domInstancesSectionDefinition = new CustomSectionDefinition
				{
					Name = name,
				};

				var domInstanceSection = domHelper.SectionDefinitions.Read(SectionDefinitionExposers.Name.Equal(domInstancesSectionDefinition.Name));
				SectionDefinition sectionDefinition;
				if (!domInstanceSection.Any())
				{
					foreach (var field in fieldDescriptors)
					{
						domInstancesSectionDefinition.AddOrReplaceFieldDescriptor(field);
					}

					sectionDefinition = domHelper.SectionDefinitions.Create(domInstancesSectionDefinition) as CustomSectionDefinition;
				}
				else
				{
					// Update Section Definition (Add missing fieldDescriptors)
					sectionDefinition = UpdateSectionDefinition(domHelper, fieldDescriptors, domInstanceSection);
				}

				return sectionDefinition;
			}

			private static SectionDefinition UpdateSectionDefinition(DomHelper domHelper, List<FieldDescriptor> fieldDescriptorList, List<SectionDefinition> sectionDefinition)
			{
				var existingSectionDefinition = sectionDefinition.First() as CustomSectionDefinition;
				var previousFieldNames = existingSectionDefinition.GetAllFieldDescriptors().Select(x => x.Name).ToList();
				List<FieldDescriptor> fieldDescriptorsToAdd = new List<FieldDescriptor>();

				// Check if there's a fieldDefinition to add
				foreach (var newfieldDescriptor in fieldDescriptorList)
				{
					if (!previousFieldNames.Contains(newfieldDescriptor.Name))
					{
						fieldDescriptorsToAdd.Add(newfieldDescriptor);
					}
				}

				if (fieldDescriptorsToAdd.Count > 0)
				{
					foreach (var field in fieldDescriptorsToAdd)
					{
						existingSectionDefinition.AddOrReplaceFieldDescriptor(field);
					}

					existingSectionDefinition = domHelper.SectionDefinitions.Update(existingSectionDefinition) as CustomSectionDefinition;
				}

				return existingSectionDefinition;
			}

			private static FieldDescriptor CreateFieldDescriptorObject<T>(string fieldName, string toolTip, bool optional)
			{
				return new FieldDescriptor
				{
					FieldType = typeof(T),
					Name = fieldName,
					Tooltip = toolTip,
					IsOptional = optional,
				};
			}

			private static FieldDescriptor CreateEnumFieldDescriptorObject(string fieldName, string toolTip, GenericEnum<string> discreets)
			{
				return new GenericEnumFieldDescriptor
				{
					FieldType = typeof(GenericEnum<string>),
					Name = fieldName,
					Tooltip = toolTip,
					GenericEnumInstance = discreets,
					IsOptional = false,
				};
			}

			private static DomInstanceFieldDescriptor CreateDomInstanceFieldDescriptorObject<T>(string fieldName, string toolTip, DomDefinitionId definitionId, bool optional)
			{
				var fieldDescriptor = new DomInstanceFieldDescriptor(ModuleId)
				{
					FieldType = typeof(T),
					Name = fieldName,
					Tooltip = toolTip,
					IsOptional = optional,
				};

				fieldDescriptor.DomDefinitionIds.Add(definitionId);
				return fieldDescriptor;
			}
		}

		public class BehaviorDefinitions
		{
			public static DomBehaviorDefinition CreateTagProvisionBehaviorDefinition(List<SectionDefinition> sections, string behaviorName)
			{
				var statuses = new List<DomStatus>
				{
					new DomStatus("draft", "Draft"),
					new DomStatus("ready", "Ready"),
					new DomStatus("in_progress", "In Progress"),
					new DomStatus("active", "Active"),
					new DomStatus("reprovision", "Reprovision"),
					new DomStatus("deactivate", "Deactivate"),
					new DomStatus("deactivating", "Deactivating"),
					new DomStatus("complete", "Complete"),
					new DomStatus("error", "Error"),
					new DomStatus("active_with_errors", "Active with Errors"),
				};

				var transitions = new List<DomStatusTransition>
				{
					new DomStatusTransition("draft_to_ready", "draft", "ready"),
					new DomStatusTransition("ready_to_inprogress", "ready", "in_progress"),
					new DomStatusTransition("inprogress_to_active", "in_progress", "active"),
					new DomStatusTransition("active_to_reprovision", "active", "reprovision"),
					new DomStatusTransition("active_to_deactivate", "active", "deactivate"),
					new DomStatusTransition("reprovision_to_inprogress", "reprovision", "in_progress"),
					new DomStatusTransition("deactivate_to_deactivating", "deactivate", "deactivating"),
					new DomStatusTransition("deactivating_to_complete", "deactivating", "complete"),
					new DomStatusTransition("complete_to_ready", "complete", "ready"),
					new DomStatusTransition("complete_to_draft", "complete", "draft"),
					new DomStatusTransition("inprogress_to_error", "in_progress", "error"),
					new DomStatusTransition("deactivating_to_error", "deactivating", "error"),
					new DomStatusTransition("error_to_reprovision", "error", "reprovision"),
					new DomStatusTransition("error_to_deactivate", "error", "deactivate"),
					new DomStatusTransition("inprogress_to_activewitherrors", "in_progress", "active_with_errors"),
					new DomStatusTransition("activewitherrors_to_reprovision", "active_with_errors", "reprovision"),
					new DomStatusTransition("activewitherrors_to_deactivate", "active_with_errors", "deactivate"),
				};

				List<IDomActionDefinition> behaviorActions = GetBehaviorActions("TAG Process", "Provision Name");

				List<IDomButtonDefinition> domButtons = GetBehaviorButtons();

				return new DomBehaviorDefinition
				{
					Name = behaviorName,
					InitialStatusId = "draft",
					Statuses = statuses,
					StatusTransitions = transitions,
					StatusSectionDefinitionLinks = GetTagProvisionStatusLinks(sections),
					ActionDefinitions = behaviorActions,
					ButtonDefinitions = domButtons,
				};
			}

			public static DomBehaviorDefinition CreateTagScanBehaviorDefinition(List<SectionDefinition> sections, string behaviorName)
			{
				var statuses = new List<DomStatus>
				{
					new DomStatus("draft", "Draft"),
					new DomStatus("ready", "Ready"),
					new DomStatus("in_progress", "In Progress"),
					new DomStatus("active", "Active"),
					new DomStatus("reprovision", "Reprovision"),
					new DomStatus("deactivate", "Deactivate"),
					new DomStatus("deactivating", "Deactivating"),
					new DomStatus("complete", "Complete"),
					new DomStatus("error", "Error"),
					new DomStatus("active_with_errors", "Active with Errors"),
				};

				var transitions = new List<DomStatusTransition>
				{
					new DomStatusTransition("draft_to_ready", "draft", "ready"),
					new DomStatusTransition("ready_to_inprogress", "ready", "in_progress"),
					new DomStatusTransition("inprogress_to_active", "in_progress", "active"),
					new DomStatusTransition("active_to_reprovision", "active", "reprovision"),
					new DomStatusTransition("active_to_deactivate", "active", "deactivate"),
					new DomStatusTransition("reprovision_to_inprogress", "reprovision", "in_progress"),
					new DomStatusTransition("deactivate_to_deactivating", "deactivate", "deactivating"),
					new DomStatusTransition("deactivating_to_complete", "deactivating", "complete"),
					new DomStatusTransition("complete_to_ready", "complete", "ready"),
					new DomStatusTransition("complete_to_draft", "complete", "draft"),
					new DomStatusTransition("inprogress_to_error", "in_progress", "error"),
					new DomStatusTransition("deactivating_to_error", "deactivating", "error"),
					new DomStatusTransition("error_to_reprovision", "error", "reprovision"),
					new DomStatusTransition("error_to_deactivate", "error", "deactivate"),
					new DomStatusTransition("inprogress_to_activewitherrors", "in_progress", "active_with_errors"),
					new DomStatusTransition("activewitherrors_to_reprovision", "active_with_errors", "reprovision"),
					new DomStatusTransition("activewitherrors_to_deactivate", "active_with_errors", "deactivate"),
				};

				List<IDomActionDefinition> behaviorActions = GetBehaviorActions("TAG Scan Process", "Scan Name");

				List<IDomButtonDefinition> domButtons = GetBehaviorButtons();

				return new DomBehaviorDefinition
				{
					Name = behaviorName,
					InitialStatusId = "draft",
					Statuses = statuses,
					StatusTransitions = transitions,
					StatusSectionDefinitionLinks = GetTagScanStatusLinks(sections),
					ActionDefinitions = behaviorActions,
					ButtonDefinitions = domButtons,
				};
			}

			private static List<IDomButtonDefinition> GetBehaviorButtons()
			{
				DomInstanceButtonDefinition provisionButton = new DomInstanceButtonDefinition("provision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "draft" }),
					ActionDefinitionIds = new List<string> { "provision" },
					Layout = new DomButtonDefinitionLayout { Text = "Provision" },
				};

				DomInstanceButtonDefinition deactivateButton = new DomInstanceButtonDefinition("deactivate")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active" }),
					ActionDefinitionIds = new List<string> { "deactivate" },
					Layout = new DomButtonDefinitionLayout { Text = "Deactivate" },
				};

				DomInstanceButtonDefinition reprovisionButton = new DomInstanceButtonDefinition("reprovision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active" }),
					ActionDefinitionIds = new List<string> { "reprovision" },
					Layout = new DomButtonDefinitionLayout { Text = "Reprovision" },
				};

				DomInstanceButtonDefinition completeProvision = new DomInstanceButtonDefinition("complete-provision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "complete" }),
					ActionDefinitionIds = new List<string> { "complete-provision" },
					Layout = new DomButtonDefinitionLayout { Text = "Provision" },
				};

				DomInstanceButtonDefinition errorReprovisionButton = new DomInstanceButtonDefinition("error-reprovision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "error" }),
					ActionDefinitionIds = new List<string> { "error-reprovision" },
					Layout = new DomButtonDefinitionLayout { Text = "Reprovision" },
				};

				DomInstanceButtonDefinition errorDeactivateButton = new DomInstanceButtonDefinition("error-deactivate")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "error" }),
					ActionDefinitionIds = new List<string> { "error-deactivate" },
					Layout = new DomButtonDefinitionLayout { Text = "Deactivate" },
				};

				DomInstanceButtonDefinition activeErrorReprovisionButton = new DomInstanceButtonDefinition("activewitherrors-reprovision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active_with_errors" }),
					ActionDefinitionIds = new List<string> { "activewitherrors-reprovision" },
					Layout = new DomButtonDefinitionLayout { Text = "Reprovision" },
				};

				DomInstanceButtonDefinition activeErrorDeactivateButton = new DomInstanceButtonDefinition("activewitherrors-deactivate")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active_with_errors" }),
					ActionDefinitionIds = new List<string> { "activewitherrors-deactivate" },
					Layout = new DomButtonDefinitionLayout { Text = "Deactivate" },
				};

				List<IDomButtonDefinition> domButtons = new List<IDomButtonDefinition>
				{
					provisionButton,
					deactivateButton,
					reprovisionButton,
					completeProvision,
					errorReprovisionButton,
					errorDeactivateButton,
					activeErrorReprovisionButton,
					activeErrorDeactivateButton,
				};

				return domButtons;
			}

			private static List<IDomButtonDefinition> GetChannelBehaviorButtons()
			{
				DomInstanceButtonDefinition provisionButton = new DomInstanceButtonDefinition("provision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "draft" }),
					ActionDefinitionIds = new List<string> { "provision" },
					Layout = new DomButtonDefinitionLayout { Text = "Provision" },
				};

				DomInstanceButtonDefinition deactivateButton = new DomInstanceButtonDefinition("deactivate")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active" }),
					ActionDefinitionIds = new List<string> { "deactivate" },
					Layout = new DomButtonDefinitionLayout { Text = "Deactivate" },
				};

				DomInstanceButtonDefinition reprovisionButton = new DomInstanceButtonDefinition("reprovision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "active" }),
					ActionDefinitionIds = new List<string> { "reprovision" },
					Layout = new DomButtonDefinitionLayout { Text = "Reprovision" },
				};

				DomInstanceButtonDefinition completeProvision = new DomInstanceButtonDefinition("complete-provision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "complete" }),
					ActionDefinitionIds = new List<string> { "complete-provision" },
					Layout = new DomButtonDefinitionLayout { Text = "Provision" },
				};

				DomInstanceButtonDefinition errorReprovisionButton = new DomInstanceButtonDefinition("error-reprovision")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "error" }),
					ActionDefinitionIds = new List<string> { "error-reprovision" },
					Layout = new DomButtonDefinitionLayout { Text = "Reprovision" },
				};

				DomInstanceButtonDefinition errorDeactivateButton = new DomInstanceButtonDefinition("error-deactivate")
				{
					VisibilityCondition = new StatusCondition(new List<string> { "error" }),
					ActionDefinitionIds = new List<string> { "error-deactivate" },
					Layout = new DomButtonDefinitionLayout { Text = "Deactivate" },
				};

				List<IDomButtonDefinition> domButtons = new List<IDomButtonDefinition>
				{
					provisionButton,
					deactivateButton,
					reprovisionButton,
					completeProvision,
					errorReprovisionButton,
					errorDeactivateButton,
				};

				return domButtons;
			}

			private static List<IDomActionDefinition> GetBehaviorActions(string processName, string businessKeyField)
			{
				var provisionAction = new ExecuteScriptDomActionDefinition("provision")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:draft_to_ready",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:provision",
					},
				};

				var deactivateAction = new ExecuteScriptDomActionDefinition("deactivate")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:active_to_deactivate",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:deactivate",
					},
				};

				var reprovisionAction = new ExecuteScriptDomActionDefinition("reprovision")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:active_to_reprovision",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:reprovision",
					},
				};

				var completeProvisionAction = new ExecuteScriptDomActionDefinition("complete-provision")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:complete_to_ready",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:complete-provision",
					},
				};

				var errorReprovisionAction = new ExecuteScriptDomActionDefinition("error-reprovision")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:error_to_reprovision",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:reprovision",
					},
				};

				var errorDeactivateAction = new ExecuteScriptDomActionDefinition("error-deactivate")
				{
					Script = "start_process",
					IsInteractive = false,
					ScriptOptions = new List<string>
					{
						$"PARAMETER:1:{processName}",
						"PARAMETER:2:error_to_deactivate",
						$"PARAMETER:3:{businessKeyField}",
						"PARAMETER:4:deactivate",
					},
				};

				var behaviorActions = new List<IDomActionDefinition>
				{
					provisionAction,
					deactivateAction,
					reprovisionAction,
					completeProvisionAction,
					errorReprovisionAction,
					errorDeactivateAction,
				};

				if (!processName.Contains("Channel"))
				{
					var activeErrorReprovisionAction = new ExecuteScriptDomActionDefinition("activewitherrors-reprovision")
					{
						Script = "start_process",
						IsInteractive = false,
						ScriptOptions = new List<string>
						{
							$"PARAMETER:1:{processName}",
							"PARAMETER:2:activewitherrors_to_reprovision",
							$"PARAMETER:3:{businessKeyField}",
							"PARAMETER:4:reprovision",
						},
					};

					var activeErrorDeactivateAction = new ExecuteScriptDomActionDefinition("activewitherrors-deactivate")
					{
						Script = "start_process",
						IsInteractive = false,
						ScriptOptions = new List<string>
						{
							$"PARAMETER:1:{processName}",
							"PARAMETER:2:activewitherrors_to_deactivate",
							$"PARAMETER:3:{businessKeyField}",
							"PARAMETER:4:deactivate",
						},
					};

					behaviorActions.Add(activeErrorDeactivateAction);
					behaviorActions.Add(activeErrorReprovisionAction);
				}

				return behaviorActions;
			}

			public static DomBehaviorDefinition CreateTagChannelBehaviorDefinition(List<SectionDefinition> sections, string behaviorName)
			{
				var statuses = new List<DomStatus>
				{
					new DomStatus("draft", "Draft"),
					new DomStatus("ready", "Ready"),
					new DomStatus("in_progress", "In Progress"),
					new DomStatus("active", "Active"),
					new DomStatus("reprovision", "Reprovision"),
					new DomStatus("deactivate", "Deactivate"),
					new DomStatus("deactivating", "Deactivating"),
					new DomStatus("complete", "Complete"),
					new DomStatus("error", "Error"),
				};

				var transitions = new List<DomStatusTransition>
				{
					new DomStatusTransition("draft_to_ready", "draft", "ready"),
					new DomStatusTransition("ready_to_inprogress", "ready", "in_progress"),
					new DomStatusTransition("inprogress_to_active", "in_progress", "active"),
					new DomStatusTransition("active_to_reprovision", "active", "reprovision"),
					new DomStatusTransition("active_to_deactivate", "active", "deactivate"),
					new DomStatusTransition("reprovision_to_inprogress", "reprovision", "in_progress"),
					new DomStatusTransition("deactivate_to_deactivating", "deactivate", "deactivating"),
					new DomStatusTransition("deactivating_to_complete", "deactivating", "complete"),
					new DomStatusTransition("complete_to_draft", "complete", "draft"),
					new DomStatusTransition("complete_to_ready", "complete", "ready"),
					new DomStatusTransition("active_to_complete", "active", "complete"),
					new DomStatusTransition("inprogress_to_error", "in_progress", "error"),
					new DomStatusTransition("deactivating_to_error", "deactivating", "error"),
					new DomStatusTransition("error_to_reprovision", "error", "reprovision"),
					new DomStatusTransition("error_to_deactivate", "error", "deactivate"),
					new DomStatusTransition("error_to_complete", "error", "complete"),
					new DomStatusTransition("error_to_draft", "error", "draft"),
				};

				List<IDomActionDefinition> behaviorActions = GetBehaviorActions("TAG Channel Process", "Channel Name");

				List<IDomButtonDefinition> domButtons = GetChannelBehaviorButtons();

				return new DomBehaviorDefinition
				{
					Name = behaviorName,
					InitialStatusId = "draft",
					Statuses = statuses,
					StatusTransitions = transitions,
					StatusSectionDefinitionLinks = GetTagChannelStatusLinks(sections),
					ButtonDefinitions = domButtons,
					ActionDefinitions = behaviorActions,
				};
			}

			private static List<DomStatusSectionDefinitionLink> GetTagProvisionStatusLinks(List<SectionDefinition> sections)
			{
				var list = new List<DomStatusSectionDefinitionLink>();
				foreach (var section in sections)
				{
					Dictionary<string, FieldDescriptorID> fieldsList = GetFieldDescriptorDictionary(section);

					var draftStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinksDraft(section, fieldsList);
					var readyStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "ready");
					var inprogressStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "in_progress");
					var activeStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "active");
					var reprovisionStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "reprovision");
					var deactivateStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "deactivate");
					var deactivatingStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "deactivating");
					var completeStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "complete");
					var errorStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "error");
					var activeWithErrorsStatusLink = StatusSectionDefinitions.GetTagProvisionSectionDefinitionLinks(section, fieldsList, "active_with_errors");

					var links = new List<DomStatusSectionDefinitionLink>
					{
						draftStatusLink,
						readyStatusLink,
						inprogressStatusLink,
						activeStatusLink,
						reprovisionStatusLink,
						deactivateStatusLink,
						deactivatingStatusLink,
						completeStatusLink,
						errorStatusLink,
						activeWithErrorsStatusLink,
					};

					list.AddRange(links);
				}

				return list;
			}

			private static List<DomStatusSectionDefinitionLink> GetTagScanStatusLinks(List<SectionDefinition> sections)
			{
				var list = new List<DomStatusSectionDefinitionLink>();
				foreach (var section in sections)
				{
					Dictionary<string, FieldDescriptorID> fieldsList = GetFieldDescriptorDictionary(section);

					var draftStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLinkDraft(section, fieldsList);
					var readyStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "ready", true);
					var inprogressStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "in_progress", true);
					var activeStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "active", true);
					var reprovisionStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "reprovision", true);
					var deactivateStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "deactivate", true);
					var deactivatingStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "deactivating", true);
					var completeStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "complete", false);
					var errorStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "error", false);
					var activeWithErrorsStatusLink = StatusSectionDefinitions.GetTagScanSectionDefinitionLink(section, fieldsList, "active_with_errors", false);

					var links = new List<DomStatusSectionDefinitionLink>
					{
						draftStatusLink,
						readyStatusLink,
						inprogressStatusLink,
						activeStatusLink,
						reprovisionStatusLink,
						deactivateStatusLink,
						deactivatingStatusLink,
						completeStatusLink,
						errorStatusLink,
						activeWithErrorsStatusLink,
					};

					list.AddRange(links);
				}

				return list;
			}

			private static List<DomStatusSectionDefinitionLink> GetTagChannelStatusLinks(List<SectionDefinition> sections)
			{
				var list = new List<DomStatusSectionDefinitionLink>();
				foreach (var section in sections)
				{
					Dictionary<string, FieldDescriptorID> fieldsList = GetFieldDescriptorDictionary(section);

					var draftStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLinkDraft(section, fieldsList);
					var readyStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "ready");
					var inprogressStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "in_progress");
					var activeStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "active");
					var reprovisionStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "reprovision");
					var deactivateStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "deactivate");
					var deactivatingStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "deactivating");
					var completeStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "complete");
					var errorStatusLink = StatusSectionDefinitions.GetTagChannelSectionDefinitionLink(section, fieldsList, "error");

					var links = new List<DomStatusSectionDefinitionLink>
					{
						draftStatusLink,
						readyStatusLink,
						inprogressStatusLink,
						activeStatusLink,
						reprovisionStatusLink,
						deactivateStatusLink,
						deactivatingStatusLink,
						completeStatusLink,
						errorStatusLink,
					};

					list.AddRange(links);
				}

				return list;
			}

			private static Dictionary<string, FieldDescriptorID> GetFieldDescriptorDictionary(SectionDefinition section)
			{
				Dictionary<string, FieldDescriptorID> fieldsList = new Dictionary<string, FieldDescriptorID>();

				var fields = section.GetAllFieldDescriptors();
				foreach (var field in fields)
				{
					var fieldName = field.Name;

					fieldsList[fieldName] = field.ID;
				}

				return fieldsList;
			}

			public class StatusSectionDefinitions
			{
				public static DomStatusSectionDefinitionLink GetTagProvisionSectionDefinitionLinksDraft(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId("draft", section.GetID());

					var sectionDefinitionLink = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
					{
						FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
						{
							new DomStatusFieldDescriptorLink(fieldsList["Source Element (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Source ID (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Provision Name (TAG Provision)"])
							{
								Visible = true,
								ReadOnly = false,
								RequiredForStatus = true,
							},
							new DomStatusFieldDescriptorLink(fieldsList["TAG Scanners (TAG Provision)"])
							{
								Visible = true,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Action (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
						},
					};

					return sectionDefinitionLink;
				}

				public static DomStatusSectionDefinitionLink GetTagProvisionSectionDefinitionLinks(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList, string status)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId(status, section.GetID());

					var sectionDefinitionLink = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
					{
						FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
						{
							new DomStatusFieldDescriptorLink(fieldsList["Source Element (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Source ID (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = false,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Provision Name (TAG Provision)"])
							{
								Visible = true,
								ReadOnly = true,
								RequiredForStatus = true,
							},
							new DomStatusFieldDescriptorLink(fieldsList["TAG Scanners (TAG Provision)"])
							{
								Visible = true,
								ReadOnly = true,
								RequiredForStatus = true,
							},
							new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = true,
							},
							new DomStatusFieldDescriptorLink(fieldsList["Action (TAG Provision)"])
							{
								Visible = false,
								ReadOnly = false,
								RequiredForStatus = true,
							},
						},
					};

					return sectionDefinitionLink;
				}

				public static DomStatusSectionDefinitionLink GetTagScanSectionDefinitionLinkDraft(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId("draft", section.GetID());
					DomStatusSectionDefinitionLink draftStatusLinkDomInstance;

					switch (section.GetName())
					{
						case "Scanner":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Source Element (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Source ID (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Element (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Device (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Interface (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Scan Name (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Asset ID (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Scan Type (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Channels (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Action (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
								},
							};
							break;

						case "Manifests":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Manifest Name (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Manifest URL (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
								},
								AllowMultipleSections = true,
							};
							break;

						default:
							return new DomStatusSectionDefinitionLink();
					}

					return draftStatusLinkDomInstance;
				}

				public static DomStatusSectionDefinitionLink GetTagScanSectionDefinitionLink(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList, string status, bool readOnly)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId(status, section.GetID());
					DomStatusSectionDefinitionLink draftStatusLinkDomInstance;
					switch (section.GetName())
					{
						case "Scanner":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Source Element (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Source ID (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Element (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Device (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Interface (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Scan Name (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Asset ID (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Scan Type (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Channels (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = true,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Action (TAG Scan)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = true,
									},
								},
							};
							break;

						case "Manifests":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Manifest Name (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Manifest URL (TAG Scan)"])
									{
										Visible = true,
										ReadOnly = readOnly,
										RequiredForStatus = true,
									},
								},
								AllowMultipleSections = true,
							};
							break;

						default:
							return new DomStatusSectionDefinitionLink();
					}

					return draftStatusLinkDomInstance;
				}

				public static DomStatusSectionDefinitionLink GetTagChannelSectionDefinitionLinkDraft(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId("draft", section.GetID());
					DomStatusSectionDefinitionLink draftStatusLinkDomInstance;

					switch (section.GetName())
					{
						case "Channel":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Channel Name (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Element (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Channel Match (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Monitoring Mode (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Threshold (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Notification (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Encryption (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["KMS (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Channel)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = false,
									},
								},
							};
							break;

						case "Layouts":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Layout Match (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Layout Position (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
								},
								AllowMultipleSections = true,
							};
							break;

						default:
							return new DomStatusSectionDefinitionLink();
					}

					return draftStatusLinkDomInstance;
				}

				public static DomStatusSectionDefinitionLink GetTagChannelSectionDefinitionLink(SectionDefinition section, Dictionary<string, FieldDescriptorID> fieldsList, string status)
				{
					var sectionStatusLinkId = new DomStatusSectionDefinitionLinkId(status, section.GetID());

					DomStatusSectionDefinitionLink draftStatusLinkDomInstance;

					switch (section.GetName())
					{
						case "Channel":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Channel Name (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["TAG Element (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Channel Match (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = true,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Monitoring Mode (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Threshold (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Notification (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Encryption (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["KMS (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["InstanceId (TAG Channel)"])
									{
										Visible = false,
										ReadOnly = false,
										RequiredForStatus = true,
									},
								},
							};
							break;

						case "Layouts":
							draftStatusLinkDomInstance = new DomStatusSectionDefinitionLink(sectionStatusLinkId)
							{
								FieldDescriptorLinks = new List<DomStatusFieldDescriptorLink>
								{
									new DomStatusFieldDescriptorLink(fieldsList["Layout Match (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
									new DomStatusFieldDescriptorLink(fieldsList["Layout Position (TAG Channel)"])
									{
										Visible = true,
										ReadOnly = false,
										RequiredForStatus = false,
									},
								},
								AllowMultipleSections = true,
							};
							break;

						default:
							return new DomStatusSectionDefinitionLink();
					}

					return draftStatusLinkDomInstance;
				}
			}
		}
	}
}