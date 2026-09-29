---
name: tunit-maintainer
description: Maintain and update the existing TUnit skill at .agents/skills/tunit/SKILL.md when TUnit, TUnit.Assertions, TUnit.Mocks, Microsoft.Testing.Platform, official documentation, or supported integrations change. Use for version audits, release updates, API/deprecation reviews, and documentation synchronization; do not create a second TUnit knowledge skill.
---

# TUnit Skill Maintenance

Maintain the existing TUnit skill as a living artifact. This skill is a procedure for researching current TUnit behavior, comparing it with the baseline, and making minimal evidence-based edits to `.agents/skills/tunit/SKILL.md`.

Do not replace the baseline with a new summary. Do not create a parallel comprehensive TUnit skill. The maintained artifact is always:

```text
.agents/skills/tunit/SKILL.md
```

## Operating Rules

- Treat the installed project's package version as the immediate compatibility target when maintaining application code.
- Treat the latest stable official TUnit release as the default documentation target when performing a general skill update.
- Preserve guidance that remains valid, even if it predates the current release.
- Prefer current APIs and patterns in the baseline; retain older APIs only in clearly labeled migration/legacy notes when existing projects may still encounter them.
- Never mark an API deprecated, removed, renamed, or behaviorally changed without authoritative evidence.
- Never infer an API from xUnit, NUnit, MSTest, FluentAssertions, Moq, or Microsoft.Testing.Platform alone.
- Never silently upgrade a repository's package versions while validating the skill.
- Use ASCII when editing unless the existing file requires otherwise.
- Use `apply_patch` for manual edits.
- If evidence conflicts, record the conflict and resolve it using the authority order in this skill.
- If the current version cannot be established confidently, stop before changing substantive guidance and report the uncertainty.

## Authority Order

Use sources in this order, while using lower-ranked sources to investigate gaps:

1. The official TUnit documentation at `https://tunit.dev/docs/intro` and its current linked hierarchy.
2. The official TUnit repository at `https://github.com/thomhurst/TUnit`, including source, tests, examples, docs, tags, commits, and release notes.
3. Official NuGet package metadata and package contents for `TUnit`, `TUnit.Core`, `TUnit.Engine`, `TUnit.Assertions`, `TUnit.Mocks`, and relevant integration packages.
4. Official Microsoft.Testing.Platform documentation and source for platform-owned runner, CLI, extension, and reporting behavior.
5. Official generated API/source artifacts from the exact package version under review.

Third-party articles, Stack Overflow, cached snippets, and search results may help locate a topic, but cannot establish current TUnit behavior.

## Version Coverage

At the start of every update, read the baseline completely and extract:

- The TUnit/NuGet version marker.
- The official repository commit or tag, if present.
- The verification date.
- Any beta, experimental, obsolete, or version-sensitive APIs.
- Any statements tied to a specific .NET SDK, C# language version, MTP version, OS, or integration package.

The baseline currently records TUnit `1.71.0`, official repository commit `c496882cee69be430b67172303aaef2fa26d82b8`, and verification date `2026-09-29`. Treat this as baseline metadata, not as the current version for future updates.

At the end of a successful update, retain a concise marker in the baseline:

```markdown
## Version Coverage

This skill was verified against TUnit X.Y.Z.

Last verified: YYYY-MM-DD

Official repository: <commit or tag>

Primary documentation: https://tunit.dev/docs/intro
```

If the baseline already has equivalent metadata, update it in place rather than adding a duplicate section.

## Phase 1: Inspect the Baseline

Before researching changes:

1. Locate the baseline with a glob for `.agents/skills/tunit/SKILL.md`; do not assume the working directory if the skill is being reused elsewhere.
2. Read the entire file, including its final sections. Do not rely on a truncated read.
3. Record its version/date/repository assumptions.
4. Inventory its major sections and quick-reference examples.
5. Search the baseline for version-sensitive markers and likely stale API terms:
   - `obsolete`, `deprecated`, `legacy`, `experimental`, `beta`, `removed`;
   - CLI flags and environment variables;
   - package names and version requirements;
   - attributes, interfaces, assertion names, configuration properties, and generated code examples.
6. Note any known contradictions already documented by the baseline. Do not erase them until the current evidence is checked.

Use the baseline as a change surface map. A claim not mentioned in release notes may still be invalid because its surrounding documentation or source changed.

## Phase 2: Determine the Current Release

