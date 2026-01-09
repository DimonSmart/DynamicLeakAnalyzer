using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace DimonSmart.DynamicLeakAnalyzer.Tests;

public static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    private const string AllowAttributeSource = @"
namespace DimonSmart
{
    [System.AttributeUsage(
        System.AttributeTargets.Class |
        System.AttributeTargets.Method |
        System.AttributeTargets.Property |
        System.AttributeTargets.Constructor,
        Inherited = true,
        AllowMultiple = false)]
    public sealed class DimonSmartAllowDynamicLeakAttribute : System.Attribute
    {
    }
}
";

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source, editorConfig: null);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync().ConfigureAwait(false);
    }

    public static async Task VerifyAnalyzerWithConfigAsync(string source, string editorConfig, params DiagnosticResult[] expected)
    {
        var test = CreateTest(source, editorConfig);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync().ConfigureAwait(false);
    }

    private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest(string source, string? editorConfig)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };

        test.TestState.Sources.Add(AllowAttributeSource);

        if (!string.IsNullOrWhiteSpace(editorConfig))
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));

        return test;
    }
}