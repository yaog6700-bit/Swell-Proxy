using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnywhereWinUI.Models
{
    internal sealed class AppGhRelease
    {
        [JsonPropertyName("tag_name")]   public string? TagName { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")]      public bool Draft { get; set; }
        [JsonPropertyName("assets")]     public List<GhAsset>? Assets { get; set; }
    }

    internal sealed class GhAsset
    {
        [JsonPropertyName("name")]                 public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
    }

    public sealed record UpdateInfo(
        Version NewVersion,
        string TagName,
        string ZipUrl,
        string Sha256Url,
        string ZipAssetName);

    public sealed record UpdateStaging(
        string ExtractedDir,
        string RunnerExePath,
        string InstallDir,
        Version NewVersion);

    public sealed record ProgressDialogUpdate(
        string StatusText,
        double? PercentComplete = null);

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppGhRelease))]
    internal partial class AppUpdateJsonSerializerContext : JsonSerializerContext
    {
    }
}
