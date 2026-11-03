using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using XtreamFootball.Configuration;

namespace XtreamFootball
{
    /// <summary>
    /// Xtream Football Finder: searches an Xtream Codes IPTV account for football matches.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static readonly Guid PluginGuid = new Guid("5c1a2f0e-7b3d-4e8a-9f21-6d4b8c0e3a71");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            if (string.IsNullOrWhiteSpace(Configuration.AccessKey))
            {
                Configuration.AccessKey = Guid.NewGuid().ToString("N");
                SaveConfiguration();
            }
        }

        public static Plugin? Instance { get; private set; }

        public override string Name => "Xtream Football Finder";

        public override Guid Id => PluginGuid;

        public override string Description =>
            "Connect an Xtream Codes IPTV account and find which channel is showing your football team's match.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "XtreamFootball",
                    EmbeddedResourcePath = "XtreamFootball.Configuration.configPage.html",
                    EnableInMainMenu = true
                }
            };
        }
    }
}
