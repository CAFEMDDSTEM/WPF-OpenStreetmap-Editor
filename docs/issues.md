# Issues and Bug Reports

Use GitHub Issues to track defects, feature requests, documentation work, and follow-up engineering tasks.

## Before opening an issue

- Search existing open and closed issues.
- Reproduce bugs on the latest target branch when possible.
- Remove access tokens, private map URLs, API keys, and personal data from logs or screenshots.
- Keep one issue focused on one problem or request.

## Issue titles

Use a short conventional prefix:

```text
bug: tiles fail to render for TMS layer
feat: add layer opacity control
docs: document custom tile URL templates
test: cover Web Mercator edge cases
ci: publish test result artifacts
```

## Bug reports

Bug reports must include:

- Summary of the defect
- Exact steps to reproduce
- Expected behavior
- Actual behavior
- Environment details: OS, .NET SDK/runtime, branch or commit, and run mode
- Relevant logs, screenshots, or stack traces when available

Good bug reports make the failure reproducible from a clean app launch.

## Feature requests and tasks

Feature requests should include:

- Problem or user workflow
- Proposed solution
- Alternatives or workarounds considered
- Acceptance criteria
- Testing or documentation expectations

## Security and private data

Do not post secrets in public issues. Remove access tokens and private service URLs from `tile_requests.log`, screenshots, terminal output, and crash reports.

If an issue cannot be described without private data, open a minimal public tracking issue and share the sensitive details through an appropriate private channel.
