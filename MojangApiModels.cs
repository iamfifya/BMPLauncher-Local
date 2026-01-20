using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace BMPLauncher.Core
{
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

    public class AccountInfo
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }
    }

    // ВАЖНО: Это правильное написание!
    public class MinecraftVersionManifest
    {
        [JsonProperty("versions")]
        public List<MinecraftVersion> Versions { get; set; }
    }

    public class MinecraftVersion
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class VersionInfo
    {
        [JsonProperty("downloads")]
        public Downloads Downloads { get; set; }

        [JsonProperty("assetIndex")]
        public AssetIndex AssetIndex { get; set; }

        [JsonProperty("libraries")]
        public List<Library> Libraries { get; set; }
    }

    public class Downloads
    {
        [JsonProperty("client")]
        public Artifact Client { get; set; }

        [JsonProperty("server")]
        public Artifact Server { get; set; }
    }

    public class Artifact
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }
    }

    public class AssetIndex
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("totalSize")]
        public int TotalSize { get; set; }
    }

    public class Library
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("downloads")]
        public LibraryDownloads Downloads { get; set; }
    }

    public class LibraryDownloads
    {
        [JsonProperty("artifact")]
        public Artifact Artifact { get; set; }
    }

    public class AssetsIndex
    {
        [JsonProperty("objects")]
        public Dictionary<string, AssetObject> Objects { get; set; }
    }

    public class AssetObject
    {
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }

public class Modpack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("minecraft")]
        public string MinecraftVersion { get; set; }

        [JsonProperty("files")]
        public List<ModpackFile> Files { get; set; }
    }

    public class ModpackFile
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("destPath")]
        public string DestinationPath { get; set; }
    }

    public class ModpackManifest
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("minecraft")]
        public string MinecraftVersion { get; set; }

        [JsonProperty("forge")]
        public string ForgeVersion { get; set; }

        [JsonProperty("files")]
        public List<ModpackFile> Files { get; set; }

        [JsonProperty("dependencies")]
        public ModpackDependencies Dependencies { get; set; }
    }

    public class ModpackDependencies
    {
        [JsonProperty("java")]
        public int JavaVersion { get; set; }

        [JsonProperty("forge")]
        public string ForgeVersion { get; set; }

        [JsonProperty("fabric")]
        public string FabricVersion { get; set; }
    }

    public class GitHubModpack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("download_url")]
        public string ManifestUrl { get; set; }
    }

    public class GitHubModpackInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("download_url")]
        public string DownloadUrl { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class GitHubModpackManifest
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("minecraft_version")]
        public string MinecraftVersion { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("files")]
        public List<ModpackFile> Files { get; set; }

        [JsonProperty("modloader")]
        public string ModLoader { get; set; } // "forge", "fabric", "quilt"

        [JsonProperty("modloader_version")]
        public string ModLoaderVersion { get; set; }

        [JsonProperty("java_version")]
        public int JavaVersion { get; set; }

        [JsonProperty("icon_url")]
        public string IconUrl { get; set; }
    }

    public class InstalledModpack
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string MinecraftVersion { get; set; }
        public string ModLoader { get; set; }
        public string InstallPath { get; set; }
        public DateTime InstallDate { get; set; }
    }
}