Establish the target version with at least two official signals when practical:

1. Read the official docs landing page and `https://tunit.dev/llms.txt`.
2. Inspect official GitHub tags/releases and the default branch's recent commits.
3. Query official NuGet registration or the package index for the latest stable `TUnit` version. Distinguish stable, prerelease, beta, alpha, and pull-request packages.
4. Check official release notes/changelog entries between the baseline version and the target.
5. Record the target package version, date, repository tag/commit, SDK/MTP version used for verification, and whether the target is stable or prerelease.

Do not use a future-dated environment clock as evidence that a package exists. If NuGet, docs, tags, and repository history disagree, investigate the discrepancy and state which version is selected and why.

For a repository-specific update, inspect its `Directory.Packages.props`, project files, lock files, `global.json`, and existing TUnit tests first. The repository's installed version governs code changes; the latest stable release governs a general skill refresh unless the user requests a specific version.

## Phase 3: Build the Change Audit

Create an internal audit before editing. It may be a temporary note and does not need to remain in the repository.

```text
[ ] New features
[ ] Changed APIs/signatures/generic constraints
[ ] Renamed or moved APIs
[ ] Deprecated APIs and replacements
[ ] Removed APIs, with evidence
[ ] Discovery behavior
[ ] Execution/lifecycle behavior
[ ] Parallelism, constraints, ordering
[ ] Data sources and enumeration
[ ] Assertions and assertion analyzers
[ ] Cancellation, timeouts, retry, repeat
[ ] Dependency injection and property injection
[ ] TestContext and output/artifacts
[ ] Configuration precedence and files
[ ] CLI flags and MTP runner behavior
[ ] Reporting, coverage, and CI integrations
[ ] Extension points and package integrations
[ ] Documentation hierarchy/terminology changes
[ ] Code examples and quick references
[ ] Migration guidance
```

For each finding, record:

```text
Topic:
Baseline statement:
Current evidence:
Change kind: new | changed | deprecated | removed | behavior | documentation-only
Affected baseline section/example:
Preferred current pattern:
Legacy/migration note needed: yes/no
Confidence:
Source URLs/paths/commit:
```

Separate documentation changes from runtime changes. A moved page is not an API change; a changed example is not proof of a removed API.

## Phase 4: Review the Documentation Hierarchy

Read `https://tunit.dev/llms.txt` and the repository's current documentation navigation, such as `docs/sidebars.ts`, to enumerate the current hierarchy. Follow links rather than assuming the sidebar is complete.

Review all branches relevant to the baseline, including:

- Getting started and installation.
- Test authoring, lifecycle, hooks, context, data sources, DI, property injection, ordering, skips, culture, and AOT.
- The complete assertion catalog and assertion extensibility.
- TUnit.Mocks and its HTTP/logging/advanced subpackages.
- Execution, filters, parameters, parallelism, cancellation, timeouts, retry, repeat, engine modes, and CI reporting.
- CLI, environment variables, test configuration, programmatic configuration, and analyzer references.
- Extensibility, event receivers, executors, custom data sources, formatters, logging, dynamic tests, coverage, and reusable libraries.
- ASP.NET Core, Aspire, Playwright, FsCheck, F#, file-based C#, tracing, global test IDs, and CI examples.
- HTML reports, aggregation, philosophy, performance, troubleshooting, comparisons, migrations, and benchmarks.

For each branch, compare current terminology, links, examples, defaults, and recommendations with the corresponding baseline sections. If the docs are incomplete, use source/tests/package metadata; do not fill gaps with assumptions from another framework.

## Phase 5: Inspect Release and Source Changes

Inspect official commits/tags between the baseline reference and target. Search commit history by feature/API names and inspect release notes, but do not treat a commit title alone as behavioral proof.

When source verification is needed:

1. Obtain the target tag or commit in a temporary directory outside the workspace.
2. Prefer a shallow clone/fetch; avoid changing the user's repository remotes or branches.
3. Compare baseline and target with `git diff`, `git log`, and targeted searches.
4. Inspect target source, XML documentation, official tests, examples, and package assets.
5. Compile a minimal probe against the exact target package when an API signature or analyzer behavior matters.
6. Run the probe with the target's supported runner and relevant flags.

Use source to answer questions such as:

