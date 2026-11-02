using System.Text;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>QA phase 17, P17-D05: what the CSP report endpoint keeps of an anonymous, attacker-controlled body.</summary>
public sealed class CspReportTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void LegacyReport_KeepsOnlyWhitelistedFields_WithoutQueryOrFragment()
    {
        var list = CspReportController.Summarize(B("""
            {"csp-report":{"document-uri":"https://hudhudnest.com/auth/reset?token=SECRET#x","blocked-uri":"https://evil.example/a.js?k=1",
             "effective-directive":"script-src-elem","disposition":"report","script-sample":"alert(1)","original-policy":"x","referrer":"https://r"}}
            """));

        var one = Assert.Single(list);
        Assert.Equal("https://hudhudnest.com/auth/reset", one.Document);
        Assert.Equal("https://evil.example/a.js", one.Blocked);
        Assert.Equal("script-src-elem", one.Directive);
        Assert.Equal("report", one.Disposition);
    }

    [Fact]
    public void ReportingApiArray_IsAccepted_AndCappedAtTen()
    {
        var items = string.Join(",", Enumerable.Range(0, 25).Select(i =>
            "{\"type\":\"csp-violation\",\"body\":{\"documentURL\":\"https://h/" + i + "\",\"blockedURL\":\"inline\",\"effectiveDirective\":\"script-src\",\"disposition\":\"report\"}}"));
        Assert.Equal(10, CspReportController.Summarize(B("[" + items + "]")).Count);
    }

    [Fact]
    public void ControlCharacters_CannotForgeLogLines()
    {
        var list = CspReportController.Summarize(B("{\"csp-report\":{\"blocked-uri\":\"inline\\nINFO fake line\\r\\n\",\"effective-directive\":\"a\\u0000b\"}}"));
        var one = Assert.Single(list);
        Assert.DoesNotContain('\n', one.Blocked);
        Assert.DoesNotContain('\r', one.Blocked);
        Assert.DoesNotContain('\0', one.Directive);
    }

    [Fact]
    public void LongValues_AreCut()
    {
        Assert.Equal(200, CspReportController.Clean(new string('a', 5000)).Length);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"csp-report\":\"str\"}")]
    public void Garbage_GivesNothing(string body)
    {
        Assert.Empty(CspReportController.Summarize(B(body)));
    }
}
