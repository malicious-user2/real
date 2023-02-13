using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octokit;
using YouRatta.Common.Proto;

namespace YouRatta.Common.GitHub;

public static class GitHubAPIClient
{
    private static void RetryCommand(Action command, TimeSpan minRetry, TimeSpan maxRetry, Action<string> logger)
    {
        int retryCount = 0;
        while (retryCount < 3)
        {
            try
            {
                command.Invoke();
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                logger.Invoke(ex.Message);
                if (retryCount > 1)
                {
                    throw;
                }
            }
            TimeSpan backOff = APIBackoffHelper.GetRandomBackoff(minRetry, maxRetry);
            Thread.Sleep(backOff);
        }
    }

    public static T? RetryCommand<T>(Func<T> command, TimeSpan minRetry, TimeSpan maxRetry, Action<string> logger)
    {
        int retryCount = 0;
        T? returnValue = default(T?);
        while (retryCount < 3)
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
                if (retryCount > 1)
                {
                    throw;
                }
            }
            TimeSpan backOff = APIBackoffHelper.GetRandomBackoff(minRetry, maxRetry);
            Thread.Sleep(backOff);
        }
        return returnValue;
    }

    public static void DeleteSecret(GitHubActionEnvironment environment, string secretName, Action<string> logger)
    {
        Action deleteSecret = (() =>
        {
            GitHubClient ghClient = new GitHubClient(GitHubConstants.ProductHeader)
            {
                Credentials = new Credentials(environment.ApiToken, AuthenticationType.Bearer)
            };
            ghClient.SetRequestTimeout(GitHubConstants.RequestTimeout);
            IApiConnection apiCon = new ApiConnection(ghClient.Connection);

            RepositorySecretsClient secClient = new RepositorySecretsClient(apiCon);
            string[] repository = environment.EnvGitHubRepository.Split("/");
            secClient.Delete(repository[0], repository[1], secretName).Wait();
        });
        RetryCommand(deleteSecret, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), logger);
    }
}
