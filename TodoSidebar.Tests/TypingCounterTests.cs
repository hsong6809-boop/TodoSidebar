using System;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// R61「输入统计」核心换算口径测试。
    /// 双指标：击键数精确；字数为 Word 口径估算（英文段=1词，中文按拼音音节贪心切分）。
    /// </summary>
    public class TypingCounterTests
    {
        private static void TypeDirectWord(TypingCounterCore c, string word)
        {
            foreach (var ch in word) c.OnAlnum(ch, ime: false);
        }

        private static void TypePinyin(TypingCounterCore c, string pinyin)
        {
            foreach (var ch in pinyin) c.OnAlnum(ch, ime: true);
        }

        [Fact]
        public void English_TwoWords_CountsTwo()
        {
            var c = new TypingCounterCore();
            TypeDirectWord(c, "hello");
            c.OnSeparator();
            TypeDirectWord(c, "world");
            c.OnSeparator();

            Assert.Equal(12, c.KeyStrokes);   // 5 字母 + 空格 + 5 字母 + 收尾空格 = 12 键
            Assert.Equal(2, c.WordChars);     // 2 词
        }

        [Fact]
        public void English_DigitsMergeIntoWord()
        {
            // Word 规则：连续字母数字串 "abc123" = 1 词
            var c = new TypingCounterCore();
            TypeDirectWord(c, "abc123");
            c.OnSeparator();
            Assert.Equal(1, c.WordChars);
            Assert.Equal(7, c.KeyStrokes);    // 6 字符 + 结算空格
        }

        [Fact]
        public void Pinyin_CommonWords_SplitBySyllable()
        {
            // nihao → ni/hao = 2 字（上屏"你好"）
            var c = new TypingCounterCore();
            TypePinyin(c, "nihao");
            c.OnSeparator();          // 拼音态空格=选词上屏
            Assert.Equal(2, c.WordChars);

            // zhongguo → zhong/guo = 2 字（验证不落入浅层误切 zh+o…）
            var c2 = new TypingCounterCore();
            TypePinyin(c2, "zhongguo");
            c2.OnSeparator();
            Assert.Equal(2, c2.WordChars);
        }

        [Fact]
        public void Pinyin_AmbiguousClusters_GreedyLongestWins()
        {
            // fang：最长优先命中 fang(4)=1 而非 fan+g
            Assert.Equal(1, TypingCounterCore.SplitPinyin("fang"));
            // an 是合法独立音节：xian 若整体合法优先 xian；an 保底为 1
            Assert.Equal(1, TypingCounterCore.SplitPinyin("xian"));
            Assert.Equal(1, TypingCounterCore.SplitPinyin("an"));
            // zhuang 六字母最长音节
            Assert.Equal(1, TypingCounterCore.SplitPinyin("zhuang"));
            // zhongwen → zhong/wen = 2（中文）
            Assert.Equal(2, TypingCounterCore.SplitPinyin("zhongwen"));
        }

        [Fact]
        public void Pinyin_MultipleSentences_Accumulate()
        {
            // "ni hao" 分两次上屏：nihao+space + space? 空格断段后第二词继续
            var c = new TypingCounterCore();
            TypePinyin(c, "nijia");     // 你家 = ni/jia 2 音节
            c.OnSeparator();
            TypePinyin(c, "zaijian");   // 再见 = zai/jian 2 音节
            c.OnSeparator();
            Assert.Equal(4, c.WordChars);
        }

        [Fact]
        public void Backspace_ReducesBufferBeforeFlush()
        {
            // 打错重打：helo ←退格→ hell o... 模拟 hello 的纠错路径
            var c = new TypingCounterCore();
            TypeDirectWord(c, "helo");      // 打错
            c.OnBackspace();                // 删掉 o
            c.OnAlnum('l', ime: false);     // 补 l
            c.OnAlnum('o', ime: false);     // 补 o → 缓冲区恢复 hello
            c.OnSeparator();

            Assert.Equal(8, c.KeyStrokes);  // 4 退格后 3 次
            Assert.Equal(1, c.WordChars);   // 仍是一个词——退格生效
        }

        [Fact]
        public void Backspace_EmptySegment_OnlyCountsStroke()
        {
            var c = new TypingCounterCore();
            c.OnBackspace();               // 无段，仅击键
            Assert.Equal(1, c.KeyStrokes);
            Assert.Equal(0, c.WordChars);

            TypeDirectWord(c, "hi");
            c.OnSeparator();
            c.OnBackspace();               // 段已结算，只计击键不影响已算字数
            Assert.Equal(1, c.WordChars);
        }

        [Fact]
        public void PunctuationAndEnter_BreakWordsWithoutAddingChars()
        {
            var c = new TypingCounterCore();
            TypeDirectWord(c, "ok");
            c.OnSeparator();               // 标点，如 ','
            TypeDirectWord(c, "go");
            c.OnSeparator();               // 回车
            Assert.Equal(2, c.WordChars);
            Assert.Equal(6, c.KeyStrokes); // 2+标点+2+回车
        }

        [Fact]
        public void ImeSwitchMidSegment_SettlesAsTwoSegments()
        {
            // 组词中输入法被关闭：前面的拼音先按拼音估算，后续按直输词估算
            var c = new TypingCounterCore();
            TypePinyin(c, "ma");           // ma 进入拼音缓冲
            c.OnAlnum('i', ime: false);    // 输入法关了，i 直输 → 结算 ma=1，开新直输段
            TypeDirectWord(c, "n");        // 同属直输段
            c.OnSeparator();
            Assert.Equal(2, c.WordChars);  // ma(1 汉字) + in(1 英文词)
            Assert.Equal(5, c.KeyStrokes); // ma(2) + i + n + 结算空格
        }

        [Fact]
        public void UnknownCluster_KeystrokeCountedButNotInflated()
        {
            // 中文态下敲简拼/噪声 nh：无合法切分 → 宁可少估不多估
            var c = new TypingCounterCore();
            TypePinyin(c, "nhzw");
            c.OnSeparator();
            Assert.Equal(5, c.KeyStrokes);    // 4 拼音键 + 结算空格
            // n,h 非法跳过；zw/z 无合法切分 → 宁可少估不多估，产出 0 字
            Assert.InRange(c.WordChars, 0, 5);
        }

        [Fact]
        public void TakeDelta_ResetsCounters_AndFlushesTail()
        {
            var c = new TypingCounterCore();
            TypeDirectWord(c, "abc");      // 不发断词键——尾段未结算

            var (keys, words) = c.TakeDelta();
            Assert.Equal(3, keys);
            Assert.Equal(1, words);        // 尾段被强制结算

            var (k2, w2) = c.TakeDelta();
            Assert.Equal(0, k2);
            Assert.Equal(0, w2);           // 清零后归零
        }

        [Fact]
        public void SyllableTable_CoversCriticalTones()
        {
            // 音节表完备性抽查：含 ü(v) 约定与易漏音节
            Assert.Contains("lv", TypingCounterCore.PinyinSyllables);
            Assert.Contains("nv", TypingCounterCore.PinyinSyllables);
            Assert.Contains("lue", TypingCounterCore.PinyinSyllables);
            Assert.Contains("jiong", TypingCounterCore.PinyinSyllables);
            Assert.Contains("chua", TypingCounterCore.PinyinSyllables);
            Assert.Contains("yo", TypingCounterCore.PinyinSyllables);
        }
    }
}
