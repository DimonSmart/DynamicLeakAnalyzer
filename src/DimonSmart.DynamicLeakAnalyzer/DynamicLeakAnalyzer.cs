using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DimonSmart.DynamicLeakAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DynamicLeakAnalyzer : DiagnosticAnalyzer
{
    public const string Dsm001 = "DSM001";
    public const string Dsm002 = "DSM002";

    private const string AllowAttributeFullName = "DimonSmart.DimonSmartAllowDynamicLeakAttribute";
    private const string OptionIgnoreDynamicToObject = "dsmdynamicleak_ignore_dynamic_to_object";
    private const string OptionAnalyzeGeneratedCode = "dsmdynamicleak_analyze_generated_code";

    private static readonly DiagnosticDescriptor RuleDsm001 = new(
        id: Dsm001,
        title: "Implicit conversion from dynamic",
        messageFormat: "Implicit conversion from 'dynamic' to '{0}'. Add an explicit cast or stop dynamic earlier.",
        category: "Typing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RuleDsm002 = new(
        id: Dsm002,
        title: "var is inferred as dynamic",
        messageFormat: "'var' is inferred as 'dynamic'. Consider writing 'dynamic' explicitly or casting to a static type.",
        category: "Typing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ConcurrentDictionary<SyntaxTree, bool> GeneratedCodeCache = new();

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleDsm001, RuleDsm002);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeImplicitDynamicConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeInvocationArguments, OperationKind.Invocation);
        context.RegisterSyntaxNodeAction(AnalyzeDynamicInvocationArguments, SyntaxKind.InvocationExpression);
        context.RegisterOperationAction(AnalyzeVarInferredDynamic, OperationKind.VariableDeclarator);
    }

    private static void AnalyzeImplicitDynamicConversion(OperationAnalysisContext context)
    {
        if (ShouldSkip(context))
            return;

        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit)
            return;

        if (conversion.Operand?.Type?.TypeKind != TypeKind.Dynamic)
            return;

        if (conversion.Type is null || conversion.Type.TypeKind == TypeKind.Dynamic)
            return;

        if (conversion.Type.SpecialType == SpecialType.System_Object &&
            GetBoolOption(context, OptionIgnoreDynamicToObject, defaultValue: true))
            return;

        if (conversion.Syntax.Parent is ArgumentSyntax)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                RuleDsm001,
                conversion.Syntax.GetLocation(),
                conversion.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static void AnalyzeVarInferredDynamic(OperationAnalysisContext context)
    {
        if (ShouldSkip(context))
            return;

        var declarator = (IVariableDeclaratorOperation)context.Operation;
        if (declarator.Symbol is not ILocalSymbol local)
            return;

        if (local.Type.TypeKind != TypeKind.Dynamic)
            return;

        if (declarator.Syntax is not VariableDeclaratorSyntax variableDeclaratorSyntax)
            return;

        if (variableDeclaratorSyntax.Parent?.Parent is not LocalDeclarationStatementSyntax localDeclarationStatement)
            return;

        if (localDeclarationStatement.Declaration.Type is not IdentifierNameSyntax typeIdentifier ||
            !string.Equals(typeIdentifier.Identifier.ValueText, "var", StringComparison.Ordinal))
            return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDsm002, localDeclarationStatement.Declaration.Type.GetLocation()));
    }

    private static void AnalyzeInvocationArguments(OperationAnalysisContext context)
    {
        if (ShouldSkip(context))
            return;

        var invocation = (IInvocationOperation)context.Operation;
        foreach (var argument in invocation.Arguments)
        {
            var parameterType = argument.Parameter?.Type;
            if (parameterType is null || parameterType.TypeKind == TypeKind.Dynamic)
                continue;

            if (argument.Value is null)
                continue;

            if (argument.Value is IConversionOperation explicitConversion && !explicitConversion.IsImplicit)
                continue;

            var sourceType = argument.Value.Type;
            if (argument.Value is IConversionOperation conversion)
                sourceType = conversion.Operand?.Type ?? sourceType;

            if (sourceType?.TypeKind != TypeKind.Dynamic)
                continue;

            if (parameterType.SpecialType == SpecialType.System_Object &&
                GetBoolOption(context, OptionIgnoreDynamicToObject, defaultValue: true))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    RuleDsm001,
                    argument.Value.Syntax.GetLocation(),
                    parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeDynamicInvocationArguments(SyntaxNodeAnalysisContext context)
    {
        if (ShouldSkip(context))
            return;

        var invocationSyntax = (InvocationExpressionSyntax)context.Node;
        var invocation = context.SemanticModel.GetOperation(invocationSyntax, context.CancellationToken) as IDynamicInvocationOperation;
        if (invocation is null)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocationSyntax, context.CancellationToken);

        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol is null && symbolInfo.CandidateSymbols.Length == 1)
            methodSymbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;

        if (methodSymbol is null)
            return;

        for (var i = 0; i < invocation.Arguments.Length; i++)
        {
            var argument = invocation.Arguments[i];
            if (argument is null)
                continue;

            IParameterSymbol? parameter = null;
            var argumentName = OperationExtensions.GetArgumentName(invocation, i);
            if (!string.IsNullOrEmpty(argumentName))
            {
                foreach (var candidate in methodSymbol.Parameters)
                {
                    if (string.Equals(candidate.Name, argumentName, StringComparison.Ordinal))
                    {
                        parameter = candidate;
                        break;
                    }
                }
            }
            else if (i < methodSymbol.Parameters.Length)
            {
                parameter = methodSymbol.Parameters[i];
            }

            if (parameter is null || parameter.Type.TypeKind == TypeKind.Dynamic)
                continue;

            var sourceType = argument.Type;
            if (argument is IConversionOperation conversion)
                sourceType = conversion.Operand?.Type ?? sourceType;

            if (sourceType?.TypeKind != TypeKind.Dynamic)
                continue;

            if (parameter.Type.SpecialType == SpecialType.System_Object &&
                GetBoolOption(context, OptionIgnoreDynamicToObject, defaultValue: true))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    RuleDsm001,
                    argument.Syntax.GetLocation(),
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static bool ShouldSkip(OperationAnalysisContext context)
    {
        return ShouldSkip(context.ContainingSymbol, context.Operation.Syntax.SyntaxTree, context.Options);
    }

    private static bool ShouldSkip(SyntaxNodeAnalysisContext context)
    {
        return ShouldSkip(context.ContainingSymbol, context.Node.SyntaxTree, context.Options);
    }

    private static bool ShouldSkip(ISymbol? containingSymbol, SyntaxTree tree, AnalyzerOptions options)
    {
        if (IsSuppressed(containingSymbol))
            return true;

        if (!GetBoolOption(options, tree, OptionAnalyzeGeneratedCode, defaultValue: false) &&
            IsGeneratedCode(tree))
            return true;

        return false;
    }

    private static bool GetBoolOption(OperationAnalysisContext context, string key, bool defaultValue)
    {
        return GetBoolOption(context.Options, context.Operation.Syntax.SyntaxTree, key, defaultValue);
    }

    private static bool GetBoolOption(SyntaxNodeAnalysisContext context, string key, bool defaultValue)
    {
        return GetBoolOption(context.Options, context.Node.SyntaxTree, key, defaultValue);
    }

    private static bool GetBoolOption(AnalyzerOptions options, SyntaxTree tree, string key, bool defaultValue)
    {
        var opts = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        if (opts.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value))
            return value;

        return defaultValue;
    }

    private static bool IsSuppressed(ISymbol? symbol)
    {
        if (symbol is null)
            return false;

        if (HasAllowAttribute(symbol))
            return true;

        if (symbol is IMethodSymbol method && method.AssociatedSymbol is not null && HasAllowAttribute(method.AssociatedSymbol))
            return true;

        var type = symbol.ContainingType;
        while (type is not null)
        {
            if (HasAllowAttribute(type))
                return true;

            type = type.ContainingType;
        }

        return false;
    }

    private static bool HasAllowAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(fullName, "global::" + AllowAttributeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsGeneratedCode(SyntaxTree tree)
    {
        return GeneratedCodeCache.GetOrAdd(tree, static t => ComputeIsGeneratedCode(t));
    }

    private static bool ComputeIsGeneratedCode(SyntaxTree tree)
    {
        var path = tree.FilePath;
        if (!string.IsNullOrEmpty(path))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var text = tree.GetText();
        if (text.Length == 0)
            return false;

        var spanLength = Math.Min(text.Length, 2048);
        var header = text.ToString(new TextSpan(0, spanLength));
        return header.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0 ||
               header.IndexOf("<autogenerated", StringComparison.OrdinalIgnoreCase) >= 0;
    }

}
