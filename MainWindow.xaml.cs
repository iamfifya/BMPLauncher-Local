using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        // Менеджеры
        private VersionDownloader _versionDownloader;
        private ModpackDownloader _modpackDownloader;

        // Свойства для привязки
        private bool _modpacksLoaded = false;
        private bool _isLoadingModpacks = false;
        public event PropertyChangedEventHandler PropertyChanged;

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

                // Инициализируем CancellationTokenSource
                _cancellationTokenSource = new CancellationTokenSource();
                _isClosing = false;

                // Инициализируем настройки
                _settings = LauncherSettings.Load();

                if (_settings == null)
                {
                    _settings = new LauncherSettings();
                    BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMPLauncher");
                    _settings.GameDirectory = BaseDirectory;
                    GameDirectory = BaseDirectory;
                    _settings.Save();
                }
                else
                {
                    BaseDirectory = _settings.GameDirectory;
                    GameDirectory = BaseDirectory;
                }

                // Создаем базовую структуру папок
                Directory.CreateDirectory(BaseDirectory);
                Directory.CreateDirectory(Path.Combine(BaseDirectory, "versions"));

                // Инициализируем менеджеры
                _versionDownloader = new VersionDownloader(GameDirectory, LogToConsole);
                _modpackDownloader = new ModpackDownloader(GameDirectory, LogToConsole);

                // Восстанавливаем настройки
                RestoreSettings();
                LogToConsole("Лаунчер инициализирован успешно!");

                // Используем Task.Run для асинхронных операций
                Task.Run(async () => await LoadVersionsAsync());
                Task.Run(async () => await LoadModpacksAutomatically());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                // Резервная инициализация
                _settings = new LauncherSettings();
                BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMPLauncher");
                GameDirectory = BaseDirectory;
                Directory.CreateDirectory(BaseDirectory);
                Directory.CreateDirectory(Path.Combine(BaseDirectory, "versions"));
            }
        }

        private void LogToConsole(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ConsoleOutput.Text += message + "\n";
                ConsoleOutput.ScrollToEnd();
            }));
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                _versionManifest = await _versionDownloader.GetVersionManifestAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    VersionComboBox.ItemsSource = _versionManifest.Versions;
                    if (_versionManifest.Versions.Count > 0)
                        VersionComboBox.SelectedIndex = 0;
                    LogToConsole($"Загружено {_versionManifest.Versions.Count} версий");
                });
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка загрузки версий: {ex.Message}");
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
                    StatusText.Text = "Загрузка модпаков...";
                    LogToConsole("Начинаем загрузку модпаков...");
                });

                await _modpackDownloader.LoadModpacksByAuthor("TheBarMaxx");
                _availableModpacks = _modpackDownloader.GetAvailableModpacks();

                await Dispatcher.InvokeAsync(() =>
                {
                    ModpacksListBox.ItemsSource = _availableModpacks;
                    ModpackCountText.Text = $"({_availableModpacks.Count} модпаков)";

                    if (_availableModpacks.Count == 0)
                    {
                        LogToConsole("⚠️ Не найдено модпаков. Проверьте API ключ и соединение.");
                    }
                    else
                    {
                        LogToConsole($"✅ Загружено {_availableModpacks.Count} модпаков");
                        // Покажем первый модпак для проверки
                        if (_availableModpacks.Count > 0)
                        {
                            LogToConsole($"Первый модпак: {_availableModpacks[0].Name}");
                        }
                    }

                    ModpacksLoaded = true;
                    StatusText.Text = "Готов";
                });
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ Критическая ошибка загрузки модпаков: {ex.Message}");
                LogToConsole($"StackTrace: {ex.StackTrace}");
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

        private void ModpackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedModpack = ModpacksListBox.SelectedItem as CFModpack;
            if (_selectedModpack != null)
            {
                LogToConsole($"Выбран модпак: {_selectedModpack.Name}");
            }
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

                string modpackDir = Path.Combine(GameDirectory, "modpacks", _selectedModpack.Name);
                await _modpackDownloader.DownloadModpackAsync(_selectedModpack.Id, modpackDir,
                    progress => Dispatcher.BeginInvoke(new Action(() => DownloadProgress.Value = progress)),
                    _cancellationTokenSource.Token);

                MessageBox.Show($"Модпак установлен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка установки: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                installButton.IsEnabled = true;
                StatusText.Text = "Готов";
                DownloadProgress.Value = 0;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var version = VersionComboBox.SelectedItem as MCVersion;
            if (version == null)
            {
                MessageBox.Show("Выберите версию!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DownloadButton.IsEnabled = false;
            StatusText.Text = $"Скачивание {version.Id}...";

            try
            {
                await _versionDownloader.DownloadVersionAsync(version.Id,
                    progress => Dispatcher.BeginInvoke(new Action(() => DownloadProgress.Value = progress)),
                    _cancellationTokenSource.Token);

                MessageBox.Show($"Версия скачана!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка скачивания: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadButton.IsEnabled = true;
                StatusText.Text = "Готов";
                DownloadProgress.Value = 0;
            }
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            var version = VersionComboBox.SelectedItem as MCVersion;
            if (version == null)
            {
                MessageBox.Show("Выберите версию!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string playerName = PlayerNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(playerName))
            {
                MessageBox.Show("Введите имя игрока!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string javaPath = JavaPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                MessageBox.Show("Укажите путь к Java!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LaunchButton.IsEnabled = false;
            StatusText.Text = "Запуск...";

            try
            {
                string xms = (XmsComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1G";
                string xmx = (XmxComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "2G";
                string javaArgs = JavaArgsTextBox.Text.Trim();

                string versionDir = Path.Combine(GameDirectory, "versions", version.Id);
                string jarPath = Path.Combine(versionDir, $"{version.Id}.jar");

                if (!File.Exists(jarPath))
                {
                    var result = MessageBox.Show("Версия не скачана. Скачать сейчас?", "Вопрос",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await _versionDownloader.DownloadVersionAsync(version.Id,
                            progress => Dispatcher.BeginInvoke(new Action(() => DownloadProgress.Value = progress)),
                            _cancellationTokenSource.Token);
                    }
                    else
                    {
                        return;
                    }
                }

                LaunchGame(javaPath, version.Id, playerName, xms, xmx, javaArgs);
            }
            catch (Exception ex)
            {
                LogToConsole($"Ошибка запуска: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LaunchButton.IsEnabled = true;
                StatusText.Text = "Готов";
            }
        }

        private void LaunchGame(string javaPath, string versionId, string playerName, string xms, string xmx, string javaArgs)
        {
            try
            {
                string versionDir = Path.Combine(GameDirectory, "versions", versionId);
                string jarPath = Path.Combine(versionDir, $"{versionId}.jar");
                string uuid = Guid.NewGuid().ToString();

                string arguments = $"-Xms{xms} -Xmx{xmx} {javaArgs} -jar \"{jarPath}\" " +
                    $"--username {playerName} --uuid {uuid} --accessToken 0 " +
                    $"--version {versionId} --gameDir \"{versionDir}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = arguments,
                    WorkingDirectory = versionDir,
                    UseShellExecute = false
                });

                LogToConsole("Minecraft запущен!");
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось запустить игру: {ex.Message}");
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
            if (_settings == null) return;

            JavaPathTextBox.Text = _settings.JavaPath ?? "";
            PlayerNameTextBox.Text = _settings.PlayerName ?? "";
            JavaArgsTextBox.Text = _settings.JavaArgs ?? "";
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

                CreateElyByLaunchProfile(VersionComboBox.SelectedItem?.ToString() ?? "1.21.11-pre3");
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

    }
}