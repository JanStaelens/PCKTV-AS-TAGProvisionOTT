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
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

dd/mm/2023	1.0.0.1		XXX, Skyline	Initial version
****************************************************************************
*/

namespace Script
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using System.Web;
	using Skyline.DataMiner.Automation;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
	using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
	using Skyline.DataMiner.ExceptionHelper;
	using Skyline.DataMiner.Core.DataMinerSystem.Automation;
	using Skyline.DataMiner.Core.DataMinerSystem.Common;
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
			var scriptName = "Monitor Scanners";

			var helper = new PaProfileLoadDomHelper(engine);
			innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

			var exceptionHelper = new ExceptionHelper(engine, innerDomHelper);

			engine.GenerateInformation("START " + scriptName);

			var channelName = helper.GetParameterValue<string>("Provision Name");

			try
			{
				var action = helper.GetParameterValue<string>("Action");

				var scanners = helper.GetParameterValue<List<Guid>>("TAG Scanners");
				Dictionary<Guid, bool> scannersComplete = new Dictionary<Guid, bool>();

				bool CheckScanners()
				{
					try
					{
						foreach (var scanner in scanners)
						{
							var scannerFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(scanner));
							var scannerInstance = innerDomHelper.DomInstances.Read(scannerFilter).First();

							if (scannerInstance.StatusId == "active" || scannerInstance.StatusId == "complete")
							{
								scannersComplete[scannerInstance.ID.Id] = true;
							}
							else
							{
								scannersComplete[scannerInstance.ID.Id] = false;
							}
						}

						if (scannersComplete.Count(x => x.Value) == scanners.Count)
						{
							return true;
						}

						return false;
					}
					catch (Exception ex)
					{
						engine.Log("Exception thrown while verifying the scan subprocess: " + ex);
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
								Description = "Exception while verifying scan processes in " + scriptName,
							},
						};

						exceptionHelper.ProcessException(ex, log);
						throw;
					}
				}

				if (Retry(CheckScanners, new TimeSpan(0, 10, 0)))
				{
					if (action == "provision" || action == "reprovision")
					{
						helper.TransitionState("inprogress_to_active");
					}
					else if (action == "deactivate")
					{
						helper.TransitionState("deactivating_to_complete");
					}

					helper.SendFinishMessageToTokenHandler();
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
							Severity = ErrorCode.SeverityType.Major,
							Description = "Scanners did not complete in time.",
						},
					};

					exceptionHelper.GenerateLog(log);
				}
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
	}
}