using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

using Xunit;

using AnalyzerUnderTest = global::DimonSmart.DynamicLeakAnalyzer.DynamicLeakAnalyzer;

namespace DimonSmart.DynamicLeakAnalyzer.Tests;

public sealed class DynamicLeakAnalyzerTests
{
    [Fact]
    public async Task Dsm001_ReturnDynamicToInt()
    {
        const string source = @"
class C
{
    int M(dynamic d)
    {
        return {|#0:d|};
    }
}
";

        var expected = new DiagnosticResult(AnalyzerUnderTest.Dsm001, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("int");

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Dsm001_AssignmentDynamicToInt()
    {
        const string source = @"
class C
{
    void M(dynamic d)
    {
        int x = {|#0:d|};
    }
}
";

        var expected = new DiagnosticResult(AnalyzerUnderTest.Dsm001, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("int");

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Dsm001_ArgumentPassingDynamicToInt()
    {
        const string source = @"
class C
{
    void Foo(int x) { }

    void M(dynamic d)
    {
        Foo({|#0:d|});
    }
}
";

        var expected = new DiagnosticResult(AnalyzerUnderTest.Dsm001, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("int");

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Dsm001_ExplicitCastDoesNotReport()
    {
        const string source = @"
class C
{
    int M(dynamic d)
    {
        return (int)d;
    }
}
";

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Dsm001_AllowedAttributeSuppressesDiagnostics()
    {
        const string source = @"
using DimonSmart;

class C
{
    [DimonSmartAllowDynamicLeak]
    int M(dynamic d)
    {
        return d;
    }
}
";

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Dsm001_DynamicToObjectIgnoredByDefault()
    {
        const string source = @"
class C
{
    object M(dynamic d)
    {
        return d;
    }
}
";

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Dsm001_DynamicToObjectReportsWhenEnabled()
    {
        const string source = @"
class C
{
    object M(dynamic d)
    {
        return {|#0:d|};
    }
}
";

        const string editorConfig = @"
root = true

[*.cs]
dsmdynamicleak_ignore_dynamic_to_object = false
";

        var expected = new DiagnosticResult(AnalyzerUnderTest.Dsm001, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("object");

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerWithConfigAsync(source, editorConfig, expected);
    }

    [Fact]
    public async Task Dsm002_VarInferredAsDynamic()
    {
        const string source = @"
class C
{
    void M(dynamic d)
    {
        {|#0:var|} x = d;
    }
}
";

        var expected = new DiagnosticResult(AnalyzerUnderTest.Dsm002, DiagnosticSeverity.Warning)
            .WithLocation(0);

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Dsm002_DynamicExplicitDoesNotReport()
    {
        const string source = @"
class C
{
    void M(dynamic d)
    {
        dynamic x = d;
    }
}
";

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Dsm002_AllowedAttributeSuppressesDiagnostics()
    {
        const string source = @"
using DimonSmart;

class C
{
    [DimonSmartAllowDynamicLeak]
    void M(dynamic d)
    {
        var x = d;
    }
}
";

        await AnalyzerVerifier<AnalyzerUnderTest>.VerifyAnalyzerAsync(source);
    }
}