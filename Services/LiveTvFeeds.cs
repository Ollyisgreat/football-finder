using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Tasks;
using XtreamFootball.Configuration;

namespace XtreamFootball.Services
{
    /// <summary>
    /// Writes the football playlist + guide to files in the plugin's data folder and registers them
    /// with Jellyfin Live TV, so nothing has to be copied or reached over the network.
    /// </summary>
    public static class LiveTvFeeds
    {
        public const string TunerName = "Football (Xtream Football Finder)";
        private const string LiveTvConfigKey = "livetv";
        private const string RefreshGuideTaskKey = "RefreshGuide";

        private static string Folder =>
            Plugin.Instance?.DataFolderPath ?? Path.Combine(Path.GetTempPath(), "XtreamFootball");

        public static string M3uPath => Path.Combine(Folder, "football.m3u");

        public static string XmlTvPath => Path.Combine(Folder, "football.xml");

        public static async Task<int> WriteFilesAsync(XtreamService service, CancellationToken ct)
        {
            var c = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var (channels, epg) = await service.GetTeamChannelsAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(M3uPath, XtreamService.BuildM3u(c, channels), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(XmlTvPath, XtreamService.BuildXmlTv(channels, epg), ct).ConfigureAwait(false);
            return channels.Count;
        }

        private static LiveTvOptions GetOptions(IConfigurationManager config) =>
            (LiveTvOptions)config.GetConfiguration(LiveTvConfigKey);

        public static bool IsRegistered(IConfigurationManager config)
        {
            var options = GetOptions(config);
            return (options.TunerHosts ?? Array.Empty<TunerHostInfo>())
                .Any(t => string.Equals(t.Url, M3uPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Adds (or updates) our M3U tuner and XMLTV guide in Jellyfin's Live TV settings.</summary>
        public static void EnsureRegistered(IConfigurationManager config, PluginConfiguration c)
        {
            var options = GetOptions(config);

            var tuners = (options.TunerHosts ?? Array.Empty<TunerHostInfo>()).ToList();
            var tuner = tuners.FirstOrDefault(t => string.Equals(t.Url, M3uPath, StringComparison.OrdinalIgnoreCase));
            if (tuner == null)
            {
                tuner = new TunerHostInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "m3u",
                    Url = M3uPath
                };
                tuners.Add(tuner);
            }

            tuner.FriendlyName = TunerName;
            tuner.UserAgent = c.UserAgent;
            options.TunerHosts = tuners.ToArray();

            var providers = (options.ListingProviders ?? Array.Empty<ListingsProviderInfo>()).ToList();
            var provider = providers.FirstOrDefault(p => string.Equals(p.Path, XmlTvPath, StringComparison.OrdinalIgnoreCase));
            if (provider == null)
            {
                provider = new ListingsProviderInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "xmltv",
                    Path = XmlTvPath
                };
                providers.Add(provider);
            }

            provider.EnableAllTuners = true;
            options.ListingProviders = providers.ToArray();

            config.SaveConfiguration(LiveTvConfigKey, options);
        }

        /// <summary>Starts Jellyfin's own "Refresh Guide" task so new channels appear.</summary>
        public static bool RefreshGuide(ITaskManager taskManager)
        {
            var worker = taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == RefreshGuideTaskKey);
            if (worker == null)
            {
                return false;
            }

            if (worker.State == TaskState.Idle)
            {
                taskManager.Execute(worker, new TaskOptions());
            }

            return true;
        }
    }
}
