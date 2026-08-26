using System;
using System.IO;
using System.Windows.Threading;

namespace TodoSidebar.Services
{
    /// <summary>
    /// v5.6 番茄白噪音：内置 4 种程序合成的无缝循环环境音。
    /// MediaPlayer 播完自动重播实现循环；停止时 400ms 音量淡出避免戛然而止。
    /// 偏好（音源/音量）持久化于 Settings。
    /// </summary>
    public class SoundService
    {
        private static readonly Lazy<SoundService> _lazy = new(() => new SoundService());
        public static SoundService Instance => _lazy.Value;

        /// <summary>可用音源（kind → 中文名 + 资源路径）。</summary>
        public static readonly (string Kind, string Label, string ResourcePath)[] Sounds =
        {
            ("rain",   "雨声", "Assets/Audio/rain.wav"),
            ("stream", "溪流", "Assets/Audio/stream.wav"),
            ("fire",   "篝火", "Assets/Audio/fire.wav"),
            ("white",  "白噪", "Assets/Audio/white.wav"),
        };

        private System.Windows.Media.MediaPlayer? _player;
        private readonly DispatcherTimer? _uiTimer; // 预留：可视化用
        private DispatcherTimer? _fadeTimer;
        private double _targetVolume = 0.6;

        public bool IsPlaying { get; private set; }
        public string CurrentKind { get; private set; } = "";

        public event EventHandler? PlaybackChanged;

        private SoundService()
        {
            try
            {
                if (double.TryParse(DatabaseService.Instance.GetSetting("NoiseVolume"),
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    _targetVolume = Math.Clamp(v, 0.05, 1.0);
                var kind = DatabaseService.Instance.GetSetting("NoiseKind") ?? "";
                if (Array.Exists(Sounds, s => s.Kind == kind)) CurrentKind = kind;
            }
            catch { /* 偏好读取失败用默认值 */ }
        }

        /// <summary>当前音量（0.05~1）。</summary>
        public double Volume => _targetVolume;

        public void SetVolume(double volume)
        {
            _targetVolume = Math.Clamp(volume, 0.05, 1.0);
            try { DatabaseService.Instance.SetSetting("NoiseVolume", _targetVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            catch { }
            if (_player != null && IsPlaying) _player.Volume = _targetVolume;
        }

        /// <summary>播放指定音源；已在播同源则视为切换/重启。失败静默。</summary>
        public void Play(string kind)
        {
            var sound = Array.Find(Sounds, s => s.Kind == kind);
            if (sound.Kind == null) return;
            try
            {
                StopInternal(fade: false);
                EnsurePlayer();
                var uri = new Uri(packUri(sound.ResourcePath), UriKind.Absolute);
                _player!.Open(uri);
                CurrentKind = kind;
                IsPlaying = true;
                _player.Position = TimeSpan.Zero;
                _player.Volume = _targetVolume;
                _player.Play();
                SaveKind();
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sound] 播放失败: {ex.Message}");
            }
        }

        public void Toggle(string kind)
        {
            if (IsPlaying && CurrentKind == kind) Stop();
            else Play(kind);
        }

        public void Stop() => StopInternal(fade: true);

        // ==================== 内部 ====================

        private static string packUri(string relative)
            => "pack://application:,,,/" + AssemblyName + ";component/" + relative;

        private const string AssemblyName = "TodoSidebar";

        private void EnsurePlayer()
        {
            if (_player != null) return;
            _player = new System.Windows.Media.MediaPlayer();
            _player.MediaEnded += (_, _) =>
            {
                // 循环重播
                try
                {
                    _player!.Position = TimeSpan.Zero;
                    _player.Play();
                }
                catch { }
            };
        }

        private void StopInternal(bool fade)
        {
            if (_player == null || !IsPlaying)
            {
                IsPlaying = false;
                return;
            }

            if (!fade)
            {
                _player.Stop();
                IsPlaying = false;
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // 400ms 淡出后停止
            _fadeTimer?.Stop();
            double startVolume = _targetVolume;
            int ticks = 8;
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            int tick = 0;
            _fadeTimer.Tick += (_, _) =>
            {
                tick++;
                if (_player == null) { _fadeTimer!.Stop(); return; }
                _player.Volume = startVolume * (1 - (double)tick / ticks);
                if (tick >= ticks)
                {
                    _fadeTimer.Stop();
                    _player.Stop();
                    _player.Volume = _targetVolume;
                    IsPlaying = false;
                    CurrentKind = "";
                    PlaybackChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _fadeTimer.Start();
        }

        private void SaveKind()
        {
            try { DatabaseService.Instance.SetSetting("NoiseKind", CurrentKind); } catch { }
        }
    }
}
