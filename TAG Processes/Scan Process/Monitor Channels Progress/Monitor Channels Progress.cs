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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Skyline.DataMiner.Automation;
using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Helpers.Logging;
using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
using Skyline.DataMiner.ExceptionHelper;
using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
using Skyline.DataMiner.Net.Sections;
using TagHelperMethods;

/// <summary>
/// DataMiner Script Class.
/// </summary>
public class Script
{
	private static PaProfileLoadDomHelper innerHelper;
	private DomHelper innerDomHelper;

	/// <summary>
	/// The Script entry point.
	/// </summary>
	/// <param name="engine">Link with SLAutomation process.</param>
	public void Run(Engine engine)
	{
		var scriptName = "Monitor Channels Progress";

		innerHelper = new PaProfileLoadDomHelper(engine);
		innerDomHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

		var exceptionHelper = new ExceptionHelper(engine, innerDomHelper);
		var sharedMethods = new SharedMethods(innerHelper, innerDomHelper);

		engine.GenerateInformation("START " + scriptName);

		var instanceId = innerHelper.GetParameterValue<string>("InstanceId");
		var instance = innerDomHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
		var status = instance.StatusId;

		if (!status.Equals("in_progress"))
		{
			innerHelper.SendErrorMessageToTokenHandler();
			return;
		}

		var scanner = new Scanner
		{
			AssetId = innerHelper.GetParameterValue<string>("Asset ID"),
			InstanceId = instanceId,
			ScanName = innerHelper.GetParameterValue<string>("Scan Name"),
			SourceElement = innerHelper.TryGetParameterValue("Source Element", out string sourceElement) ? sourceElement : String.Empty,
			SourceId = innerHelper.TryGetParameterValue("Source ID", out string sourceId) ? sourceId : String.Empty,
			TagDevice = innerHelper.GetParameterValue<string>("TAG Device"),
			TagElement = innerHelper.GetParameterValue<string>("TAG Element"),
			TagInterface = innerHelper.GetParameterValue<string>("TAG Interface"),
			ScanType = innerHelper.GetParameterValue<string>("Scan Type"),
			Action = innerHelper.GetParameterValue<string>("Action"),
			Channels = innerHelper.TryGetParameterValue("Channels", out List<Guid> channels) ? channels : new List<Guid>(),
		};

		try
		{
			var totalChannels = scanner.Channels.Count;
			var expectedChannels = 0;

			bool CheckStateChange()
			{
				try
				{
					foreach (var channel in scanner.Channels)
					{
						var channelFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(channel));
						var subInstance = innerDomHelper.DomInstances.Read(channelFilter).First();

						if ((scanner.Action == "provision" || scanner.Action == "reprovision") && subInstance.StatusId == "active")
						{
							expectedChannels++;
						}
					}

					return expectedChannels == totalChannels;
				}
				catch (Exception ex)
				{
					innerHelper.Log("Exception thrown while verifying the subprocess: " + ex, PaLogLevel.Error);
					throw;
				}
			}

			if (Retry(CheckStateChange, new TimeSpan(0, 5, 0)))
			{
				if (scanner.Action == "provision" || scanner.Action == "reprovision")
				{
					innerHelper.TransitionState("inprogress_to_active");
					innerHelper.SendFinishMessageToTokenHandler();
				}
			}
			else
			{
				// failed to execute in time
				var log = new Log
				{
					AffectedItem = scriptName,
					AffectedService = scanner.ScanName,
					Timestamp = DateTime.Now,
					ErrorCode = new ErrorCode
					{
						ConfigurationItem = scanner.ScanName,
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Severity = ErrorCode.SeverityType.Warning,
						Source = scriptName,
						Description = "Channel subprocess didn't finish (wrong status on linked instances).",
					},
				};
				exceptionHelper.GenerateLog(log);
				innerHelper.SendErrorMessageToTokenHandler();
			}
		}
		catch (ScriptAbortException)
		{
			// no issue
		}
		catch (Exception ex)
		{
			var log = new Log
			{
				AffectedItem = scriptName,
				AffectedService = scanner.ScanName,
				Timestamp = DateTime.Now,
				ErrorCode = new ErrorCode
				{
					ConfigurationItem = scanner.ScanName,
					ConfigurationType = ErrorCode.ConfigType.Automation,
					Severity = ErrorCode.SeverityType.Warning,
					Source = scriptName,
				},
			};
			exceptionHelper.ProcessException(ex, log);
			innerHelper.SendErrorMessageToTokenHandler();
			throw;
		}
	}

	// <summary>
	// Retry until success or until timeout.
	// </summary>
	// <param name="func">Operation to retry.</param>
	// <param name="timeout">Max TimeSpan during which the operation specified in <paramref name="func"/> can be retried.</param>
	// <returns><c>true</c> if one of the retries succeeded within the specified <paramref name="timeout"/>. Otherwise <c>false</c>.</returns>
	private bool Retry(Func<bool> func, TimeSpan timeout)
	{
		bool success;

		Stopwatch sw = new Stopwatch();
		sw.Start();

		do
		{
			success = func();
			if (!success)
			{
				Thread.Sleep(5000);
			}
		}
		while (!success && sw.Elapsed <= timeout);
		return success;
	}
}