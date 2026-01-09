# DimonSmart.DynamicLeakAnalyzer

Roslyn analyzer that detects unintended dynamic leaks: implicit conversions from `dynamic` to static types (DSM001) and `var` inferred as `dynamic` (DSM002).

## Install

```xml
<ItemGroup>
  <PackageReference Include="DimonSmart.DynamicLeakAnalyzer" Version="0.1.0" PrivateAssets="all" />
</ItemGroup>
```

## Strict .editorconfig

Recommended settings to surface leaks early:

```ini
root = true

[*.cs]
# Report dynamic -> object (disabled by default)
dsmdynamicleak_ignore_dynamic_to_object = false

# Analyze generated code if you want it included (default false)
dsmdynamicleak_analyze_generated_code = false

# Standard severity control
# dotnet_diagnostic.DSM001.severity = warning
# dotnet_diagnostic.DSM002.severity = warning
```

## Suppress allowed zones

Use the attribute to allow dynamic usage inside specific members or types:

```csharp
using DimonSmart;

[DimonSmartAllowDynamicLeak]
public int Parse(dynamic row) => row.Value;
```

Suppression applies to DSM001 and DSM002 inside the attributed member or type.

## Examples

DSM001 - implicit dynamic conversion:

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

DSM002 - `var` inferred as `dynamic`:

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

## Recommended style

Allow `dynamic` in boundary layers (interop, JSON/DB adapters, integration code), but stop leaks before they reach core domain logic.
