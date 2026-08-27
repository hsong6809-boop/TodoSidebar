using System;
using System.Collections.Generic;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 按键流 → 双指标（击键数 + Word 口径字数）纯换算算法。
    /// 无 Win32 / UI / IO 依赖，全部逻辑可单元测试（TypingCounterTests 锁定口径）。
    ///
    /// 口径约定（功能「输入统计」）：
    ///  - 击键数：产生文本交互的物理按键（字母/数字/空格/回车/退格/Tab/标点），
    ///    修饰键与 F 功能键、导航键由调用方过滤，不进入本类；
    ///  - 字数：Word 式口径估算。英文按连续字母数字段 = 1 词；中文段按拼音音节贪心切分 ≈ 汉字数。
    ///
    /// 精度天花板（设置页已向用户明示）：WH_KEYBOARD_LL 只能看到物理按键流，
    /// 看不到输入法最终上屏结果；简拼、中英混打存在 ±15% 内偏差。
    /// 贪心策略对未知串宁可少估不多估（非法音节逐字符跳过计 0）。
    /// </summary>
    public sealed class TypingCounterCore
    {
        private enum SegmentKind { None, Direct, Pinyin }

        private readonly List<char> _buffer = new(32);
        private SegmentKind _kind = SegmentKind.None;

        /// <summary>累计有效击键数。</summary>
        public long KeyStrokes { get; private set; }

        /// <summary>累计换算字数（估算值）。</summary>
        public long WordChars { get; private set; }

        /// <summary>
        /// 字母/数字键。<paramref name="ime"/>=true 表示该键此刻进入的是输入法拼音组词串（候选窗打开），
        /// false 表示直接上屏（英文直输或输入法处于英文模式）。
        /// </summary>
        public void OnAlnum(char ch, bool ime)
        {
            KeyStrokes++;

            if (_kind == SegmentKind.None)
            {
                _kind = ime ? SegmentKind.Pinyin : SegmentKind.Direct;
                _buffer.Add(char.ToLowerInvariant(ch));
                return;
            }

            if (_kind == SegmentKind.Pinyin && !ime)
            {
                // 组词途中输入法被关闭/上屏了部分文本：先按拼音结算旧段，再开直输新段
                Flush();
                _kind = SegmentKind.Direct;
            }

            _buffer.Add(char.ToLowerInvariant(ch));
        }

        /// <summary>断词键：空格 / 回车 / Tab / 标点。结算当前段（空格在拼音态即选词上屏，同样适用）。</summary>
        public void OnSeparator()
        {
            KeyStrokes++;
            if (_kind != SegmentKind.None) Flush();
        }

        /// <summary>退格：从当前段缓冲扣一个字符（无缓冲时只计击键），字数随缓冲实时回退。</summary>
        public void OnBackspace()
        {
            KeyStrokes++;
            if (_buffer.Count > 0) _buffer.RemoveAt(_buffer.Count - 1);
        }

        /// <summary>
        /// 取走自上次采样以来的增量并清零。尾段即使未被断词键结束也参与结算，
        /// 保证落库前数据完整。服务每分钟调用一次并批量写入 DailyTypingStat。
        /// </summary>
        public (long keys, long words) TakeDelta()
        {
            Flush();
            var result = (KeyStrokes, WordChars);
            KeyStrokes = 0;
            WordChars = 0;
            return result;
        }

        /// <summary>
        /// R61 实时显示：只读窥视当前累计（不重置计数器）。
        /// 尾段同样先行结算进总数，保证正在输入的词立即被统计。
        /// </summary>
        public (long keys, long words) Peek()
        {
            Flush();
            return (KeyStrokes, WordChars);
        }

        private void Flush()
        {
            if (_buffer.Count > 0)
            {
                var s = string.Concat(_buffer);
                WordChars += _kind == SegmentKind.Pinyin ? SplitPinyin(s) : 1;
                _buffer.Clear();
            }
            _kind = SegmentKind.None;
        }

        /// <summary>
        /// 拼音串 → 估算汉字数：贪心最长匹配合法无调音节表。
        /// 例：nihao→2（ni+hao）；zhongguo→2（zhong+guo，验证不落入 zh+o… 的浅匹配）；
        /// an 与 ang、fang 与 fan 等歧义由最长优先消解。
        /// </summary>
        internal static int SplitPinyin(string input)
        {
            int i = 0, count = 0;
            while (i < input.Length)
            {
                int maxLen = Math.Min(MaxSyllableLength, input.Length - i);
                int matched = 0;
                for (int len = maxLen; len >= 1; len--)
                {
                    if (PinyinSyllables.Contains(input.Substring(i, len)))
                    {
                        matched = len;
                        break;
                    }
                }
                if (matched > 0) { count++; i += matched; }
                else i++;
            }
            return count;
        }

        internal const int MaxSyllableLength = 6; // zhuang 最长

        /// <summary>标准普通话无调音节表（ü 按 v 约定，如 lv/nv），共约 410 个。</summary>
        internal static readonly HashSet<string> PinyinSyllables = BuildSyllables();

        private static HashSet<string> BuildSyllables()
        {
            var raw =
                "a ai an ang ao " +
                "ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu " +
                "ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi chong chou chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo " +
                "da dai dan dang dao de dei den deng di dia dian diao die ding diu dong dou du duan dui dun duo " +
                "e ei en eng er " +
                "fa fan fang fei fen feng fo fou fu " +
                "ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo " +
                "ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo " +
                "ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun " +
                "ka kai kan kang kao ke kei ken keng kong kou ku kua kuai kuan kuang kui kun kuo " +
                "la lai lan lang lao le lei leng lia li lian liang liao lie lin ling liu lo long lou lu luan lun luo lv lue " +
                "ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu " +
                "na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou nu nuan nuo nv nue " +
                "o ou " +
                "pa pai pan pang pao pei pen peng pi pian piao pie pin ping po pou pu " +
                "qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun " +
                "ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo " +
                "sa sai san sang sao se sen seng sha shai shan shang shao she shei shen sheng shi shou shu shua shuai shuan shuang shui shun shuo si song sou su suan sui sun suo " +
                "ta tai tan tang tao te teng ti tian tiao tie ting tong tou tu tuan tui tun tuo " +
                "wa wai wan wang wei wen weng wo wu " +
                "xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun " +
                "ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun " +
                "za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhen zheng zhi zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui zun zuo";

            return new HashSet<string>(
                raw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }
    }
}
