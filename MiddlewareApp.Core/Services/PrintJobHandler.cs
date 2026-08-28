using System.Text.Json.Nodes;
using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Core.Services;

public sealed class JobEvaluation
{
    public bool ShouldPrint { get; init; }
    /// <summary>Result message when not printing (ignored / skipped / failed validation).</summary>
    public string? Message { get; init; }
    public PrintConfig? Printer { get; init; }
    public string? Html { get; init; }
    public bool OpenCashbox { get; init; }

    public static JobEvaluation Skip(string message) => new() { ShouldPrint = false, Message = message };
    public static JobEvaluation Print(PrintConfig printer, string html, bool openCashbox = false) =>
        new() { ShouldPrint = true, Printer = printer, Html = html, OpenCashbox = openCashbox };
}

/// <summary>
/// Pure job-handling rules (spec §4.1/§4.2) — payload unwrapping, command filter,
/// terminal filter, printer resolution with terminal fallback.
/// </summary>
public static class PrintJobHandler
{
    /// <summary>Cash drawer kick after print — cashier receipts only, never KOT/kitchen.</summary>
    public static bool ShouldOpenCashbox(string? command) =>
        string.Equals(command?.Trim(), "PRINT_RECEIPT", StringComparison.OrdinalIgnoreCase);

    public static JobEvaluation Evaluate(string rawPayload, AgentConfigs configs)
    {
        var job = UnwrapPayload(rawPayload);

        // 1. Command filter: PRINT, PRINT_RECEIPT, PRINT_KOT, or anything starting with
        //    PRINT_ (case-insensitive).
        var command = job?["command"]?.GetValue<string>();
        var cmd = command?.Trim().ToUpperInvariant();
        var isPrint = cmd == "PRINT" || cmd == "PRINT_RECEIPT" || cmd == "PRINT_KOT" ||
                      (cmd != null && cmd.StartsWith("PRINT_"));
        if (!isPrint)
            return JobEvaluation.Skip($"Ignored command: {command ?? "(none)"}");

        // 2. Terminal filter: jobs with terminal_id null are handled by everyone on the channel.
        var terminalId = GetInt(job, "terminal_id");
        if (terminalId.HasValue && terminalId.Value != configs.SelectedDeviceId)
            return JobEvaluation.Skip($"Skipped — terminal_id {terminalId.Value} ≠ this device {configs.SelectedDeviceId}");

        // 3. HTML may be under html or HTML.
        var html = job?["html"]?.GetValue<string>() ?? job?["HTML"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(html))
            return JobEvaluation.Skip("Print job has empty HTML");

        // 4. Printer resolution: department's config, else fall back to the terminal's.
        var departmentId = GetInt(job, "department_id");
        PrintConfig? printer = null;
        if (departmentId.HasValue)
        {
            var dept = configs.Departments.FirstOrDefault(d => d.Id == departmentId.Value);
            printer = ConfiguredPrinter(dept);
        }
        printer ??= ConfiguredPrinter(configs.Device);
        if (printer == null)
            return JobEvaluation.Skip("No printer configured. Set IP for terminal/department in middleware first.");

        return JobEvaluation.Print(printer, html, ShouldOpenCashbox(command));
    }

    private static PrintConfig? ConfiguredPrinter(SlotConfig? slot)
    {
        var cfg = slot?.PrintConfig;
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.Ip)) return null;
        return cfg;
    }

    /// <summary>
    /// Payload may arrive as a JSON string or object; may be wrapped. Unwrap order:
    /// if it has command, use as-is; else try .data, then .message, else use raw.
    /// </summary>
    private static JsonNode? UnwrapPayload(string raw)
    {
        var node = ParseLoose(raw);
        if (node == null) return null;

        if (node["command"] != null) return node;

        var data = Reparse(node["data"]);
        if (data != null) return data;

        var message = Reparse(node["message"]);
        if (message != null) return message;

        return node;
    }

    /// <summary>Parses JSON, unwrapping up to two levels of string-encoded JSON.</summary>
    private static JsonNode? ParseLoose(string raw)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(raw); }
        catch { return null; }

        for (var i = 0; i < 2 && node is JsonValue v && v.TryGetValue<string>(out var inner); i++)
        {
            try { node = JsonNode.Parse(inner); }
            catch { return null; }
        }
        return node;
    }

    private static JsonNode? Reparse(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            try { return JsonNode.Parse(s); }
            catch { return null; }
        }
        // Detach so the returned node can be queried independently.
        return node;
    }

    private static int? GetInt(JsonNode? job, string key)
    {
        var node = job?[key];
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
