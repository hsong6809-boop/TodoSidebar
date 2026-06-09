using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TodoSidebar.Services;
using TodoSidebar.ViewModels;

namespace TodoSidebar
{
    public partial class App : Application
    {
        /// <summary>
        /// 共享的 ViewModel 实例，确保窗口切换时数据同步
        /// </summary>
        public static MainViewModel SharedViewModel { get; set; } = null!;

        /// <summary>
        /// 全局快捷键服务
        /// </summary>
        private HotkeyService? _hotkeyService;
        
        /// <summary>
        /// 是否需要登录（通过命令行参数控制）
        /// </summary>
        public static bool RequireLogin { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 注册全局异常处理
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                // 检查启动参数
                RequireLogin = !e.Args.Contains("--no-sync");  // 默认需要登录，除非明确传入 --no-sync
                bool isSidebarMode = e.Args.Contains("--sidebar");
                
                // 初始化认证服务（使用 Task.Run 避免死锁）
                Task.Run(async () =>
                {
                    await InitializeAuthAsync();
                }).Wait();
                
                // 在 UI 线程上检查登录状态并显示窗口
                Dispatcher.Invoke(() =>
                {
                    // 如果未登录，显示登录窗口
                    if (!AuthService.Instance.IsLoggedIn)
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
                        try
                        {
                            mainWindow?.Activate();
                        }
                        catch { }
                    };
                    
                    _hotkeyService.SearchRequested += (s, args) =>
                    {
                        try
                        {
                            mainWindow?.Activate();
                        }
                        catch { }
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

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"UI thread unhandled exception: {e.Exception}");
            
            // 记录详细错误信息
            var errorMessage = $"发生未处理的异常:\n\n{e.Exception.Message}";
            if (e.Exception.InnerException != null)
            {
                errorMessage += $"\n\n内部异常:\n{e.Exception.InnerException.Message}";
            }
            
            // 不显示对话框，避免阻塞
            System.Diagnostics.Debug.WriteLine(errorMessage);
            
            // 标记为已处理，防止应用崩溃
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppDomain unhandled exception: {ex}");
            }
        }
        
        /// <summary>
        /// 初始化认证服务
        /// </summary>
        private async Task InitializeAuthAsync()
        {
            try
            {
                await AuthService.Instance.InitializeAsync();
                
                // 登录成功后启动同步服务
                AuthService.Instance.LoginStateChanged += async (s, isLoggedIn) =>
                {
                    if (isLoggedIn)
                    {
                        await SyncService.Instance.InitializeAsync();
                    }
                };
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
            }
            catch { }
            
            base.OnExit(e);
        }
    }
}
