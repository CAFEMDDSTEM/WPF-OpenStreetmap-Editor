# Code Style

This project uses C# with file-scoped namespaces, nullable reference types, implicit usings, and K&R brace style.

## K&R braces

Opening braces stay on the same line as the declaration or statement:

```csharp
public class TileService {
    public string BuildTileUrl(int z, int x, int y, string? accessToken) {
        if (string.IsNullOrEmpty(TileTemplate)) {
            throw new InvalidOperationException("Tile template is not set");
        } else {
            return TileTemplate;
        }
    }
}
```

Do not use Allman-style braces:

```csharp
public class TileService
{
    public string BuildTileUrl()
    {
        return string.Empty;
    }
}
```

## General C# rules

- Prefer file-scoped namespaces.
- Keep nullable annotations enabled and fix nullability warnings in touched code.
- Prefer `var` when the type is obvious from the right-hand side.
- Keep methods focused on one behavior.
- Avoid unrelated formatting churn in PRs.
- Use structured APIs for paths, JSON, HTTP, and XML instead of ad hoc string parsing where practical.

## Formatting enforcement

The repository includes [.editorconfig](../.editorconfig) so IDEs and `dotnet format` can apply the project style consistently.

Before submitting a PR, run:

```powershell
dotnet format WPF-OpenStreetmap-Editor.slnx
.\scripts\build.ps1
.\scripts\test.ps1
```
