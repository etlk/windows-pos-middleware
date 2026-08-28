using MiddlewareApp.Core.Models;
using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class PrintJobHandlerTests
{
    private static AgentConfigs Configs(
        PrintConfig? terminal = null,
        (int id, PrintConfig? cfg)[]? departments = null,
        int selectedDeviceId = 1)
    {
        return new AgentConfigs
        {
            SelectedDeviceId = selectedDeviceId,
            Device = new SlotConfig
            {
                Id = selectedDeviceId,
                IsMiddlewareConfigured = terminal != null,
                PrintConfig = terminal,
            },
            Departments = (departments ?? Array.Empty<(int, PrintConfig?)>())
                .Select(d => new SlotConfig
                {
                    Id = d.Item1,
                    IsMiddlewareConfigured = d.Item2 != null,
                    PrintConfig = d.Item2,
                }).ToList(),
        };
    }

    private static PrintConfig Printer(string ip, int port = 9100) =>
        new() { Ip = ip, Port = port, PaperSize = "80mm" };

    [Theory]
    [InlineData("PRINT")]
    [InlineData("PRINT_RECEIPT")]
    [InlineData("PRINT_KOT")]
    [InlineData("print_receipt")]
    [InlineData("PRINT_SOMETHING_NEW")]
    public void PrintCommands_AreAccepted(string command)
    {
        var raw = $$"""{"command":"{{command}}","terminal_id":1,"html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void NonPrintCommand_IsIgnored_WithMessage()
    {
        var result = PrintJobHandler.Evaluate(
            """{"command":"RELOAD_MENU"}""", Configs(Printer("10.0.0.5")));
        Assert.False(result.ShouldPrint);
        Assert.Equal("Ignored command: RELOAD_MENU", result.Message);
    }

    [Fact]
    public void WrongTerminal_IsSkipped_WithExactMessage()
    {
        var raw = """{"command":"PRINT_RECEIPT","terminal_id":7,"html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5"), selectedDeviceId: 3));
        Assert.False(result.ShouldPrint);
        Assert.Equal("Skipped — terminal_id 7 ≠ this device 3", result.Message);
    }

    [Fact]
    public void NullTerminalId_IsHandledByEveryone()
    {
        var raw = """{"command":"PRINT_RECEIPT","terminal_id":null,"html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5"), selectedDeviceId: 3));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void EmptyHtml_Fails()
    {
        var raw = """{"command":"PRINT_RECEIPT","terminal_id":1,"html":""}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.False(result.ShouldPrint);
        Assert.Equal("Print job has empty HTML", result.Message);
    }

    [Fact]
    public void HtmlUnderUppercaseKey_IsAccepted()
    {
        var raw = """{"command":"PRINT_RECEIPT","HTML":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
        Assert.Equal("<p>x</p>", result.Html);
    }

    [Fact]
    public void DepartmentJob_UsesDepartmentPrinter()
    {
        var configs = Configs(Printer("10.0.0.5"), new[] { (4, (PrintConfig?)Printer("10.0.0.9", 9101)) });
        var raw = """{"command":"PRINT_KOT","department_id":4,"html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, configs);
        Assert.True(result.ShouldPrint);
        Assert.Equal("10.0.0.9", result.Printer!.Ip);
        Assert.Equal(9101, result.Printer.Port);
    }

    [Fact]
    public void DepartmentWithoutPrinter_FallsBackToTerminal()
    {
        var configs = Configs(Printer("10.0.0.5"), new[] { (4, (PrintConfig?)null) });
        var raw = """{"command":"PRINT_KOT","department_id":4,"html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, configs);
        Assert.True(result.ShouldPrint);
        Assert.Equal("10.0.0.5", result.Printer!.Ip);
    }

    [Fact]
    public void NoPrinterAnywhere_FailsWithExactMessage()
    {
        var raw = """{"command":"PRINT_RECEIPT","html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(terminal: null));
        Assert.False(result.ShouldPrint);
        Assert.Equal("No printer configured. Set IP for terminal/department in middleware first.", result.Message);
    }

    [Fact]
    public void Payload_WrappedInData_IsUnwrapped()
    {
        var raw = """{"data":{"command":"PRINT_RECEIPT","html":"<p>x</p>"}}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void Payload_WrappedInDataAsJsonString_IsUnwrapped()
    {
        var raw = """{"data":"{\"command\":\"PRINT_RECEIPT\",\"html\":\"<p>x</p>\"}"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void Payload_WrappedInMessage_IsUnwrapped()
    {
        var raw = """{"message":{"command":"PRINT_RECEIPT","html":"<p>x</p>"}}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void Payload_AsDoubleEncodedJsonString_IsUnwrapped()
    {
        // Pusher sometimes hands over the event data as a JSON-encoded string.
        var raw = "\"{\\\"command\\\":\\\"PRINT_RECEIPT\\\",\\\"html\\\":\\\"<p>x</p>\\\"}\"";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
    }

    [Fact]
    public void Garbage_IsIgnoredGracefully()
    {
        var result = PrintJobHandler.Evaluate("not json at all", Configs(Printer("10.0.0.5")));
        Assert.False(result.ShouldPrint);
        Assert.StartsWith("Ignored command:", result.Message);
    }

    [Theory]
    [InlineData("PRINT_RECEIPT", true)]
    [InlineData("print_receipt", true)]
    [InlineData("PRINT_KOT", false)]
    [InlineData("PRINT", false)]
    [InlineData("PRINT_SOMETHING", false)]
    [InlineData(null, false)]
    public void ShouldOpenCashbox_OnlyForPrintReceipt(string? command, bool expected)
    {
        Assert.Equal(expected, PrintJobHandler.ShouldOpenCashbox(command));
    }

    [Fact]
    public void PrintReceipt_SetsOpenCashboxFlag()
    {
        var raw = """{"command":"PRINT_RECEIPT","html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
        Assert.True(result.OpenCashbox);
    }

    [Fact]
    public void PrintKot_DoesNotOpenCashbox()
    {
        var raw = """{"command":"PRINT_KOT","html":"<p>x</p>"}""";
        var result = PrintJobHandler.Evaluate(raw, Configs(Printer("10.0.0.5")));
        Assert.True(result.ShouldPrint);
        Assert.False(result.OpenCashbox);
    }
}
