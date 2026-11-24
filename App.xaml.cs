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
            MigrateDataFolder();
            Helpers.LanguageHelper.InitializeFromSettings();

            Services = ConfigureServices();
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.IsTerminating)
                {
                    try { SystemProxyManager.DisableProxyIfOwned(); } catch { }
                }
                LogCrash(e.ExceptionObject as Exception, $"AppDomain Unhandled: {e.ExceptionObject}", isFatal: e.IsTerminating);
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogCrash(e.Exception, "TaskScheduler Unobserved Task Exception", isFatal: false);
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
            bool isAutoStart = false;
            bool isSilentBoot = false;
            int parentPid = 0;

            foreach (var arg in cmdArgs)
            {
                if (arg == "--tun" || arg == "--tun-start" || arg == "--tun-system" || arg == "--tun-system-start")
                {
                    isAutoStart |= (arg == "--tun-start" || arg == "--tun-system-start");
                    int proxyModeIndex = arg.StartsWith("--tun-system", StringComparison.Ordinal) ? 3 : 1;
                    AppSession.Instance.ProxyModeIndex = proxyModeIndex;
                    Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", proxyModeIndex);
                }
                else if (arg == "--auto-start-proxy")
                {
                    isAutoStart = true;
                }
                else if (arg == "--autostart")
                {
                    isSilentBoot = true;
                    isAutoStart = true;
                }
                else if (arg == "-silent")
                {
                    isSilentBoot = true;
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
                // Another instance is already running — ask it to restore from tray and exit gracefully.
                Helpers.SystemTrayManager.NotifyExistingInstanceToShow();
                Environment.Exit(0);
                return;
            }

            try
            {
                // ── One-time migration: rename AnywhereProxy folder → SwellProxy ────────
                MigrateDataFolder();

                // ── Cleanup leftover staged update packages & stale crash system-proxy state on cold startup ──
                if (parentPid == 0)
                {
                    new Services.AppUpdateService().CleanupOldStagingDirs();
                    SystemProxyManager.RecoverCrashLeftoversOnStartup();
                }

                AnywhereWinUI.Services.ClipboardMonitorService.Instance.Start();

                // ── Register notification service (required for Toast to work) ──────
                try { AppNotificationManager.Default.Register(); } catch { }

                var mainWindow = new MainWindow();
                _window = mainWindow;
                if (isSilentBoot)
                {
                    mainWindow.StartSilentlyInTray();
                }
                else
                {
                    _window.Activate();
                }

                if (Helpers.AdminHelper.IsAdministrator())
                {
                    AutostartManager.UpgradeToElevatedTaskIfNeeded();
                }

                // ── Ordered startup: load plugins BEFORE auto-starting core so OnBeforeCoreStart hooks run ──
                _ = Task.Run(async () =>
                {
                    if (AppSession.Instance.EnablePlugins)
                    {
                        try
                        {
                            await PluginManager.Instance.LoadAllAsync();
                            await PluginManager.Instance.FireAsync(PluginTrigger.OnStartup);
                        }
                        catch (Exception pEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PluginStartup] {pEx.Message}");
                        }
                    }

                    if (isAutoStart)
                    {
                        try
                        {
                            var cfg = await ConfigBuilder.BuildAsync();
                            await CoreManager.Instance.StartAsync(cfg);
                        }
                        catch (Exception startEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoStartProxy] {startEx.Message}");
                        }
                    }
                });

                StartSubscriptionAutoRefreshMonitor();
            }
            catch (Exception ex)
            {
                LogCrash(ex, "OnLaunched Execution", isFatal: true);
                throw;
            }
        }

        public static async Task RestartAsync()
        {
            bool wasRunning = CoreManager.Instance.IsRunning;
            if (wasRunning)
            {
                await CoreManager.Instance.StopAsync();
            }

            try
            {
                await PluginManager.Instance.FireAsync(PluginTrigger.OnShutdown);
            }
            catch { }

            MainWindow.Instance?.DisposeTrayForRestart();
            ReleaseSingleInstanceMutex();

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string restartArgs = wasRunning
                    ? $"--parent-pid={Environment.ProcessId} --auto-start-proxy"
                    : $"--parent-pid={Environment.ProcessId}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = restartArgs,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false
                });
            }

            Environment.Exit(0);
        }

        internal static void ReleaseSingleInstanceMutex()
        {
            var m = System.Threading.Interlocked.Exchange(ref _singleInstanceMutex, null);
            if (m == null) return;
            try { m.ReleaseMutex(); } catch { }
            try { m.Dispose(); } catch { }
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
            LogCrash(e.Exception, e.Message, isFatal: false);
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

        private static void LogCrash(Exception? ex, string message, bool isFatal = true)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine($"[Crash Timestamp] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (Fatal={isFatal})");
                sb.AppendLine($"[Error Message] {message}");
                if (ex != null)
                {
                    sb.AppendLine($"[Exception Type] {ex.GetType().FullName}");
                    sb.AppendLine("[Stack Trace]");
                    sb.AppendLine(ex.ToString());

                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"[Inner Exception Type] {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"[Inner Exception Message] {ex.InnerException.Message}");
                        sb.AppendLine("[Inner Exception Stack Trace]");
                        sb.AppendLine(ex.InnerException.ToString());
                    }
                }
                sb.AppendLine("==================================================");
                string content = sb.ToString();

                try
                {
                    string appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SwellProxy");
                    Directory.CreateDirectory(appDataDir);
                    File.AppendAllText(Path.Combine(appDataDir, "crash_log.txt"), content);
                }
                catch { }

                if (isFatal)
                {
                    try
                    {
                        string desktopLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SwellProxy_crash.txt");
                        File.AppendAllText(desktopLog, content);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