- Does the type/member/attribute still exist?
- Is it public and available from the package the baseline claims?
- Did the namespace, signature, generic constraint, or lifecycle change?
- Is a deprecation enforced by an obsolete attribute, analyzer, warning, or only documentation?
- Does the current runner register the documented CLI/reporting option?
- Does a behavior claim appear in official tests or engine implementation?

Do not claim removal merely because a symbol is absent from one page. Verify the target package/source and distinguish package-specific APIs from engine/platform APIs.

## Phase 6: Verify Representative Examples

Do not mechanically rewrite every code block. Classify examples:

- **Compile-critical:** attributes, assertions, data sources, hooks, DI, context, executors, mocks, configuration APIs, and custom extensions.
- **Command-critical:** `dotnet` invocation, MTP selection, filters, reporting, coverage, and CI commands.
- **Conceptual:** pseudocode or application-specific examples whose API surface is intentionally illustrative.

For compile-critical and command-critical examples that changed or are version-sensitive:

1. Create a temporary minimal test project using the target version and supported target framework.
2. Compile with nullable/analyzers enabled.
3. Run discovery with `--list-tests` and a minimum expected count where appropriate.
4. Run focused tests and the full probe with normal parallelism.
5. Verify generated artifacts when reporting is involved.
6. For source generation/AOT claims, inspect generated diagnostics and publish/run the intended target when feasible.

When a full probe is impractical, validate the exact symbol through target package metadata/source and label the example as unverified rather than guessing.

Always check:

- Assertions are awaited.
- Test and hook signatures match the target release.
- Data-source arity, typing, factories, and discovery timing are correct.
- Test context access uses the current interface organization.
- Shared fixtures and constraints match current parallel semantics.
- CLI syntax matches the selected SDK/MTP mode.
- Report flags are registered by the referenced packages.

## Phase 7: Decide Whether to Edit

Make the smallest correct update.

If no meaningful behavior/API/documentation change affects the baseline, update only version/date/repository metadata when justified. Do not reformat, reorder, or rewrite a working skill for stylistic reasons.

Edit the baseline when evidence shows a change in:

- Preferred API or current recommendation.
- Public API/signature/namespace/generic constraint.
- Analyzer/compiler behavior.
- Default behavior or lifecycle/execution semantics.
- Supported CLI/configuration/reporting.
- Official integration package behavior.
- Documentation-supported feature coverage.
- Example validity.

Remove obsolete guidance when the API is actually removed or the old pattern would cause current code to fail. Preserve useful historical guidance only when it helps maintain older projects.

## Legacy and Deprecation Policy

Never present a deprecated or legacy API as the default current approach.

Use this pattern when historical knowledge matters:

```markdown
### Legacy: `OldApi`

`OldApi` was used before TUnit X.Y. It remains relevant when maintaining projects pinned to older packages.

For new code and projects on TUnit X.Y+, use `NewApi`.

Do not introduce `OldApi` in new tests.
```

For each deprecation, include the replacement, affected version if known, whether the old API still compiles, and the migration trigger. For removals, delete normal authoring examples and retain only concise migration information if older projects may require it.

Do not call a compatibility alias deprecated unless an official source says so. Do not call a feature removed unless target source/package evidence proves it is unavailable.

## Updating the Existing Skill

Edit `.agents/skills/tunit/SKILL.md` in place. Keep its established structure unless the current docs require a new conceptual section.

Update the affected locations, not just the version header:

- Version metadata and maintenance notes.
- Project setup and package guidance.
- Core execution/discovery model.
- Assertion catalog and assertion extensibility.
- Data-source choice tables and examples.
- DI, property injection, lifecycle, hooks, events, and context.
- Parallelism, constraints, dependencies, ordering, and isolation.
- Cancellation, timeouts, retries, repeats, skips, and filtering.
- Configuration, environment variables, CLI, MTP, reporting, coverage, and CI.
- Extension points, integrations, mocks, and advanced features.
- Migration mappings and legacy notes.
- Common AI mistakes, debugging, review checklist, validation commands, and quick reference.

When several sections mention the same API or default, update every occurrence. Search for the old name, flag, version, package, and recommendation after patching.

Do not add a second complete TUnit catalog to `tunit-maintainer`. This updater contains process knowledge; the `tunit` skill contains TUnit usage knowledge.

## Internal Consistency Review

After editing, read the complete baseline back. Search for:

