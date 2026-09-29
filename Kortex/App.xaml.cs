using System;
using System.IO;
using System.Windows;
using Kortex.Services;

namespace Kortex
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception);
                MessageBox.Show("Непредвиденная ошибка:\n\n" + args.Exception.Message,
                    "Kortex", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            if (Environment.GetEnvironmentVariable("KBS_TRACE") == "1")
                EnableBindingTrace();

            AppServices.Init();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppServices.Shutdown();
            base.OnExit(e);
        }

        private static void EnableBindingTrace()
        {
            string path = Path.Combine(Path.GetTempPath(), "keyboardstudio-trace.log");
            var writer = new StreamWriter(path, false) { AutoFlush = true };
            System.Diagnostics.PresentationTraceSources.Refresh();
            var source = System.Diagnostics.PresentationTraceSources.DataBindingSource;
            source.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(writer));
            source.Switch.Level = System.Diagnostics.SourceLevels.Warning;
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(ProfileStore.Folder);
                File.AppendAllText(Path.Combine(ProfileStore.Folder, "crash.log"),
                    DateTime.Now + "\n" + ex + "\n\n");
            }
            catch { }
        }
    }
}
