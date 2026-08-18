using System.Text.Json.Serialization;

namespace MiddlewareApp.Core.Models;

public class Location
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("devices")] public List<Device> Devices { get; set; } = new();
}

public class Device
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("device_name")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("serial_number")] public string? SerialNumber { get; set; }
    [JsonPropertyName("device_status")] public string? DeviceStatus { get; set; }
    [JsonPropertyName("location_id")] public int LocationId { get; set; }
}

public class PrintConfig
{
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; } = 9100;
    [JsonPropertyName("paper_size")] public string PaperSize { get; set; } = "80mm";
}

/// <summary>One printable slot: the terminal ("device") or a department.</summary>
public class SlotConfig
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("is_middleware_configured")] public bool IsMiddlewareConfigured { get; set; }
    [JsonPropertyName("print_config")] public PrintConfig? PrintConfig { get; set; }

    public SlotConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsMiddlewareConfigured = IsMiddlewareConfigured,
        PrintConfig = PrintConfig == null
            ? null
            : new PrintConfig { Ip = PrintConfig.Ip, Port = PrintConfig.Port, PaperSize = PrintConfig.PaperSize },
    };
}

/// <summary>
/// Shape of GET print-config's data and of the PATCH body (spec §3.2/§3.3 — the full
/// state is sent every time, never a delta).
/// </summary>
public class PrintConfigState
{
    [JsonPropertyName("device")] public SlotConfig Device { get; set; } = new();
    [JsonPropertyName("departments")] public List<SlotConfig> Departments { get; set; } = new();

    public PrintConfigState Clone() => new()
    {
        Device = Device.Clone(),
        Departments = Departments.Select(d => d.Clone()).ToList(),
    };
}