- Old and new names appearing together without labels.
- Contradictory version/date/commit markers.
- Examples using an API described as removed or deprecated.
- Commands incompatible with the documented MTP/SDK mode.
- A recommendation that conflicts with a later pitfall.
- An example that injects or accesses context differently from the core model.
- Defaults repeated with different values.
- A package feature described as if it were built into another package.
- Historical APIs presented outside migration sections.

Use a temporary script or `rg`/Grep for exact stale identifiers. Do not change unrelated user edits in the worktree.

## Maintenance Notes

Keep a short change history in the baseline only when it helps future audits. Include the target version/date and material changes, for example:

```markdown
## Maintenance Notes

- Updated for TUnit X.Y.Z on YYYY-MM-DD.
- Replaced deprecated `OldApi` guidance with `NewApi`.
- Added `FeatureName` and updated MTP command examples.
```

Do not turn the history into a release-note archive. The official release notes and repository remain the detailed history.

## Major Release Procedure

For a major release, do not patch only the APIs named in the changelog:

1. Identify breaking changes and official migration guidance.
2. Compare the complete baseline section inventory with the current docs hierarchy.
3. Re-evaluate core assumptions first: discovery, invocation, lifecycle, parallelism, context, assertions, packages, and runner.
4. Re-check every dependent section and quick-reference example.
5. Update preferred current patterns.
6. Preserve concise legacy guidance for older projects.
7. Validate representative examples against the target package.
8. Perform a second full consistency review.

Major version changes can alter seemingly unrelated defaults, generated code, analyzer diagnostics, package composition, or CLI behavior.

## No-Change Procedure

If the current version has no meaningful effect on the baseline:

1. Verify that the latest docs and source were actually checked.
2. Update only version/date/commit metadata if the audit supports it.
3. Do not change examples or recommendations speculatively.
4. State that no substantive baseline changes were required.

## Final Validation Checklist

Before finishing an update, verify:

- The baseline was located and read completely before editing.
- Current stable/prerelease status and target version were established from official sources.
- The represented baseline version was compared against the target.
- Official release notes, docs, and relevant source/tests were reviewed.
- New features were checked across the entire documentation hierarchy.
- Changed APIs and behavior were identified.
- Deprecations have current replacements and are not taught as defaults.
- Removals have evidence and no normal examples remain.
- Configuration, CLI, MTP, reporting, and CI behavior were checked.
- Assertions, data sources, lifecycle, parallelism, DI, and extensions were audited.
- Changed compile-critical examples were compiled or explicitly marked unverified.
- The baseline was edited in place, not duplicated.
- Repeated old identifiers and contradictory statements were searched for.
- Version/date/repository metadata is accurate.
- The final baseline was read completely after the edit.
- `git diff --check` passes for the intended changes.
- No unrelated user changes were reverted or modified.

## Output

When reporting an update, provide:

1. The maintained artifact path: `.agents/skills/tunit/SKILL.md`.
2. The target TUnit version, stability channel, verification date, repository commit/tag, and SDK/MTP used.
3. A concise summary of substantive changes.
4. Deprecations/removals and their replacements.
5. Validation performed and any unverified or unresolved items.
6. Confirmation that no duplicate TUnit skill was created.

If the user requested only an audit, do not edit the baseline; return the change audit and proposed edits instead. Otherwise, apply the update end to end.

## Quick Procedure

```text
Locate .agents/skills/tunit/SKILL.md
        |
Read it completely and record represented version
        |
Determine current official stable/prerelease version
        |
Inspect official release notes, tags, docs, source, tests, and packages
        |
Build the new/changed/deprecated/removed/behavior audit
        |
Review the current documentation hierarchy, not just release notes
        |
Compile representative changed examples against the target package
        |
Patch the existing TUnit skill in place
        |
Search for stale names, contradictions, and obsolete commands
        |
Read the complete result back
        |
Run validation and record version/date/commit
```

## Official Entry Points

- Documentation: `https://tunit.dev/docs/intro`
- Documentation inventory: `https://tunit.dev/llms.txt`
- Repository: `https://github.com/thomhurst/TUnit`
- NuGet package: `https://www.nuget.org/packages/TUnit`
- NuGet registration: `https://api.nuget.org/v3/registration5-gz-semver2/tunit/index.json`
- Microsoft MTP/.NET runner documentation: `https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test`

Fetch the relevant current pages during each maintenance run. Do not assume URLs, package composition, CLI syntax, or defaults remain unchanged.
