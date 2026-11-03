using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using XtreamFootball.Configuration;
using XtreamFootball.Services;

namespace XtreamFootball.Api
{
    [ApiController]
    [Route("XtreamFootball")]
    public class XtreamFootballController : ControllerBase
    {
        private readonly XtreamService _service;
        private readonly ILogger<XtreamFootballController> _logger;
        private readonly IConfigurationManager _configManager;
        private readonly ITaskManager _taskManager;

        public XtreamFootballController(
            IHttpClientFactory httpClientFactory,
            ILogger<XtreamFootballController> logger,
            IConfigurationManager configManager,
            ITaskManager taskManager)
        {
            _logger = logger;
            _configManager = configManager;
            _taskManager = taskManager;
            _service = new XtreamService(httpClientFactory, logger);
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>Checks the IPTV login. Admins only.</summary>
        [HttpGet("Test")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces("application/json")]
        public async Task<ActionResult<AccountInfo>> Test(CancellationToken cancellationToken)
        {
            try
            {
                return await _service.TestAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new AccountInfo { Error = Friendly(ex) };
            }
        }

        /// <summary>Finds matches for a team. Admins only (results contain stream links with your IPTV login).</summary>
        [HttpGet("Search")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces("application/json")]
        public async Task<ActionResult<SearchResponse>> Search(
            [FromQuery] string? q,
            [FromQuery] int? days,
            [FromQuery] bool? desc,
            [FromQuery] bool refresh,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _service
                    .SearchAsync(q ?? string.Empty, days ?? Config.LookaheadDays, desc, refresh, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new SearchResponse { Error = Friendly(ex) };
            }
        }

        /// <summary>
        /// One-tap Live TV: optionally adds a team to the saved list, writes the football playlist/guide files,
        /// registers them in Jellyfin Live TV and refreshes the guide. Admins only.
        /// </summary>
        [HttpPost("SetupLiveTv")]
        [Authorize(Policy = "RequiresElevation")]
        [Produces("application/json")]
        public async Task<ActionResult<SetupResult>> SetupLiveTv([FromQuery] string? addTeam, CancellationToken cancellationToken)
        {
            var result = new SetupResult();
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    result.Error = "Plugin not loaded - restart Jellyfin.";
                    return result;
                }

                var c = plugin.Configuration;
                XtreamService.EnsureConfigured(c);

                if (!string.IsNullOrWhiteSpace(addTeam))
                {
                    var lines = TeamMatcher.SplitTeamLines(c.Teams);
                    var norm = TeamMatcher.Normalize(addTeam);
                    if (!lines.Exists(l => TeamMatcher.Normalize(l) == norm))
                    {
                        lines.Add(addTeam.Trim());
                        c.Teams = string.Join("\n", lines);
                        plugin.SaveConfiguration();
                    }
                }

                result.Teams = TeamMatcher.SplitTeamLines(c.Teams);
                if (result.Teams.Count == 0)
                {
                    result.Error = "Add at least one team first (search for it, then tap \"Add to Live TV\").";
                    return result;
                }

                result.Channels = await LiveTvFeeds.WriteFilesAsync(_service, cancellationToken).ConfigureAwait(false);
                LiveTvFeeds.EnsureRegistered(_configManager, c);
                result.GuideRefreshStarted = LiveTvFeeds.RefreshGuide(_taskManager);
                result.Ok = true;
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("XtreamFootball Live TV setup failed: {Message}", ex.ToString());
                result.Error = Friendly(ex);
                return result;
            }
        }

        /// <summary>M3U playlist of channels showing your saved teams. Add as an M3U tuner in Jellyfin Live TV.</summary>
        [HttpGet("playlist.m3u")]
        [AllowAnonymous]
        public async Task<ActionResult> Playlist([FromQuery] string? key, CancellationToken cancellationToken)
        {
            if (!KeyOk(key))
            {
                return Unauthorized();
            }

            try
            {
                var (channels, _) = await _service.GetTeamChannelsAsync(cancellationToken).ConfigureAwait(false);
                return Content(XtreamService.BuildM3u(Config, channels), "audio/x-mpegurl");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("XtreamFootball playlist failed: {Message}", ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, Friendly(ex));
            }
        }

        /// <summary>XMLTV guide for the channels in playlist.m3u. Add as an XMLTV guide provider in Jellyfin Live TV.</summary>
        [HttpGet("guide.xml")]
        [AllowAnonymous]
        public async Task<ActionResult> Guide([FromQuery] string? key, CancellationToken cancellationToken)
        {
            if (!KeyOk(key))
            {
                return Unauthorized();
            }

            try
            {
                var (channels, epg) = await _service.GetTeamChannelsAsync(cancellationToken).ConfigureAwait(false);
                return Content(XtreamService.BuildXmlTv(channels, epg), "application/xml");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("XtreamFootball guide failed: {Message}", ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, Friendly(ex));
            }
        }

        private static bool KeyOk(string? key)
        {
            var expected = Config.AccessKey;
            return !string.IsNullOrEmpty(expected) && string.Equals(key, expected, StringComparison.Ordinal);
        }

        private static string Friendly(Exception ex)
        {
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return "The IPTV server took too long to answer. Try again in a minute.";
            }

            if (ex is HttpRequestException && ex.InnerException != null)
            {
                return "Could not reach the IPTV server: " + ex.InnerException.Message;
            }

            return ex.Message;
        }
    }
}
