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

	internal class Script
	{
		private const int NoLayout = 0;
		private DomHelper innerDomHelper;

		/// <summary>
		/// The Script entry point.
		/// </summary>
		/// <param name="engine">The <see cref="Engine" /> instance used to communicate with DataMiner.</param>
		public void Run(Engine engine)
		{
			engine.SetFlag(RunTimeFlags.NoCheckingSets);

			var scriptName = "Clear Layout";
			var tagElementName = "Pre-Code";
			var channelName = "Pre-Code";
			engine.GenerateInformation("START " + scriptName);
			var helper = new PaProfileLoadDomHelper(engine);
			innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");
			var exceptionHelper = new ExceptionHelper(engine, innerDomHelper);

			try
			{
				tagElementName = helper.GetParameterValue<string>("TAG Element");
				channelName = helper.GetParameterValue<string>("Channel Name");
				var channelMatch = helper.GetParameterValue<string>("Channel Match");

				var instanceId = helper.GetParameterValue<string>("InstanceId");
				var instance = innerDomHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
				var status = instance.StatusId;

				// only run this activity if reprovision or deactivate
				if (status.Equals("ready"))
				{
					helper.ReturnSuccess();
					return;
				}

				IDms thisDms = engine.GetDms();
				var tagElement = thisDms.GetElement(tagElementName);
				var engineTag = engine.FindElement(tagElement.Name);
				var allLayoutChannelsTable = tagElement.GetTable(10300);

				var filterColumn = new ColumnFilter { ComparisonOperator = ComparisonOperator.Equal, Value = channelMatch, Pid = 10303 };
				var channelLayoutRows = allLayoutChannelsTable.QueryData(new List<ColumnFilter> { filterColumn });
				if (channelLayoutRows.Any())
				{
					foreach (var row in channelLayoutRows)
					{
						engineTag.SetParameterByPrimaryKey(10353, Convert.ToString(row[0]), NoLayout);
						Thread.Sleep(1000);
					}
				}
				else
				{
					// no channels to clear
					engine.GenerateInformation("Did not find any channels with match: " + channelMatch);
				}

				if (status.Equals("deactivate"))
				{
					helper.TransitionState("deactivate_to_deactivating");
					helper.ReturnSuccess();
					return;
				}
				else if (status.Equals("reprovision"))
				{
					helper.TransitionState("reprovision_to_inprogress");
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
							Description = $"Cannot execute the transition as the current status is unexpected. Current status: {status}",
						},
					};

					helper.Log($"Cannot execute the transition as the status. Current status: {status}", PaLogLevel.Error);
					exceptionHelper.GenerateLog(log);
				}

				engine.GenerateInformation("Successfully executed " + scriptName + " for: " + tagElementName);
				helper.ReturnSuccess();
			}
			catch (Exception ex)
			{
				engine.GenerateInformation("ERROR in clear layout: " + ex);
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
						Description = "Exception while processing Clear Layout",
					},
				};

				exceptionHelper.ProcessException(ex, log);

				helper.Log($"An issue occurred while executing {scriptName} activity for {channelName}: {ex}", PaLogLevel.Error);
				helper.SendErrorMessageToTokenHandler();
			}
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
}