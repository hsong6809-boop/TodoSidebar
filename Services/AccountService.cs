using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// v5.2 账号中心服务。
    /// 数据流：云端 account_profile 为权威 → 本地 Settings 键（Acct* 前缀 + AcctOwner 归属标记）缓存
    /// → ProfileChanged 事件驱动 UI 刷新。账号切换时靠 AcctOwner 自失效，无需清库。
    /// 未建表 / 断网：静默降级纯本地模式，修改记 _pendingUpload 待下次供给时补传。
    /// </summary>
    public sealed class AccountService : IAccountService
    {
        private static AccountService? _instance;
        public static AccountService Instance => _instance ??= new AccountService();

        // 本地缓存键（Settings 表）
        private const string KeyOwner = "AcctOwner";       // 缓存归属的 auth uuid；不匹配即视为陈旧
        private const string KeyUid = "AcctUid";
        private const string KeyNick = "AcctNick";         // 账号昵称（云端权威）；旧 "Nickname" 键仅作迁移源
        private const string KeyKind = "AcctAvatarKind";

        /// <summary>自定义头像本地缓存文件。</summary>
        private static readonly string AvatarFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSidebar", "avatar.png");

        /// <summary>昵称最大长度（字符），超出截断。</summary>
        internal const int NicknameMaxLength = 24;

        public string Uid { get; private set; } = string.Empty;
        public string Nickname { get; private set; } = string.Empty;
        public string AvatarKind { get; private set; } = "d1";
        public bool IsProvisioned => !string.IsNullOrEmpty(Uid);

        public event EventHandler? ProfileChanged;

        private string? _ownerId;                 // 当前缓存归属（null = 尚未加载）
        private string? _customAvatarBase64;      // custom 头像的 base64（上传用）
        private volatile bool _pendingUpload;     // 本地有未上云的修改
        private int _provisioning;                // EnsureProvisionAsync 防重入

        private AccountService() { }

        // ==================== 纯逻辑（可测） ====================

        /// <summary>内置头像类型数量。</summary>
        internal const int BuiltInAvatarCount = 8;

        /// <summary>校验 8 位短 ID 格式（首位非零）。</summary>
        internal static bool IsValidUid(string? uid)
            => uid != null && uid.Length == 8 && uid[0] != '0' && uid.All(char.IsDigit);

        /// <summary>生成随机短 ID（首位非零）。</summary>
        internal static string GenerateUid()
            => Random.Shared.Next(1, 10).ToString() + Random.Shared.Next(0, 10000000).ToString("D7");

        /// <summary>规范化头像类型：d1~d8 大小写兼容、非法回退 d1、"custom" 原样。</summary>
        internal static string NormalizeKind(string? kind)
        {
            if (string.Equals(kind, "custom", StringComparison.OrdinalIgnoreCase)) return "custom";
            var k = (kind ?? "").Trim().ToLowerInvariant();
            if (k.Length == 2 && k[0] == 'd' && char.IsDigit(k[1]) && k[1] >= '1')
            {
                var n = k[1] - '0';
                if (n <= BuiltInAvatarCount) return k;
            }
            return "d1";
        }

        /// <summary>清洗昵称：去首尾、压空白、剔控制符与换行、限长截断。</summary>
        internal static string CleanNickname(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var chars = raw.Where(c => !char.IsControl(c) && c != '\n' && c != '\r');
            var joined = string.Concat(chars);
            foreach (var sep in new[] { ' ', '\u3000', '\t' })
                joined = joined.Replace(sep.ToString(), " ", StringComparison.Ordinal);
            while (joined.Contains("  ")) joined = joined.Replace("  ", " ", StringComparison.Ordinal);
            joined = joined.Trim();
            return joined.Length <= NicknameMaxLength ? joined : joined[..NicknameMaxLength];
        }

        // ==================== 供给 ====================

        public async Task EnsureProvisionAsync()
        {
            var userId = AuthService.Instance.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) return;
            if (Interlocked.CompareExchange(ref _provisioning, 1, 0) != 0) return;

            try
            {
                LoadLocal(userId);
                RaiseProfileChanged(); // 先以本地缓存渲染，避免等待网络期间空白

                SyncAccountProfile? remote = null;
                try
                {
                    var res = await SupabaseClientService.Client
                        .From<SyncAccountProfile>()
                        .Where(x => x.UserId == userId)
                        .Get();
                    remote = res.Models.OrderByDescending(m => m.UpdatedAt).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    // 未建表（404 等）或断网：降级纯本地。首次建档留待下次机会。
                    System.Diagnostics.Debug.WriteLine($"AccountService: 拉取云端档案失败（降级本地模式）: {ex.Message}");
                }

                if (remote == null)
                {
                    if (_pendingUpload || !IsProvisioned)
                        await ProvisionNewProfileAsync(userId);
                    return;
                }

                // 云端有档案：云端为准应用（登录时刻云端赢，多设备编辑以后一次登录为准）
                ApplyRemote(userId, remote);
                _pendingUpload = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AccountService: 供给流程异常: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _provisioning, 0);
            }
        }

        /// <summary>云端无档案时的首次建档：生成唯一短 ID + 迁移旧本地昵称。</summary>
        private async Task ProvisionNewProfileAsync(string userId)
        {
            var legacyNick = CleanNickname(DatabaseService.Instance.GetSetting("Nickname"));
            var nick = Nickname;
            if (nick.Length == 0 && legacyNick.Length > 0)
            {
                nick = legacyNick; // v5.2 迁移：老用户设置页存的昵称升级为账号昵称
            }

            var avatarKind = AvatarKind;
            var avatarData = avatarKind == "custom" ? _customAvatarBase64 : null;

            // 客户端预生成短 ID，unique 冲突自动换号重试（最多 3 次）
            Exception? lastError = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var uid = IsValidUid(Uid) ? Uid : GenerateUid();
                try
                {
                    await UploadProfileCoreAsync(userId, uid, nick, avatarKind, avatarData);
                    ApplyLocal(userId, uid, nick, avatarKind, avatarData);
                    _pendingUpload = false;
                    RaiseProfileChanged();
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    System.Diagnostics.Debug.WriteLine($"AccountService: 建档第 {attempt + 1} 次失败: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"AccountService: 建档失败（保持本地态待重试）: {lastError?.Message}");
        }

        private void ApplyRemote(string userId, SyncAccountProfile remote)
        {
            var data = remote.AvatarData;
            if (remote.AvatarKind == "custom" && !string.IsNullOrEmpty(data))
            {
                // 仅当内容变化才重写缓存文件，避免无谓磁盘 IO
                if (!TryReadAvatarFile(out var existing) || existing != data)
                    WriteAvatarFile(data);
            }
            ApplyLocal(userId, remote.Uid, CleanNickname(remote.Nickname),
                NormalizeKind(remote.AvatarKind), data);
        }

        private void ApplyLocal(string userId, string uid, string nick, string kind, string? avatarDataBase64)
        {
            _ownerId = userId;
            Uid = uid;
            Nickname = nick;
            AvatarKind = kind;
            _customAvatarBase64 = kind == "custom" ? avatarDataBase64 : null;

            var db = DatabaseService.Instance;
            db.SetSetting(KeyOwner, userId);
            db.SetSetting(KeyUid, uid);
            db.SetSetting(KeyNick, nick);
            db.SetSetting(KeyKind, kind);

            // 过渡期兼容：既有问候语/头像首字母消费方仍读 App.Nickname，保持同步（M3 收敛）
            App.Nickname = nick;
            RaiseProfileChanged();
        }

        private void LoadLocal(string userId)
        {
            if (_ownerId == userId && IsProvisioned) return; // 已加载且归属一致

            var db = DatabaseService.Instance;
            if (!string.Equals(db.GetSetting(KeyOwner), userId, StringComparison.Ordinal))
            {
                // 归属变更（切换账号 / 首次）：旧缓存自失效，等云端供给
                _ownerId = userId;
                Uid = string.Empty;
                Nickname = string.Empty;
                AvatarKind = "d1";
                _customAvatarBase64 = null;
                return;
            }

            _ownerId = userId;
            Uid = db.GetSetting(KeyUid) ?? string.Empty;
            Nickname = CleanNickname(db.GetSetting(KeyNick));
            AvatarKind = NormalizeKind(db.GetSetting(KeyKind));
            if (AvatarKind == "custom")
                _customAvatarBase64 = TryReadAvatarFile(out var b64) ? b64 : null;
        }

        // ==================== 修改操作 ====================

        public async Task SetNicknameAsync(string nickname)
        {
            var clean = CleanNickname(nickname);
            if (clean == Nickname) return;

            Nickname = clean;
            PersistLocal();
            App.Nickname = clean;
            RaiseProfileChanged();
            await UploadBestEffortAsync();
        }

        public async Task SetBuiltInAvatarAsync(string kind)
        {
            var normalized = NormalizeKind(kind);
            if (normalized == AvatarKind) return;

            AvatarKind = normalized;
            _customAvatarBase64 = null;
            PersistLocal();
            RaiseProfileChanged();
            await UploadBestEffortAsync();
        }

        public async Task SetCustomAvatarAsync(string imageFilePath)
        {
            // R23 修复（审查 M4/v5.x-M18）：解码/裁剪/编码是重 CPU 操作（最大 20MB 图片），
            // 原实现虽名为 Async 却全程跑在调用线程（UI 线程）上，大图会冻结整个界面。
            // 移入线程池执行；结果为纯字符串可安全跨线程回传。
            var base64 = await Task.Run(() => ProcessImageToBase64(imageFilePath)).ConfigureAwait(true);
            if (base64 == null) throw new InvalidOperationException("图片读取或解码失败，请更换图片文件");

            _customAvatarBase64 = base64;
            AvatarKind = "custom";
            WriteAvatarFile(base64);
            PersistLocal();
            RaiseProfileChanged();
            await UploadBestEffortAsync();
        }

        public string? GetCustomAvatarPath()
            => AvatarKind == "custom" && File.Exists(AvatarFilePath) ? AvatarFilePath : null;

        // ==================== 上传 ====================

        /// <summary>尽力上传：失败置 pending 标记，由下次 EnsureProvisionAsync 补传。</summary>
        private async Task UploadBestEffortAsync()
        {
            var userId = AuthService.Instance.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId)) return;
            try
            {
                await UploadProfileCoreAsync(userId, Uid, Nickname, AvatarKind,
                    AvatarKind == "custom" ? _customAvatarBase64 : null);
                _pendingUpload = false;
            }
            catch (Exception ex)
            {
                _pendingUpload = true;
                System.Diagnostics.Debug.WriteLine($"AccountService: 上传失败（已挂起待补传）: {ex.Message}");
            }
        }

        private async Task UploadProfileCoreAsync(
            string userId, string uid, string nick, string kind, string? avatarData)
        {
            await SupabaseClientService.Client.From<SyncAccountProfile>().Upsert(new SyncAccountProfile
            {
                UserId = userId,
                Uid = uid,
                Nickname = nick,
                AvatarKind = kind,
                AvatarData = avatarData,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // ==================== 本地持久化 / 头像文件 / 事件 ====================

        private void PersistLocal()
        {
            if (_ownerId == null) return;
            var db = DatabaseService.Instance;
            db.SetSetting(KeyOwner, _ownerId);
            db.SetSetting(KeyUid, Uid);
            db.SetSetting(KeyNick, Nickname);
            db.SetSetting(KeyKind, AvatarKind);
        }

        private static bool TryReadAvatarFile(out string base64)
        {
            base64 = string.Empty;
            try
            {
                if (!File.Exists(AvatarFilePath)) return false;
                base64 = Convert.ToBase64String(File.ReadAllBytes(AvatarFilePath));
                return true;
            }
            catch { return false; }
        }

        private static void WriteAvatarFile(string base64)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AvatarFilePath)!);
                File.WriteAllBytes(AvatarFilePath, Convert.FromBase64String(base64));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AccountService: 头像缓存写入失败: {ex.Message}");
            }
        }

        private void RaiseProfileChanged()
        {
            try { ProfileChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AccountService: ProfileChanged 处理器异常: {ex.Message}"); }
        }

        // ==================== 图片处理 ====================

        /// <summary>
        /// 图片 → 居中方形裁剪 → 128×128 PNG base64。
        /// 输入上限 20MB（防解码内存放大）；任何解码异常返回 null。
        /// </summary>
        internal static string? ProcessImageToBase64(string imagePath)
        {
            try
            {
                var fileInfo = new FileInfo(imagePath);
                if (!fileInfo.Exists || fileInfo.Length is < 1 or > 20 * 1024 * 1024) return null;

                BitmapSource? frame;
                using (var stream = fileInfo.OpenRead())
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    frame = decoder.Frames.FirstOrDefault();
                }
                if (frame == null) return null;
                frame.Freeze();

                var srcSide = Math.Min(frame.PixelWidth, frame.PixelHeight);
                if (srcSide < 16) return null;

                var cropped = new CroppedBitmap(frame, new System.Windows.Int32Rect(
                    (frame.PixelWidth - srcSide) / 2, (frame.PixelHeight - srcSide) / 2, srcSide, srcSide));

                var scale = 128.0 / srcSide;
                var scaled = new TransformedBitmap(cropped, new System.Windows.Media.ScaleTransform(scale, scale));
                scaled.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(scaled));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return null;
            }
        }
    }
}
