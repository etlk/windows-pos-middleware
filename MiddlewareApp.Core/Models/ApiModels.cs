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
    [JsonPropertyName("connection_type")] public string? ConnectionType { get; set; }
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; } = 9100;
    [JsonPropertyName("paper_size")] public string PaperSize { get; set; } = "80mm";
    [JsonPropertyName("usb_vendor_id")] public int? UsbVendorId { get; set; }
    [JsonPropertyName("usb_product_id")] public int? UsbProductId { get; set; }
    /// <summary>Windows printer queue name when connection_type is usb.</summary>
    [JsonPropertyName("usb_device_name")] public string? UsbDeviceName { get; set; }

    public PrintConfig Clone() => new()
    {
        ConnectionType = ConnectionType,
        Ip = Ip,
        Port = Port,
        PaperSize = PaperSize,
        UsbVendorId = UsbVendorId,
        UsbProductId = UsbProductId,
        UsbDeviceName = UsbDeviceName,
    };
}

/// <summary>Helpers mirroring Android printConfig.ts.</summary>
public static class PrintConfigHelpers
{
    public static string NormalizeConnectionType(PrintConfig? cfg)
    {
        if (string.Equals(cfg?.ConnectionType, "usb", StringComparison.OrdinalIgnoreCase))
            return "usb";
        return "tcp";
    }

    public static bool IsUsb(PrintConfig? cfg) =>
        NormalizeConnectionType(cfg) == "usb" &&
        !string.IsNullOrWhiteSpace(cfg?.UsbDeviceName);

    public static bool IsTcp(PrintConfig? cfg) =>
        NormalizeConnectionType(cfg) == "tcp" &&
        !string.IsNullOrWhiteSpace(cfg?.Ip);

    public static bool IsConfigured(PrintConfig? cfg) => IsUsb(cfg) || IsTcp(cfg);

    public static string Label(PrintConfig? cfg)
    {
        if (!IsConfigured(cfg)) return "Not set";
        if (IsUsb(cfg))
            return $"USB · {cfg!.UsbDeviceName!.Trim()}";
        return $"{cfg!.Ip}:{cfg.Port > 0 ? cfg.Port : 9100}";
    }

    public static string QueueKey(PrintConfig cfg)
    {
        if (IsUsb(cfg))
            return $"usb:{cfg.UsbDeviceName!.Trim()}";
        return $"{cfg.Ip.Trim()}:{cfg.Port > 0 ? cfg.Port : 9100}";
    }
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
        PrintConfig = PrintConfig?.Clone(),
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
