namespace AnywhereWinUI.Models
{
    public class RoutingRuleItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string Type { get; set; } = "domain";
        public string Match { get; set; } = string.Empty;
        public string OutboundTag { get; set; } = "proxy";
        public bool IsEnabled { get; set; } = true;
        public bool IsBuiltIn { get; set; }
        public int MatchVersion { get; set; }

        public RoutingRuleItem Clone() => new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IconUrl = IconUrl,
            Type = Type,
            Match = Match,
            OutboundTag = OutboundTag,
            IsEnabled = IsEnabled,
            IsBuiltIn = IsBuiltIn,
            MatchVersion = MatchVersion
        };
    }
}
