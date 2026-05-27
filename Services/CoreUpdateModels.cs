using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnywhereWinUI.Services
{
    public class GhRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GhAsset>? Assets { get; set; }
    }

    public class GhAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? Url { get; set; }
    }

    public enum ChecksumFormat { Sha256, Dgst }

    public sealed record CoreUpdateInfo(
        Version NewVersion,
        string TagName,
        string ZipUrl,
        string? ChecksumUrl,
        ChecksumFormat? ChecksumFormat,
        string TargetExeName,
        string ZipAssetName);

    [JsonSerializable(typeof(GhRelease))]
    public partial class UpdateJsonSerializerContext : JsonSerializerContext
    {
    }
}
