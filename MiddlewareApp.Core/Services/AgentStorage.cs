using System.Text.Json;
using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// Persists the agent session + configs (spec §8) as JSON blobs in %APPDATA% —
/// same keys/shapes as the Android AsyncStorage entries.
/// </summary>
public static class AgentStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudPOSMiddleware");

    private static string SessionPath => Path.Combine(Folder, "mwire.agent.session.v1.json");
    private static string ConfigsPath => Path.Combine(Folder, "mwire.agent.configs.v1.json");

    public static void SaveSession(AgentSession session) => WriteAtomic(SessionPath, session);
    public static void SaveConfigs(AgentConfigs configs) => WriteAtomic(ConfigsPath, configs);

    public static AgentSession? LoadSession() => Read<AgentSession>(SessionPath);
    public static AgentConfigs? LoadConfigs() => Read<AgentConfigs>(ConfigsPath);

    public static void Clear()
    {
        TryDelete(SessionPath);
        TryDelete(ConfigsPath);
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Folder);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return null; // corrupt file ⇒ behave like no session
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
