using MediaBrowser.Model.Plugins;

namespace XtreamFootball.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>Xtream server, e.g. http://line.example.com:8080</summary>
        public string ServerUrl { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        /// <summary>One team per line. Use | for alternative names, e.g. "Man Utd|Manchester United".</summary>
        public string Teams { get; set; } = string.Empty;

        /// <summary>Comma separated. Only channels whose category name contains one of these are searched. Empty = all.</summary>
        public string CategoryFilter { get; set; } = string.Empty;

        /// <summary>Comma separated. Programmes whose title contains one of these are ignored.</summary>
        public string ExcludeWords { get; set; } = "highlights, review, classic, replay, rewind, magazine, preview, goals";

        public int LookaheadDays { get; set; } = 3;

        public bool SearchDescriptions { get; set; } = true;

        /// <summary>"ts" or "m3u8".</summary>
        public string StreamFormat { get; set; } = "ts";

        public string UserAgent { get; set; } = "VLC/3.0.20 LibVLC/3.0.20";

        public int CacheHours { get; set; } = 6;

        /// <summary>Secret used by the M3U / XMLTV links that Jellyfin Live TV reads.</summary>
        public string AccessKey { get; set; } = string.Empty;
    }
}
