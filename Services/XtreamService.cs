using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using XtreamFootball.Configuration;

namespace XtreamFootball.Services
{
    /// <summary>
    /// Talks to the Xtream Codes API (player_api.php / xmltv.php) and keeps a shared in-memory cache.
    /// </summary>
    public sealed class XtreamService
    {
        private const int MaxDescriptionLength = 400;

        private static readonly SemaphoreSlim StreamLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim EpgLock = new SemaphoreSlim(1, 1);

        private static string _streamsKey = string.Empty;
        private static StreamCatalog? _streams;
        private static string _epgKey = string.Empty;
        private static EpgData? _epg;
        private static string? _lastEpgError;
        private static DateTime _lastEpgErrorUtc;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;

        public XtreamService(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private static PluginConfiguration Config =>
            Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // ---------------------------------------------------------------- helpers

        public static string BaseUrl(PluginConfiguration c)
        {
            var url = (c.ServerUrl ?? string.Empty).Trim();
            if (url.Length == 0)
            {
                return string.Empty;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            // People often paste a full player_api / get.php link - keep only scheme://host:port
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }

            return url.TrimEnd('/');
        }

        private static string Credentials(PluginConfiguration c) =>
            "username=" + Uri.EscapeDataString(c.Username.Trim()) + "&password=" + Uri.EscapeDataString(c.Password.Trim());

        private static string CacheKey(PluginConfiguration c) =>
            BaseUrl(c) + "|" + c.Username.Trim() + "|" + c.Password.Trim();

        public static void EnsureConfigured(PluginConfiguration c)
        {
            if (string.IsNullOrWhiteSpace(c.ServerUrl) || string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrWhiteSpace(c.Password))
            {
                throw new InvalidOperationException("Enter your Xtream server URL, username and password and press Save first.");
            }
        }

        public static string StreamUrl(PluginConfiguration c, string streamId)
        {
            var ext = string.Equals(c.StreamFormat, "m3u8", StringComparison.OrdinalIgnoreCase) ? "m3u8" : "ts";
            return BaseUrl(c) + "/live/" + Uri.EscapeDataString(c.Username.Trim()) + "/"
                + Uri.EscapeDataString(c.Password.Trim()) + "/" + Uri.EscapeDataString(streamId) + "." + ext;
        }

        private HttpClient CreateClient(TimeSpan timeout)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = timeout;
            return client;
        }

