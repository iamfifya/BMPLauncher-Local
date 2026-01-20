using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BMPLauncher
{
    public class DownloadItem
    {
        public string Url { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public string Type { get; set; }
    }

    public class DownloadSpeedCalculator
    {
        private long _totalBytes;
        private DateTime _startTime;
        private readonly object _lock = new object();

        public DownloadSpeedCalculator()
        {
            _startTime = DateTime.Now;
        }

        public void AddBytes(long bytes)
        {
            lock (_lock)
            {
                _totalBytes += bytes;
            }
        }

        public double GetSpeedMBps()
        {
            lock (_lock)
            {
                double elapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;
                return elapsedSeconds > 0 ? _totalBytes / elapsedSeconds / 1024.0 / 1024.0 : 0;
            }
        }
    }

    public class DownloadManager
    {
        private readonly string _versionDirectory; // Директория конкретной версии
        private readonly Func<string, Task> _logAction;
        private readonly Func<string, double, Task> _progressAction;
        private readonly CancellationToken _cancellationToken;
        private int _maxParallelism;

        public DownloadManager(string versionDirectory,
                              Func<string, Task> logAction,
                              Func<string, double, Task> progressAction,
                              bool turboMode = true,
                              CancellationToken cancellationToken = default)
        {
            _versionDirectory = versionDirectory;
            _logAction = logAction;
            _progressAction = progressAction;
            _cancellationToken = cancellationToken;
            _maxParallelism = turboMode ? 4 : 8;
        }

        public async Task DownloadAllAsync(VersionInfo versionInfo, string versionId)
        {
            var downloads = new List<DownloadItem>();

            // 1. Клиент JAR файл
            if (versionInfo.Downloads?.Client != null)
            {
                string jarPath = Path.Combine(_versionDirectory, $"{versionId}.jar");
                Directory.CreateDirectory(_versionDirectory);

                downloads.Add(new DownloadItem
                {
                    Url = versionInfo.Downloads.Client.Url,
                    Path = jarPath,
                    Size = versionInfo.Downloads.Client.Size,
                    Type = "Client"
                });

                await _logAction($"Добавлен клиент: {versionId}.jar");
            }

            // 2. Библиотеки
            if (versionInfo.Libraries != null)
            {
                string librariesDir = Path.Combine(_versionDirectory, "libraries");
                Directory.CreateDirectory(librariesDir);

                foreach (var library in versionInfo.Libraries)
                {
                    if (library.Downloads?.Artifact != null)
                    {
                        string libPath = Path.Combine(librariesDir, library.Downloads.Artifact.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(libPath));

                        downloads.Add(new DownloadItem
                        {
                            Url = library.Downloads.Artifact.Url,
                            Path = libPath,
                            Size = library.Downloads.Artifact.Size,
                            Type = "Library"
                        });
                    }
                }
                await _logAction($"Добавлено библиотек: {downloads.Count(d => d.Type == "Library")}");
            }

            await _logAction($"Всего файлов: {downloads.Count}");

            // 3. Загружаем последовательно
            await DownloadSequentially(downloads);
        }

        private async Task DownloadSequentially(List<DownloadItem> downloads)
        {
            int completed = 0;
            int total = downloads.Count;

            foreach (var download in downloads)
            {
                if (_cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await _logAction($"Загружаем: {Path.GetFileName(download.Path)}");
                    await DownloadFileAsync(download);
                    completed++;

                    double progress = (double)completed / total * 100;
                    await _progressAction($"Загружено {completed}/{total}", progress);
                }
                catch (Exception ex)
                {
                    await _logAction($"❌ Ошибка: {Path.GetFileName(download.Path)} - {ex.Message}");
                }
            }

            await _logAction($"✅ Загрузка завершена: {completed}/{total} файлов");
        }

        private async Task DownloadFileAsync(DownloadItem download)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                using (var response = await client.GetAsync(download.Url, _cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    Directory.CreateDirectory(Path.GetDirectoryName(download.Path));

                    using (var fileStream = new FileStream(download.Path, FileMode.Create, FileAccess.Write))
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
        }
    }
}