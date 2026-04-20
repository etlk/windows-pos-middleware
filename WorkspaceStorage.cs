using System;
using System.IO;
using System.Text.Json;
using MiddlewareApp.Models;

namespace WsWpfListener
{
    public static class WorkspaceStorage
    {
        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WsWpfListener");

        private static readonly string FilePath = Path.Combine(Folder, "workspace.json");

        public static void Save(Data data)
        {
            Directory.CreateDirectory(Folder);

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempFile = FilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Copy(tempFile, FilePath, true);
            File.Delete(tempFile);

        }

        public static Data? Load()
        {
            if (!File.Exists(FilePath)) return null;

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Data>(json);
        }
    }

    
}
