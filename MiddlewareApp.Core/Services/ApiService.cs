using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Core.Services;

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

/// <summary>
/// Tenant API client (spec §3). All requests send Accept: application/json.
/// Non-2xx ⇒ "Server {status}: {body}"; network failure ⇒ "Cannot reach:\n{url}\n\n({message chain})".
/// </summary>
public class ApiService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public ApiService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<List<Location>> GetLocationsAsync(string businessCode, CancellationToken ct = default)
    {
        var url = $"{AppConfig.BaseUrlFor(businessCode)}/api/v1/locations";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);

        // Unwrap json.data.locations ?? json.data ?? []
        var data = json?["data"];
        var locationsNode = data?["locations"] ?? data;
        if (locationsNode is not JsonArray arr)
            return new List<Location>();
        return arr.Deserialize<List<Location>>(JsonOpts) ?? new List<Location>();
    }

    public async Task<PrintConfigState> GetPrintConfigAsync(string businessCode, int locationId, int deviceId, CancellationToken ct = default)
    {
        var url = $"{AppConfig.BaseUrlFor(businessCode)}/api/v1/locations/{locationId}/devices/{deviceId}/print-config";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var state = json?["data"]?.Deserialize<PrintConfigState>(JsonOpts);
        return state ?? new PrintConfigState();
    }

    /// <summary>PATCH saves the full state every time (device + all departments — not a delta).</summary>
    public async Task PatchPrintConfigAsync(string businessCode, int locationId, int deviceId, PrintConfigState body, CancellationToken ct = default)
    {
        var url = $"{AppConfig.BaseUrlFor(businessCode)}/api/v1/locations/{locationId}/devices/{deviceId}/print-config";
        var payload = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new ApiException($"Cannot reach:\n{url}\n\n({DescribeFailure(ex)})");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ApiException($"Server {(int)response.StatusCode}: {text}");
        }
    }

    /// <summary>
    /// Messages like "The SSL connection could not be established, see inner exception."
    /// are useless without the inner chain, so include each inner message too.
    /// </summary>
    public static string DescribeFailure(Exception ex)
    {
        var sb = new StringBuilder();
        string? previous = null;
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (string.IsNullOrEmpty(e.Message) || e.Message == previous)
                continue;
            if (sb.Length > 0)
                sb.Append(" — ");
            sb.Append(e.Message);
            previous = e.Message;
        }
        return sb.Length > 0 ? sb.ToString() : ex.GetType().Name;
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new ApiException($"Cannot reach:\n{url}\n\n({DescribeFailure(ex)})");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ApiException($"Server {(int)response.StatusCode}: {text}");
            try
            {
                return JsonNode.Parse(text);
            }
            catch (Exception ex)
            {
                throw new ApiException($"Server {(int)response.StatusCode}: invalid JSON ({ex.Message})");
            }
        }
    }
}
