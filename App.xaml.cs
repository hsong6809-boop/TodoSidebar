using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TodoSidebar.Services;
using TodoSidebar.ViewModels;

namespace TodoSidebar
{
    public partial class App : Application
    {
        /// <summary>
        /// 全局 DI 容器。任何地方可以通过 App.Services.GetService<T>() 获取服务。
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        /// <summary>
        /// 共享的 ViewModel 实例，确保窗口切换时数据同步
        /// </summary>
        public static MainViewModel SharedViewModel { get; set; } = null!;
        
        private static EventHandler<bool>? _loginStateHandler;

        /// <summary>
        /// 全局快捷键服务
        /// </summary>
        private HotkeyService? _hotkeyService;
        
        /// <summary>
        /// 应用日志文件路径（%APPDATA%\TodoSidebar\logs\app.log）
        /// </summary>
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSidebar", "logs", "app.log");

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // === 配置依赖注入（最先执行）===
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            // 将 FeatureFlag 注入 SyncService（SyncService 是单例，不在 DI 中构造）
            var featureFlags = Services.GetRequiredService<IFeatureFlagService>();
            SyncService.Instance.SetFeatureFlags(featureFlags);

            // 注册全局异常处理
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                // 检查启动参数
                bool isSidebarMode = e.Args.Contains("--sidebar");
                
                // 异步初始化认证服务（await 期间 UI 线程不阻塞）
                await InitializeAuthAsync();

                var authService = Services.GetRequiredService<IAuthService>();

                if (!authService.IsLoggedIn)
                {
                    var loginWindow = new LoginWindow();
                    loginWindow.Show();
                    return;
                }

                // 创建共享的 ViewModel
                SharedViewModel = new MainViewModel();

                // 启动通知服务
                NotificationService.Instance.Start();

                Window mainWindow;
                if (isSidebarMode)
                    mainWindow = new MainWindow();
                else
                    mainWindow = new FullWindow();

                mainWindow.Show();

                // 注册全局快捷键
                _hotkeyService = new HotkeyService();
                _hotkeyService.RegisterHotkeys(mainWindow);
                
                _hotkeyService.ToggleSidebarRequested += (s, args) =>
                {
                    try
                    {
                        if (mainWindow is MainWindow sidebar)
                        {
                            var fullWindow = new FullWindow();
                            fullWindow.Show();
                            sidebar.Close();
                            mainWindow = fullWindow;
                            _hotkeyService.ReRegisterHotkeys(fullWindow);
                        }
                        else if (mainWindow is FullWindow full)
                        {
                            var sidebarWindow = new MainWindow();
                            sidebarWindow.Show();
                            full.Close();
                            mainWindow = sidebarWindow;
                            _hotkeyService.ReRegisterHotkeys(sidebarWindow);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("ToggleSidebar error", ex);
                    }
                };
                
                // 新建任务/搜索热键：统一激活窗口
                EventHandler activateHandler = (s, args) =>
                {
                    try { mainWindow?.Activate(); } catch (Exception ex) { LogError("Hotkey activate error", ex); }
                };
                _hotkeyService.NewTaskRequested += activateHandler;
                _hotkeyService.SearchRequested += activateHandler;
            }
            catch (Exception ex)
            {
                LogError("Startup failed", ex);
                MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// 停止所有后台服务并释放共享 ViewModel（登出时调用）。
        /// </summary>
        public static void StopBackgroundServices()
        {
            try
            {
                SyncService.Instance.Stop();
                NotificationService.Instance.Stop();
            }
            catch (Exception ex)
            {
                LogError("StopBackgroundServices error", ex);
            }

            SharedViewModel?.Dispose();
            SharedViewModel = null!;
        }

        /// <summary>
        /// 配置 DI 容器。所有服务注册为 Singleton（保持与原有单例模式兼容）。
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // === 商业化基础设施 ===
            services.AddSingleton<ILicenseService, LicenseService>();
            services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

            // === 核心服务（使用现有单例实例，保持兼容）===
            services.AddSingleton<IAuthService>(AuthService.Instance);
            services.AddSingleton<IDatabaseService>(DatabaseService.Instance);
            services.AddSingleton<ITaskService>(sp =>
                new TaskService(DatabaseService.Instance, MessageService.Instance));
            services.AddSingleton<ISyncService>(SyncService.Instance);
            services.AddSingleton<IExportService>(sp =>
                new ExportService(DatabaseService.Instance));
            services.AddSingleton<IThemeManager>(ThemeManager.Instance);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("UI thread unhandled exception", e.Exception);
            var errorMessage = $"发生未处理的异常:\n\n{e.Exception.Message}";
            if (e.Exception.InnerException != null)
                errorMessage += $"\n\n内部异常:\n{e.Exception.InnerException.Message}";
            System.Diagnostics.Debug.WriteLine(errorMessage);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                LogError("AppDomain unhandled exception", ex);
        }
        
        private async Task InitializeAuthAsync()
        {
            try
            {
                await AuthService.Instance.InitializeAsync();
                _loginStateHandler = async (s, isLoggedIn) =>
                {
                    try
                    {
                        if (isLoggedIn)
                        {
                            // S6 修复：登录/切换账号时校验本地数据归属
                            var uid = AuthService.Instance.CurrentUser?.Id;
                            if (!string.IsNullOrEmpty(uid))
                                DatabaseService.Instance.EnsureUserScope(uid);

                            await SyncService.Instance.InitializeAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("Login state handler error", ex);
                    }
                };
                AuthService.Instance.LoginStateChanged += _loginStateHandler;

                // S4 修复：会话恢复成功时 LoginStateChanged 事件在订阅之前就已发出，
                // 处理器收不到通知导致自动同步永不启动。此处补一次显式检查。
                if (AuthService.Instance.IsLoggedIn)
                {
                    var restoredUserId = AuthService.Instance.CurrentUser?.Id;
                    if (!string.IsNullOrEmpty(restoredUserId))
                        DatabaseService.Instance.EnsureUserScope(restoredUserId);
                    await SyncService.Instance.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                LogError("InitializeAuth error", ex);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _hotkeyService?.Dispose();
                NotificationService.Instance.Stop();
                SyncService.Instance.Stop();
                SharedViewModel?.Dispose();
                if (_loginStateHandler != null)
                    AuthService.Instance.LoginStateChanged -= _loginStateHandler;
                DatabaseService.Instance.Dispose();
                NetworkMonitor.Instance.Dispose();
            }
            catch (Exception ex)
            {
                LogError("OnExit cleanup error", ex);
            }
            base.OnExit(e);
        }

        /// <summary>
        /// 追加写日志到 %APPDATA%\TodoSidebar\logs\app.log（Release 下也可见，避免异常被静默吞掉）。
        /// </summary>
        private static void LogError(string message, Exception? ex = null)
        {
            System.Diagnostics.Debug.WriteLine(message + (ex != null ? ": " + ex.Message : ""));
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(ex != null ? ": " + ex : "")}{Environment.NewLine}");
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }
    }
}
