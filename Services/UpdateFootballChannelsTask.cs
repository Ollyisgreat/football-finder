using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace XtreamFootball.Services
{
    /// <summary>
    /// Every few hours: rebuild the football channel list for the saved teams and refresh the Live TV guide.
    /// Only does anything once Live TV has been set up from the plugin page.
    /// </summary>
    public class UpdateFootballChannelsTask : IScheduledTask
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UpdateFootballChannelsTask> _logger;
        private readonly IConfigurationManager _config;
        private readonly ITaskManager _taskManager;

        public UpdateFootballChannelsTask(
            IHttpClientFactory httpClientFactory,
            ILogger<UpdateFootballChannelsTask> logger,
            IConfigurationManager config,
            ITaskManager taskManager)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _config = config;
            _taskManager = taskManager;
        }

        public string Name => "Update football channels";

        public string Key => "XtreamFootballUpdateChannels";

        public string Description => "Finds upcoming matches for your saved teams and updates the Football Live TV channels.";

        public string Category => "Live TV";

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var c = Plugin.Instance?.Configuration;
            if (c == null || string.IsNullOrWhiteSpace(c.ServerUrl) || !LiveTvFeeds.IsRegistered(_config))
            {
                return;
            }

            progress.Report(10);
            var service = new XtreamService(_httpClientFactory, _logger);
            var count = await LiveTvFeeds.WriteFilesAsync(service, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("XtreamFootball: {Count} football channels written for Live TV", count);
            progress.Report(80);
            LiveTvFeeds.RefreshGuide(_taskManager);
            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }
    }
}
