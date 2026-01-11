using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BMPLauncher
{
    public class VersionDownloader
    {
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly HttpClient _httpClient;

        public VersionDownloader(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<VersionManifest> GetVersionManifestAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest.json");
                return JsonConvert.DeserializeObject<VersionManifest>(json);
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка получения манифеста версий: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadVersionAsync(string versionId, Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"Начинаем скачивание версии {versionId}");

                // Получаем манифест
                var manifest = await GetVersionManifestAsync();
                var versionInfo = manifest.Versions.FirstOrDefault(v => v.Id == versionId);

                if (versionInfo == null)
                    throw new Exception($"Версия {versionId} не найдена");

                // Получаем детальную информацию о версии
                var versionDetails = await GetVersionDetailsAsync(versionInfo.Url);

                // Создаем директории
                string versionDir = Path.Combine(_gameDirectory, "versions", versionId);
                Directory.CreateDirectory(versionDir);
                Directory.CreateDirectory(Path.Combine(versionDir, "libraries"));

                // Скачиваем клиент JAR
                string jarPath = Path.Combine(versionDir, $"{versionId}.jar");
                await DownloadFileAsync(versionDetails.Downloads.Client.Url, jarPath, progressCallback, cancellationToken);

                _logAction($"Версия {versionId} успешно скачана");
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка скачивания версии {versionId}: {ex.Message}");
                throw;
            }
        }

        private async Task<VersionDetails> GetVersionDetailsAsync(string url)
        {
            try
            {
                var json = await _httpClient.GetStringAsync(url);
                return JsonConvert.DeserializeObject<VersionDetails>(json);
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка получения деталей версии: {ex.Message}");
                throw;
            }
        }

        private async Task DownloadFileAsync(string url, string path, Action<double> progressCallback, CancellationToken cancellationToken)
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progressCallback != null;

                using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var bytesRead = 0;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            progressCallback((double)totalRead / totalBytes * 100);
                        }
                    }
                }
            }
        }
    }

    // Вспомогательные классы для парсинга JSON
    public class VersionDetails
    {
        [JsonProperty("downloads")]
        public Downloads Downloads { get; set; }
    }

    public class Downloads
    {
        [JsonProperty("client")]
        public DownloadItem Client { get; set; }

        [JsonProperty("server")]
        public DownloadItem Server { get; set; }
    }

    public class DownloadItem
    {
        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }

}