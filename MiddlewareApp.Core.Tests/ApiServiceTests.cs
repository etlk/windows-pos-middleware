using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class ApiServiceTests
{
    [Fact]
    public void DescribeFailure_SingleException_ReturnsItsMessage()
    {
        var ex = new HttpRequestException("No such host is known.");
        Assert.Equal("No such host is known.", ApiService.DescribeFailure(ex));
    }

    [Fact]
    public void DescribeFailure_IncludesInnerExceptionChain()
    {
        var inner = new Exception("The remote certificate is invalid because of errors in the certificate chain: NotTimeValid");
        var middle = new Exception("The SSL connection could not be established, see inner exception.", inner);
        var outer = new HttpRequestException("The SSL connection could not be established, see inner exception.", middle);

        var result = ApiService.DescribeFailure(outer);

        Assert.Equal(
            "The SSL connection could not be established, see inner exception. — " +
            "The remote certificate is invalid because of errors in the certificate chain: NotTimeValid",
            result);
    }

    [Fact]
    public void DescribeFailure_SkipsConsecutiveDuplicateMessages()
    {
        var inner = new Exception("same message");
        var outer = new Exception("same message", inner);
        Assert.Equal("same message", ApiService.DescribeFailure(outer));
    }

    [Fact]
    public void DescribeFailure_AllMessagesEmpty_FallsBackToTypeName()
    {
        var ex = new HttpRequestException("");
        Assert.Equal(nameof(HttpRequestException), ApiService.DescribeFailure(ex));
    }
}
