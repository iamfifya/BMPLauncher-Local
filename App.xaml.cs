using System;
using System.Windows;

namespace BMPLauncher.Core
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Устанавливаем обработчики исключений
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                // Логируем и продолжаем
            };

            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true; // Предотвращаем крах приложения
            };

            base.OnStartup(e);
        }
    }
}