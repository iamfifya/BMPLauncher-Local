using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ProcessBuilder;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BMPLauncher.Core
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Константы
        private const string ClientId = "bmplauncher3";
        private const string ClientSecret = "fEf2GGmZg7I0gzDWJnSs9se2u8v6lWoDDpCW9WiB02szHxV_vg8eehXh3g5u92Ej";
        private const string RedirectUri = "http://localhost:8081/ely-callback";

        // Переменные состояния
        private string BaseDirectory;
        private string GameDirectory;
        private VersionManifest _versionManifest;
        private JavaInfo _currentJavaInfo;
        private string _currentAccessToken;
        private HttpListener _httpListener;
        private bool _isAuthInProgress = false;
        private ElyAccountInfo _currentProfile;
        private LauncherSettings _settings;
        private List<CFModpack> _availableModpacks = new List<CFModpack>();
        private CFModpack _selectedModpack;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isClosing = false;
        private readonly Action<string> _logAction;

        private ForgeInstaller _forgeInstaller;

        // Менеджеры
        private VersionDownloader _versionDownloader;
        private ModpackDownloader _modpackDownloader;

        // Свойства для привязки
        private bool _modpacksLoaded = false;
        private bool _isLoadingModpacks = false;
        public event PropertyChangedEventHandler PropertyChanged;

        private string _sanitizedModpackName;

        // GameLauncher
        private MinecraftLauncher _minecraftLauncher;
        private MinecraftPath _minecraftPath;

        public bool ModpacksLoaded
        {
            get => _modpacksLoaded;
            set
            {
                _modpacksLoaded = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoadingModpacks
        {
            get => _isLoadingModpacks;
            set
            {
                _isLoadingModpacks = value;
                OnPropertyChanged();
            }
        }

        public bool IsAuthInProgress
        {
            get => _isAuthInProgress;
            set
            {
                _isAuthInProgress = value;
                OnPropertyChanged();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AuthStatusText.Text = value ? "Авторизация..." : (_currentProfile != null ? "Авторизован" : "Не авторизован");
                    AuthStatusText.Foreground = value ? System.Windows.Media.Brushes.Yellow :
                        (_currentProfile != null ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.LightCoral);
                }));
            }
        }


        private void DebugLaunchOptionProperties()
        {
            var launchOption = new MLaunchOption();
            var properties = launchOption.GetType().GetProperties();

            LogToConsole("=== Свойства MLaunchOption ===");
            foreach (var prop in properties)
            {
                LogToConsole($"{prop.Name} ({prop.PropertyType.Name})");
            }
            LogToConsole("==============================");
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                // Инициализируем лог
                LogToConsole("Инициализация лаунчера...");

                // Инициализируем CancellationTokenSource
                _cancellationTokenSource = new CancellationTokenSource();
                _isClosing = false;

                // Устанавливаем путь по умолчанию
                BaseDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BMPLauncher");
                GameDirectory = BaseDirectory;

                LogToConsole($"Базовый путь: {BaseDirectory}");

                // Пробуем загрузить настройки
                try
                {
                    _settings = LauncherSettings.Load();
                    LogToConsole("Настройки загружены");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Ошибка загрузки настроек: {ex.Message}");
                    _settings = null;
                }

                // Если настройки не загрузились или GameDirectory пустое - создаем новые
                if (_settings == null || string.IsNullOrEmpty(_settings.GameDirectory))
                {
                    LogToConsole("Создаем новые настройки");
                    _settings = new LauncherSettings();
                    _settings.GameDirectory = BaseDirectory;

                    // Используем путь из настроек
                    GameDirectory = _settings.GameDirectory;

                    // Сохраняем сразу
                    _settings.Save();
                    LogToConsole($"Сохранен GameDirectory: {GameDirectory}");
                }
                else
                {
                    // Используем сохраненный путь
                    GameDirectory = _settings.GameDirectory;
                    LogToConsole($"Используем сохраненный GameDirectory: {GameDirectory}");
                }

                // Создаем базовую структуру папок
                try
                {
                    Directory.CreateDirectory(GameDirectory);
                    Directory.CreateDirectory(Path.Combine(GameDirectory, "versions"));
                    Directory.CreateDirectory(Path.Combine(GameDirectory, "modpacks"));
                    LogToConsole("Структура папок создана");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Ошибка создания папок: {ex.Message}");
                }

                // Инициализируем CmlLib
                try
                {
                    _minecraftPath = new MinecraftPath(GameDirectory);
                    _minecraftLauncher = new MinecraftLauncher(_minecraftPath);

                    // Настраиваем события прогресса
                    _minecraftLauncher.FileProgressChanged += (sender, e) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = $"Загрузка: {e.ProgressedTasks}/{e.TotalTasks} файлов";
                            if (e.TotalTasks > 0)
                            {
                                DownloadProgress.Value = (double)e.ProgressedTasks / e.TotalTasks * 100;
                            }
                        });
                    };

                    LogToConsole("CmlLib инициализирован");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Ошибка инициализации CmlLib: {ex.Message}");
                }

                // ТОЛЬКО ПОСЛЕ ЭТОГО инициализируем менеджеры
                try
                {
                    LogToConsole("Инициализация менеджеров...");
                    _versionDownloader = new VersionDownloader(GameDirectory, LogToConsole);
                    _modpackDownloader = new ModpackDownloader(GameDirectory, LogToConsole);
                    LogToConsole("Менеджеры инициализированы");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Ошибка инициализации менеджеров: {ex.Message}");
                }

                // Восстанавливаем настройки
                try
                {
                    RestoreSettings();
                    LogToConsole("Настройки восстановлены");
                }
                catch (Exception ex)
                {
                    LogToConsole($"Ошибка восстановления настроек: {ex.Message}");
                }

                LogToConsole("Лаунчер инициализирован успешно!");

                // Асинхронная загрузка модпаков
                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    await LoadModpacksAutomatically();
                });
            }
            catch (Exception ex)
            {
                string errorMsg = $"Ошибка инициализации: {ex.Message}\nStackTrace: {ex.StackTrace}";
                LogToConsole(errorMsg);

                MessageBox.Show($"Ошибка инициализации: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogToConsole(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogToConsole(message));
                return;
            }

            ConsoleOutput.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            ConsoleOutput.ScrollToEnd();
        }

        private void ModpackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedModpack = ModpacksListBox.SelectedItem as CFModpack;
            if (_selectedModpack != null)
            {
                _sanitizedModpackName = SanitizeFileName(_selectedModpack.Name);
                LogToConsole($"Выбран модпак: {_selectedModpack.Name} (папка: {_sanitizedModpackName})");
            }
        }

        private async Task LoadModpacksAutomatically()
        {
            if (IsLoadingModpacks) return;
            IsLoadingModpacks = true;

            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = "Загрузка модпаков TheBarMaxx...";
                });

                await _modpackDownloader.LoadTheBarMaxxModpacks();
                _availableModpacks = _modpackDownloader.GetAvailableModpacks();

                await Dispatcher.InvokeAsync(() =>
                {
                    ModpacksListBox.ItemsSource = _availableModpacks;
                    ModpackCountText.Text = $"({_availableModpacks.Count} модпаков TheBarMaxx)";

                    if (_availableModpacks.Count == 0)
                    {
                        LogToConsole("⚠️ Не загружены модпаки TheBarMaxx");
                    }
                    else
                    {
                        LogToConsole($"✅ Загружено {_availableModpacks.Count} модпаков TheBarMaxx");
                    }

                    ModpacksLoaded = true;
                    StatusText.Text = "Готов";
                });
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Ошибка загрузки модпаков: {ex.Message}");
            }
            finally
            {
                IsLoadingModpacks = false;
            }
        }

        private void SearchModpacksButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() => SearchModpacks(SearchModpackTextBox.Text));
        }

        private void SearchModpackTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Task.Run(() => SearchModpacks(SearchModpackTextBox.Text));
            }
        }

        private void SearchModpacks(string query)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    ModpacksListBox.ItemsSource = _availableModpacks;
                    ModpackCountText.Text = $"({_availableModpacks.Count} модпаков)";
                    return;
                }

                var filtered = _availableModpacks
                    .Where(m => m.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               m.Description?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                ModpacksListBox.ItemsSource = filtered;
                ModpackCountText.Text = filtered.Count > 0 ? $"({filtered.Count} найдено)" : "Ничего не найдено";
            });
        }

        private async void InstallModpack_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpack == null)
            {
                MessageBox.Show("Выберите модпак!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var installButton = sender as Button;
            installButton.IsEnabled = false;

            try
            {
                StatusText.Text = $"Установка {_selectedModpack.Name}...";

                LogToConsole($"=== ИНФОРМАЦИЯ О МОДПАКЕ ===");
                LogToConsole($"ID: {_selectedModpack.Id}");
                LogToConsole($"Имя: {_selectedModpack.Name}");
                LogToConsole($"FileId: {_selectedModpack.GameVersionLatestFiles?.FirstOrDefault()?.ProjectFileId ?? 0}");
                LogToConsole($"Имя файла: {_selectedModpack.GameVersionLatestFiles?.FirstOrDefault()?.ProjectFileName}");
                LogToConsole($"============================");

                string sanitizedModpackName = SanitizeFileName(_selectedModpack.Name);
                string modpackDir = Path.Combine(GameDirectory, "modpacks", sanitizedModpackName);

                LogToConsole($"Оригинальное имя: {_selectedModpack.Name}");
                LogToConsole($"Очищенное имя: {sanitizedModpackName}");
                LogToConsole($"Путь установки: {modpackDir}");

                await _modpackDownloader.DownloadModpackAsync(_selectedModpack.Id, modpackDir,
                    progress => Dispatcher.BeginInvoke(new Action(() => DownloadProgress.Value = progress)),
                    _cancellationTokenSource.Token);

                MessageBox.Show($"Модпак установлен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка установки: {ex.Message}");
                LogToConsole($"StackTrace: {ex.StackTrace}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                installButton.IsEnabled = true;
                StatusText.Text = "Готов";
                DownloadProgress.Value = 0;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Modpack";

            string invalidChars = new string(Path.GetInvalidFileNameChars()) + "[]";
            string sanitized = fileName;

            foreach (char c in invalidChars)
            {
                sanitized = sanitized.Replace(c.ToString(), "");
            }

            sanitized = sanitized.Trim();

            if (string.IsNullOrEmpty(sanitized))
                sanitized = "Modpack_" + Guid.NewGuid().ToString().Substring(0, 8);

            return sanitized;
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpack == null)
            {
                MessageBox.Show("Выберите модпак!");
                return;
            }

            string playerName = string.IsNullOrEmpty(PlayerNameTextBox.Text) ? "Player" : PlayerNameTextBox.Text;

            LaunchButton.IsEnabled = false;
            LogToConsole("--- ЗАПУСК ИГРЫ ---");

            try
            {
                string sanitizedModpackName = SanitizeFileName(_selectedModpack.Name);
                string modpackDir = Path.Combine(GameDirectory, "modpacks", sanitizedModpackName);

                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"📂 Папка сборки: {modpackDir}");
                });

                bool modpackExists = await Task.Run(() => Directory.Exists(modpackDir));

                if (!modpackExists)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        throw new Exception($"Папка модпака не найдена: {sanitizedModpackName}\n" +
                                          $"Путь: {modpackDir}\n" +
                                          $"Возможно, модпак еще не установлен.");
                    });
                }

                string manifestPath = Path.Combine(modpackDir, "manifest.json");
                bool manifestExists = await Task.Run(() => File.Exists(manifestPath));

                if (!manifestExists)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        throw new Exception($"Файл manifest.json не найден!\n" +
                                          $"Путь: {manifestPath}\n" +
                                          $"Проверьте установку модпака.");
                    });
                }

                string javaPath = await Task.Run(() =>
                {
                    string path = _currentJavaInfo?.Path;

                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        return TryFindJava8();
                    }
                    return path;
                });

                if (string.IsNullOrEmpty(javaPath))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        throw new Exception("Java 8 не найдена! Установите её или укажите путь в настройках.");
                    });
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"☕ Используем Java: {javaPath}");
                    if (!File.Exists(javaPath))
                    {
                        LogToConsole($"❌ Файл Java не найден: {javaPath}");
                        // Попробуйте найти Java автоматически:
                        var javaInfo = JavaHelper.FindJava();
                        if (javaInfo != null)
                        {
                            javaPath = javaInfo.Path;
                            LogToConsole($"✅ Найдена Java: {javaPath}");
                        }
                    }
                });

                var manifestData = await Task.Run(() =>
                {
                    string manifestJson = File.ReadAllText(manifestPath);
                    var manifest = JsonConvert.DeserializeObject<CFManifest>(manifestJson);
                    return new
                    {
                        Version = manifest.Minecraft.Version,
                        ForgeId = manifest.Minecraft.ModLoaders.FirstOrDefault()?.Id ?? ""
                    };
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"🎮 Версия Minecraft: {manifestData.Version}");
                    LogToConsole($"🔨 Версия Forge: {manifestData.ForgeId}");
                });

                await Task.Run(async () =>
                {
                    try
                    {
                        await LaunchWithCmlLib(modpackDir, manifestData.Version, manifestData.ForgeId, javaPath, playerName);
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            LogToConsole($"❌ ОШИБКА ПРИ ЗАПУСКЕ: {ex.Message}");
                            LogToConsole($"StackTrace: {ex.StackTrace}");
                        });
                        throw;
                    }
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole("✅ Запрос на запуск игры отправлен!");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"❌ ОШИБКА: {ex.Message}");
                    MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LaunchButton.IsEnabled = true;
                });
            }
        }

        private async Task LaunchWithCmlLib(string modpackDir, string minecraftVersion, string forgeVersion, string javaPath, string playerName)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => LogToConsole("🚀 Запуск через CmlLib..."));

                string versionToLaunch = minecraftVersion;

                // 1. Установка Forge (если требуется)
                if (!string.IsNullOrEmpty(forgeVersion) && forgeVersion.Contains("forge"))
                {
                    await Dispatcher.InvokeAsync(() => LogToConsole("🔨 Устанавливаем Forge..."));

                    try
                    {
                        string forgeVersionNumber = forgeVersion.Replace("forge-", "");
                        var forgeInstaller = new CmlLib.Core.Installer.Forge.ForgeInstaller(_minecraftLauncher);
                        versionToLaunch = await forgeInstaller.Install(minecraftVersion, forgeVersionNumber);

                        await Dispatcher.InvokeAsync(() => LogToConsole($"✅ Forge установлен: {versionToLaunch}"));
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                            LogToConsole($"❌ ОШИБКА установки Forge: {ex.Message}"));
                        await Dispatcher.InvokeAsync(() =>
                            LogToConsole($"Попробуем запустить без Forge, но моды не будут работать!"));
                        // Не переключаемся на ванильную версию - если Forge не установился, моды не заработают
                    }
                }

                // 2. Создаем сессию
                var session = MSession.CreateOfflineSession(playerName);

                // 3. Получаем значения из UI-элементов
                int minRam = 0, maxRam = 0;
                await Dispatcher.InvokeAsync(() =>
                {
                    minRam = ParseRamToMb(GetComboBoxValue(XmsComboBox) ?? "1G");
                    maxRam = ParseRamToMb(GetComboBoxValue(XmxComboBox) ?? "2G");
                });

                // 4. Инициализируем MLaunchOption
                var launchOption = new MLaunchOption
                {
                    Session = session,
                    MinimumRamMb = minRam,
                    MaximumRamMb = maxRam,
                    Path = new MinecraftPath(GameDirectory),
                    JavaPath = javaPath
                };

                await Dispatcher.InvokeAsync(() => LogToConsole("⚙️ Создаем процесс..."));

                // 5. Создаем процесс через лаунчер
                var process = await _minecraftLauncher.CreateProcessAsync(versionToLaunch, launchOption);

                // 6. КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Вручную исправляем аргументы для модпака
                await Dispatcher.InvokeAsync(() => LogToConsole("🔧 Корректируем аргументы для модпака..."));

                // Исправляем gameDir в аргументах
                string oldGameDir = $"--gameDir {GameDirectory}";
                string newGameDir = $"--gameDir \"{modpackDir}\"";

                if (process.StartInfo.Arguments.Contains(oldGameDir))
                {
                    process.StartInfo.Arguments = process.StartInfo.Arguments.Replace(oldGameDir, newGameDir);
                }
                else
                {
                    // Если gameDir в другом формате, добавляем его
                    process.StartInfo.Arguments += $" {newGameDir}";
                }

                // Добавляем аргумент для версии Forge если нужно
                if (versionToLaunch.Contains("forge"))
                {
                    process.StartInfo.Arguments += $" --version {versionToLaunch}";
                }

                // 7. Устанавливаем рабочую директорию
                process.StartInfo.WorkingDirectory = modpackDir;

                // 8. Отладочное логирование
                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"🔍 Аргументы процесса: {process.StartInfo.Arguments}");
                    LogToConsole($"📁 Рабочая директория: {process.StartInfo.WorkingDirectory}");
                    LogToConsole($"☕ Java: {process.StartInfo.FileName}");
                    LogToConsole($"🎮 Запускаемая версия: {versionToLaunch}");

                    // Проверяем пути к модам
                    string modsPath = System.IO.Path.Combine(modpackDir, "mods");
                    if (System.IO.Directory.Exists(modsPath))
                    {
                        int modCount = System.IO.Directory.GetFiles(modsPath, "*.jar").Length;
                        LogToConsole($"📦 Найдено модов: {modCount}");
                    }
                    else
                    {
                        LogToConsole($"⚠️ Папка mods не найдена: {modsPath}");
                    }
                });

                // 9. Настраиваем перехват вывода
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = false;

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() => LogToConsole($"[Game] {e.Data}"));
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() => LogToConsole($"[Error] {e.Data}"));
                    }
                };

                // 10. Запускаем процесс
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"✅ Игра запущена! PID: {process.Id}");
                    LogToConsole($"⚠️ ВНИМАНИЕ: Если моды не загружаются, проверьте:");
                    LogToConsole($"   1. Установлен ли Forge версии {forgeVersion}");
                    LogToConsole($"   2. Находится ли папка mods в: {modpackDir}\\mods");
                    LogToConsole($"   3. Совместимы ли моды с версией {minecraftVersion}");
                });

                // 11. Фоновое отслеживание
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);

                    if (process.HasExited)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogToConsole($"⚠️ Игра завершилась. Код: {process.ExitCode}");
                            if (process.ExitCode != 0)
                            {
                                LogToConsole($"❌ Возможные проблемы:");
                                LogToConsole($"   - Не установлен Forge");
                                LogToConsole($"   - Несовместимая версия Java");
                                LogToConsole($"   - Отсутствуют библиотеки");
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogToConsole($"🎮 Игра работает (PID: {process.Id})");
                        });

                        try
                        {
                            await process.WaitForExitAsync();
                            Dispatcher.Invoke(() =>
                            {
                                LogToConsole($"🎮 Игра завершена. Код: {process.ExitCode}");
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                LogToConsole($"⚠️ Ошибка ожидания завершения: {ex.Message}");
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LogToConsole($"❌ КРИТИЧЕСКАЯ ОШИБКА CmlLib: {ex.Message}");
                    LogToConsole($"Stack Trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        LogToConsole($"Детали: {ex.InnerException.Message}");
                    }
                });
                throw;
            }
        }

        private int ParseRamToMb(string ramString)
        {
            try
            {
                ramString = ramString.Trim().ToUpper();
                if (ramString.EndsWith("G"))
                {
                    string numStr = ramString.Substring(0, ramString.Length - 1);
                    if (int.TryParse(numStr, out int gb))
                        return gb * 1024;
                }
                else if (ramString.EndsWith("M") || ramString.EndsWith("MB"))
                {
                    string numStr = ramString.Substring(0, ramString.Length - (ramString.EndsWith("MB") ? 2 : 1));
                    if (int.TryParse(numStr, out int mb))
                        return mb;
                }
                else if (int.TryParse(ramString, out int mb2))
                {
                    return mb2;
                }
            }
            catch { }

            // Значения по умолчанию
            return ramString.Contains("Xmx") ? 2048 : 1024;
        }

        private string GetComboBoxValue(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? comboBox.Text;
        }

        private string TryFindJava8()
        {
            string[] commonPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
                @"D:\Program Files\Java"
            };

            foreach (var baseDir in commonPaths)
            {
                if (Directory.Exists(baseDir))
                {
                    var javaDirs = Directory.GetDirectories(baseDir, "*1.8*");
                    foreach (var dir in javaDirs)
                    {
                        string exePath = Path.Combine(dir, "bin", "java.exe");
                        if (File.Exists(exePath)) return exePath;
                    }
                }
            }
            return null;
        }

        private void AutoDetectJava_Click(object sender, RoutedEventArgs e)
        {
            AutoDetectJava();
        }

        private void AutoDetectJava()
        {
            _currentJavaInfo = JavaHelper.FindJava();
            if (_currentJavaInfo != null)
            {
                JavaPathTextBox.Text = _currentJavaInfo.Path;
                LogToConsole($"Java найдена: {_currentJavaInfo.Version}");
                SaveSettings();
            }
            else
            {
                JavaPathTextBox.Text = "";
                LogToConsole("Java не найдена");
            }
        }

        private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Java (java.exe)|java.exe",
                Title = "Выберите java.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                JavaPathTextBox.Text = dialog.FileName;
                SaveSettings();
            }
        }

        private void CheckJavaVersion_Click(object sender, RoutedEventArgs e)
        {
            string javaPath = JavaPathTextBox.Text.Trim();
            if (File.Exists(javaPath))
            {
                string version = JavaHelper.GetJavaVersion(javaPath);
                _currentJavaInfo = new JavaInfo(javaPath, version);
                LogToConsole($"Версия Java: {version}");
            }
            else
            {
                MessageBox.Show("Путь к Java не существует!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreSettings()
        {
            if (_settings == null) return;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    JavaPathTextBox.Text = _settings.JavaPath ?? "";
                    PlayerNameTextBox.Text = _settings.PlayerName ?? "";
                    JavaArgsTextBox.Text = _settings.JavaArgs ?? "";

                    if (string.IsNullOrEmpty(JavaPathTextBox.Text))
                    {
                        AutoDetectJava();
                    }
                });
            }
            catch { }
        }

        private void SaveSettings()
        {
            if (_settings == null) return;

            _settings.JavaPath = JavaPathTextBox.Text;
            _settings.PlayerName = PlayerNameTextBox.Text;
            _settings.JavaArgs = JavaArgsTextBox.Text;
            _settings.Save();
        }

        private void PlayerNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void JavaArgsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettings();
        }

        // Авторизация Ely.by
        private async void ElyAuthButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsAuthInProgress)
            {
                ConsoleOutput.Text += "Авторизация уже выполняется...\n";
                return;
            }

            IsAuthInProgress = true;

            try
            {
                ConsoleOutput.Text += "=== Начало авторизации Ely.by ===\n";

                string authUrl = "https://account.ely.by/oauth2/v1/auth?" +
                        "response_type=code&" +
                        "client_id=" + ClientId + "&" +
                        "redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&" +
                        "scope=account_info+minecraft_server_session";

                ConsoleOutput.Text += "Открываем браузер для авторизации...\n";
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                ConsoleOutput.Text += "Запускаем сервер для приема ответа...\n";
                string authCode = await ListenForCallback();

                if (string.IsNullOrEmpty(authCode))
                {
                    ConsoleOutput.Text += "❌ Не удалось получить код авторизации.\n";
                    return;
                }

                ConsoleOutput.Text += "✅ Код авторизации получен!\n";

                ConsoleOutput.Text += "Получаем токен доступа...\n";
                string accessToken = await GetAccessToken(authCode);

                if (string.IsNullOrEmpty(accessToken))
                {
                    ConsoleOutput.Text += "❌ Не удалось получить токен доступа.\n";
                    return;
                }

                ConsoleOutput.Text += "✅ Токен доступа получен!\n";

                ConsoleOutput.Text += "Получаем информацию об аккаунте...\n";
                _currentProfile = await GetAccountInfo(accessToken);
                _currentAccessToken = accessToken;

                if (_currentProfile == null)
                {
                    ConsoleOutput.Text += "❌ Не удалось получить информацию об аккаунте.\n";
                    return;
                }

                PlayerNameTextBox.Text = _currentProfile.Username;
                UpdateAuthStatus();
                SaveSettings();

                ConsoleOutput.Text += "🎉 Успешная авторизация!\n";
                ConsoleOutput.Text += "   👤 Ник: " + _currentProfile.Username + "\n";
                ConsoleOutput.Text += "   🔑 UUID: " + _currentProfile.Uuid + "\n";
                ConsoleOutput.Text += "   📧 Email: " + _currentProfile.Email + "\n";
                ConsoleOutput.Text += "=== Авторизация завершена ===\n";
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += "💥 Ошибка авторизации: " + ex.Message + "\n";

                if (ex.InnerException != null)
                {
                    ConsoleOutput.Text += "   Внутренняя ошибка: " + ex.InnerException.Message + "\n";
                }
            }
            finally
            {
                IsAuthInProgress = false;
            }
        }

        private async Task<string> ListenForCallback()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(RedirectUri + "/");

            try
            {
                _httpListener.Start();
                ConsoleOutput.Text += "Ожидаем авторизацию... (сервер запущен)\n";

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                var listenTask = _httpListener.GetContextAsync();

                var completedTask = await Task.WhenAny(listenTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    ConsoleOutput.Text += "Таймаут авторизации. Попробуйте снова.\n";
                    return null;
                }

                var context = await listenTask;
                var request = context.Request;
                string authCode = request.QueryString["code"];

                string responseText = @"
<html>
<head><title>Успешная авторизация</title></head>
<body style='font-family: Arial; text-align: center; margin-top: 50px;'>
    <h2>✅ Авторизация успешна!</h2>
    <p>Вы можете закрыть это окно и вернуться в лаунчер.</p>
    <script>setTimeout(() => window.close(), 500);</script>
</body>
</html>";

                await WriteResponse(context.Response, responseText);

                ConsoleOutput.Text += "Получен код авторизации: " + (authCode?.Substring(0, Math.Min(10, authCode.Length))) + "...\n";
                return authCode;
            }
            catch (ObjectDisposedException)
            {
                ConsoleOutput.Text += "Сервер авторизации был закрыт\n";
                return null;
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += "Ошибка сервера авторизации: " + ex.Message + "\n";
                return null;
            }
            finally
            {
                SafeStopHttpListener();
            }
        }

        private async Task WriteResponse(HttpListenerResponse response, string responseText)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentLength64 = buffer.Length;
                response.ContentType = "text/html; charset=utf-8";
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += $"Ошибка отправки ответа: {ex.Message}\n";
            }
        }

        private void UpdateAuthStatus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_currentProfile != null)
                {
                    AuthStatusText.Text = $"Авторизован: {_currentProfile.Username}";
                    AuthStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    PlayerNameTextBox.Text = _currentProfile.Username;
                }
                else
                {
                    AuthStatusText.Text = "Не авторизован";
                    AuthStatusText.Foreground = System.Windows.Media.Brushes.LightCoral;
                }
            }));
        }

        private void SafeStopHttpListener()
        {
            try
            {
                if (_httpListener != null)
                {
                    if (_httpListener.IsListening)
                    {
                        _httpListener.Stop();
                    }
                    _httpListener.Close();
                    _httpListener = null;
                }
            }
            catch (ObjectDisposedException)
            {
                _httpListener = null;
            }
            catch (Exception ex)
            {
                if (!_isClosing)
                    ConsoleOutput.Text += "Ошибка остановки HttpListener: " + ex.Message + "\n";
                _httpListener = null;
            }
        }

        private async Task<string> GetAccessToken(string authCode)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var requestBody = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("client_id", ClientId),
                        new KeyValuePair<string, string>("client_secret", ClientSecret),
                        new KeyValuePair<string, string>("code", authCode),
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                        new KeyValuePair<string, string>("redirect_uri", RedirectUri)
                    });

                    ConsoleOutput.Text += "Отправляем запрос на получение токена...\n";

                    var response = await client.PostAsync("https://account.ely.by/api/oauth2/v1/token", requestBody);
                    var responseText = await response.Content.ReadAsStringAsync();

                    ConsoleOutput.Text += "Ответ сервера: " + response.StatusCode + "\n";

                    if (!response.IsSuccessStatusCode)
                    {
                        ConsoleOutput.Text += "❌ Ошибка получения токена: " + responseText + "\n";
                        return null;
                    }

                    dynamic json = JsonConvert.DeserializeObject(responseText);
                    string token = json.access_token?.ToString();

                    if (string.IsNullOrEmpty(token))
                    {
                        ConsoleOutput.Text += "❌ Токен не найден в ответе\n";
                        return null;
                    }

                    return token;
                }
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += "❌ Ошибка при получении токена: " + ex.Message + "\n";
                return null;
            }
        }

        private async Task<ElyAccountInfo> GetAccountInfo(string accessToken)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    client.Timeout = TimeSpan.FromSeconds(30);

                    ConsoleOutput.Text += "Запрашиваем информацию об аккаунте...\n";
                    var response = await client.GetAsync("https://account.ely.by/api/account/v1/info");
                    var responseText = await response.Content.ReadAsStringAsync();

                    ConsoleOutput.Text += "Ответ от Ely.by: " + response.StatusCode + "\n";

                    if (!response.IsSuccessStatusCode)
                    {
                        ConsoleOutput.Text += $"Ошибка: {responseText}\n";
                        return null;
                    }

                    var accountInfo = JsonConvert.DeserializeObject<ElyAccountInfo>(responseText);
                    ConsoleOutput.Text += $"✅ Получена информация: {accountInfo.Username}\n";
                    return accountInfo;
                }
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += $"❌ Ошибка получения информации: {ex.Message}\n";
                return null;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _isClosing = true;
            _cancellationTokenSource?.Cancel();
            base.OnClosing(e);
        }
    }
}