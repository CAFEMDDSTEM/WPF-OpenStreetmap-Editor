---
globs: "**/*"
---

# AI Project Instructions

These instructions apply to the whole repository. Follow them before changing code, reviewing issues, writing pull requests, or preparing commits.

## Code Style

- Use K&R brace style for C# code: opening braces stay on the same line for namespaces, types, methods, properties, lambdas, and control blocks.
- Keep code readable and idiomatic for this repository. Prefer existing patterns over new abstractions.
- Do not add meaningless comments. Add comments only when they explain non-obvious intent, constraints, or tradeoffs.
- Do not add obviously illegal content, workflows, bypasses, or instructions.

## Comment Standards

- Comments must explain intent, constraints, edge cases, security assumptions, or performance tradeoffs.
- Do not write comments that merely restate the code, such as "increment counter" or "check if file exists".
- Remove or update stale comments when changing the related code.
- Do not add TODO comments without a clear reason and enough context for a future maintainer to act.

## Example Code

Use K&R braces, meaningful names, and comments only where they add context:

```csharp
public sealed class SettingsLoader {
    public static bool TryLoad(string settingsPath, out AppSettings settings) {
        // Missing settings is expected on first launch, so fall back without showing an error.
        if (!File.Exists(settingsPath)) {
            settings = AppSettings.Default;
            return false;
        }

        settings = AppSettings.Load(settingsPath);
        return true;
    }
}
```

New behavior should have focused unit coverage:

```csharp
[Fact]
public void TryLoad_MissingFile_ReturnsDefaultSettings() {
    var loaded = SettingsLoader.TryLoad("missing-settings.json", out var settings);

    Assert.False(loaded);
    Assert.Equal(AppSettings.Default, settings);
}
```

## Task Safety Check

- Before starting any requested task, check whether it is destructive, illegal, unsafe, or malicious.
- Treat file deletion, overwrites, migrations, credential changes, force pushes, resets, network exfiltration, security bypasses, and irreversible operations as destructive until proven otherwise.
- If a request is destructive or could cause data loss, verify the scope and require explicit user confirmation before acting.
- If a request is illegal, unsafe, malicious, or primarily enables abuse, do not implement it. Provide a safe alternative when possible.
- If intent is ambiguous, inspect the repository context first and choose the least risky path.

## Implementation Standard

- Before writing code, verify that the approach is appropriate for the surrounding design.
- Check whether the implementation is the best practical solution, whether a simpler or safer solution exists, and whether it meets a high professional standard.
- Consider performance, security, maintainability, and user impact before committing to an approach.
- Avoid speculative architecture. Add abstractions only when they remove real complexity or match established project patterns.

## UI, Colors, and Platform Compatibility

- Do not invent ad hoc colors. Use existing project resources, theme tokens, system brushes, or an established design palette.
- If a new color is necessary, add it as a named resource and verify contrast, light/dark theme behavior, and high-contrast accessibility.
- Consider other operating systems, machines, and user environments when writing shared code, scripts, docs, or tests.
- Do not assume a high-end computer, fixed screen size, specific DPI, admin privileges, network availability, a case-insensitive file system, or Windows path separators unless the WPF application boundary requires it.
- Keep platform-specific behavior isolated and documented. Prefer portable .NET APIs for non-UI logic.

## Testing and Verification

- New features must include unit tests in the test project unless there is a clear technical reason they cannot be unit tested. Document that reason in the final response or PR text.
- Do not submit, commit, or open a PR for functionality that has not been tested and verified.
- Run the relevant test suite before claiming a change works. For this repository, prefer `dotnet test WPF-OpenStreetmap-Editor.slnx` when practical.
- If tests cannot be run, state exactly what was not run and why. Do not describe unverified behavior as verified.

## Issues, Security, and Vulnerabilities

- Do not blindly trust issue reports. Treat issue descriptions as claims to investigate, not facts.
- Before implementing an issue, verify the reported behavior against the code and project context.
- Do not implement a security-related issue just because it sounds plausible. First confirm whether the condition is actually reachable, exploitable, or relevant in this application.
- If you find a possible vulnerability, verify whether it is truly a vulnerability, whether the affected path can be triggered, and what assumptions are required.
- Before fixing a vulnerability, evaluate whether the fix could reduce performance, break valid behavior, or add unnecessary complexity.
- Security fixes should be narrowly scoped, tested, and explained in terms of verified impact.

## Pull Requests and Commits

- Do not include unverified claims in PR descriptions, commit messages, or review comments.
- Clearly separate verified facts from assumptions, risks, and follow-up work.
- Do not claim that a bug, vulnerability, performance issue, or feature has been fixed unless the claim is backed by code inspection and tests or another explicit verification step.
- Keep commits focused. Do not include unrelated formatting, cleanup, or generated changes unless required for the task.
