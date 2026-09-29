---
name: csharp-comment-headers
description: Use when creating or modifying any C# file in AterraEngine; apply the repository's required Imports, namespace, Code, and member-section comment headers.
---

# C# Comment Headers

Every generated or edited `.cs` file in this repository must use the project's
comment-header convention. Apply it before writing the rest of the file.

## File Header

Start the file with an `Imports` section. Keep the separator exactly as shown:

```csharp
// ---------------------------------------------------------------------------------------------------------------------
// Imports
// ---------------------------------------------------------------------------------------------------------------------
using System.Collections.Immutable;

namespace AterraEngine.Example;
// ---------------------------------------------------------------------------------------------------------------------
// Code
// ---------------------------------------------------------------------------------------------------------------------
public sealed class Example {
}
```

Rules:

- Use `// ---------------------------------------------------------------------------------------------------------------------` for every separator line.
- Put `// Imports` between the first two separators.
- Put all `using` directives below `// Imports`, followed by one blank line.
- Put the file-scoped namespace after the imports when the file uses one.
- Put one blank line after the namespace, then the `// Code` separator block.
- Put the first type or other code declaration immediately after the `Code` block.
- Do not replace this convention with a file banner, copyright notice, or XML documentation header.
- If there are no imports, keep the `Imports` block and place the namespace directly after it.

## Member Sections

For substantial type members, use the same separator style to label logical
sections:

```csharp
// -----------------------------------------------------------------------------------------------------------------
// Constructors
// -----------------------------------------------------------------------------------------------------------------
```

Use concise section names such as `Fields`, `Properties`, `Constructors`,
`Methods`, or `Operators`. Keep one blank line before each section. Do not add
section headers for a trivial type where they would add noise.

When modifying an existing C# file, preserve its existing header and section
style rather than introducing a different formatting scheme.
