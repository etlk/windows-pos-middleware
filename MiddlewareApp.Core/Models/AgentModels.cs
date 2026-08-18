using System.Text.Json.Serialization;

namespace MiddlewareApp.Core.Models;

/// <summary>Persisted session (spec §8), key mwire.agent.session.v1.</summary>
public class AgentSession
{
    [JsonPropertyName("businessCode")] public string BusinessCode { get; set; } = "";
    [JsonPropertyName("locationId")] public int LocationId { get; set; }
    [JsonPropertyName("locationName")] public string LocationName { get; set; } = "";
    [JsonPropertyName("deviceId")] public int DeviceId { get; set; }
    [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    public bool SameTarget(AgentSession other) =>
        AppConfig.NormalizeBusinessCode(BusinessCode) == AppConfig.NormalizeBusinessCode(other.BusinessCode)
        && LocationId == other.LocationId
        && DeviceId == other.DeviceId;
}

/// <summary>Persisted configs (spec §8), key mwire.agent.configs.v1.</summary>
public class AgentConfigs
{
    [JsonPropertyName("device")] public SlotConfig? Device { get; set; }
    [JsonPropertyName("departments")] public List<SlotConfig> Departments { get; set; } = new();
    [JsonPropertyName("selectedDeviceId")] public int SelectedDeviceId { get; set; }
}

/// <summary>A printer found by discovery, entered manually, or restored from config.</summary>
public sealed record DiscoveredPrinter(string Name, string Ip, int Port, string Source);
