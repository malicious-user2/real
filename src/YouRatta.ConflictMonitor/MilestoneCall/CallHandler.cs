using System;
using System.Globalization;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Octokit;
using YouRatta.Common.Configurations;
using YouRatta.Common.GitHub;
using YouRatta.Common.Proto;
using YouRatta.ConflictMonitor.MilestoneData;
using YouRatta.ConflictMonitor.Workflow;
using static YouRatta.Common.Proto.MilestoneActionIntelligence.Types;

namespace YouRatta.ConflictMonitor.MilestoneCall;

internal class CallHandler
{
    private readonly StringBuilder _logBuilder = new StringBuilder();

    internal CallHandler()
    {
        _logBuilder = new StringBuilder();
    }

    internal GitHubActionEnvironment GetGithubActionEnvironment(YouRattaConfiguration appConfig, GitHubEnvironment environment, ConflictMonitorWorkflow workflow)
    {
        GitHubActionEnvironment actionEnvironment = environment.GetActionEnvironment();
        if (!appConfig.ActionCutOuts.DisableConflictMonitorGitHubOperations && workflow.GitHubToken != null && workflow.ApiToken != null)
        {
            GitHubClient ghClient = new GitHubClient(GitHubConstants.ProductHeader);
            ghClient.Credentials = new Credentials(workflow.GitHubToken, AuthenticationType.Bearer);
            ResourceRateLimit ghRateLimit = ghClient.RateLimit.GetRateLimits().Result.Resources;

            actionEnvironment.RateLimitCoreRemaining = ghRateLimit.Core.Remaining;
            actionEnvironment.RateLimitCoreLimit = ghRateLimit.Core.Limit;
            actionEnvironment.RateLimitCoreReset = ghRateLimit.Core.Reset.ToUnixTimeSeconds();
            actionEnvironment.GitHubToken = workflow.GitHubToken;
            actionEnvironment.ApiToken = workflow.ApiToken;
        }
        return actionEnvironment;
    }

    internal ClientSecrets GetClientSecrets(YouRattaConfiguration appConfig, ConflictMonitorWorkflow workflow, ILogger logger)
    {
        ClientSecrets secrets = new ClientSecrets();
        if (!appConfig.ActionCutOuts.DisableYouTubeClientSecretsDiscovery && workflow.YouTubeClientSecrets != null)
        {
            try
            {
                secrets = JsonParser.Default.Parse<ClientSecrets>(workflow.YouTubeClientSecrets);
            }
            catch (InvalidJsonException e)
            {
                logger.LogError($"InvalidJson on GetClientSecrets: {e.Message}");
            }
        }
        else
        {
            secrets.InstalledClientSecrets = new InstalledClientSecrets();
        }
        return secrets;
    }

    internal MilestoneActionIntelligence GetMilestoneActionIntelligence(YouRattaConfiguration appConfig, MilestoneIntelligenceRegistry milestoneIntelligence)
    {
        MilestoneActionIntelligence actionIntelligence = new MilestoneActionIntelligence();
        actionIntelligence.InitialSetup = new InitialSetupActionIntelligence();
        actionIntelligence.InitialSetup.Condition = milestoneIntelligence.InitialSetup.Condition;

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

    internal string GetConfigJson(YouRattaConfiguration appConfig)
    {
        return JsonConvert.SerializeObject(appConfig, Formatting.None);
    }

    internal void AppendLog(string message)
    {
        lock (_logBuilder)
        {
            _logBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}", message));
        }
    }

    internal string GetLogs()
    {
        return _logBuilder.ToString();
    }

    internal void ClearLogs()
    {
        _logBuilder.Clear();
    }
}
