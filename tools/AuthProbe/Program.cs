// AuthProbe：脱离 WPF UI 线程，直接探测认证链路。
// 用错误密码调用登录——若库与网络正常，应在几秒内返回"凭据错误"；
// 若 30 秒仍无返回 => 库内部同步阻塞/网络黑洞，即定位成功。
using TodoSidebar.Services;

Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 探针启动（后台线程池上下文，无 SynchronizationContext）");

var sw = System.Diagnostics.Stopwatch.StartNew();

// 先验证配置读取
try
{
    Console.WriteLine($"URL 配置读取 OK: {TodoSidebar.Config.SupabaseConfig.Url}");
}
catch (Exception ex)
{
    Console.WriteLine($"配置缺失: {ex.Message}");
    return;
}

// 客户端构造测试
try
{
    var c = SupabaseClientService.Client;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client 构造完成: {sw.ElapsedMilliseconds}ms");
}
catch (Exception ex)
{
    Console.WriteLine($"Client 构造失败({sw.ElapsedMilliseconds}ms): {ex.Message}");
    return;
}

var task = AuthService.Instance.LoginWithEmailPasswordAsync("authprobe@nowhere.test", "definitely-wrong-pw");
var done = await Task.WhenAny(task, Task.Delay(30_000));

if (done != task)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] !!! 登录任务 30 秒无返回 —— 同步阻塞/黑洞确认 !!!");
    return;
}

var r = task.Result;
Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 返回: 耗时={sw.ElapsedMilliseconds}ms Success={r.Success} Error={r.Error?.Substring(0, Math.Min(120, r.Error?.Length ?? 0))}");
