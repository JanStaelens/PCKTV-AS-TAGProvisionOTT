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

        /// <summary>
        /// The Script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(Engine engine)
        {
            var scriptName = "Start Scanners";

            var helper = new PaProfileLoadDomHelper(engine);
            this.innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

            var exceptionHelper = new ExceptionHelper(engine, this.innerDomHelper);

            engine.GenerateInformation("START " + scriptName);

            var channelName = helper.GetParameterValue<string>("Provision Name (TAG Provision)");

            try
            {
                var action = helper.GetParameterValue<string>("Action (TAG Provision)");
                var scanners = helper.GetParameterValue<List<Guid>>("TAG Scanners (TAG Provision)");

                foreach (var scanner in scanners)
                {
                    var scannerFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(scanner));
                    var scannerInstance = this.innerDomHelper.DomInstances.Read(scannerFilter).First();
                    engine.GenerateInformation("status of scanner instance: " + scannerInstance.StatusId);
                    this.ExecuteActionOnScanners(action, scannerInstance);
                }

                if (action == "provision")
                {
                    helper.TransitionState("ready_to_inprogress");
                }
                else if (action == "deactivate")
                {
                    helper.TransitionState("deactivate_to_deactivating");
                }
                else if (action == "reprovision")
                {
                    helper.TransitionState("reprovision_to_inprogress");
                }

                helper.ReturnSuccess();
            }
            catch (Exception ex)
            {
                engine.GenerateInformation($"ERROR in {scriptName} " + ex);
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

        private void ExecuteActionOnScanners(string action, DomInstance instance)
        {
            foreach (var section in instance.Sections)
            {
                Func<SectionDefinitionID, SectionDefinition> sectionDefinitionFunc = this.SetSectionDefinitionById;

                section.Stitch(sectionDefinitionFunc);
                var fieldDescriptors = section.GetSectionDefinition().GetAllFieldDescriptors();
                if (fieldDescriptors.Any(x => x.Name.Contains("Action")))
                {
                    var fieldToUpdate = fieldDescriptors.First(x => x.Name.Contains("Action"));
                    instance.AddOrUpdateFieldValue(section.GetSectionDefinition(), fieldToUpdate, action);
                    this.innerDomHelper.DomInstances.Update(instance);

                    this.innerDomHelper.DomInstances.ExecuteAction(instance.ID, action);

                    break;
                }
            }
        }

        private SectionDefinition SetSectionDefinitionById(SectionDefinitionID sectionDefinitionId)
        {
            return this.innerDomHelper.SectionDefinitions.Read(SectionDefinitionExposers.ID.Equal(sectionDefinitionId)).First();
        }
    }
}