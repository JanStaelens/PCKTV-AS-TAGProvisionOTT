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
    /// <summary>
    /// The Script entry point.
    /// </summary>
    /// <param name="engine">Link with SLAutomation process.</param>
    public void Run(Engine engine)
    {
        var scriptName = "PA_TAG_Monitor Channels Progress";
		var scanName = String.Empty;

        var helper = new PaProfileLoadDomHelper(engine);
        var domHelper = new DomHelper(engine.SendSLNetMessages, "process_automation");

        var exceptionHelper = new ExceptionHelper(engine, domHelper);
        var sharedMethods = new SharedMethods(helper, domHelper);

        engine.GenerateInformation("START " + scriptName);

        var instanceId = helper.GetParameterValue<string>("InstanceId (TAG Scan)");
        var instance = domHelper.DomInstances.Read(DomInstanceExposers.Id.Equal(new DomInstanceId(Guid.Parse(instanceId)))).First();
        var status = instance.StatusId;

        if (!status.Equals("in_progress"))
        {
            helper.SendErrorMessageToTokenHandler();
            return;
        }

        try
        {
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
			var totalChannels = scanner.Channels.Count;
            
            bool CheckStateChange()
            {
                try
                {
					var finishedChannels = 0;

					foreach (var channel in scanner.Channels)
                    {
                        var channelFilter = DomInstanceExposers.Id.Equal(new DomInstanceId(channel));
                        var subInstance = domHelper.DomInstances.Read(channelFilter).First();

                        if (subInstance.StatusId == "active")
                        {
                            finishedChannels++;
                        }
                    }

					engine.GenerateInformation($"finished channels: {finishedChannels} vs total: {totalChannels}");
                    return finishedChannels == totalChannels;
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
							ConfigurationItem = scriptName + " Script",
							ConfigurationType = ErrorCode.ConfigType.Automation,
							Source = "CheckStateChange()",
							Severity = ErrorCode.SeverityType.Critical,
						},
					};
					exceptionHelper.ProcessException(ex, log);

					helper.Log("Exception thrown while verifying the subprocess: " + ex, PaLogLevel.Error);
                    throw;
                }
            }

            if (this.Retry(CheckStateChange, new TimeSpan(0, 5, 0)))
            {
                helper.TransitionState("inprogress_to_active");
                helper.SendFinishMessageToTokenHandler();
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
						ConfigurationItem = scriptName + " Script",
						ConfigurationType = ErrorCode.ConfigType.Automation,
						Severity = ErrorCode.SeverityType.Warning,
						Source = "Retry condition",
						Description = "Channel subprocess didn't finish (wrong status on linked instances).",
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
					Source = "Run() method - exception",
				},
			};
			exceptionHelper.ProcessException(ex, log);
            helper.SendErrorMessageToTokenHandler();
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