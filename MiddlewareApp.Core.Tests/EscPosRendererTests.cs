using System.Text;
using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class EscPosRendererTests
{
    private static readonly byte[] BoldOn = { 0x1B, 0x45, 0x01 };
    private static readonly byte[] BoldOff = { 0x1B, 0x45, 0x00 };
    private static readonly byte[] DoubleSize = { 0x1D, 0x21, 0x11 };
    private static readonly byte[] NormalSize = { 0x1D, 0x21, 0x00 };

    private static Task<byte[]> RenderAsync(params ReceiptLine[] lines) =>
        new EscPosRenderer().RenderAsync(lines, 48);

    private static Task<byte[]> RenderAsync(bool openCashbox, params ReceiptLine[] lines) =>
        new EscPosRenderer().RenderAsync(lines, 48, openCashbox: openCashbox);

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                match = haystack[i + j] == needle[j];
            if (match) return true;
        }
        return false;
    }

    [Fact]
    public async Task BoldLine_IsWrappedInBoldOnOff()
    {
        var bytes = await RenderAsync(new TextLine("TOTAL", LineAlign.Left, Bold: true));
        Assert.True(ContainsSequence(bytes, BoldOn));
        Assert.True(ContainsSequence(bytes, BoldOff));
    }

    [Fact]
    public async Task WideLine_UsesDoubleSize_AndHalfWidthPadding()
    {
        var bytes = await RenderAsync(new TextLine("HI", LineAlign.Center, Wide: true));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.True(ContainsSequence(bytes, DoubleSize));
        Assert.True(ContainsSequence(bytes, NormalSize));
        // Centered on a 24-column wide line: (24 - 2) / 2 = 11 leading spaces.
        Assert.Contains(new string(' ', 11) + "HI", text);
    }

    [Fact]
    public async Task PlainLine_HasNoStyleCommands()
    {
        var bytes = await RenderAsync(new TextLine("hello", LineAlign.Left));
        Assert.False(ContainsSequence(bytes, BoldOn));
        Assert.False(ContainsSequence(bytes, DoubleSize));
    }

    private sealed class CapturingDecoder : IReceiptImageDecoder
    {
        public int? TargetWidthDots;
        public bool? ExactWidth;

        public MonoImage? Decode(byte[] data, int targetWidthDots, bool exactWidth)
        {
            TargetWidthDots = targetWidthDots;
            ExactWidth = exactWidth;
            return new MonoImage { Width = 8, Height = 1, Rows = new byte[] { 0xFF } };
        }
    }

    [Fact]
    public async Task Image_WithTemplateWidth_ScalesToExactDots()
    {
        var decoder = new CapturingDecoder();
        var renderer = new EscPosRenderer(decoder, _ => Task.FromResult<byte[]?>(new byte[] { 1 }));
        await renderer.RenderAsync(new ReceiptLine[] { new ImageLine("https://x/logo.png", WidthPx: 150) }, 48);

        // 150 CSS px (96 dpi) → 317 printer dots (203 dpi), scaled exactly.
        Assert.Equal(317, decoder.TargetWidthDots);
        Assert.True(decoder.ExactWidth);
    }

    [Fact]
    public async Task Image_WithoutTemplateWidth_UsesDownscaleCap()
    {
        var decoder = new CapturingDecoder();
        var renderer = new EscPosRenderer(decoder, _ => Task.FromResult<byte[]?>(new byte[] { 1 }));
        await renderer.RenderAsync(new ReceiptLine[] { new ImageLine("https://x/logo.png") }, 48);

        // 75% of the 72 mm printable width at 203 dpi, downscale-only.
        Assert.Equal(431, decoder.TargetWidthDots);
        Assert.False(decoder.ExactWidth);
    }

    [Fact]
    public async Task Image_TemplateWiderThanPrintableArea_ClampsToPrintableDots()
    {
        var decoder = new CapturingDecoder();
        var renderer = new EscPosRenderer(decoder, _ => Task.FromResult<byte[]?>(new byte[] { 1 }));
        await renderer.RenderAsync(new ReceiptLine[] { new ImageLine("https://x/logo.png", WidthPx: 800) }, 48);

        Assert.Equal(576, decoder.TargetWidthDots);
        Assert.True(decoder.ExactWidth);
    }

    [Fact]
    public async Task OpenCashbox_AppendsDrawerKickAfterCut()
    {
        var bytes = await RenderAsync(openCashbox: true, new TextLine("TOTAL", LineAlign.Left));
        Assert.True(ContainsSequence(bytes, new byte[] { 0x1D, 0x56, 0x00 }));
        Assert.True(ContainsSequence(bytes, new byte[] { 0x1B, 0x70, 0x00, 0x3C, 0xFF }));
        Assert.True(bytes[^5] == 0x1B && bytes[^4] == 0x70);
    }

    [Fact]
    public async Task WithoutOpenCashbox_NoDrawerKick()
    {
        var bytes = await RenderAsync(openCashbox: false, new TextLine("TOTAL", LineAlign.Left));
        Assert.False(ContainsSequence(bytes, new byte[] { 0x1B, 0x70, 0x00, 0x3C, 0xFF }));
    }
}
