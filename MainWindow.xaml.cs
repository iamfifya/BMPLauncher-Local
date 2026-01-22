using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private string BaseDirectory; // Добавлено
        private string GameDirectory;
        private VersionManifest _versionManifest;
        private JavaInfo _currentJavaInfo;
        private string _currentAccessToken;
        private HttpListener _httpListener;
        private bool _isAuthInProgress = false; // Добавлено инициализация
        private ElyAccountInfo _currentProfile;
        private LauncherSettings _settings;
        private List<CFModpack> _availableModpacks = new List<CFModpack>();
        private CFModpack _selectedModpack;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isClosing = false;
        private readonly Action<string> _logAction;

        private ForgeInstaller _forgeInstaller;
        private GameLauncher _gameLauncher;

        private MinecraftLauncher _cmlLauncher;
        private MinecraftPath _minecraftPath;

        // Менеджеры
        private VersionDownloader _versionDownloader;
        private ModpackDownloader _modpackDownloader;

        // Свойства для привязки
        private bool _modpacksLoaded = false;
        private bool _isLoadingModpacks = false;
        public event PropertyChangedEventHandler PropertyChanged;

        private string _sanitizedModpackName;

        private void ModpackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedModpack = ModpacksListBox.SelectedItem as CFModpack;
            if (_selectedModpack != null)
            {
                _sanitizedModpackName = SanitizeFileName(_selectedModpack.Name);
                LogToConsole($"Выбран модпак: {_selectedModpack.Name} (папка: {_sanitizedModpackName})");
            }
        }

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

        // Свойство для авторизации (добавлено)
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

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                _minecraftPath = new MinecraftPath(GameDirectory);
                _cmlLauncher = new MinecraftLauncher(_minecraftPath);

                // Инициализируем лог сразу
                LogToConsole("Инициализация лаунчера...");

                _gameLauncher = new GameLauncher(GameDirectory, LogToConsole);

                // Инициализируем CancellationTokenSource
                _cancellationTokenSource = new CancellationTokenSource();
                _isClosing = false;

                // ВАЖНО: Сначала устанавливаем путь по умолчанию
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

                // Создаем базовую структуру папок (если не существует)
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
                    // Продолжаем работу
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
                    throw; // Прерываем инициализацию
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

                _gameLauncher = new GameLauncher(GameDirectory, LogToConsole);

                LogToConsole("Лаунчер инициализирован успешно!");

                // Асинхронная загрузка модпаков
                Task.Run(async () =>
                {
                    await Task.Delay(1000); // Небольшая задержка для отображения UI
                    await LoadModpacksAutomatically();
                });
            }
            catch (Exception ex)
            {
                string errorMsg = $"Ошибка инициализации: {ex.Message}\nStackTrace: {ex.StackTrace}";
                LogToConsole(errorMsg);

                MessageBox.Show($"Ошибка инициализации: {ex.Message}\n\nПроверьте права доступа к папке AppData.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

                // Пробуем использовать временную папку
                try
                {
                    BaseDirectory = Path.GetTempPath() + "BMPLauncher";
                    GameDirectory = BaseDirectory;

                    Directory.CreateDirectory(BaseDirectory);
                    Directory.CreateDirectory(Path.Combine(BaseDirectory, "versions"));

                    _versionDownloader = new VersionDownloader(GameDirectory, LogToConsole);
                    _modpackDownloader = new ModpackDownloader(GameDirectory, LogToConsole);

                    LogToConsole($"Используем временную папку: {BaseDirectory}");
                }
                catch (Exception ex2)
                {
                    LogToConsole($"Критическая ошибка: {ex2.Message}");
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }

        private void LogToConsole(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogToConsole(message));
                return;
            }

            ConsoleOutput.Text += message + "\n";
            ConsoleOutput.ScrollToEnd();
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

        // Метод для обновления статуса авторизации (добавлено)
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

                // ЗДЕСЬ ПРОСТО ЗАГРУЖАЕМ МОДПАКИ THEBARMAXX - БЕЗ ФИЛЬТРАЦИИ
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
            // Task.Run(() => SearchModpacks(SearchModpackTextBox.Text));
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

                // ДИАГНОСТИКА: показываем информацию о модпаке
                LogToConsole($"=== ИНФОРМАЦИЯ О МОДПАКЕ ===");
                LogToConsole($"ID: {_selectedModpack.Id}");
                LogToConsole($"Имя: {_selectedModpack.Name}");
                LogToConsole($"FileId: {_selectedModpack.GameVersionLatestFiles?.FirstOrDefault()?.ProjectFileId ?? 0}");
                LogToConsole($"Имя файла: {_selectedModpack.GameVersionLatestFiles?.FirstOrDefault()?.ProjectFileName}");
                LogToConsole($"============================");

                // УДАЛЯЕМ КВАДРАТНЫЕ СКОБКИ И ДРУГИЕ НЕДОПУСТИМЫЕ СИМВОЛЫ ИЗ ИМЕНИ ПАПКИ
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

        // Метод для очистки имени файла/папки от недопустимых символов
        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Modpack";

            // Удаляем квадратные скобки и другие недопустимые символы
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + "[]";
            string sanitized = fileName;

            foreach (char c in invalidChars)
            {
                sanitized = sanitized.Replace(c.ToString(), "");
            }

            // Убираем лишние пробелы
            sanitized = sanitized.Trim();

            // Если после очистки строка пустая, задаем имя по умолчанию
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "Modpack_" + Guid.NewGuid().ToString().Substring(0, 8);

            return sanitized;
        }









        private async Task DownloadMinecraftVersionAsync(string version)
        {
            var tcs = new TaskCompletionSource<bool>();

            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var downloader = new VersionDownloader(GameDirectory, (msg) =>
                    {
                        Dispatcher.Invoke(() => LogToConsole(msg));
                    });

                    await downloader.DownloadVersionAsync(version,
                        progress => LogToConsole($"Прогресс скачивания: {progress}%"),
                        new CancellationToken());

                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            await tcs.Task;
        }









        private string TryFindJava8()
        {
            // Список мест, где обычно живет Java 8
            string[] commonPaths = {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
        @"D:\Program Files\Java" // Твой случай из прошлых логов
    };

            foreach (var baseDir in commonPaths)
            {
                if (Directory.Exists(baseDir))
                {
                    // Ищем папки jre1.8 или jdk1.8
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

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Блокируем кнопку на время запуска
                LaunchButton.IsEnabled = false;
                StatusText.Text = "Запуск игры...";

                LogToConsole("=== ЗАПУСК ИГРЫ ===");

                // 1. Проверяем выбор модпака
                if (_selectedModpack == null)
                {
                    MessageBox.Show("Выберите модпак из списка!");
                    return;
                }

                // 2. Получаем путь к модпаку
                string sanitizedModpackName = SanitizeFileName(_selectedModpack.Name);
                string modpackDir = Path.Combine(GameDirectory, "modpacks", sanitizedModpackName);

                if (!Directory.Exists(modpackDir))
                {
                    MessageBox.Show($"Модпак '{_selectedModpack.Name}' не установлен!\n\n" +
                                  "Сначала нажмите кнопку 'Установить'.",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 3. Читаем manifest.json
                string manifestPath = Path.Combine(modpackDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    MessageBox.Show("Файл manifest.json не найден!\n" +
                                  "Модпак установлен неправильно.",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var manifest = JsonConvert.DeserializeObject<CFManifest>(File.ReadAllText(manifestPath));

                LogToConsole($"📋 Модпак: {_selectedModpack.Name}");
                LogToConsole($"🎮 Minecraft: {manifest.Minecraft.Version}");
                LogToConsole($"🔨 Forge: {manifest.Minecraft.ModLoaders?.FirstOrDefault()?.Id ?? "Нет"}");

                // 4. Получаем Java путь
                string javaPath = await GetJavaPathAsync();
                if (string.IsNullOrEmpty(javaPath))
                {
                    MessageBox.Show("Java не найдена!\n\n" +
                                  "1. Установите Java 8 или новее\n" +
                                  "2. Укажите путь в настройках\n" +
                                  "3. Или установите переменную окружения JAVA_HOME",
                                  "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 5. Получаем имя игрока
                string playerName = string.IsNullOrWhiteSpace(PlayerNameTextBox.Text)
                    ? "Player_" + Guid.NewGuid().ToString().Substring(0, 5)
                    : PlayerNameTextBox.Text;

                // 6. Получаем настройки RAM
                int minRam = ParseRamToMb(GetSelectedComboBoxValue(XmsComboBox) ?? "1G");
                int maxRam = ParseRamToMb(GetSelectedComboBoxValue(XmxComboBox) ?? "2G");

                // 7. Получаем аргументы Java
                string javaArgs = JavaArgsTextBox.Text?.Trim() ?? "";

                // 8. Запускаем через GameLauncher
                var process = await _gameLauncher.LaunchModpackAsync(
                    modpackDir: modpackDir,
                    minecraftVersion: manifest.Minecraft.Version,
                    forgeVersion: manifest.Minecraft.ModLoaders?.FirstOrDefault()?.Id,
                    javaPath: javaPath,
                    playerName: playerName,
                    minRamMb: minRam,
                    maxRamMb: maxRam,
                    javaArgs: javaArgs
                );

                // 9. Запускаем процесс
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                LogToConsole($"✅ Игра запущена! PID: {process.Id}");

                // Сообщение об успехе
                MessageBox.Show($"Игра успешно запущена!\n\n" +
                               $"PID: {process.Id}\n" +
                               $"Игрок: {playerName}\n" +
                               $"RAM: {minRam / 1024}G - {maxRam / 1024}G\n\n" +
                               "Консоль игры будет отображаться ниже.",
                               "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                // 10. Следим за завершением игры в фоне
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000); // Ждем 5 секунд для инициализации

                    if (process.HasExited)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogToConsole($"⚠️ Игра завершилась. Код: {process.ExitCode}");
                            StatusText.Text = "Готов";
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogToConsole($"🎮 Игра работает (PID: {process.Id})");
                            StatusText.Text = "Игра запущена";
                        });

                        await process.WaitForExitAsync();
                        Dispatcher.Invoke(() =>
                        {
                            LogToConsole($"🎮 Игра завершена. Код: {process.ExitCode}");
                            StatusText.Text = "Готов";
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Ошибка: {ex.Message}");
                if (ex.InnerException != null)
                    LogToConsole($"Подробности: {ex.InnerException.Message}");

                MessageBox.Show($"Ошибка запуска: {ex.Message}",
                              "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LaunchButton.IsEnabled = true;
                StatusText.Text = "Готов";
            }
        }
        private async Task<string> GetJavaPathAsync()
        {
            // 1. Проверяем сохраненный путь
            if (!string.IsNullOrEmpty(_settings?.JavaPath) && File.Exists(_settings.JavaPath))
            {
                return _settings.JavaPath;
            }

            // 2. Автопоиск через JavaHelper
            var javaInfo = JavaHelper.FindJava();
            if (javaInfo != null)
            {
                _settings.JavaPath = javaInfo.Path;
                _settings.Save();
                return javaInfo.Path;
            }

            // 3. Пробуем стандартные пути
            string[] commonPaths =
            {
                @"D:\Program Files\Java\jre1.8.0_431\bin\java.exe",
                @"C:\Program Files\Java\jre1.8.0_431\bin\java.exe",
                @"C:\Program Files (x86)\Java\jre1.8.0_431\bin\java.exe",
                @"C:\Program Files\Java\jdk1.8.0_431\bin\java.exe",
                @"java.exe" // Пробуем из PATH
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    _settings.JavaPath = path;
                    _settings.Save();
                    return path;
                }
            }

            return null;
        }

        private int ParseRamToMb(string ramString)
        {
            if (string.IsNullOrWhiteSpace(ramString)) return 1024;

            ramString = ramString.ToUpper().Trim();

            // Убираем пробелы
            ramString = ramString.Replace(" ", "");

            if (ramString.EndsWith("G"))
            {
                if (int.TryParse(ramString.TrimEnd('G'), out int gb))
                    return gb * 1024;
            }
            else if (ramString.EndsWith("M"))
            {
                if (int.TryParse(ramString.TrimEnd('M'), out int mb))
                    return mb;
            }
            else if (int.TryParse(ramString, out int value))
            {
                return value;
            }

            return 1024; // 1GB по умолчанию
        }

        private string GetSelectedComboBoxValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString();

            return comboBox.Text;
        }


        // Вспомогательный метод для логов
        private void AttachProcessLogger(Process process)
        {
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    LogToConsole($"[Game]: {e.Data}");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    LogToConsole($"[ERR]: {e.Data}");
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private async Task DownloadFileAsync(string url, string path)
        {
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(new Uri(url), path);
            }
        }
        private void AutoDetectJava()
        {
            _currentJavaInfo = JavaHelper.FindJava();
            if (_currentJavaInfo != null)
            {
                JavaPathTextBox.Text = _currentJavaInfo.Path;
                LogToConsole($"Java найдена: {_currentJavaInfo.Version}");
            }
            else
            {
                JavaPathTextBox.Text = "";
                LogToConsole("Java не найдена");
            }
        }

        private void AutoDetectJava_Click(object sender, RoutedEventArgs e) => AutoDetectJava();

        private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
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
            if (_settings == null)
            {
                LogToConsole("Настройки не загружены, пропускаем восстановление");
                return;
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    JavaPathTextBox.Text = _settings.JavaPath ?? "";
                    PlayerNameTextBox.Text = _settings.PlayerName ?? "";
                    JavaArgsTextBox.Text = _settings.JavaArgs ?? "";

                    // Автоопределение Java если путь пустой
                    if (string.IsNullOrEmpty(JavaPathTextBox.Text))
                    {
                        AutoDetectJava();
                    }
                });

                LogToConsole("Настройки восстановлены");
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка восстановления настроек: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            if (_settings == null) return;

            _settings.JavaPath = JavaPathTextBox.Text;
            _settings.PlayerName = PlayerNameTextBox.Text;
            _settings.JavaArgs = JavaArgsTextBox.Text;
            _settings.Save();
        }

        private void PlayerNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => SaveSettings();
        private void JavaArgsTextBox_TextChanged(object sender, TextChangedEventArgs e) => SaveSettings();

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

                // 1. Открываем браузер для авторизации
                string authUrl = "https://account.ely.by/oauth2/v1/auth?" +
                        "response_type=code&" +
                        "client_id=" + ClientId + "&" +
                        "redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&" +
                        "scope=account_info+minecraft_server_session";

                ConsoleOutput.Text += "Открываем браузер для авторизации...\n";
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                // 2. Запускаем локальный сервер для перехвата callback-а
                ConsoleOutput.Text += "Запускаем сервер для приема ответа...\n";
                string authCode = await ListenForCallback();

                if (string.IsNullOrEmpty(authCode))
                {
                    ConsoleOutput.Text += "❌ Не удалось получить код авторизации.\n";
                    return;
                }

                ConsoleOutput.Text += "✅ Код авторизации получен!\n";

                // 3. Получаем access_token
                ConsoleOutput.Text += "Получаем токен доступа...\n";
                string accessToken = await GetAccessToken(authCode);

                if (string.IsNullOrEmpty(accessToken))
                {
                    ConsoleOutput.Text += "❌ Не удалось получить токен доступа.\n";
                    return;
                }

                ConsoleOutput.Text += "✅ Токен доступа получен!\n";

                // 4. Получаем информацию об аккаунте
                ConsoleOutput.Text += "Получаем информацию об аккаунте...\n";
                _currentProfile = await GetAccountInfo(accessToken);
                _currentAccessToken = accessToken;

                if (_currentProfile == null)
                {
                    ConsoleOutput.Text += "❌ Не удалось получить информацию об аккаунте.\n";
                    return;
                }

                // 5. Обновляем UI
                PlayerNameTextBox.Text = _currentProfile.Username;
                UpdateAuthStatus();
                SaveSettings();

                ConsoleOutput.Text += "🎉 Успешная авторизация!\n";
                ConsoleOutput.Text += "   👤 Ник: " + _currentProfile.Username + "\n";
                ConsoleOutput.Text += "   🔑 UUID: " + _currentProfile.Uuid + "\n";
                ConsoleOutput.Text += "   📧 Email: " + _currentProfile.Email + "\n";
                ConsoleOutput.Text += "=== Авторизация завершена ===\n";

                // CreateElyByLaunchProfile(VersionComboBox.SelectedItem?.ToString() ?? "1.21.11-pre3");
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

                // Таймаут 2 минуты
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                var listenTask = _httpListener.GetContextAsync();

                // Ждем либо ответ, либо таймаут
                var completedTask = await Task.WhenAny(listenTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    ConsoleOutput.Text += "Таймаут авторизации. Попробуйте снова.\n";
                    return null;
                }

                var context = await listenTask;
                var request = context.Request;
                string authCode = request.QueryString["code"];

                // Отправляем успешный ответ пользователю
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

                ConsoleOutput.Text += "Получен код авторизации: " + (authCode?.Substring(0, 10)) + "...\n";
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

        private void CreateElyByLaunchProfile(string versionId)
        {
            try
            {
                string launcherProfilesPath = Path.Combine(GameDirectory, "launcher_profiles.json");

                // Создаем базовую структуру если файла нет
                if (!File.Exists(launcherProfilesPath))
                {
                    var initialProfiles = new
                    {
                        profiles = new JObject(),
                        settings = new JObject(),
                        version = 1
                    };
                    File.WriteAllText(launcherProfilesPath, JsonConvert.SerializeObject(initialProfiles, Formatting.Indented));
                }

                // Читаем существующие профили
                string json = File.ReadAllText(launcherProfilesPath);
                JObject profilesData = JObject.Parse(json);

                // Создаем профиль для Ely.by
                var elyProfile = new JObject
                {
                    ["name"] = "Ely.by - " + _currentProfile.Username,
                    ["type"] = "custom",
                    ["created"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["lastUsed"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["icon"] = "Crafting_Table",
                    ["lastVersionId"] = versionId,
                    ["gameDir"] = GameDirectory,
                    ["javaArgs"] = "-javaagent:\"" + Path.Combine(GameDirectory, "authlib-injector.jar") + "\"=https://authserver.ely.by/api/ " +
                                  "-Dauthlibinjector.yggdrasil.prefetched={\"name\":\"Ely.by\",\"apiUrl\":\"https://authserver.ely.by/api/\"}",
                    ["resolution"] = new JObject
                    {
                        ["width"] = 1024,
                        ["height"] = 768
                    }
                };

                string profileKey = "elyby_" + _currentProfile.Uuid.Replace("-", "");
                profilesData["profiles"][profileKey] = elyProfile;

                File.WriteAllText(launcherProfilesPath, profilesData.ToString(Formatting.Indented));
                ConsoleOutput.Text += "Создан профиль запуска для Ely.by\n";
            }
            catch (Exception ex)
            {
                ConsoleOutput.Text += "Ошибка создания профиля Ely.by: " + ex.Message + "\n";
            }
        }

        private string GenerateClasspath(string librariesDir, string minecraftJarPath)
        {
            if (!Directory.Exists(librariesDir)) return minecraftJarPath;

            // Собираем все .jar файлы из папки libraries и подпапок
            var files = Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories);

            // Объединяем их через ; (разделитель для Windows)
            string classpath = string.Join(";", files);

            return $"{classpath};{minecraftJarPath}";
        }
    }
}