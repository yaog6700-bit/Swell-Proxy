using System;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using AnywhereWinUI.Services;

namespace AnywhereWinUI
{
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Gets the current <see cref="App"/> instance in use
        /// </summary>
        public static new App Current => (App)Application.Current;

        /// <summary>
        /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
        /// </summary>
        public IServiceProvider Services { get; }

        public App()
        {
            Services = ConfigureServices();
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogCrash(e.ExceptionObject as Exception, $"AppDomain Unhandled: {e.ExceptionObject}");
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogCrash(e.Exception, "TaskScheduler Unobserved Task Exception");
                e.SetObserved(); // Mark as observed to prevent crash on background task GC
            };
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Register ViewModels (Singleton to persist state across navigations)
            services.AddSingleton<AnywhereWinUI.ViewModels.ServersViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.SettingsViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.DashboardViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.RoutingViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.TrafficViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.ConnectionsViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.LogsViewModel>();
            services.AddSingleton<AnywhereWinUI.ViewModels.MainViewModel>();

            return services.BuildServiceProvider();
        }

        private static System.Threading.Mutex? _singleInstanceMutex = null;

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            bool isTunRestart = false;
            bool isAutoStart = false;
            int parentPid = 0;

            foreach (var arg in cmdArgs)
            {
                if (arg == "--tun" || arg == "--tun-start")
                {
                    isTunRestart = true;
                    isAutoStart = (arg == "--tun-start");
                    AppSession.Instance.ProxyModeIndex = 1;
                    Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", 1);
                }
                else if (arg.StartsWith("--parent-pid="))
                {
                    if (int.TryParse(arg.Substring("--parent-pid=".Length), out var pid))
                    {
                        parentPid = pid;
                    }
                }
            }

            if (parentPid > 0)
            {
                try
                {
                    var parentProcess = System.Diagnostics.Process.GetProcessById(parentPid);
                    parentProcess.WaitForExit(3000);
                }
                catch { /* Ignore, process might already be dead */ }
            }

            // Ensure only one instance of the application is running
            const string appMutexName = "AnywhereWinUI_Global_SingleInstance";
            _singleInstanceMutex = new System.Threading.Mutex(true, appMutexName, out bool createdNew);
            
            if (!createdNew)
            {
                // Another instance is already running. Exit gracefully.
                Environment.Exit(0);
                return;
            }

            try
            {
                _window = new MainWindow();
                _window.Activate();
                
                if (isTunRestart && isAutoStart)
                {
                    // Auto-start proxy if it was running before UAC restart
                    _ = Task.Run(async () =>
                    {
                        var cfg = await ConfigBuilder.BuildAsync();
                        await CoreManager.Instance.StartAsync(cfg);
                    });
                }
            }
            catch (Exception ex)
            {
                LogCrash(ex, "OnLaunched Execution");
                throw;
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception, e.Message);
            // Mark as handled to prevent the runtime from terminating the process.
            // Non-fatal UI exceptions (e.g., stale WinRT object references after page unload)
            // should not bring down the whole app.
            e.Handled = true;
        }

        private static void LogCrash(Exception? ex, string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "crash_log.txt");
                string desktopLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AnywhereProxy_crash.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"==================================================");
                sb.AppendLine($"[Crash Timestamp] {DateTime.Now}");
                sb.AppendLine($"[Error Message] {message}");
                if (ex != null)
                {
                    sb.AppendLine($"[Exception Type] {ex.GetType().FullName}");
                    sb.AppendLine($"[Stack Trace]");
                    sb.AppendLine(ex.ToString());
                    
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"[Inner Exception Type] {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"[Inner Exception Message] {ex.InnerException.Message}");
                        sb.AppendLine($"[Inner Exception Stack Trace]");
                        sb.AppendLine(ex.InnerException.ToString());
                    }
                }
                sb.AppendLine($"==================================================");
                string content = sb.ToString();
                File.AppendAllText(logPath, content);
                File.AppendAllText(desktopLog, content); // 同时写桌面，方便查找
            }
            catch { }
        }
    }
}
