# DimonSmart.DynamicLeakAnalyzer

`dynamic` is not a type. It is a trap door.

You add it for a quick win (often because Dapper tutorials do it), and a week later you are debugging a hot path that suddenly does runtime binding, silent boxing, and extra GC work. The worst part is that method signatures can still look perfectly static.

This repo contains a Roslyn analyzer that detects unintended **dynamic leaks** before they spread.

- **DSM001**: a `dynamic` expression is used where a static type is expected (return, assignment, argument, etc.). It compiles, but the conversion happens at runtime.
- **DSM002**: `var` is inferred as `dynamic`. It looks harmless. It is not.

## The leak, in one screenshot worth of code

Dapper makes this super easy to write. Many examples show `Query<dynamic>()`, `QueryFirst()` without `<T>`, or just `var row = ...` and then `row.Id`. See for example:
- Dapper dynamic types overview and examples: https://conradakunga.com/blog/dapper-part-9-using-dynamic-types/
- QueryFirst / QuerySingle articles that mention mapping to a dynamic object: https://www.learndapper.com/dapper-query/selecting-single-rows
- Dynamic query walkthroughs: https://dev.to/shps951023/trace-dapper-net-source-code-38hm

Now the fun part:

```csharp
using Dapper;
using System.Data;

public static class Repo
{
    // Looks safe: returns int.
    public static int GetActiveUserId(IDbConnection cn)
    {
        // QueryFirst() without <T> returns a dynamic row (DapperRow).
        var row = cn.QueryFirst("select Id from Users where IsActive = 1");

        // Surprise: row.Id is dynamic.
        // The conversion to int happens at runtime.
        return row.Id; // DSM001
    }
}
```

This is exactly the kind of code that passes reviews because the signature screams "int".  
But inside, `dynamic` already entered the building.

Why do you care?

- `dynamic` is `object` at runtime.
- Value types flowing through `dynamic` tend to be boxed and unboxed as the DLR binder does its job.
- Dynamic member access and dynamic arithmetic add runtime work that is invisible in signatures.
- `var` can silently become `dynamic`, and then the leak spreads like a glitter bomb.

This analyzer exists to make that leak loud.

## What it detects

### DSM001: implicit dynamic conversion

Before:

```csharp
int M(dynamic d)
{
    return d; // DSM001
}
```

After:

```csharp
int M(dynamic d)
{
    return (int)d;
}
```

The fix is not "always cast". The fix is "stop the leak at the boundary".  
Sometimes a cast is the right boundary. Sometimes the right answer is "do not use dynamic here".

### DSM002: `var` inferred as `dynamic`

Before:

```csharp
var x = GetValue(dynamicRow); // DSM002
```

After:

```csharp
dynamic x = GetValue(dynamicRow);
// or
int x = (int)GetValue(dynamicRow);
```

If you love `var`, you probably want this rule. It tells you when `var` is not what you think.

## Install

Add the analyzer to your project:

```xml
<ItemGroup>
  <PackageReference Include="DimonSmart.DynamicLeakAnalyzer" Version="1.*" PrivateAssets="all" />
</ItemGroup>
```

If you prefer exact versions, pin it to the latest release tag in this repo.

## Make warnings hurt (turn them into errors)

The simplest and most explicit way is `.editorconfig`:

```editorconfig
root = true

[*.cs]
dotnet_diagnostic.DSM001.severity = error
dotnet_diagnostic.DSM002.severity = error
```

Alternative: you can also treat specific IDs as errors in your project file:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);DSM001;DSM002</WarningsAsErrors>
</PropertyGroup>
```

I recommend `.editorconfig` because it is obvious in PRs and works consistently across IDE and CI.

## Configuration (.editorconfig)

Recommended settings to surface leaks early:

```editorconfig
root = true

[*.cs]
# Report dynamic -> object (disabled by default)
dsmdynamicleak_ignore_dynamic_to_object = false

# Analyze generated code if you want it included (default false)
dsmdynamicleak_analyze_generated_code = false

# Severity control
# dotnet_diagnostic.DSM001.severity = warning
# dotnet_diagnostic.DSM002.severity = warning
```

## Allowed zones (suppression)

Sometimes you really do want `dynamic` in a small, controlled bubble.
Use the attribute to allow dynamic usage inside specific members or types:

```csharp
using DimonSmart;

[DimonSmartAllowDynamicLeak]
public int Parse(dynamic row) => row.Value;
```

Suppression applies to DSM001 and DSM002 inside the attributed member or type.

## Recommended style

Use `dynamic` at the boundary, then kill it immediately.

Good boundary examples:
- COM interop
- JSON adapters
- database adapters (yes, including Dapper dynamic rows)
- integration glue code

Bad place for `dynamic`:
- core domain logic
- hot loops
- libraries that other people consume

If you want Dapper convenience without dynamic leaks, prefer:
- `Query<T>()`, `QuerySingle<T>()`, `QueryFirst<T>()`
- mapping to a small DTO record
- explicit casting right after the query, once, at the boundary

## Samples

See `samples/DemoApp` for minimal repros and fixes.

## License

MIT
