using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BMPLauncher
{
    public class ModpackDownloader
    {
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly HttpClient _httpClient;
        private List<CFModpack> _availableModpacks = new List<CFModpack>();
        private const int MAX_PARALLEL_DOWNLOADS = 6;
        private const string API_KEY = "$2a$10$exoj8LP0e3YmndJrzmyM1ug2PNmk9jlZHxDfGJrAbURBZSgndnZZq";

        public ModpackDownloader(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", API_KEY);
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task LoadModpacksByAuthor(string authorName)
        {
            try
            {
                string url = $"https://api.curseforge.com/v1/mods/search?gameId=432&classId=4471&searchFilter={Uri.EscapeDataString(authorName)}&pageSize=50";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<CFSearchResponse>(json);

                    _availableModpacks = result?.Data?.Where(m =>
                        m.Authors?.Any(a => a.Name?.Equals(authorName, StringComparison.OrdinalIgnoreCase) == true) == true)
                        .ToList() ?? new List<CFModpack>();
                }
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка загрузки модпаков: {ex.Message}");
            }
        }

        public async Task DownloadModpackAsync(int modpackId, string installDir, Action<double> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                _logAction($"Начинаем скачивание модпака");

                // Получаем манифест
                var manifest = await GetModpackManifestAsync(modpackId);
                if (manifest == null) throw new Exception("Не удалось получить манифест");

                // Создаем директории
                CreateModpackDirectories(installDir);

                // Скачиваем файлы
                await DownloadModpackFilesAsync(manifest, installDir, progressCallback, cancellationToken);

                _logAction($"Модпак скачан");
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка скачивания модпака: {ex.Message}");
                throw;
            }
        }

        private async Task<CFManifest> GetModpackManifestAsync(int modpackId)
        {
            try
            {
                // Получаем информацию о файлах
                string filesUrl = $"https://api.curseforge.com/v1/mods/{modpackId}/files";
                var filesResponse = await _httpClient.GetAsync(filesUrl);

                if (!filesResponse.IsSuccessStatusCode) return null;

                string filesJson = await filesResponse.Content.ReadAsStringAsync();
                var filesResult = JsonConvert.DeserializeObject<CFFilesResponse>(filesJson);
                var latestFile = filesResult?.Data?.OrderByDescending(f => f.Id).FirstOrDefault();

                if (latestFile == null) return null;

                // Скачиваем файл модпака
                string tempFile = await DownloadFileAsync(latestFile.DownloadUrl, Path.GetTempFileName());

                // Извлекаем манифест
                var manifest = ExtractManifest(tempFile);
                File.Delete(tempFile);

                return manifest;
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка получения манифеста: {ex.Message}");
                return null;
            }
        }

        private void CreateModpackDirectories(string installDir)
        {
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(Path.Combine(installDir, "mods"));
            Directory.CreateDirectory(Path.Combine(installDir, "config"));
            Directory.CreateDirectory(Path.Combine(installDir, "shaderpacks"));
            Directory.CreateDirectory(Path.Combine(installDir, "resourcepacks"));
        }

        private async Task DownloadModpackFilesAsync(CFManifest manifest, string installDir, Action<double> progressCallback, CancellationToken cancellationToken)
        {
            if (manifest.Files == null || manifest.Files.Count == 0) return;

            var semaphore = new SemaphoreSlim(MAX_PARALLEL_DOWNLOADS);
            var tasks = new List<Task>();
            int completed = 0;

            foreach (var file in manifest.Files)
            {
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var fileInfo = await GetFileInfoAsync(file.ProjectId, file.FileId);
                        if (fileInfo != null)
                        {
                            string destPath = Path.Combine(installDir, "mods", fileInfo.FileName);
                            await DownloadFileAsync(fileInfo.DownloadUrl, destPath);

                            Interlocked.Increment(ref completed);
                            double progress = (double)completed / manifest.Files.Count * 100;
                            progressCallback?.Invoke(progress);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logAction($"Ошибка загрузки файла: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private async Task<CFFileInfo> GetFileInfoAsync(int projectId, int fileId)
        {
            try
            {
                string url = $"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<CFFileResponse>(json);
                    return result?.Data;
                }
            }
            catch { }
            return null;
        }

        private async Task<string> DownloadFileAsync(string url, string destPath)
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
            return destPath;
        }

        private CFManifest ExtractManifest(string archivePath)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        return JsonConvert.DeserializeObject<CFManifest>(json);
                    }
                }
            }
            return null;
        }

        public List<CFModpack> GetAvailableModpacks() => _availableModpacks;
    }
}