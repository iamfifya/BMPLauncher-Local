using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace BMPLauncher
{
    // Java информация
    public class JavaInfo
    {
        public string Path { get; }
        public string Version { get; }
        public JavaInfo(string path, string version)
        {
            Path = path;
            Version = version;
        }
    }

namespace BMPLauncher
    {
        // Модель для аккаунта Ely.by
        public class ElyAccountInfo
        {
            [JsonProperty("username")]
            public string Username { get; set; }

            [JsonProperty("uuid")]
            public string Uuid { get; set; }

            [JsonProperty("email")]
            public string Email { get; set; }

            [JsonProperty("registeredAt")]
            public DateTime RegisteredAt { get; set; }
        }

        // Модель для версий Minecraft
        public class MCVersion
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("time")]
            public DateTime Time { get; set; }

            [JsonProperty("releaseTime")]
            public DateTime ReleaseTime { get; set; }

            public override string ToString() => Id;
        }

        public class VersionManifest
        {
            [JsonProperty("latest")]
            public LatestVersions Latest { get; set; }

            [JsonProperty("versions")]
            public List<MCVersion> Versions { get; set; }
        }

        public class LatestVersions
        {
            [JsonProperty("release")]
            public string Release { get; set; }

            [JsonProperty("snapshot")]
            public string Snapshot { get; set; }
        }

        // CurseForge модели
        public class CFModpack
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("summary")]
            public string Description { get; set; }

            [JsonProperty("downloadCount")]
            public long DownloadCount { get; set; }

            [JsonProperty("dateModified")]
            public DateTime DateModified { get; set; }

            [JsonProperty("authors")]
            public List<CFAuthor> Authors { get; set; }

            [JsonProperty("gameVersionLatestFiles")]
            public List<CFGameVersionFile> GameVersionLatestFiles { get; set; }
        }

        public class CFAuthor
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }
        }

        public class CFGameVersionFile
        {
            [JsonProperty("gameVersion")]
            public string GameVersion { get; set; }

            [JsonProperty("projectFileId")]
            public int ProjectFileId { get; set; }

            [JsonProperty("projectFileName")]
            public string ProjectFileName { get; set; }
        }

        public class CFSearchResponse
        {
            [JsonProperty("data")]
            public List<CFModpack> Data { get; set; }

            [JsonProperty("pagination")]
            public CFPagination Pagination { get; set; }
        }

        public class CFPagination
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("pageSize")]
            public int PageSize { get; set; }

            [JsonProperty("resultCount")]
            public int ResultCount { get; set; }

            [JsonProperty("totalCount")]
            public int TotalCount { get; set; }
        }

        public class CFFilesResponse
        {
            [JsonProperty("data")]
            public List<CFFileInfo> Data { get; set; }
        }

        public class CFFileInfo
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("displayName")]
            public string DisplayName { get; set; }

            [JsonProperty("fileName")]
            public string FileName { get; set; }

            [JsonProperty("downloadUrl")]
            public string DownloadUrl { get; set; }

            [JsonProperty("gameVersions")]
            public List<string> GameVersions { get; set; }

            [JsonProperty("fileDate")]
            public DateTime FileDate { get; set; }

            [JsonProperty("fileLength")]
            public long FileLength { get; set; }
        }

        public class CFFileResponse
        {
            [JsonProperty("data")]
            public CFFileInfo Data { get; set; }
        }

        public class CFManifest
        {
            [JsonProperty("minecraft")]
            public CFMinecraft Minecraft { get; set; }

            [JsonProperty("manifestType")]
            public string ManifestType { get; set; }

            [JsonProperty("overrides")]
            public string Overrides { get; set; }

            [JsonProperty("files")]
            public List<CFFileReference> Files { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("author")]
            public string Author { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        public class CFMinecraft
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("modLoaders")]
            public List<CFModLoader> ModLoaders { get; set; }
        }

        public class CFModLoader
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("primary")]
            public bool Primary { get; set; }
        }

        public class CFFileReference
        {
            [JsonProperty("projectID")]
            public int ProjectId { get; set; }

            [JsonProperty("fileID")]
            public int FileId { get; set; }

            [JsonProperty("required")]
            public bool Required { get; set; }
        }
    }

    // Настройки лаунчера
    public class LauncherSettings
    {
        public string GameDirectory { get; set; }
        public string JavaPath { get; set; }
        public string PlayerName { get; set; }
        public string JavaArgs { get; set; }
        public string LastVersion { get; set; }
        public string Xms { get; set; }
        public string Xmx { get; set; }
        public List<string> DownloadedVersions { get; set; } = new List<string>();

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BMPLauncher",
            "launcher_settings.json");

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public static LauncherSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    return JsonConvert.DeserializeObject<LauncherSettings>(File.ReadAllText(SettingsPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            }
            return new LauncherSettings();
        }
    }

    // Остальные существующие модели остаются без изменений...
    // [остальной код из Models.cs]
}