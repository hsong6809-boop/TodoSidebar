using System;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>B1：Peek 必须无副作用——不 Flush 段缓冲、不重置计数。</summary>
    public class TypingPeekNonMutatingTests
    {
        [Fact]
        public void Peek_DoesNotMutateCounters_OrBuffer()
        {
            var c = new TypingCounterCore();
            foreach (var ch in "hello") c.OnAlnum(ch, ime: false);

            var p1 = c.Peek();
            var p2 = c.Peek();
            // 反复 Peek 结果稳定（原实现在第一次 Peek 就会 Flush，后续 Peek 不再含尾段或重复累加）
            Assert.Equal(p1, p2);
            Assert.Equal(5, p1.keys);
            Assert.Equal(1, p1.words);
        }

        [Fact]
        public void Peek_TwiceThenTakeDelta_NoDoubleCount()
        {
            var c = new TypingCounterCore();
            foreach (var ch in "world") c.OnAlnum(ch, ime: false);

            var _ = c.Peek();
            _ = c.Peek();
            var (keys, words) = c.TakeDelta();

            // Peek 若变异（Flush 进 WordChars），TakeDelta 会再算一次 → words 变 2
            Assert.Equal(5, keys);
            Assert.Equal(1, words);
        }

        [Fact]
        public void Peek_IncludesPendingTail_WithoutFlushing()
        {
            var c = new TypingCounterCore();
            foreach (var ch in "nihao") c.OnAlnum(ch, ime: true);
            var (k, w) = c.Peek();
            Assert.Equal(5, k);
            Assert.Equal(2, w); // ni+hao 尾段临时估算
            // 状态未变：再 Peek 相同
            Assert.Equal((k, w), c.Peek());
        }

        [Fact]
        public void ImeSwitch_DirectToPinyin_FlushesDirectSegment()
        {
            // B11：Direct→Pinyin 中途切换也要结算旧段
            var c = new TypingCounterCore();
            foreach (var ch in "ok") c.OnAlnum(ch, ime: false);
            // 切到输入法
            c.OnAlnum('n', ime: true);
            foreach (var ch in "ihao") c.OnAlnum(ch, ime: true);
            c.OnSeparator();

            // "ok"=1 英文词 + "nihao"≈2 汉字
            Assert.Equal(3, c.WordChars);
        }
    }
}
