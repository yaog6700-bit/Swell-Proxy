using System.Text.Json.Serialization;
using System.Collections.Generic;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Models
{
    [JsonSerializable(typeof(NodesConfig))]
    [JsonSerializable(typeof(PersistedNode))]
    [JsonSerializable(typeof(List<PersistedNode>))]
    [JsonSerializable(typeof(PersistedSubscription))]
    [JsonSerializable(typeof(List<PersistedSubscription>))]
    [JsonSerializable(typeof(DailyTraffic))]
    [JsonSerializable(typeof(List<DailyTraffic>))]
    [JsonSerializable(typeof(CustomRule))]
    [JsonSerializable(typeof(List<CustomRule>))]
    [JsonSerializable(typeof(RoutingRuleItem))]
    [JsonSerializable(typeof(List<RoutingRuleItem>))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(ClashConnectionsMessage))]
    [JsonSerializable(typeof(ClashConnectionNode))]
    [JsonSerializable(typeof(ClashMetadata))]
    [JsonSerializable(typeof(IpInfoResponse))]
    [JsonSerializable(typeof(AnywhereWinUI.Plugins.PluginManifest))]
    [JsonSerializable(typeof(List<AnywhereWinUI.Plugins.PluginManifest>))]
    [JsonSerializable(typeof(AnywhereWinUI.Plugins.PluginConfigItem))]
    [JsonSerializable(typeof(List<AnywhereWinUI.Plugins.PluginConfigItem>))]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }
}
