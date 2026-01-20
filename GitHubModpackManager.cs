using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

namespace BMPLauncher
{
    public class GitHubModpackManager
    {
        private readonly string _githubOwner;
        private readonly string _githubRepo;
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly HttpClient _httpClient;

        public GitHubModpackManager(string githubOwner, string githubRepo,
                                  string gameDirectory, Action<string> logAction)
        {
            _githubOwner = githubOwner;
            _githubRepo = githubRepo;
            _gameDirectory = gameDirectory;
            _logAction = logAction;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BMPLauncher/1.0");
        }

        // В методе GetAvailableModpacksAsync обновите URL:
        public async Task<List<GitHubModpackInfo>> GetAvailableModpacksAsync()
        {
            try
            {
                _logAction("Получаем список модпаков с GitHub...");

                // Получаем корневой каталог репозитория
                string apiUrl = $"https://api.github.com/repos/{_githubOwner}/{_githubRepo}/contents";

                var response = await _httpClient.GetStringAsync(apiUrl);
                var items = JsonConvert.DeserializeObject<List<GitHubModpackInfo>>(response);

                // Фильтруем только папки (игнорируем файлы и скрытые папки)
                var modpacks = new List<GitHubModpackInfo>();

                foreach (var item in items.Where(d => d.Type == "dir" && !d.Name.StartsWith(".")))
                {
                    try
                    {
                        // Проверяем есть ли manifest.json в папке
                        string manifestUrl = $"https://api.github.com/repos/{_githubOwner}/{_githubRepo}/contents/{item.Name}/manifest.json";
                        var manifestResponse = await _httpClient.GetAsync(manifestUrl);

                        if (manifestResponse.IsSuccessStatusCode)
                        {
                            var modpackInfo = new GitHubModpackInfo
                            {
                                Name = item.Name,
                                Path = item.Path,
                                DownloadUrl = $"https://raw.githubusercontent.com/{_githubOwner}/{_githubRepo}/main/{item.Name}/manifest.json",
                                Type = "modpack"
                            };
                            modpacks.Add(modpackInfo);
                        }
                    }
                    catch
                    {
                        // Пропускаем папки без манифеста
                    }
                }

                _logAction($"Найдено модпаков: {modpacks.Count}");
                return modpacks;
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка получения списка модпаков: {ex.Message}");
                return new List<GitHubModpackInfo>();
            }
        }

        // В методе GetModpackManifestAsync:
        public async Task<GitHubModpackManifest> GetModpackManifestAsync(string modpackName)
        {
            try
            {
                _logAction($"Загружаем манифест модпака: {modpackName}");

                // Новый URL для репозитория BMProjects-Development/mods
                string manifestUrl = $"https://raw.githubusercontent.com/{_githubOwner}/{_githubRepo}/main/{modpackName}/manifest.json";
                var manifestJson = await _httpClient.GetStringAsync(manifestUrl);

                var manifest = JsonConvert.DeserializeObject<GitHubModpackManifest>(manifestJson);
                manifest.Name = modpackName; // Убедимся что имя установлено

                return manifest;
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка загрузки манифеста: {ex.Message}");
                return null;
            }
        }

        public async Task InstallModpackAsync(GitHubModpackManifest manifest,
                                            Action<string> updateStatus,
                                            Action<double> updateProgress,
                                            CancellationToken cancellationToken)
        {
            try
            {
                // Создаем директорию для модпака
                string modpackDir = Path.Combine(_gameDirectory, "modpacks", manifest.Name);
                Directory.CreateDirectory(modpackDir);

                // Создаем папки
                string modsDir = Path.Combine(modpackDir, "mods");
                string configDir = Path.Combine(modpackDir, "config");
                string shaderpacksDir = Path.Combine(modpackDir, "shaderpacks");

                Directory.CreateDirectory(modsDir);
                Directory.CreateDirectory(configDir);
                Directory.CreateDirectory(shaderpacksDir);

                _logAction($"Устанавливаем модпак: {manifest.Name} v{manifest.Version}");
                _logAction($"Minecraft: {manifest.MinecraftVersion}");
                _logAction($"Модлоадер: {manifest.ModLoader} {manifest.ModLoaderVersion}");

                // Скачиваем файлы
                if (manifest.Files != null && manifest.Files.Count > 0)
                {
                    int totalFiles = manifest.Files.Count;
                    int downloadedFiles = 0;

                    foreach (var file in manifest.Files)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        updateStatus($"Скачиваем {file.Name}...");

                        string filePath = Path.Combine(modpackDir, file.DestinationPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                        await DownloadFileWithRetryAsync(file.Url, filePath, 3, cancellationToken);

                        downloadedFiles++;
                        double progress = (double)downloadedFiles / totalFiles * 100;
                        updateProgress(progress);

                        _logAction($"✓ {file.Name}");
                    }
                }

                // Сохраняем информацию об установке
                SaveInstallationInfo(manifest, modpackDir);

                _logAction($"✅ Модпак {manifest.Name} успешно установлен!");
            }
            catch (Exception ex)
            {
                _logAction($"❌ Ошибка установки модпака: {ex.Message}");
                throw;
            }
        }

        private async Task DownloadFileWithRetryAsync(string url, string filePath, int maxRetries, CancellationToken cancellationToken)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            await stream.CopyToAsync(fileStream, 81920, cancellationToken);
                        }
                    }

                    return; // Успешно скачали
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (retry < maxRetries - 1)
                {
                    _logAction($"Повторная попытка {retry + 1}/{maxRetries} для {Path.GetFileName(filePath)}: {ex.Message}");
                    await Task.Delay(1000 * (retry + 1), cancellationToken);

                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
            }
        }

        private void SaveInstallationInfo(GitHubModpackManifest manifest, string installPath)
        {
            var installedModpack = new InstalledModpack
            {
                Name = manifest.Name,
                Version = manifest.Version,
                MinecraftVersion = manifest.MinecraftVersion,
                ModLoader = manifest.ModLoader,
                InstallPath = installPath,
                InstallDate = DateTime.Now
            };

            string installedModpacksFile = Path.Combine(_gameDirectory, "installed_modpacks.json");
            List<InstalledModpack> installedModpacks;

            if (File.Exists(installedModpacksFile))
            {
                var json = File.ReadAllText(installedModpacksFile);
                installedModpacks = JsonConvert.DeserializeObject<List<InstalledModpack>>(json) ?? new List<InstalledModpack>();
            }
            else
            {
                installedModpacks = new List<InstalledModpack>();
            }

            // Удаляем старую версию если есть
            installedModpacks.RemoveAll(m => m.Name == manifest.Name);
            installedModpacks.Add(installedModpack);

            File.WriteAllText(installedModpacksFile, JsonConvert.SerializeObject(installedModpacks, Formatting.Indented));
        }

        public List<InstalledModpack> GetInstalledModpacks()
        {
            try
            {
                string installedModpacksFile = Path.Combine(_gameDirectory, "installed_modpacks.json");

                if (File.Exists(installedModpacksFile))
                {
                    var json = File.ReadAllText(installedModpacksFile);
                    return JsonConvert.DeserializeObject<List<InstalledModpack>>(json) ?? new List<InstalledModpack>();
                }
            }
            catch (Exception ex)
            {
                _logAction($"Ошибка чтения установленных модпаков: {ex.Message}");
            }

            return new List<InstalledModpack>();
        }
    }
}