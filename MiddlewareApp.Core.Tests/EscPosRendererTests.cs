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
}
