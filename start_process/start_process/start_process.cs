namespace Script
{
	using System;
	using System.Linq;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.MessageHandler;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
	using Skyline.DataMiner.Net.Apps.DataMinerObjectModel.Actions;
	using Skyline.DataMiner.Net.Messages.SLDataGateway;
	using Skyline.DataMiner.Net.Sections;

	public class Script
	{
		private DomHelper innerDomHelper;

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

				var instanceId = context.ContextId as DomInstanceId;
				innerDomHelper = new DomHelper(engine.SendSLNetMessages, instanceId.ModuleId);
				string businessKey = GetBusinessKey(engine, keyField, instanceId);

				if (!String.IsNullOrWhiteSpace(businessKey))
				{
					innerDomHelper.DomInstances.DoStatusTransition(instanceId, transition);
					ProcessHelper.PushToken(process, businessKey, instanceId);
				}
			}
			catch (Exception ex)
			{
				engine.GenerateInformation("Exception starting process: " + ex);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:Prefix local calls with this", Justification = "Ignored")]
		private string GetBusinessKey(IEngine engine, string keyField, DomInstanceId instanceId)
		{
			string businessKey = "default";
			var dominstance = DomInstanceExposers.Id.Equal(instanceId);
			var instance = innerDomHelper.DomInstances.Read(dominstance).First();
			var instanceSet = false;
			var keyFound = false;

			SectionDefinition sectionToUpdate = null;
			FieldDescriptor fieldToUpdate = null;

			foreach (var section in instance.Sections)
			{
				Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = SetSectionDefinitionById;
				section.Stitch(sectionDefinitionFunc);

				foreach (var field in section.FieldValues)
				{
					if (field.GetFieldDescriptor().Name.Contains("InstanceId"))
					{
						sectionToUpdate = section.GetSectionDefinition();
						fieldToUpdate = field.GetFieldDescriptor();
						instanceSet = true;
					}

					if (field.GetFieldDescriptor().Name.Contains(keyField))
					{
						businessKey = field.Value.ToString();
						keyFound = true;
					}

					if (keyFound && instanceSet)
					{
						break;
					}
				}

				if (keyFound && instanceSet)
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
	}
}