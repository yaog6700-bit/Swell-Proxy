using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.AppNotifications;
using AnywhereWinUI.Services;
using AnywhereWinUI.Plugins;

namespace AnywhereWinUI
{
    public partial class App : Application
    {
        private Window? _window;
        public IntPtr MainWindowHandle => _window != null
            ? WinRT.Interop.WindowNative.GetWindowHandle(_window)
            : IntPtr.Zero;



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
                if (arg == "--tun" || arg == "--tun-start" || arg == "--tun-system" || arg == "--tun-system-start")
                {
                    isTunRestart = true;
                    isAutoStart = (arg == "--tun-start" || arg == "--tun-system-start");
                    int proxyModeIndex = arg.StartsWith("--tun-system", StringComparison.Ordinal) ? 3 : 1;
                    AppSession.Instance.ProxyModeIndex = proxyModeIndex;
                    Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", proxyModeIndex);
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
                // ── One-time migration: rename AnywhereProxy folder → SwellProxy ────────
                MigrateDataFolder();

                // ── Cleanup leftover staged update packages on startup ────────────────────
                new Services.AppUpdateService().CleanupOldStagingDirs();

                AnywhereWinUI.Services.ClipboardMonitorService.Instance.Start();

                // ── Register notification service (required for Toast to work) ──────
                try { AppNotificationManager.Default.Register(); } catch { }

                _window = new MainWindow();
                _window.Activate();

                // ── Plugin system: load manifests and initialise enabled plugins ──────
                if (AppSession.Instance.EnablePlugins)
                {
                    _ = Task.Run(async () =>
                    {
                        await PluginManager.Instance.LoadAllAsync();
                        await PluginManager.Instance.FireAsync(PluginTrigger.OnStartup);
                    });
                }

                if (isTunRestart && isAutoStart)
                {
                    // Auto-start proxy if it was running before UAC restart
                    _ = Task.Run(async () =>
                    {
                        var cfg = await ConfigBuilder.BuildAsync();
                        await CoreManager.Instance.StartAsync(cfg);
                    });
                }

                StartSubscriptionAutoRefreshMonitor();
            }
            catch (Exception ex)
            {
                LogCrash(ex, "OnLaunched Execution");
                throw;
            }
        }

        private void StartSubscriptionAutoRefreshMonitor()
        {
            _ = Task.Run(async () =>
            {
                await RefreshDueSubscriptionsAndReloadAsync();

                using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromHours(1));
                while (await timer.WaitForNextTickAsync())
                    await RefreshDueSubscriptionsAndReloadAsync();
            });
        }

        private async Task RefreshDueSubscriptionsAndReloadAsync()
        {
            try
            {
                int refreshed = await NodesManager.Instance.RefreshDueSubscriptionsAsync();
                if (refreshed > 0)
                {
                    MainWindow.Instance?.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (Services.GetService(typeof(AnywhereWinUI.ViewModels.ServersViewModel)) is AnywhereWinUI.ViewModels.ServersViewModel vm)
                        {
                            vm.LoadSubscriptions();
                            vm.LoadServersList();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SubscriptionAutoRefresh] failed: {ex.Message}");
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

        /// <summary>
        /// One-time migration: if the old "AnywhereProxy" data folder exists and the new
        /// "SwellProxy" folder does not, rename it so existing users keep their settings.
        /// </summary>
        private static void MigrateDataFolder()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var oldDir = Path.Combine(localAppData, "AnywhereProxy");
                var newDir = Path.Combine(localAppData, "SwellProxy");

                if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                {
                    Directory.Move(oldDir, newDir);
                }
            }
            catch { }
        }

        private static void LogCrash(Exception? ex, string message)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "crash_log.txt");
                string desktopLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SwellProxy_crash.txt");
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