        private static HttpRequestMessage Request(string url, PluginConfiguration c)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(c.UserAgent))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", c.UserAgent.Trim());
            }

            return req;
        }

        private async Task<JsonElement> GetJsonAsync(string url, PluginConfiguration c, CancellationToken ct)
        {
            using var client = CreateClient(TimeSpan.FromMinutes(3));
            using var req = Request(url, c);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException("IPTV server answered HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase
                    + ". Check the server URL, username and password.");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new HttpRequestException("IPTV server did not return valid data. Check the server URL (it should look like http://host:port).");
            }
        }

        private static string Str(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
            {
                return string.Empty;
            }

            switch (v.ValueKind)
            {
                case JsonValueKind.String:
                    return v.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    return v.GetRawText();
                case JsonValueKind.True:
                    return "1";
                case JsonValueKind.False:
                    return "0";
                default:
                    return string.Empty;
            }
        }

        // ---------------------------------------------------------------- account

        public async Task<AccountInfo> TestAsync(CancellationToken ct)
        {
            var c = Config;
            EnsureConfigured(c);
            var root = await GetJsonAsync(BaseUrl(c) + "/player_api.php?" + Credentials(c), c, ct).ConfigureAwait(false);
            var info = new AccountInfo();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("user_info", out var user))
            {
                info.Error = "Unexpected reply from server - is this an Xtream Codes server?";
                return info;
            }

            info.Ok = Str(user, "auth") == "1";
            info.Status = Str(user, "status");
            info.ActiveConnections = Str(user, "active_cons");
            info.MaxConnections = Str(user, "max_connections");
            var exp = Str(user, "exp_date");
            if (long.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
            {
                info.Expires = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                info.Expires = "never / unknown";
            }

            if (!info.Ok)
            {
                info.Error = "Login rejected by the IPTV server (wrong username/password or account expired).";
                return info;
            }

            var catalog = await GetStreamsAsync(true, ct).ConfigureAwait(false);
            info.LiveChannels = catalog.Streams.Count;
            return info;
        }

        // ---------------------------------------------------------------- channels

        public async Task<StreamCatalog> GetStreamsAsync(bool forceRefresh, CancellationToken ct)
        {
            var c = Config;
            EnsureConfigured(c);
            var key = CacheKey(c);
            var maxAge = TimeSpan.FromHours(Math.Max(1, c.CacheHours));

            await StreamLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _streams != null && _streamsKey == key && DateTime.UtcNow - _streams.LoadedUtc < maxAge)
                {
                    return _streams;
                }

                var baseApi = BaseUrl(c) + "/player_api.php?" + Credentials(c);

                var categories = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    var cats = await GetJsonAsync(baseApi + "&action=get_live_categories", c, ct).ConfigureAwait(false);
                    if (cats.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cat in cats.EnumerateArray())
                        {
                            var id = Str(cat, "category_id");
                            if (id.Length > 0)
                            {
                                categories[id] = Str(cat, "category_name");
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("XtreamFootball: could not load categories: {Message}", ex.Message);
                }

                var streamsJson = await GetJsonAsync(baseApi + "&action=get_live_streams", c, ct).ConfigureAwait(false);
                if (streamsJson.ValueKind != JsonValueKind.Array)
                {
                    throw new HttpRequestException("Server did not return a channel list. Check your login details.");
                }

                var list = new List<LiveStream>();
                foreach (var s in streamsJson.EnumerateArray())
                {
                    var id = Str(s, "stream_id");
                    if (id.Length == 0)
                    {
                        continue;
                    }

                    var catId = Str(s, "category_id");
                    categories.TryGetValue(catId, out var catName);
                    int.TryParse(Str(s, "num"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num);
                    var name = Str(s, "name").Trim();
                    list.Add(new LiveStream
                    {
                        Id = id,
                        Name = name,
                        NormName = TeamMatcher.Normalize(name),
                        EpgId = Str(s, "epg_channel_id").Trim(),
                        Logo = Str(s, "stream_icon"),
                        CategoryId = catId,
                        CategoryName = catName ?? string.Empty,
                        NormCategory = TeamMatcher.Normalize(catName),
                        Number = num
                    });
                }

                _streams = new StreamCatalog { Streams = list, LoadedUtc = DateTime.UtcNow };
                _streamsKey = key;
                _logger.LogInformation("XtreamFootball: loaded {Count} live channels", list.Count);
                return _streams;
            }
            finally
            {
                StreamLock.Release();
            }
        }

        // ---------------------------------------------------------------- EPG

        public async Task<EpgData?> GetEpgAsync(StreamCatalog catalog, bool forceRefresh, CancellationToken ct)
        {
            var c = Config;
            var key = CacheKey(c);
            var maxAge = TimeSpan.FromHours(Math.Max(1, c.CacheHours));

            await EpgLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _epg != null && _epgKey == key && DateTime.UtcNow - _epg.LoadedUtc < maxAge)
                {
                    return _epg;
                }

                // Don't hammer a failing EPG on every search; retry after 15 minutes (or on manual refresh).
                if (!forceRefresh && _lastEpgError != null && DateTime.UtcNow - _lastEpgErrorUtc < TimeSpan.FromMinutes(15))
                {
                    return _epg != null && _epgKey == key ? _epg : null;
                }

                var wanted = new HashSet<string>(
                    catalog.Streams.Where(s => s.EpgId.Length > 0).Select(s => s.EpgId),
                    StringComparer.OrdinalIgnoreCase);

                var tempFile = Path.Combine(Path.GetTempPath(), "xtreamfootball-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    using (var client = CreateClient(TimeSpan.FromMinutes(10)))
                    using (var req = Request(BaseUrl(c) + "/xmltv.php?" + Credentials(c), c))
                    using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException("EPG download failed: HTTP " + (int)resp.StatusCode);
                        }

                        await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        await using var file = File.Create(tempFile);
                        await net.CopyToAsync(file, ct).ConfigureAwait(false);
                    }

                    var now = DateTime.UtcNow;
                    var windowStart = now.AddHours(-6);
                    var windowEnd = now.AddDays(8);
                    var programmes = await Task.Run(() => ParseXmlTv(tempFile, wanted, windowStart, windowEnd), ct).ConfigureAwait(false);

                    _epg = new EpgData { Programmes = programmes, LoadedUtc = DateTime.UtcNow };
                    _epgKey = key;
                    _lastEpgError = null;
                    _logger.LogInformation("XtreamFootball: loaded {Count} EPG programmes", programmes.Count);
                    return _epg;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _lastEpgError = ex.Message;
                    _lastEpgErrorUtc = DateTime.UtcNow;
                    _logger.LogWarning("XtreamFootball: EPG load failed: {Message}", ex.Message);
                    return _epg != null && _epgKey == key ? _epg : null;
                }
                finally
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch (Exception)
                    {
                        // temp file clean-up is best effort
                    }
                }
            }
            finally
            {
                EpgLock.Release();
            }
        }

        public static string? LastEpgError => _lastEpgError;

        private List<EpgProgramme> ParseXmlTv(string path, HashSet<string> wanted, DateTime windowStart, DateTime windowEnd)
        {
            var result = new List<EpgProgramme>();
            using var fileStream = File.OpenRead(path);
            Stream input = fileStream;

            // Some servers send gzip without saying so.
            var b1 = fileStream.ReadByte();
            var b2 = fileStream.ReadByte();
            fileStream.Position = 0;
            if (b1 == 0x1f && b2 == 0x8b)
            {
                input = new GZipStream(fileStream, CompressionMode.Decompress);
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                CheckCharacters = false,
                XmlResolver = null
            };

            try
            {
                using var reader = XmlReader.Create(input, settings);
                reader.MoveToContent();
                while (!reader.EOF)
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "programme")
                    {
                        var channel = reader.GetAttribute("channel") ?? string.Empty;
                        var startAttr = reader.GetAttribute("start");
                        var stopAttr = reader.GetAttribute("stop");
                        var el = (XElement)XNode.ReadFrom(reader); // advances the reader

                        if (channel.Length == 0 || (wanted.Count > 0 && !wanted.Contains(channel)))
                        {
                            continue;
                        }

                        if (!TryParseXmlTvTime(startAttr, out var start))
                        {
                            continue;
                        }

                        if (!TryParseXmlTvTime(stopAttr, out var stop))
                        {
                            stop = start.AddHours(2);
                        }

                        if (stop < windowStart || start > windowEnd)
                        {
                            continue;
                        }

                        var title = (string?)el.Element("title") ?? string.Empty;
                        if (title.Length == 0)
                        {
                            continue;
                        }

                        var desc = ((string?)el.Element("desc") ?? string.Empty)
                            + " " + ((string?)el.Element("sub-title") ?? string.Empty);
                        desc = desc.Trim();
                        if (desc.Length > MaxDescriptionLength)
                        {
                            desc = desc.Substring(0, MaxDescriptionLength) + "…";
                        }

                        result.Add(new EpgProgramme
                        {
                            ChannelId = channel,
                            StartUtc = start,
                            EndUtc = stop,
                            Title = title.Trim(),
                            Description = desc,
                            NormTitle = TeamMatcher.Normalize(title),
                            NormDescription = TeamMatcher.Normalize(desc)
                        });
                    }
                    else
                    {
                        reader.Read();
                    }
                }
            }
            catch (XmlException ex)
            {
                // Broken XML part way through: keep what we have.
                _logger.LogWarning("XtreamFootball: EPG XML error after {Count} programmes: {Message}", result.Count, ex.Message);
                if (result.Count == 0)
                {
                    throw new InvalidDataException("EPG from server is not valid XMLTV: " + ex.Message);
                }
            }
            finally
            {
                if (!ReferenceEquals(input, fileStream))
                {
                    input.Dispose();
                }
            }

            return result;
        }

        /// <summary>Parses "20261103150000 +0300" (offset optional) into UTC.</summary>
        public static bool TryParseXmlTvTime(string? value, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();
            var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
            var rest = value.Substring(digits.Length).Trim();
            if (digits.Length < 12)
            {
                return false;
            }

            if (digits.Length > 14)
            {
                digits = digits.Substring(0, 14);
            }

            if (digits.Length == 12)
            {
                digits += "00";
            }

            if (!DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return false;
            }

            var offset = TimeSpan.Zero;
            if (rest.Length >= 5 && (rest[0] == '+' || rest[0] == '-'))
            {
                if (int.TryParse(rest.Substring(1, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh)
                    && int.TryParse(rest.Substring(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm))
                {
                    offset = new TimeSpan(hh, mm, 0);
                    if (rest[0] == '-')
                    {
                        offset = -offset;
                    }
                }
            }

            utc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
            return true;
        }

        // ---------------------------------------------------------------- search

        public async Task<SearchResponse> SearchAsync(string query, int days, bool? searchDescriptions, bool refresh, CancellationToken ct)
        {
            var c = Config;
            var response = new SearchResponse();
            var terms = TeamMatcher.ExpandTerms(query);
            if (terms.Count == 0)
            {
                response.Error = "Type a team name (at least 2 letters).";
                return response;
            }

            response.Terms = terms.Select(t => t.Trim()).ToList();
            var catalog = await GetStreamsAsync(refresh, ct).ConfigureAwait(false);
            var epg = await GetEpgAsync(catalog, refresh, ct).ConfigureAwait(false);

            response.StreamsLoadedUtc = catalog.LoadedUtc;
            response.EpgLoadedUtc = epg?.LoadedUtc;
            if (epg == null)
            {
                response.EpgError = "TV guide (EPG) could not be loaded" + (LastEpgError != null ? ": " + LastEpgError : ".")
                    + " Only channel names were searched.";
            }

            Run(c, catalog, epg, terms, days, searchDescriptions ?? c.SearchDescriptions, response);
            return response;
        }

        private static void Run(
            PluginConfiguration c,
            StreamCatalog catalog,
            EpgData? epg,
            List<string> terms,
            int days,
            bool searchDescriptions,
            SearchResponse response)
        {
            var categoryWords = TeamMatcher.SplitCsv(c.CategoryFilter);
            var excludeWords = TeamMatcher.SplitCsv(c.ExcludeWords).Select(w => " " + w + " ").ToList();

            var allowed = catalog.Streams
                .Where(s => categoryWords.Count == 0 || TeamMatcher.ContainsAny(s.NormCategory, categoryWords))
                .ToList();
            response.ChannelsSearched = allowed.Count;

            // 1) Channel names (event / PPV channels like "EPL 03: Arsenal v Chelsea 20:00")
            response.NamedChannels = allowed
                .Where(s => TeamMatcher.MatchesAny(s.NormName, terms))
                .OrderBy(s => s.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .Select(s => ToResult(c, s))
                .ToList();

            if (epg == null)
            {
                return;
            }

            // 2) TV guide
            var byEpgId = allowed
                .Where(s => s.EpgId.Length > 0)
                .GroupBy(s => s.EpgId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var until = now.AddDays(Math.Clamp(days, 1, 7));
            var searched = 0;
            var hits = new List<EpgProgramme>();
            foreach (var p in epg.Programmes)
            {
                if (p.EndUtc <= now || p.StartUtc >= until || !byEpgId.ContainsKey(p.ChannelId))
                {
                    continue;
                }

                searched++;
                var hit = TeamMatcher.MatchesAny(p.NormTitle, terms)
                    || (searchDescriptions && TeamMatcher.MatchesAny(p.NormDescription, terms));
                if (!hit || TeamMatcher.MatchesAny(p.NormTitle, excludeWords))
                {
                    continue;
                }

                hits.Add(p);
            }

            response.ProgrammesSearched = searched;

            response.Matches = hits
                .GroupBy(p => (p.NormTitle, p.StartUtc))
                .Select(g =>
                {
                    var first = g.First();
                    var channels = g
                        .SelectMany(p => byEpgId[p.ChannelId])
                        .GroupBy(s => s.Id)
                        .Select(x => x.First())
                        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(s => ToResult(c, s))
                        .ToList();
                    return new MatchResult
                    {
                        Title = first.Title,
                        Description = g.Select(p => p.Description).FirstOrDefault(d => d.Length > 0) ?? string.Empty,
                        StartUtc = first.StartUtc,
                        EndUtc = g.Max(p => p.EndUtc),
                        IsLive = first.StartUtc <= now && g.Max(p => p.EndUtc) > now,
                        Channels = channels
                    };
                })
                .OrderBy(m => m.StartUtc)
                .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                .Take(150)
                .ToList();
        }

        private static ChannelResult ToResult(PluginConfiguration c, LiveStream s) => new ChannelResult
        {
            Name = s.Name,
            StreamId = s.Id,
            Category = s.CategoryName,
            Logo = s.Logo,
            Url = StreamUrl(c, s.Id)
        };

        // ---------------------------------------------------------------- Live TV feeds

        /// <summary>Channels that show any of the saved teams in the next LookaheadDays (plus their guide data).</summary>
        public async Task<(List<LiveStream> Channels, EpgData? Epg)> GetTeamChannelsAsync(CancellationToken ct)
        {
            var c = Config;
            var catalog = await GetStreamsAsync(false, ct).ConfigureAwait(false);
            var epg = await GetEpgAsync(catalog, false, ct).ConfigureAwait(false);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var team in TeamMatcher.SplitTeamLines(c.Teams))
            {
                var terms = TeamMatcher.ExpandTerms(team);
                if (terms.Count == 0)
                {
                    continue;
                }

                var r = new SearchResponse();
                Run(c, catalog, epg, terms, Math.Clamp(c.LookaheadDays, 1, 7), c.SearchDescriptions, r);
                foreach (var ch in r.NamedChannels)
                {
                    ids.Add(ch.StreamId);
                }

                foreach (var m in r.Matches)
                {
                    foreach (var ch in m.Channels)
                    {
                        ids.Add(ch.StreamId);
                    }
                }
            }

            var channels = catalog.Streams.Where(s => ids.Contains(s.Id)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return (channels, epg);
        }

        public static string BuildM3u(PluginConfiguration c, List<LiveStream> channels)
        {
            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n");
            var n = 1;
            foreach (var s in channels)
            {
                var tvgId = s.EpgId.Length > 0 ? s.EpgId : "xf." + s.Id;
                sb.Append("#EXTINF:-1 tvg-id=\"").Append(Attr(tvgId))
                    .Append("\" tvg-name=\"").Append(Attr(s.Name))
                    .Append("\" tvg-chno=\"").Append(n.ToString(CultureInfo.InvariantCulture))
                    .Append("\" tvg-logo=\"").Append(Attr(s.Logo))
                    .Append("\" group-title=\"Football\",").Append(s.Name.Replace('\n', ' ').Replace('\r', ' '))
                    .Append('\n')
                    .Append(StreamUrl(c, s.Id))
                    .Append('\n');
                n++;
            }

            return sb.ToString();
        }

        private static string Attr(string v) => (v ?? string.Empty).Replace("\"", "'").Replace('\n', ' ').Replace('\r', ' ');

        public static string BuildXmlTv(List<LiveStream> channels, EpgData? epg)
        {
            var settings = new XmlWriterSettings { Indent = false, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
            var sb = new StringBuilder();
            using (var sw = new Utf8StringWriter(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("tv");
                w.WriteAttributeString("generator-info-name", "Xtream Football Finder");

                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in channels)
                {
                    var id = s.EpgId.Length > 0 ? s.EpgId : "xf." + s.Id;
                    if (!written.Add(id))
                    {
                        continue;
                    }

                    w.WriteStartElement("channel");
                    w.WriteAttributeString("id", Clean(id));
                    w.WriteElementString("display-name", Clean(s.Name));
                    if (s.Logo.Length > 0)
                    {
                        w.WriteStartElement("icon");
                        w.WriteAttributeString("src", Clean(s.Logo));
                        w.WriteEndElement();
                    }

                    w.WriteEndElement();
                }

                if (epg != null)
                {
                    var now = DateTime.UtcNow.AddHours(-6);
                    foreach (var p in epg.Programmes)
                    {
                        if (p.EndUtc < now || !written.Contains(p.ChannelId))
                        {
                            continue;
                        }

                        w.WriteStartElement("programme");
                        w.WriteAttributeString("start", p.StartUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000");
                        w.WriteAttributeString("stop", p.EndUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000");
                        w.WriteAttributeString("channel", Clean(p.ChannelId));
                        w.WriteElementString("title", Clean(p.Title));
                        if (p.Description.Length > 0)
                        {
                            w.WriteElementString("desc", Clean(p.Description));
                        }

                        w.WriteElementString("category", "Sports");
                        w.WriteEndElement();
                    }
                }

                w.WriteEndElement();
                w.WriteEndDocument();
            }

            return sb.ToString();
        }

        /// <summary>Removes characters that are illegal in XML 1.0 (IPTV EPGs are full of them).</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        sb.Append(ch).Append(s[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (XmlConvert.IsXmlChar(ch))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb)
                : base(sb, CultureInfo.InvariantCulture)
            {
            }

            public override Encoding Encoding => new UTF8Encoding(false);
        }
    }
}
