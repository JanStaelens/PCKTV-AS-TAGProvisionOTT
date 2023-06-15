namespace Script
{
	using System;
	using System.Linq;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.MessageHandler;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Actions;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Settings;
	using Skyline.DataMiner.Net.Messages.SLDataGateway;
	using Skyline.DataMiner.Net.Sections;

	public class Script
	{
		private DomHelper innerDomHelper;
		private Field actionField;

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">Link with SLAutomation process.</param>
		public void Run(Engine engine)
		{
			engine.ExitFail("This script should be executed using the 'OnDomAction' entry point");
		}

		[AutomationEntryPoint(AutomationEntryPointType.Types.OnDomAction)]
		public void OnDomActionMethod(IEngine engine, ExecuteScriptDomActionContext context)
		{
			try
			{
				var process = engine.GetScriptParam("process").Value;
				var transition = engine.GetScriptParam("transition").Value;
				var keyField = engine.GetScriptParam("key").Value;
				var actionValue = engine.GetScriptParam("action").Value;

				var instanceId = context.ContextId as DomInstanceId;
				innerDomHelper = new DomHelper(engine.SendSLNetMessages, instanceId.ModuleId);
				string businessKey = GetBusinessKey(engine, keyField, instanceId);

				if (!String.IsNullOrWhiteSpace(businessKey))
				{
					UpdateAction(instanceId, actionValue);
					innerDomHelper.DomInstances.DoStatusTransition(instanceId, transition);
					ProcessHelper.PushToken(process, businessKey, instanceId);
				}
			}
			catch (Exception ex)
			{
				engine.GenerateInformation("Exception starting process: " + ex);
			}
		}

		private void UpdateAction(DomInstanceId instanceId, string actionValue)
		{
			if (this.actionField == null)
			{
				return;
			}

			var dominstance = DomInstanceExposers.Id.Equal(instanceId);
			var instance = this.innerDomHelper.DomInstances.Read(dominstance).First();
			instance.AddOrUpdateFieldValue(this.actionField.SectionDefinition, this.actionField.FieldDescriptor, actionValue);
			this.innerDomHelper.DomInstances.Update(instance);
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:Prefix local calls with this", Justification = "Ignored")]
		private string GetBusinessKey(IEngine engine, string keyField, DomInstanceId instanceId)
		{
			string businessKey = "default";
			var dominstance = DomInstanceExposers.Id.Equal(instanceId);
			var instance = innerDomHelper.DomInstances.Read(dominstance).First();
			var instanceSet = false;
			var keyFound = false;
			var actionSet = false;

			SectionDefinition sectionToUpdate = null;
			FieldDescriptor fieldToUpdate = null;

			foreach (var section in instance.Sections)
			{
				Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = SetSectionDefinitionById;
				section.Stitch(sectionDefinitionFunc);

				var sectionDefinition = section.GetSectionDefinition();
				var fields = sectionDefinition.GetAllFieldDescriptors();

				foreach (var field in fields)
				{
					if (field.Name.Contains("InstanceId"))
					{
						sectionToUpdate = sectionDefinition;
						fieldToUpdate = field;
						instanceSet = true;
					}

					if (field.Name.Contains("Action"))
					{
						this.actionField = new Field
						{
							SectionDefinition = section.GetSectionDefinition(),
							FieldDescriptor = field,
						};
						actionSet = true;
					}

					if (field.Name.Contains(keyField))
					{
						businessKey = Convert.ToString(section.GetFieldValueById(field.ID).Value.Value);
						keyFound = true;
					}

					if (keyFound && instanceSet && actionSet)
					{
						break;
					}
				}

				if (keyFound && instanceSet && actionSet)
				{
					break;
				}
			}

			if (sectionToUpdate == null || fieldToUpdate == null)
			{
				engine.GenerateInformation("Failed to find section/field for updating InstanceId");
				return null;
			}

			instance.AddOrUpdateFieldValue(sectionToUpdate, fieldToUpdate, instanceId.Id.ToString());
			innerDomHelper.DomInstances.Update(instance);

			return businessKey;
		}

		private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
		{
			return this.innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
		}

		public class Field
		{
			public SectionDefinition SectionDefinition { get; set; }

			public FieldDescriptor FieldDescriptor { get; set; }
		}
	}
}