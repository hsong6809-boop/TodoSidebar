// v5.6 白噪音资源生成器：程序化合成 4 种无缝循环环境音（免版权）。
// 运行：dotnet run --project tools/NoiseGen   （输出到 Assets/Audio/*.wav）
// 规格：22050Hz / 单声道 / PCM16 / 约 12 秒，尾部与头部交叉渐变实现无缝循环。

const int SampleRate = 22050;
const int Seconds = 12;
const double CrossfadeSeconds = 0.8;

// 锚定仓库根：从输出目录逐级向上找到包含主工程文件的目录
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TodoSidebar.csproj")))
        dir = dir.Parent;
    if (dir == null) throw new InvalidOperationException("未找到 TodoSidebar.csproj，无法定位仓库根");
    return dir.FullName;
}

string outDir = Path.Combine(FindRepoRoot(), "Assets", "Audio");
Directory.CreateDirectory(outDir);

WriteWav(Path.GetFullPath(Path.Combine(outDir, "rain.wav")), GenerateRain());
WriteWav(Path.GetFullPath(Path.Combine(outDir, "stream.wav")), GenerateStream());
WriteWav(Path.GetFullPath(Path.Combine(outDir, "fire.wav")), GenerateFire());
WriteWav(Path.GetFullPath(Path.Combine(outDir, "white.wav")), GenerateWhite());

Console.WriteLine("生成完成: Assets/Audio/{rain,stream,fire,white}.wav");
return;

// ==================== 合成器 ====================

static double[] GenerateWhite()
{
    var rng = new Random(20260826);
    int n = SampleRate * Seconds;
    var buf = new double[n];
    for (int i = 0; i < n; i++) buf[i] = rng.NextDouble() * 2 - 1;
    return SoftClip(buf, 0.55);
}

// 棕噪（积分白噪）+ 低频起伏，作为雨声基底
static double[] Brown(int n, Random rng)
{
    var buf = new double[n];
    double acc = 0;
    for (int i = 0; i < n; i++)
    {
        acc += (rng.NextDouble() * 2 - 1) * 0.02;
        acc *= 0.997;                 // 泄漏防漂移
        buf[i] = acc * 8;
    }
    return buf;
}

static double[] GenerateRain()
{
    var rng = new Random(1);
    int n = SampleRate * Seconds;
    var buf = Brown(n, rng);

    // 细密雨滴：高频短促衰减簇
    for (int drop = 0; drop < n / 900; drop++)
    {
        int pos = rng.Next(n - 800);
        double freq = 1200 + rng.NextDouble() * 2600;
        double amp = 0.05 + rng.NextDouble() * 0.12;
        for (int i = 0; i < 700 && pos + i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 60);
            buf[pos + i] += Math.Sin(2 * Math.PI * freq * t) * amp * env;
        }
    }
    return SoftClip(buf, 0.6);
}

static double[] GenerateStream()
{
    var rng = new Random(7);
    int n = SampleRate * Seconds;
    var white = new double[n];
    for (int i = 0; i < n; i++) white[i] = rng.NextDouble() * 2 - 1;

    // 简易带通：一阶高通 + 低通组合，模拟流水沙沙声
    var buf = new double[n];
    double lp = 0, prevIn = 0, hp = 0;
    for (int i = 0; i < n; i++)
    {
        lp += 0.28 * (white[i] - lp);          // 低通 ~1.4kHz
        hp = lp - prevIn;                       // 减去更慢的包络近似高通
        prevIn = lp;
        buf[i] = hp * 2.4;
    }
    return SoftClip(buf, 0.5);
}

static double[] GenerateFire()
{
    var rng = new Random(13);
    int n = SampleRate * Seconds;
    var buf = Brown(n, rng);

    // 木柴爆裂：低频宽衰减噼啪
    for (int crackle = 0; crackle < n / 2600; crackle++)
    {
        int pos = rng.Next(n - 1500);
        double amp = 0.15 + rng.NextDouble() * 0.35;
        for (int i = 0; i < 1200 && pos + i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * 25);
            buf[pos + i] += (rng.NextDouble() * 2 - 1) * amp * env;
        }
    }
    return SoftClip(buf, 0.65);
}

// ==================== 公共处理 ====================

static double[] SoftClip(double[] buf, double gain)
{
    var outBuf = new double[buf.Length];
    for (int i = 0; i < buf.Length; i++)
    {
        double x = buf[i] * gain;
        outBuf[i] = Math.Tanh(x);              // 软削波防爆音
    }
    return MakeSeamless(outBuf);
}

// 尾部 Crossfade 回头部并截断，消除循环接缝咔哒
static double[] MakeSeamless(double[] buf)
{
    int fade = (int)(SampleRate * CrossfadeSeconds);
    if (fade <= 0 || fade >= buf.Length) return buf;
    int bodyLen = buf.Length - fade;
    var result = new double[bodyLen];
    for (int i = 0; i < bodyLen; i++)
        result[i] = buf[i];
    for (int i = 0; i < fade; i++)
    {
        double t = (double)i / fade;           // 头部淡入 × 尾部样本淡出叠加
        result[i] = result[i] * t + buf[bodyLen + i] * (1 - t);
    }
    return result;
}

static void WriteWav(string path, double[] samples)
{
    using var fs = new FileStream(path, FileMode.Create);
    using var bw = new BinaryWriter(fs);
    int byteCount = samples.Length * 2;

    bw.Write("RIFF"u8);
    bw.Write(36 + byteCount);
    bw.Write("WAVE"u8);
    bw.Write("fmt "u8);
    bw.Write(16);
    bw.Write((short)1);            // PCM
    bw.Write((short)1);            // mono
    bw.Write(SampleRate);
    bw.Write(SampleRate * 2);      // byte rate
    bw.Write((short)2);            // block align
    bw.Write((short)16);           // bits
    bw.Write("data"u8);
    bw.Write(byteCount);

    foreach (var s in samples)
    {
        // 先钳位再放大到 PCM16 满量程（原写法先截断成 short 会把 (-1,1) 变成全零静音）
        var clamped = Math.Clamp(s, -1.0, 1.0);
        bw.Write((short)Math.Round(clamped * short.MaxValue));
    }

    bw.Flush();
    Console.WriteLine($"{Path.GetFileName(path)}: {samples.Length} samples, {fs.Length / 1024}KB");
}
