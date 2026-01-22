using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BMPLauncher.Core
{
    public class ModpackDownloader
    {
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly HttpClient _httpClient;
        private List<CFModpack> _availableModpacks = new List<CFModpack>();

        public ModpackDownloader(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // Словарь с модпаками
        private static readonly Dictionary<int, CFModpack> _theBarMaxxModpacks = new Dictionary<int, CFModpack>()
    {
        {
            715304, // Gloomy Rise [FORGE]
            new CFModpack
            {
                Id = 715304,
                Name = "Gloomy Rise [FORGE]",
                Description = "Модпак TheBarMaxx",
                DownloadCount = 50000,
                DateModified = new DateTime(2024, 1, 1),
                Authors = new List<CFAuthor>
                {
                    new CFAuthor { Name = "TheBarMaxx", Url = "" }
                },
                GameVersionLatestFiles = new List<CFGameVersionFile>
                {
                    new CFGameVersionFile
                    {
                        GameVersion = "1.20.1",
                        ProjectFileId = 7396468,
                        ProjectFileName = "Gloomy Rise-v1.4.3.zip"
                    }
                }
            }
        }
    };

        public async Task LoadTheBarMaxxModpacks()
        {
            try
            {
                _logAction("Загрузка модпаков TheBarMaxx...");
                _availableModpacks = new List<CFModpack>(_theBarMaxxModpacks.Values);
                _logAction($"Загружено {_availableModpacks.Count} модпаков");
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка: {ex.Message}");
                _availableModpacks = new List<CFModpack>();
            }
        }

        public List<CFModpack> GetAvailableModpacks()
        {
            return _availableModpacks;
        }

        // Основной метод установки модпака
        public async Task DownloadModpackAsync(int modpackId, string installDir,
    Action<double> progressCallback, CancellationToken cancellationToken)
        {
            string tempFile = null;

            try
            {
                _logAction($"=== УСТАНОВКА МОДПАКА {modpackId} ===");

                // Получаем информацию о модпаке
                CFModpack modpackInfo;
                if (!_theBarMaxxModpacks.TryGetValue(modpackId, out modpackInfo))
                {
                    throw new Exception($"Модпак с ID {modpackId} не найден");
                }

                var fileInfo = modpackInfo.GameVersionLatestFiles?.FirstOrDefault();
                if (fileInfo == null) throw new Exception("Не найден fileId");

                int fileId = fileInfo.ProjectFileId;

                // 1. Скачиваем архив модпака (10% прогресса)
                string downloadUrl = $"https://curseforge.com/api/v1/mods/{modpackId}/files/{fileId}/download";
                _logAction($"Скачиваем архив модпака: {downloadUrl}");

                string tempDir = Path.Combine(Path.GetTempPath(), "BMPLauncher");
                Directory.CreateDirectory(tempDir);
                tempFile = Path.Combine(tempDir, $"{modpackId}_{fileId}.zip");

                await DownloadFileWithRetryAsync(downloadUrl, tempFile, cancellationToken);
                progressCallback?.Invoke(10);

                _logAction("Архив скачан, ищем manifest.json...");

                // 2. Извлекаем и парсим manifest.json (5% прогресса)
                ModpackManifest manifest = await ExtractManifestFromArchive(tempFile, installDir);
                progressCallback?.Invoke(15);

                if (manifest == null)
                {
                    throw new Exception("Не найден manifest.json в архиве");
                }

                _logAction($"Найдено модов для скачивания: {manifest.Files?.Count ?? 0}");

                // 3. Создаем структуру папок (5% прогресса)
                CreateModpackStructure(installDir, manifest);
                progressCallback?.Invoke(20);

                // 4. Скачиваем моды ПАРАЛЛЕЛЬНО (75% прогресса)
                if (manifest.Files != null && manifest.Files.Count > 0)
                {
                    await DownloadModsAsync(manifest.Files, installDir, progress =>
                    {
                        // От 20% до 95%
                        progressCallback?.Invoke(20 + progress * 0.75);
                    }, cancellationToken);
                }

                // 5. Извлекаем overrides если есть (5% прогресса)
                if (!string.IsNullOrEmpty(manifest.Overrides))
                {
                    await ExtractOverridesAsync(tempFile, manifest.Overrides, installDir);
                }
                progressCallback?.Invoke(100);

                // 6. Удаляем временный файл
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                _logAction($"✅ Модпак '{modpackInfo.Name}' полностью установлен!");
            }
            catch (Exception ex)
            {
                _logAction($"❌ Ошибка: {ex.Message}");
                _logAction($"StackTrace: {ex.StackTrace}");

                if (tempFile != null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); }
                    catch { }
                }

                throw;
            }
        }

        // Метод скачивания файла
        private async Task DownloadFileAsync(string url, string destination,
            Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(60);

                    using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1 && progressCallback != null;

                        using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;

                            int bytesRead;
                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (canReportProgress)
                                {
                                    double progress = (double)totalRead / totalBytes * 100;
                                    progressCallback(progress);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка скачивания файла: {ex.Message}");
                throw;
            }
        }

        // Метод извлечения manifest.json (переименован для избежания конфликта)
        // Метод извлечения manifest.json
        private async Task<ModpackManifest> ExtractManifestFromArchive(string archivePath, string installDir)
        {
            try
            {
                _logAction("Ищем manifest.json в архиве...");

                using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                {
                    // Ищем manifest.json
                    ZipArchiveEntry manifestEntry = archive.Entries
                        .FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));

                    if (manifestEntry == null)
                    {
                        // Ищем в подпапках
                        manifestEntry = archive.Entries
                            .FirstOrDefault(e => e.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
                    }

                    if (manifestEntry != null)
                    {
                        // Читаем manifest.json
                        using (Stream stream = manifestEntry.Open())
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string json = await reader.ReadToEndAsync();
                            _logAction($"manifest.json найден, размер: {json.Length} символов");

                            ModpackManifest manifest = JsonConvert.DeserializeObject<ModpackManifest>(json);

                            // УБЕДИТЕСЬ ЧТО ПАПКА СУЩЕСТВУЕТ ПЕРЕД СОХРАНЕНИЕМ
                            Directory.CreateDirectory(installDir);

                            string manifestPath = Path.Combine(installDir, "manifest.json");
                            File.WriteAllText(manifestPath, json);

                            _logAction($"manifest.json сохранен в: {manifestPath}");

                            return manifest;
                        }
                    }
                }

                _logAction("manifest.json не найден в архиве");
                return null;
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка чтения manifest.json: {ex.Message}");
                _logAction($"StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        // Метод скачивания модов (ПАРАЛЛЕЛЬНАЯ ВЕРСИЯ)
        private async Task DownloadModsAsync(List<ModFile> modFiles, string installDir,
            Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"Начинаем ПАРАЛЛЕЛЬНОЕ скачивание {modFiles.Count} модов...");

                string modsDir = Path.Combine(installDir, "mods");
                Directory.CreateDirectory(modsDir);

                int totalMods = modFiles.Count;
                int downloadedMods = 0;
                object lockObject = new object();

                // Параметры параллелизма (можно регулировать)
                int maxParallelDownloads = 8; // Одновременно скачиваем 8 модов
                var semaphore = new SemaphoreSlim(maxParallelDownloads);

                // Создаем задачи для каждого мода
                var downloadTasks = modFiles.Select(async modFile =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        string modUrl = $"https://curseforge.com/api/v1/mods/{modFile.ProjectId}/files/{modFile.FileId}/download";
                        string modFileName = $"{modFile.ProjectId}_{modFile.FileId}.jar";
                        string modPath = Path.Combine(modsDir, modFileName);

                        // Пропускаем если файл уже существует
                        if (File.Exists(modPath) && new FileInfo(modPath).Length > 1024)
                        {
                            _logAction($"✓ Мод уже скачан: {modFile.ProjectId}/{modFile.FileId}");
                        }
                        else
                        {
                            try
                            {
                                await DownloadFileWithRetryAsync(modUrl, modPath, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logAction($"✗ Ошибка скачивания мода {modFile.ProjectId}: {ex.Message}");
                                return; // Пропускаем этот мод
                            }
                        }

                        // Обновляем прогресс
                        lock (lockObject)
                        {
                            downloadedMods++;
                            double progress = (double)downloadedMods / totalMods * 100;
                            progressCallback?.Invoke(progress);

                            // Логируем каждые 10% или когда скачано 10 модов
                            if (downloadedMods % 10 == 0 || downloadedMods == totalMods)
                            {
                                _logAction($"📦 Прогресс: {downloadedMods}/{totalMods} ({progress:F1}%)");
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Ждем завершения всех задач
                await Task.WhenAll(downloadTasks);

                _logAction($"✅ Скачано модов: {downloadedMods}/{totalMods}");
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка скачивания модов: {ex.Message}");
                throw;
            }
        }

        // Метод скачивания файла с повторными попытками
        private async Task DownloadFileWithRetryAsync(string url, string destination,
            CancellationToken cancellationToken, int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(30);

                        using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                        {
                            response.EnsureSuccessStatusCode();

                            using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            {
                                await stream.CopyToAsync(fileStream, 81920, cancellationToken);
                            }
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
                    _logAction($"Повторная попытка {retry + 1}/{maxRetries} для {Path.GetFileName(destination)}");
                    await Task.Delay(1000 * (retry + 1), cancellationToken);

                    if (File.Exists(destination))
                        File.Delete(destination);
                }
            }

            throw new Exception($"Не удалось скачать файл после {maxRetries} попыток: {Path.GetFileName(destination)}");
        }

        // Создание структуры папок
        // Создание структуры папок
        private void CreateModpackStructure(string installDir, ModpackManifest manifest)
        {
            _logAction("Создаем структуру папок модпака...");

            try
            {
                // УБЕДИТЕСЬ ЧТО ПАПКА СУЩЕСТВУЕТ
                Directory.CreateDirectory(installDir);

                // Основные папки
                Directory.CreateDirectory(Path.Combine(installDir, "mods"));
                Directory.CreateDirectory(Path.Combine(installDir, "config"));
                Directory.CreateDirectory(Path.Combine(installDir, "shaderpacks"));
                Directory.CreateDirectory(Path.Combine(installDir, "resourcepacks"));
                Directory.CreateDirectory(Path.Combine(installDir, "saves"));

                // Записываем информацию о модпаке
                string infoFile = Path.Combine(installDir, "MODPACK_INFO.txt");
                File.WriteAllText(infoFile,
                    $"Модпак: {manifest.Name}\n" +
                    $"Версия: {manifest.Version}\n" +
                    $"Автор: {manifest.Author}\n" +
                    $"Minecraft: {manifest.Minecraft?.Version}\n" +
                    $"Модлоадер: {manifest.Minecraft?.ModLoaders?.FirstOrDefault()?.Id}\n" +
                    $"Модов: {manifest.Files?.Count ?? 0}\n" +
                    $"Установлен: {DateTime.Now}");

                _logAction("Структура папок создана");
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка создания структуры папок: {ex.Message}");
                throw;
            }
        }

        // Извлечение overrides
        private async Task ExtractOverridesAsync(string archivePath, string overridesPath, string installDir)
        {
            await Task.Run(() =>
            {
                try
                {
                    _logAction($"Извлекаем overrides: {overridesPath}");

                    using (ZipArchive archive = ZipFile.OpenRead(archivePath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.StartsWith(overridesPath))
                            {
                                string relativePath = entry.FullName.Substring(overridesPath.Length);
                                string destination = Path.Combine(installDir, relativePath);

                                // Создаем директорию если нужно
                                string dir = Path.GetDirectoryName(destination);
                                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                {
                                    Directory.CreateDirectory(dir);
                                }

                                // Извлекаем файл
                                entry.ExtractToFile(destination, true);
                            }
                        }
                    }

                    _logAction("Overrides извлечены");
                }
                catch (Exception ex)
                {
                    _logAction($"Ошибка извлечения overrides: {ex.Message}");
                }
            });
        }
    }
}