using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Octokit;
using YouRatta.Common;
using YouRatta.Common.Configurations;
using YouRatta.Common.GitHub;
using YouRatta.Common.Proto;
using YouRatta.ConflictMonitor.MilestoneData;
using YouRatta.ConflictMonitor.Workflow;
using static YouRatta.Common.Proto.MilestoneActionIntelligence.Types;

namespace YouRatta.ConflictMonitor.MilestoneCall;

internal class CallHandler
{
    private readonly List<string> _logBuilder;

    internal CallHandler()
    {
        _logBuilder = new List<string>();
    }

    public static T? RetryCommand<T>(Func<T> command, TimeSpan minRetry, TimeSpan maxRetry, Action<string> logger)
    {
        int retryCount = 0;
        T? returnValue = default(T?);
        while (retryCount < 10)
        {
            try
            {
                returnValue = command.Invoke();
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                logger.Invoke(ex.Message);
                if (retryCount > 9)
                {
                    throw;
                }
            }
            TimeSpan backOff = APIBackoffHelper.GetRandomBackoff(minRetry, maxRetry);
            Thread.Sleep(backOff);
        }
        return returnValue;
    }

    internal GitHubActionEnvironment GetGithubActionEnvironment(YouRattaConfiguration appConfig, GitHubEnvironment environment, ConflictMonitorWorkflow workflow)
    {
        GitHubActionEnvironment actionEnvironment = environment.GetActionEnvironment();
        if (!appConfig.ActionCutOuts.DisableConflictMonitorGitHubOperations && workflow.GitHubToken != null && workflow.ApiToken != null)
        {
            ResourceRateLimit? ghRateLimit = default;
            Func<ResourceRateLimit> getResourceRateLimit = (() =>
            {
                GitHubClient ghClient = new GitHubClient(GitHubConstants.ProductHeader);
                ghClient.Credentials = new Credentials(workflow.GitHubToken, AuthenticationType.Bearer);
                return ghClient.RateLimit.GetRateLimits().Result.Resources;
            });
            ghRateLimit = RetryCommand<ResourceRateLimit>(getResourceRateLimit, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), AppendLog);
            if (ghRateLimit != null)
            {
                actionEnvironment.RateLimitCoreRemaining = ghRateLimit.Core.Remaining;
                actionEnvironment.RateLimitCoreLimit = ghRateLimit.Core.Limit;
                actionEnvironment.RateLimitCoreReset = ghRateLimit.Core.Reset.ToUnixTimeSeconds();
            }
            actionEnvironment.GitHubToken = workflow.GitHubToken;
            actionEnvironment.ApiToken = workflow.ApiToken;
        }
        return actionEnvironment;
    }

    internal string GetStoredTokenResponse(YouRattaConfiguration appConfig, ConflictMonitorWorkflow workflow)
    {
        if (!appConfig.ActionCutOuts.DisableStoredTokenResponseDiscovery && workflow.StoredTokenResponse != null)
        {
            return workflow.StoredTokenResponse;
        }
        return string.Empty;
    }

    internal string GetAppClientId(YouRattaConfiguration appConfig, ConflictMonitorWorkflow workflow)
    {
        if (workflow.ClientId != null)
        {
            return workflow.ClientId;
        }
        return string.Empty;
    }

    internal string GetAppClientSecret(YouRattaConfiguration appConfig, ConflictMonitorWorkflow workflow)
    {
        if (workflow.ClientSecret != null)
        {
            return workflow.ClientSecret;
        }
        return string.Empty;
    }

    internal MilestoneActionIntelligence GetMilestoneActionIntelligence(YouRattaConfiguration appConfig, MilestoneIntelligenceRegistry milestoneIntelligence)
    {
        MilestoneActionIntelligence actionIntelligence = new MilestoneActionIntelligence();
        actionIntelligence.InitialSetup = new InitialSetupActionIntelligence();
        actionIntelligence.InitialSetup.Condition = milestoneIntelligence.InitialSetup.Condition;
        actionIntelligence.InitialSetup.ProcessId = milestoneIntelligence.InitialSetup.ProcessId;
        actionIntelligence.YouTubeSync = new YouTubeSyncActionIntelligence();
        actionIntelligence.YouTubeSync.Condition = milestoneIntelligence.YouTubeSync.Condition;
        actionIntelligence.YouTubeSync.ProcessId = milestoneIntelligence.YouTubeSync.ProcessId;

        return actionIntelligence;
    }

    internal void UpdateInitialSetupMilestoneIntelligence(YouRattaConfiguration appConfig, MilestoneIntelligenceRegistry milestoneIntelligence, InitialSetupActionIntelligence actionIntelligence)
    {
        long updateTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        milestoneIntelligence.InitialSetup.Condition = actionIntelligence.Condition;
        milestoneIntelligence.InitialSetup.ProcessId = actionIntelligence.ProcessId;
        if (milestoneIntelligence.InitialSetup.StartTime == 0)
        {
            milestoneIntelligence.InitialSetup.StartTime = updateTime;
        }
        milestoneIntelligence.InitialSetup.LastUpdate = updateTime;
    }

    internal void UpdateYouTubeSyncActionIntelligence(YouRattaConfiguration appConfig, MilestoneIntelligenceRegistry milestoneIntelligence, YouTubeSyncActionIntelligence actionIntelligence)
    {
        long updateTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        milestoneIntelligence.YouTubeSync.Condition = actionIntelligence.Condition;
        milestoneIntelligence.YouTubeSync.ProcessId = actionIntelligence.ProcessId;
        if (milestoneIntelligence.YouTubeSync.StartTime == 0)
        {
            milestoneIntelligence.YouTubeSync.StartTime = updateTime;
        }
        milestoneIntelligence.YouTubeSync.LastUpdate = updateTime;
    }

    internal string GetConfigJson(YouRattaConfiguration appConfig)
    {
        return JsonConvert.SerializeObject(appConfig, Formatting.None);
    }

    internal void LogMessage(string message)
    {
        StringBuilder lineBuilder = new StringBuilder();
        lineBuilder.Append("[");
        lineBuilder.Append(DateTime.Now.ToString(TimeConstants.ZuluTimeFormat, CultureInfo.InvariantCulture));
        lineBuilder.Append("] ");
        lineBuilder.Append("[Message] ");
        lineBuilder.Append(message);
        AppendLog(lineBuilder.ToString());
    }

    internal void AppendLog(string message)
    {
        lock (_logBuilder)
        {
            _logBuilder.Add(message);
        }
    }

    internal List<string> GetLogs()
    {
        return _logBuilder;
    }

    internal void ClearLogs()
    {
        _logBuilder.Clear();
    }
}
