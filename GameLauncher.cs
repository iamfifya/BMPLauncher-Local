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
        private MinecraftLauncher _launcher;
        private MinecraftPath _minecraftPath;

        public GameLauncher(string gameDirectory, Action<string> logAction)
        {
            _gameDirectory = gameDirectory;
            _logAction = logAction;
            Initialize();
        }

        private void Initialize()
        {
            _minecraftPath = new MinecraftPath(_gameDirectory);
            _launcher = new MinecraftLauncher(_minecraftPath);

            // Настраиваем прогресс (правильное событие для версии 4.0.6)
            _launcher.ProgressChanged += (sender, e) =>
            {
                if (e is DownloadFileChangedEventArgs downloadArgs)
                {
                    _logAction?.Invoke($"Загрузка: {downloadArgs.FileName} ({downloadArgs.ProgressPercentage}%)");
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

                // Устанавливаем Java путь если указан
                if (!string.IsNullOrEmpty(javaPath) && File.Exists(javaPath))
                {
                    launchOption.JavaPath = javaPath;
                }

                // Добавляем дополнительные аргументы Java
                if (!string.IsNullOrEmpty(javaArgs))
                {
                    // В 4.0.6 дополнительные аргументы добавляются через StartOption
                    launchOption.StartOption.AdditionalJavaArguments = javaArgs;
                }

                // Устанавливаем рабочую директорию (GameDirectory) - это важно!
                launchOption.StartOption.WorkingDirectory = modpackDir;

                _logAction?.Invoke($"⚙️ Создаем процесс для версии: {versionName}");

                // Создаем процесс
                var process = await _launcher.CreateProcessAsync(versionName, launchOption);

                // Настраиваем логирование
                SetupProcessLogging(process);

                _logAction?.Invoke("🎮 Запускаем игру...");

                return process;
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"❌ Ошибка запуска: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logAction?.Invoke($"Детали: {ex.InnerException.Message}");
                }
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

                    _logAction?.Invoke($"✅ Forge готов: {versionName}");
                    return versionName;
                }
                catch (Exception ex)
                {
                    _logAction?.Invoke($"⚠️ Не удалось установить Forge: {ex.Message}");
                    return minecraftVersion; // Возвращаем ванильную версию
                }
            }

            return minecraftVersion;
        }

        private bool IsForgeInstalled(string versionName)
        {
            string versionDir = Path.Combine(_gameDirectory, "versions", versionName);
            return Directory.Exists(versionDir);
        }

        private void SetupProcessLogging(Process process)
        {
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = false; // Окно будет видно!

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
        }
    }
}