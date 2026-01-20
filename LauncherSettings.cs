using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace BMPLauncher
{
    public class LauncherSettings
    {
        public string GameDirectory { get; set; } // Основная директория лаунчера
        public string JavaPath { get; set; }
        public string PlayerName { get; set; }
        public string LastVersion { get; set; }
        public string Xms { get; set; }
        public string Xmx { get; set; }
        public string JavaArgs { get; set; }

        // Добавляем информацию о скачанных версиях
        public List<string> DownloadedVersions { get; set; } = new List<string>();

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BMPLauncher",
            "launcher_settings.json");

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public static LauncherSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<LauncherSettings>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            }

            // Возвращаем настройки по умолчанию
            return new LauncherSettings
            {
                GameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMPLauncher"),
                Xms = "1G",
                Xmx = "2G",
            };
        }
    }
}