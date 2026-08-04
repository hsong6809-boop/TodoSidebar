using System;
using System.Linq;
using System.Threading;
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

        // 修复 F1：单实例锁 —— 防止多个实例同时写 SQLite 导致锁库损坏
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = "TodoSidebar_SingleInstance_Mutex";
        
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 单实例检查（最先执行，重复实例直接退出）
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show("TodoSidebar 已经在运行中。\n\n请查看系统托盘或任务栏。", 
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

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

            // 修复 F2：每日首次启动自动备份（保留最近10份）
            try
            {
                var exportService = new ExportService(DatabaseService.Instance);
                var todayKey = "LastAutoBackupDate";
                var lastBackupDate = DatabaseService.Instance.GetSetting(todayKey);
                if (lastBackupDate != DateTime.Today.ToString("yyyy-MM-dd"))
                {
                    exportService.CreateBackup();
                    DatabaseService.Instance.SetSetting(todayKey, DateTime.Today.ToString("yyyy-MM-dd"));
                    System.Diagnostics.Debug.WriteLine("[App] 每日自动备份完成");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"自动备份失败: {ex.Message}");
            }

            try
            {
                // 检查启动参数
                bool isSidebarMode = e.Args.Contains("--sidebar");
                
                // 初始化认证服务（同步等待，避免 AggregregateException 包装）
                Task.Run(async () =>
                {
                    await InitializeAuthAsync();
                }).GetAwaiter().GetResult();
                
                // 在 UI 线程上检查登录状态并显示窗口
                Dispatcher.Invoke(() =>
                {
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
                            System.Diagnostics.Debug.WriteLine($"ToggleSidebar error: {ex.Message}");
                        }
                    };
                    
                    _hotkeyService.NewTaskRequested += (s, args) =>
                    {
                        try { mainWindow?.Activate(); } catch (Exception) { /* Window may have been closed */ }
                    };
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}", 
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
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
            System.Diagnostics.Debug.WriteLine($"UI thread unhandled exception: {e.Exception}");
            var errorMessage = $"发生未处理的异常:\n\n{e.Exception.Message}";
            if (e.Exception.InnerException != null)
                errorMessage += $"\n\n内部异常:\n{e.Exception.InnerException.Message}";
            System.Diagnostics.Debug.WriteLine(errorMessage);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                System.Diagnostics.Debug.WriteLine($"AppDomain unhandled exception: {ex}");
        }
        
        private async Task InitializeAuthAsync()
        {
            try
            {
                await AuthService.Instance.InitializeAsync();
                _loginStateHandler = async (s, isLoggedIn) =>
                {
                    if (isLoggedIn)
                        await SyncService.Instance.InitializeAsync();
                };
                AuthService.Instance.LoginStateChanged += _loginStateHandler;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeAuth error: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"OnExit cleanup error: {ex.Message}");
            }
            
            // 释放单实例锁
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mutex release error: {ex.Message}");
            }
            
            base.OnExit(e);
        }
    }
}
