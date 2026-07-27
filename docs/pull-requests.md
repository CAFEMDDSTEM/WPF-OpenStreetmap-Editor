# Pull Requests

Pull requests should be small enough to review with confidence and complete enough to merge without hidden follow-up work.

## Before opening a PR

- Link the related issue when one exists.
- Keep unrelated refactors out of feature or bug-fix PRs.
- Update tests for changed logic.
- Update documentation for changed setup, behavior, CLI commands, or workflows.
- Remove secrets, access tokens, private service URLs, local paths, and generated logs.

## PR title format

Use a short conventional prefix:

```text
fix: handle TMS y-axis conversion
feat: add layer opacity control
docs: add bug report guidance
test: cover tile URL parsing
ci: run tests on pull requests
refactor: centralize runtime paths
```

## PR description

Include:

- Summary of what changed
- Linked issue or motivation
- Validation commands and results
- Screenshots or recordings for UI changes
- Notes about risk, compatibility, or follow-up work

## Review expectations

Reviewers should focus on correctness, user-visible behavior, test coverage, maintainability, security of logs/configuration, and consistency with the project style guide.

Authors should respond by either updating the PR or explaining the tradeoff. Resolve review threads only after the underlying concern is addressed or explicitly accepted.

## Merge readiness

A PR is ready to merge when:

- CI passes.
- Required tests pass locally or in CI.
- The PR template checklist is complete.
- User-facing changes are documented.
- The code follows the K&R brace style required by this project.
