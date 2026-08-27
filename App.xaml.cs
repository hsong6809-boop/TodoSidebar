using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TodoSidebar.Config;
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

        /// <summary>
        /// V2：用户昵称（设置页可改；侧边栏问候语与头像优先显示）。
        /// </summary>
        public static string Nickname { get; set; } = string.Empty;
        
        private static EventHandler<bool>? _loginStateHandler;

        /// <summary>
        /// 全局快捷键服务（R41：改静态承载，供登录/登出路径迁移注册）
        /// </summary>
        private static HotkeyService? _hotkeyService;

        // R40 修复（审查 H5）：单实例互斥体——双开会导致两个进程同时打开同一 SQLite 库、
        // 通知/同步/每日检查翻倍，且第二实例的全局热键静默全灭
        private static Mutex? _singleInstanceMutex;

        // R41 修复（审查 H4）：当前主窗口引用（静态）。
        // 原实现热键处理器闭包捕获启动时的局部窗口变量，登出重登后该引用指向已销毁窗口，
        // 基于过期引用判断可能开出第二个主窗口
        private static Window? _currentMainWindow;

        /// <summary>R41：登录/切换主窗口后调用，让全局热键迁移到新窗口并更新当前窗口引用。</summary>
        public static void AttachHotkeysTo(Window newMain)
        {
            _currentMainWindow = newMain;
            try
            {
                // RegisterHotkeys 内部会先 Unregister 再注册（HotkeyService.cs:51）
                (_hotkeyService ??= new HotkeyService()).RegisterHotkeys(newMain);
            }
            catch (Exception ex)
            {
                LogError("AttachHotkeys error", ex);
            }

            // v5.6 审查修复：热键事件处理器必须在首次 Attach 时接线——
            // 原实现只在 OnStartup 的"已登录"分支订阅；未登录启动的用户经登录窗
            // 进入后热键已注册却没有任何监听者，Ctrl+Alt+Space/T/N/F 全部静默失效
            WireHotkeyHandlers();
        }

        private static bool _hotkeyHandlersWired;

        /// <summary>幂等接线全部热键事件处理器（仅订阅一次，重复 Attach 不叠加）。</summary>
        private static void WireHotkeyHandlers()
        {
            if (_hotkeyHandlersWired || _hotkeyService == null) return;
            _hotkeyHandlersWired = true;

            _hotkeyService.ToggleSidebarRequested += (s, args) =>
            {
                try
                {
                    // R41 修复（审查 H4）：读取静态"当前主窗口"引用而非闭包捕获的启动窗口——
                    // 登出重登后旧闭包引用指向已销毁窗口，会误判模式并可能开出第二个主窗口
                    var currentMain = _currentMainWindow;
                    if (currentMain is MainWindow sidebar)
                    {
                        var fullWindow = new FullWindow();
                        fullWindow.Show();
                        sidebar.Close();
                        _currentMainWindow = fullWindow;
                        _hotkeyService.ReRegisterHotkeys(fullWindow);
                    }
                    else if (currentMain is FullWindow full)
                    {
                        var sidebarWindow = new MainWindow();
                        sidebarWindow.Show();
                        full.Close();
                        _currentMainWindow = sidebarWindow;
                        _hotkeyService.ReRegisterHotkeys(sidebarWindow);
                    }
                }
                catch (Exception ex)
                {
                    LogError("ToggleSidebar error", ex);
                }
            };

            // 新建任务/搜索热键：统一激活当前主窗口
            EventHandler activateHandler = (s, args) =>
            {
                try { _currentMainWindow?.Activate(); } catch (Exception ex) { LogError("Hotkey activate error", ex); }
            };
            _hotkeyService.NewTaskRequested += activateHandler;
            _hotkeyService.SearchRequested += activateHandler;

            // v5.4：Ctrl+Alt+Space 全局快速添加浮窗（Spotlight 式，重复按切换开关）
            _hotkeyService.QuickAddRequested += (s, args) =>
            {
                try { QuickAddWindow.Toggle(); }
                catch (Exception ex) { LogError("QuickAdd toggle error", ex); }
            };
        }

        /// <summary>R41：登出时注销全局热键（旧窗口即将销毁，注册其上的热键随之失效）。</summary>
        public static void DetachHotkeys()
        {
            try { _hotkeyService?.UnregisterHotkeys(); }
            catch (Exception ex) { LogError("DetachHotkeys error", ex); }
        }
        
        /// <summary>
        /// 应用日志文件路径（%APPDATA%\TodoSidebar\logs\app.log）
        /// </summary>
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSidebar", "logs", "app.log");

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // v5.6：Toast 激活事件必须在最早期接管——从通知按钮冷启动时，
            // Toolkit 会在订阅瞬间同步派发排队中的激活（完成/稍后提醒），
            // 之后才进入单实例互斥；若放在互斥之后，激活会被第二实例退出吞掉
            TodoSidebar.Services.ToastService.EnsureActivatedHandler();

            // R40 修复（审查 H5）：单实例保护。已有实例在运行时提示后退出，
            // 避免双开共享同一 SQLite 库/热键冲突
            _singleInstanceMutex = new Mutex(true, "Local\\TodoSidebar.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("TodoSidebar 已经在运行中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // V2 收尾：读取"减少动态效果"设置
            try { AnimationService.ReduceMotion = DatabaseService.Instance.GetSetting("ReduceMotion") == "true"; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"读取 ReduceMotion 设置失败: {ex.Message}"); }

            // R61「输入统计」：仅当用户此前显式开启过才恢复计数（默认关闭、不开钩子）。
            // 与登录状态无关——这是本机终身数据，登出/切换账号不中断统计
            try
            {
                if (DatabaseService.Instance.GetSetting("TypingStatsEnabled") == "true")
                    TypingStatsService.Instance.SetEnabled(true);
            }
            catch (Exception ex) { LogError("输入统计服务启动失败", ex); }

            // V2：加载昵称
            try { Nickname = DatabaseService.Instance.GetSetting("Nickname") ?? string.Empty; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"读取昵称失败: {ex.Message}"); }

            // v5.3 回收站：启动时清除超过保留期（30 天）且已同步的软删除任务；失败不影响启动
            try { DatabaseService.Instance.PurgeExpiredDeletedTasks(); }
            catch (Exception ex) { LogError("回收站过期清理失败", ex); }

            // 注册全局异常处理
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                // 检查启动参数
                bool isSidebarMode = e.Args.Contains("--sidebar");
                
                // 异步初始化认证服务（await 期间 UI 线程不阻塞）
                await InitializeAuthAsync();

                // M40：每日首次启动静默检测更新（后台执行；发现新版本才弹窗，
                // 检测失败/无网络完全静默，不影响启动流程与登录分支）
                _ = TodoSidebar.Services.UpdateChecker.RunDailyCheckAsync();

                var authService = Services.GetRequiredService<IAuthService>();

                if (!authService.IsLoggedIn)
                {
                    // M37：启动即校验 Supabase 配置。原先配置缺失被静默吞掉，
                    // 全新安装的机器上用户点登录只会莫名失败，这里给出明确提示。
                    try
                    {
                        _ = SupabaseConfig.Url;
                        _ = SupabaseConfig.AnonKey;
                    }
                    catch (Exception cfgEx)
                    {
                        MessageBox.Show(
                            "同步服务配置缺失，登录 / 注册 / 忘记密码均不可用。\n\n" +
                            $"{cfgEx.Message}\n\n" +
                            "请将 supabase.json 放到程序安装目录后重新打开程序。",
                            "配置缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

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

                // R41：注册全局快捷键并登记当前主窗口（内部幂等接线全部热键处理器）
                AttachHotkeysTo(mainWindow);
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
        /// R41：登出即注销全局热键——旧主窗口即将销毁，热键注册随之失效；
        /// 重登成功后由 LoginWindow 调 AttachHotkeysTo 重新注册到新窗口。
        /// </summary>
        public static void StopBackgroundServices()
        {
            DetachHotkeys();
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
            services.AddSingleton<IAccountService>(AccountService.Instance);
        }

        // R42 修复（审查 M3）：全局异常熔断——60 秒窗口内连续 ≥5 次未处理异常
        // 说明应用已进入损坏状态，继续 e.Handled=true 吞掉只会变成"僵尸态"，
        // 此时转 Shutdown 让用户感知而不是无限静默
        private static readonly Queue<DateTime> _recentUiErrors = new();

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("UI thread unhandled exception", e.Exception);

            // 致命类型不标记 Handled：XAML 解析错误通常意味着资源/主题字典已坏，吞掉只会更诡异
            if (e.Exception is System.Windows.Markup.XamlParseException)
            {
                MessageBox.Show($"发生致命的界面初始化错误:\n\n{e.Exception.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var now = DateTime.UtcNow;
            _recentUiErrors.Enqueue(now);
            while (_recentUiErrors.Count > 0 && now - _recentUiErrors.Peek() > TimeSpan.FromSeconds(60))
                _recentUiErrors.Dequeue();

            if (_recentUiErrors.Count >= 5)
            {
                MessageBox.Show("应用连续发生多次内部错误，即将退出。\n请重新打开；若反复出现请联系开发者并提供日志文件。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

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
                            // R58 修复（审查 M1）：归属校验（EnsureUserScope）已移交给
                            // LoginWindow 在登录路径上同步执行——那里有用户在场，
                            // 可以先弹"将丢失 N 条未同步数据"确认框再决定是否清库。
                            // 处理器只负责无 UI 的网络类初始化，避免在确认前就把库清了
                            TodoSidebar.Services.AuthService.LogAuthDiag("[handler] 开始 SyncService 初始化");

                            await SyncService.Instance.InitializeAsync();
                            TodoSidebar.Services.AuthService.LogAuthDiag("[handler] SyncService 初始化完成");

                            // v5.2 账号中心：登录后供给账号档案（短 ID/昵称/头像），失败静默降级
                            _ = AccountService.Instance.EnsureProvisionAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("Login state handler error", ex);
                        TodoSidebar.Services.AuthService.LogAuthDiag($"[handler] 异常: {ex.GetType().Name}: {ex.Message}");
                    }
                };
                AuthService.Instance.LoginStateChanged += _loginStateHandler;

                // S4 修复：会话恢复成功时 LoginStateChanged 事件在订阅之前就已发出，
                // 处理器收不到通知导致自动同步永不启动。此处补一次显式检查。
                // R59 修复（审查 M1）：恢复的是"与本地数据归属不同的账号"且存在未上云脏数据时，
                // 不再静默清库——转为本机登出，让用户在登录窗口显式选择（有确认弹窗兜底）
                if (AuthService.Instance.IsLoggedIn)
                {
                    var restoredUserId = AuthService.Instance.CurrentUser?.Id;
                    if (!string.IsNullOrEmpty(restoredUserId))
                    {
                        var lastUserId = DatabaseService.Instance.GetSetting("LastUserId");
                        var dirtyCount = DatabaseService.Instance.GetDirtyTaskCount();
                        var sameAccount = string.IsNullOrEmpty(lastUserId) || lastUserId == restoredUserId;

                        if (sameAccount || dirtyCount == 0)
                        {
                            // 同号重登（幂等无操作）或本机没有会丢失的未同步数据：正常恢复
                            DatabaseService.Instance.EnsureUserScope(restoredUserId);
                            await SyncService.Instance.InitializeAsync();

                            // v5.2 账号中心：会话恢复路径同样供给账号档案（不阻塞启动）
                            _ = AccountService.Instance.EnsureProvisionAsync();
                        }
                        else
                        {
                            // 本地脏数据属于上一个账号而本次恢复的是另一账号：
                            // 自动清库会永久丢失离线数据，拒绝静默切换
                            LogError("Session restore skipped: local unsynced data belongs to another account", null);
                            await AuthService.Instance.LogoutAsync();
                        }
                    }
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
                // L9 修复：退出时若有进行中的番茄会话，按中断落一条会话记录，
                // 避免用户已专注的时间静默蒸发（无 XP，但保留时长统计）
                var pomoState = PomodoroService.Instance.State;
                if (pomoState == PomodoroState.Focus || pomoState == PomodoroState.Paused)
                {
                    PomodoroService.Instance.Stop(false);
                }

                _hotkeyService?.Dispose();
                _hotkeyService = null;
                _currentMainWindow = null;
                NotificationService.Instance.Stop();
                SyncService.Instance.Stop();
                // R61：退出前冲刷残余打字增量并卸载键盘钩子（必须在 DatabaseService.Dispose 之前）
                TypingStatsService.Instance.Dispose();
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
            finally
            {
                // R40：释放单实例互斥体
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                    _singleInstanceMutex?.Dispose();
                    _singleInstanceMutex = null;
                }
                catch { /* 尽力释放 */ }
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
