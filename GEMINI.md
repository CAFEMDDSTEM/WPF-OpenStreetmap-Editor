# AI Project Instructions

These instructions apply to the whole repository. Follow them before changing code, reviewing issues, writing pull requests, or preparing commits.

## Core Standards

- Inspect the relevant project context before changing code. Treat issue reports as claims to verify, not facts to accept blindly.
- At the start of each task, ask the maintainer whether to enable multi-agent parallel work.
- If multi-agent work is enabled, start an audit agent alongside implementation to review the code with the strictest, longest-term maintainability perspective.
- Prefer the simplest practical solution that fits the surrounding design. Use existing patterns before adding new abstractions.
- Do not create or expand god files, god classes, or catch-all managers. Keep files focused on one clear responsibility; split unrelated UI, state, serialization, I/O, and business logic into focused existing or new types.
- Keep changes focused. Avoid unrelated formatting, cleanup, generated files, or metadata churn unless required for the task.

## C# Style

- Use K&R brace style: opening braces stay on the same line for namespaces, types, methods, properties, lambdas, and control blocks.
- Write readable, idiomatic C# that matches the repository's existing style.
- Add comments only for intent, constraints, edge cases, security assumptions, or performance tradeoffs. Remove stale comments when changing related code.
- Do not add illegal content, abuse workflows, bypasses, or instructions.

## UI and Platform

- Use existing project resources, theme tokens, system brushes, or established palettes. Do not invent ad hoc colors.
- If a new color is necessary, add it as a named resource and verify contrast, light/dark theme behavior, and high-contrast accessibility.
- Avoid assumptions about screen size, DPI, admin privileges, network availability, hardware speed, case-insensitive file systems, or Windows path separators unless the WPF boundary requires them.
- Keep platform-specific behavior isolated and documented. Prefer portable .NET APIs for non-UI logic.

## Safety

- Before acting, check whether the request is destructive, illegal, unsafe, or malicious.
- Treat file deletion, overwrites, migrations, credential changes, force pushes, resets, network exfiltration, security bypasses, and irreversible operations as destructive until proven otherwise.
- Require explicit user confirmation before destructive or data-loss-prone actions.
- Refuse illegal, unsafe, malicious, or abuse-enabling requests. Provide a safe alternative when possible.

## Testing and Verification

- Add focused unit tests for new behavior unless there is a clear technical reason they cannot be unit tested.
- Run the relevant test suite before claiming a change works. Prefer `dotnet test WPF-OpenStreetmap-Editor.slnx` when practical.
- If tests cannot be run, state exactly what was not run and why. Do not present unverified behavior as verified.

## Security, Pull Requests, and Commits

- Verify bug and security claims against the code before implementing or describing them as facts.
- Security fixes must be narrow, tested, and explained in terms of verified reachability and impact.
- Separate verified facts from assumptions, risks, and follow-up work in PR descriptions, commit messages, and review comments.
- Do not claim that a bug, vulnerability, performance issue, or feature is fixed unless code inspection and tests or another explicit verification step support the claim.
