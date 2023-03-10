using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YouRatta.Common.Configurations;
using YouRatta.ConflictMonitor.MilestoneData;
using static YouRatta.Common.Proto.MilestoneActionIntelligence.Types;

namespace YouRatta.ConflictMonitor.MilestoneProcess;

internal class MilestoneLifetimeManager : IDisposable
{
    private readonly WebApplication _webApp;
    private readonly MilestoneIntelligenceRegistry _milestoneIntelligence;
    private readonly object _lock = new object();
    private bool _disposed;
    private readonly CancellationTokenSource _stopTokenSource;

    internal MilestoneLifetimeManager(WebApplication webApp, MilestoneIntelligenceRegistry milestoneIntelligence)
    {
        _webApp = webApp;
        _milestoneIntelligence = milestoneIntelligence;
        _stopTokenSource = new CancellationTokenSource();
        StartLoop();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _stopTokenSource.Cancel();
                _disposed = true;
            }
        }
    }

    private void StartLoop()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                Task.Run(() =>
                {
                    while (!_disposed && !_stopTokenSource.Token.IsCancellationRequested)
                    {
                        Task.Delay(MilestoneLifetimeConstants.LifetimeCheckInterval, _stopTokenSource.Token);
                        ProcessLifetimeManager();
                    }
                    _stopTokenSource.Dispose();
                });
            }
        }
    }

    private void ProcessLifetimeManager()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                IOptions<YouRattaConfiguration>? options = _webApp.Services.GetService<IOptions<YouRattaConfiguration>>();
                if (options == null) return;
                MilestoneLifetimeConfiguration config = options.Value.MilestoneLifetime;
                if (config == null) return;
                ILogger<MilestoneLifetimeManager>? logger = _webApp.Services.GetService<ILogger<MilestoneLifetimeManager>>();
                if (logger == null) return;
                foreach (BaseMilestoneIntelligence milestoneIntelligence in _milestoneIntelligence.Milestones)
                {
                    if ((milestoneIntelligence.Condition == MilestoneCondition.MilestoneRunning ||
                        milestoneIntelligence.Condition == MilestoneCondition.MilestoneCompleted) &&
                    milestoneIntelligence.LastUpdate != 0 &&
                    milestoneIntelligence.StartTime != 0 &&
                    milestoneIntelligence.ProcessId != 0)
                    {

                        long dwellTime = DateTimeOffset.Now.ToUnixTimeSeconds() - milestoneIntelligence.LastUpdate;
                        long runTime = DateTimeOffset.Now.ToUnixTimeSeconds() - milestoneIntelligence.StartTime;
                        if (dwellTime > config.MaxUpdateDwellTime ||
                            runTime > config.MaxRunTime)
                        {
                            Process milestoneProcess = Process.GetProcessById(milestoneIntelligence.ProcessId);
                            if (milestoneProcess != null && !milestoneProcess.HasExited)
                            {
                                milestoneProcess.Kill();
                                logger.LogWarning($"Milestone {milestoneIntelligence.GetType().Name} was forcefully killed");
                                milestoneIntelligence.Condition = MilestoneCondition.MilestoneFailed;
                            }
                        }
                    }
                }
            }
        }
    }
}
