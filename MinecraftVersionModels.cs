using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace BMPLauncher
{
    // Классы для скачивания версий Minecraft
    public class MinecraftVersionManifest
    {
        [JsonProperty("latest")]
        public LatestVersions Latest { get; set; }

        [JsonProperty("versions")]
        public List<MCVersion> Versions { get; set; }
    }

    public class VersionInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("downloads")]
        public VersionDownloads Downloads { get; set; }

        [JsonProperty("libraries")]
        public List<Library> Libraries { get; set; }

        [JsonProperty("assetIndex")]
        public AssetIndex AssetIndex { get; set; }
    }

    public class VersionDownloads
    {
        [JsonProperty("client")]
        public DownloadItem Client { get; set; }

        [JsonProperty("server")]
        public DownloadItem Server { get; set; }
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
        public LibraryArtifact Artifact { get; set; }
    }

    public class LibraryArtifact
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class AssetIndex
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
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

    public class VersionDownloadItem
    {
        public string Url { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public string Type { get; set; }
        public string Hash { get; set; }
    }
}