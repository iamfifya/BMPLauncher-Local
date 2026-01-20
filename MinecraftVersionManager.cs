using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BMPLauncher.Core
{
    public class MinecraftVersionManager
    {
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly HttpClient _httpClient;
        private const int MAX_PARALLEL_DOWNLOADS = 8;
        private const string VERSIONS_URL = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

        public MinecraftVersionManager(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<MinecraftVersionManifest> GetVersionManifestAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(VERSIONS_URL);
                return JsonConvert.DeserializeObject<MinecraftVersionManifest>(json);
            }
            catch (Exception ex)
            {
                _logAction($"❌ Ошибка получения манифеста версий: {ex.Message}");
                throw;
            }
        }

        public async Task DownloadVersionAsync(string versionId, Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"🚀 Начинаем скачивание версии {versionId}");

                // Получаем информацию о версии
                var versionManifest = await GetVersionManifestAsync();
                var versionInfo = versionManifest.Versions.FirstOrDefault(v => v.Id == versionId);

                if (versionInfo == null)
                {
                    throw new Exception($"Версия {versionId} не найдена");
                }

                // Получаем детальную информацию о версии
                var versionDetails = await GetVersionDetailsAsync(versionInfo.Url);

                // Создаем структуру директорий
                string versionDir = Path.Combine(_gameDirectory, "versions", versionId);
                CreateVersionDirectoryStructure(versionDir);

                // Параллельное скачивание
                await DownloadVersionFilesParallelAsync(versionDetails, versionDir, progressCallback, cancellationToken);

                _logAction($"✅ Версия {versionId} успешно скачана");
            }
            catch (OperationCanceledException)
            {
                _logAction("❌ Скачивание отменено");
                throw;
            }
            catch (Exception ex)
            {
                _logAction($"❌ Ошибка скачивания версии {versionId}: {ex.Message}");
                throw;
            }
        }

        private async Task<VersionInfo> GetVersionDetailsAsync(string versionUrl)
        {
            try
            {
                var json = await _httpClient.GetStringAsync(versionUrl);
                return JsonConvert.DeserializeObject<VersionInfo>(json);
            }
            catch (Exception ex)
            {
                _logAction($"❌ Ошибка получения деталей версии: {ex.Message}");
                throw;
            }
        }

        private void CreateVersionDirectoryStructure(string versionDir)
        {
            Directory.CreateDirectory(versionDir);
            Directory.CreateDirectory(Path.Combine(versionDir, "libraries"));
            Directory.CreateDirectory(Path.Combine(versionDir, "assets"));
            Directory.CreateDirectory(Path.Combine(versionDir, "assets", "objects"));
            Directory.CreateDirectory(Path.Combine(versionDir, "assets", "indexes"));
            Directory.CreateDirectory(Path.Combine(versionDir, "natives"));

            _logAction($"📁 Создана структура директорий: {versionDir}");
        }

        private async Task DownloadVersionFilesParallelAsync(VersionInfo versionInfo, string versionDir,
            Action<double> progressCallback, CancellationToken cancellationToken)
        {
            var downloadItems = new List<VersionDownloadItem>();

            // 1. Клиент JAR
            if (versionInfo.Downloads?.Client != null)
            {
                downloadItems.Add(new VersionDownloadItem
                {
                    Url = versionInfo.Downloads.Client.Url,
                    Path = Path.Combine(versionDir, $"{Path.GetFileName(versionDir)}.jar"),
                    Size = versionInfo.Downloads.Client.Size,
                    Type = "Client"
                });
            }

            // 2. Библиотеки
            if (versionInfo.Libraries != null)
            {
                string librariesDir = Path.Combine(versionDir, "libraries");
                foreach (var library in versionInfo.Libraries)
                {
                    if (library.Downloads?.Artifact != null)
                    {
                        string libPath = Path.Combine(librariesDir, library.Downloads.Artifact.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(libPath));

                        downloadItems.Add(new VersionDownloadItem
                        {
                            Url = library.Downloads.Artifact.Url,
                            Path = libPath,
                            Size = library.Downloads.Artifact.Size,
                            Type = "Library"
                        });
                    }
                }
            }

            // 3. Ассеты (если есть)
            if (versionInfo.AssetIndex != null)
            {
                await DownloadAssetsAsync(versionInfo.AssetIndex, versionDir, progressCallback, cancellationToken);
            }

            _logAction($"📥 Всего файлов для загрузки: {downloadItems.Count}");

            // Параллельное скачивание
            await DownloadFilesParallelAsync(downloadItems, progressCallback, cancellationToken);
        }

        private async Task DownloadAssetsAsync(AssetIndex assetIndex, string versionDir,
            Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"📥 Загружаем ассеты: {assetIndex.Id}");

                // Загружаем индекс ассетов
                var assetsJson = await _httpClient.GetStringAsync(assetIndex.Url);
                var assetsIndex = JsonConvert.DeserializeObject<AssetsIndex>(assetsJson);

                string assetsDir = Path.Combine(versionDir, "assets");
                string objectsDir = Path.Combine(assetsDir, "objects");
                string indexesDir = Path.Combine(assetsDir, "indexes");

                // Сохраняем индекс
                string indexPath = Path.Combine(indexesDir, $"{assetIndex.Id}.json");
                File.WriteAllText(indexPath, assetsJson);

                var assetDownloads = new List<VersionDownloadItem>();
                int totalAssets = assetsIndex.Objects.Count;
                int processed = 0;

                // Подготавливаем загрузку ассетов
                foreach (var asset in assetsIndex.Objects)
                {
                    string hash = asset.Value.Hash;
                    string hashPrefix = hash.Substring(0, 2);
                    string assetPath = Path.Combine(objectsDir, hashPrefix, hash);

                    Directory.CreateDirectory(Path.GetDirectoryName(assetPath));

                    // Проверяем, не скачан ли уже ассет
                    if (!File.Exists(assetPath))
                    {
                        assetDownloads.Add(new VersionDownloadItem
                        {
                            Url = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}",
                            Path = assetPath,
                            Size = asset.Value.Size,
                            Type = "Asset",
                            Hash = hash
                        });
                    }

                    processed++;
                    progressCallback?.Invoke((double)processed / totalAssets * 50); // Первые 50% - подготовка
                }

                _logAction($"📥 Ассетов для загрузки: {assetDownloads.Count}");

                // Скачиваем ассеты
                await DownloadFilesParallelAsync(assetDownloads,
                    progress => progressCallback?.Invoke(50 + progress * 0.5), // Вторые 50%
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logAction($"⚠️ Ошибка загрузки ассетов: {ex.Message}");
            }
        }

        private async Task DownloadFilesParallelAsync(List<VersionDownloadItem> downloads,
            Action<double> progressCallback, CancellationToken cancellationToken)
        {
            if (downloads.Count == 0) return;

            var semaphore = new SemaphoreSlim(MAX_PARALLEL_DOWNLOADS);
            var tasks = new List<Task>();
            int completed = 0;
            int total = downloads.Count;
            object lockObject = new object();

            foreach (var download in downloads)
            {
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await DownloadFileWithRetryAsync(download, cancellationToken);

                        lock (lockObject)
                        {
                            completed++;
                            double progress = (double)completed / total * 100;
                            progressCallback?.Invoke(progress);

                            if (completed % 10 == 0 || completed == total)
                            {
                                _logAction($"📥 Прогресс: {completed}/{total} ({progress:F1}%)");
                            }
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _logAction($"❌ Ошибка загрузки {Path.GetFileName(download.Path)}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private async Task DownloadFileWithRetryAsync(VersionDownloadItem download, CancellationToken cancellationToken, int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (var response = await _httpClient.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var fileStream = new FileStream(download.Path, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            await stream.CopyToAsync(fileStream, 81920, cancellationToken);
                        }
                    }

                    return; // Успешно
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (retry < maxRetries - 1)
                {
                    _logAction($"Повторная попытка {retry + 1}/{maxRetries} для {Path.GetFileName(download.Path)}");
                    await Task.Delay(1000 * (retry + 1), cancellationToken);

                    if (File.Exists(download.Path))
                        File.Delete(download.Path);
                }
            }

            throw new Exception($"Не удалось скачать файл после {maxRetries} попыток: {Path.GetFileName(download.Path)}");
        }

        public bool IsVersionDownloaded(string versionId)
        {
            string versionDir = Path.Combine(_gameDirectory, "versions", versionId);
            string jarPath = Path.Combine(versionDir, $"{versionId}.jar");

            return File.Exists(jarPath) && new FileInfo(jarPath).Length > 1024 * 1024;
        }

        public List<string> GetDownloadedVersions()
        {
            var versions = new List<string>();
            string versionsDir = Path.Combine(_gameDirectory, "versions");

            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    string versionId = Path.GetFileName(dir);
                    if (IsVersionDownloaded(versionId))
                    {
                        versions.Add(versionId);
                    }
                }
            }

            return versions;
        }
    }
}