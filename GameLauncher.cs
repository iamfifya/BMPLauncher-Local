// GameLauncher.cs
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ProcessBuilder;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BMPLauncher.Core
{
    public class GameLauncher
    {
        private readonly string _gameDirectory;
        private readonly Action<string> _logAction;
        private readonly MinecraftLauncher _launcher;
        private readonly MinecraftPath _minecraftPath;

        public GameLauncher(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;
            _minecraftPath = new MinecraftPath(_gameDirectory);
            _launcher = new MinecraftLauncher(_minecraftPath);

            // Правильные события для версии 4.0.6
            _launcher.FileProgressChanged += (sender, e) =>
            {
                if (e.TotalTasks > 0)
                {
                    _logAction?.Invoke($"Загрузка: {e.ProgressedTasks}/{e.TotalTasks} файлов");
                }
            };
        }

        public async Task<Process> LaunchModpackAsync(
            string modpackDir,
            string minecraftVersion,
            string forgeVersion,
            string javaPath,
            string playerName,
            int minRamMb = 1024,
            int maxRamMb = 4096,
            string javaArgs = "")
        {
            try
            {
                _logAction?.Invoke("🚀 Инициализация запуска...");

                // Определяем версию для запуска
                string versionName = await PrepareVersionAsync(minecraftVersion, forgeVersion);

                // Создаем сессию
                var session = MSession.CreateOfflineSession(playerName);

                // Создаем опции запуска (правильные для 4.0.6)
                var launchOption = new MLaunchOption
                {
                    Session = session,
                    MaximumRamMb = maxRamMb,
                    MinimumRamMb = minRamMb,
                    Path = _minecraftPath
                };

                // Устанавливаем Java путь
                if (!string.IsNullOrEmpty(javaPath) && File.Exists(javaPath))
                {
                    launchOption.JavaPath = javaPath;
                }

                _logAction?.Invoke($"⚙️ Создаем процесс для версии: {versionName}");

                // Создаем процесс
                var process = await _launcher.CreateProcessAsync(versionName, launchOption);

                // Устанавливаем рабочую директорию
                process.StartInfo.WorkingDirectory = modpackDir;

                // Добавляем дополнительные аргументы Java
                if (!string.IsNullOrEmpty(javaArgs))
                {
                    process.StartInfo.Arguments = $"{javaArgs} {process.StartInfo.Arguments}";
                }

                // Настраиваем логирование
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = false;

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logAction?.Invoke($"[Game] {e.Data}");
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logAction?.Invoke($"[Error] {e.Data}");
                };

                _logAction?.Invoke("🎮 Запускаем игру...");

                return process;
            }
            catch (Exception)
            {
                _logAction?.Invoke("❌ Ошибка при создании процесса");
                throw;
            }
        }

        private async Task<string> PrepareVersionAsync(string minecraftVersion, string forgeVersion)
        {
            if (string.IsNullOrEmpty(forgeVersion))
                return minecraftVersion;

            if (forgeVersion.Contains("forge"))
            {
                try
                {
                    string forgeVersionNumber = forgeVersion.Replace("forge-", "");
                    string versionName = $"forge-{minecraftVersion}-{forgeVersionNumber}";

                    // Проверяем, установлен ли Forge
                    if (!IsForgeInstalled(versionName))
                    {
                        _logAction?.Invoke($"🔨 Устанавливаем Forge {forgeVersionNumber}...");

                        var forgeInstaller = new ForgeInstaller(_launcher);
                        versionName = await forgeInstaller.Install(minecraftVersion, forgeVersionNumber);
                    }

                    return versionName;
                }
                catch
                {
                    _logAction?.Invoke("⚠️ Не удалось установить Forge, используем ванильную версию");
                    return minecraftVersion;
                }
            }

            return minecraftVersion;
        }

        private bool IsForgeInstalled(string versionName)
        {
            string versionDir = Path.Combine(_gameDirectory, "versions", versionName);
            return Directory.Exists(versionDir);
        }
    }
}