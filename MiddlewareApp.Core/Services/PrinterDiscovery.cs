using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MiddlewareApp.Core.Models;
using Zeroconf;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// LAN printer discovery (spec §7): mDNS/Bonjour browse + full /24 TCP probe on port
/// 9100, both in parallel, streaming deduplicated results (dedupe by IP, first-seen
/// name/port wins).
/// </summary>
public class PrinterDiscovery
{
    private static readonly string[] MdnsProtocols =
    {
        "_pdl-datastream._tcp.local.",
        "_printer._tcp.local.",
        "_ipp._tcp.local.",
    };

    private static readonly TimeSpan MdnsScanTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
    private const int ProbeConcurrency = 24;
    private const int PrinterPort = 9100;

    public async Task ScanAsync(Action<DiscoveredPrinter> onFound, CancellationToken ct = default)
    {
        var seen = new HashSet<string>();
        var gate = new object();

        void Report(DiscoveredPrinter printer)
        {
            lock (gate)
            {
                if (!seen.Add(printer.Ip)) return; // keep first-seen name/port
            }
            onFound(printer);
        }

        var mdns = ScanMdnsAsync(Report, ct);
        var subnet = ScanSubnetAsync(Report, ct);
        await Task.WhenAll(mdns, subnet).ConfigureAwait(false);
    }

    private static async Task ScanMdnsAsync(Action<DiscoveredPrinter> report, CancellationToken ct)
    {
        try
        {
            await ZeroconfResolver.ResolveAsync(
                MdnsProtocols,
                scanTime: MdnsScanTime,
                callback: host =>
                {
                    var ip = host.IPAddresses?.FirstOrDefault(a => a.Contains('.')) ?? host.IPAddress;
                    if (string.IsNullOrWhiteSpace(ip)) return;
                    var port = host.Services?.Values
                        .Select(s => s.Port)
                        .FirstOrDefault(p => p > 0) ?? 0;
                    report(new DiscoveredPrinter(
                        Name: string.IsNullOrWhiteSpace(host.DisplayName) ? ip : host.DisplayName,
                        Ip: ip,
                        Port: port > 0 ? port : PrinterPort,
                        Source: "zeroconf"));
                },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch
        {
            // mDNS can fail on odd network stacks; the subnet probe still runs.
        }
    }

    private static async Task ScanSubnetAsync(Action<DiscoveredPrinter> report, CancellationToken ct)
    {
        var localIp = GetLocalIPv4();
        if (localIp == null)
            return; // LAN IP undetermined ⇒ skip silently (spec §7)

        var parts = localIp.Split('.');
        var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}";

        using var limiter = new SemaphoreSlim(ProbeConcurrency);
        var probes = new List<Task>(254);
        for (var host = 1; host <= 254; host++)
        {
            var ip = $"{prefix}.{host}";
            probes.Add(ProbeAsync(ip, limiter, report, ct));
        }
        try { await Task.WhenAll(probes).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private static async Task ProbeAsync(string ip, SemaphoreSlim limiter, Action<DiscoveredPrinter> report, CancellationToken ct)
    {
        await limiter.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(ip, PrinterPort, cts.Token).ConfigureAwait(false);
            report(new DiscoveredPrinter($"Printer {ip}", ip, PrinterPort, "subnet"));
        }
        catch
        {
            // closed port / timeout — not a printer
        }
        finally
        {
            limiter.Release();
        }
    }

    private static string? GetLocalIPv4()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254.") || ip == "127.0.0.1") continue;
                    return ip;
                }
            }
        }
        catch { }
        return null;
    }
}
