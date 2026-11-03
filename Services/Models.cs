using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XtreamFootball.Services
{
    public sealed class LiveStream
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string NormName { get; set; } = " ";

        public string EpgId { get; set; } = string.Empty;

        public string Logo { get; set; } = string.Empty;

        public string CategoryId { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string NormCategory { get; set; } = " ";

        public int Number { get; set; }
    }

    public sealed class StreamCatalog
    {
        public List<LiveStream> Streams { get; set; } = new List<LiveStream>();

        public DateTime LoadedUtc { get; set; }
    }

    public sealed class EpgProgramme
    {
        public string ChannelId { get; set; } = string.Empty;

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string NormTitle { get; set; } = " ";

        public string NormDescription { get; set; } = " ";
    }

    public sealed class EpgData
    {
        public List<EpgProgramme> Programmes { get; set; } = new List<EpgProgramme>();

        public DateTime LoadedUtc { get; set; }
    }

    public sealed class ChannelResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("streamId")]
        public string StreamId { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("logo")]
        public string Logo { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public sealed class MatchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("startUtc")]
        public DateTime StartUtc { get; set; }

        [JsonPropertyName("endUtc")]
        public DateTime EndUtc { get; set; }

        [JsonPropertyName("isLive")]
        public bool IsLive { get; set; }

        [JsonPropertyName("channels")]
        public List<ChannelResult> Channels { get; set; } = new List<ChannelResult>();
    }

    public sealed class SearchResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("epgError")]
        public string? EpgError { get; set; }

        [JsonPropertyName("terms")]
        public List<string> Terms { get; set; } = new List<string>();

        [JsonPropertyName("matches")]
        public List<MatchResult> Matches { get; set; } = new List<MatchResult>();

        [JsonPropertyName("namedChannels")]
        public List<ChannelResult> NamedChannels { get; set; } = new List<ChannelResult>();

        [JsonPropertyName("channelsSearched")]
        public int ChannelsSearched { get; set; }

        [JsonPropertyName("programmesSearched")]
        public int ProgrammesSearched { get; set; }

        [JsonPropertyName("streamsLoadedUtc")]
        public DateTime? StreamsLoadedUtc { get; set; }

        [JsonPropertyName("epgLoadedUtc")]
        public DateTime? EpgLoadedUtc { get; set; }
    }

    public sealed class AccountInfo
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("expires")]
        public string Expires { get; set; } = string.Empty;

        [JsonPropertyName("activeConnections")]
        public string ActiveConnections { get; set; } = string.Empty;

        [JsonPropertyName("maxConnections")]
        public string MaxConnections { get; set; } = string.Empty;

        [JsonPropertyName("liveChannels")]
        public int LiveChannels { get; set; }
    }
}
