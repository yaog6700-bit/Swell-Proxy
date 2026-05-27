using System.Text.Json.Serialization;
using System.Collections.Generic;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Models
{
    [JsonSerializable(typeof(NodesConfig))]
    [JsonSerializable(typeof(PersistedNode))]
    [JsonSerializable(typeof(List<PersistedNode>))]
    [JsonSerializable(typeof(DailyTraffic))]
    [JsonSerializable(typeof(List<DailyTraffic>))]
    [JsonSerializable(typeof(CustomRule))]
    [JsonSerializable(typeof(List<CustomRule>))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }
}
