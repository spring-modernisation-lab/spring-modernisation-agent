# Plan: AI-assisted upgrade demo

Status: **awaiting review** (written 2 November 2026)

## Goal

Show where AI adds real value in a framework upgrade, in one end-to-end demo:

1. **Assess.** Deterministic estate analysis and waves; the AI writes the strategy (already done).
2. **Transform.** OpenRewrite migrates a repo, and the build **fails** on something the recipes don't cover.
3. **Remediate.** The AI diagnoses the failure and proposes a bounded patch. C# applies it and re-runs Maven. The build goes green, or the AI explains why it can't fix it.
4. **Hand over.** A preserved diff plus an AI-written reviewer summary that separates OpenRewrite's changes from the AI's.

Closing message: *automation did what it reliably could; AI handled the long tail; Maven, not the AI, decided success.*

### Demo success criteria

- [ ] One real CI run in which OpenRewrite leaves at least one genuine compile or test failure.
- [ ] The Remediation Agent fixes it, and Maven validation passes afterwards.
- [ ] At least one seeded case the AI **can't** or **shouldn't** fix is reported clearly and not "fixed" (this shows the boundary holds).
- [ ] The artifacts include: the OpenRewrite patch, the AI patch, the validation evidence for each attempt, and the reviewer summary.

### Out of scope for the demo (kept in the backlog)

Multi-wave hand-off (merge, publish, re-assess), durable run store, cross-run persistence, parallel execution, automatic PR merge, and the production portal.

---

## Phase 0: Demo estate decision

The transform command always picks the earliest eligible wave. In the current estate that is `platform-parent`, which is just a POM with no code to break. Code repos sit in wave 3, and they would migrate against an unmigrated parent.

- [x] **Decided:** reuse the existing `spring-modernisation-lab` organisation. Add a new **standalone** Spring Boot 2.7 app repo (working name `orders-service`) with **no** internal dependencies. Dependency analysis then puts it in wave 1 alongside `platform-parent`, with no code change and no special-case flag.
- [x] The existing four repos stay as they are for the assessment and strategy part of the demo.
- [x] Scaffold `orders-service` locally at `C:\Users\y1dar\orders-service` (Boot 2.7.18, Java 11 to match the baseline workflow, Maven, tests).
- [ ] Create the GitHub repo `spring-modernisation-lab/orders-service` and push (user).

## Phase 1: Seed and prove the "long tail"

Put realistic upgrade traps into the demo app or apps, then **prove empirically** which ones OpenRewrite leaves broken. Don't assume.

- [x] Five traps seeded; catalogue and hypotheses in `tasks/demo-traps.md` (kept out of the target repo on purpose).
- [x] Probe workflow added: `.github/workflows/openrewrite-probe.yml`. It reads the recipes from `spring.yaml` and uploads the patch, logs and surefire reports.
- [ ] Run the probe and record the results in `tasks/demo-traps.md`.
- [x] Add `orders-service` to `portfolio-baseline-build.yml` (still a hardcoded list; making it discovery-driven is backlog).
- Original candidate list (superseded by `demo-traps.md`):
  - [ ] Custom Spring Security config with unusual `WebSecurityConfigurerAdapter` overrides.
  - [ ] A `javax.*` reference by reflection or string literal (e.g. `Class.forName("javax.servlet...")`).
  - [ ] A third-party library with no Boot 3 / Jakarta-compatible version at the current coordinates.
  - [ ] Use of a deprecated or removed Spring API with no recipe (e.g. a removed property or a `PathMatcher` behaviour change).
  - [ ] A test that depends on Boot 2.x auto-configuration behaviour (trailing-slash matching, etc.).
  - [ ] **One "should not fix" case**, e.g. a failure that needs a business decision or a dependency replacement outside policy.
- [ ] Confirm the baseline build passes on Boot 2.7 / Java 11 (the probe's baseline step checks this; it has not been compiled locally because there is no JDK or Maven here).
- [ ] Run OpenRewrite in CI against each demo repo and record which traps remain broken. Write the findings into this file.

## Phase 2: Unblock CI execution (prerequisites)

- [x] **Fix wave gating to fail closed.** Done with `MigrationWaveGate` (10 tests): `transformation-plans.json` only contains plans up to and including the earliest wave that has a non-Ready repo. The strategy document still shows every plan. Today, blocked repos are filtered out of `transformation-plans.json` (`AssessCommand.cs:351`), so a blocked wave disappears and later waves become eligible. Make a non-Ready repo in a wave block every later wave (planner-level test first).
- [x] **Fail closed on POM parse errors** (`AssessCommand.cs:82`): an unparseable repo must block, not vanish from the graph.
- [x] **Feed baseline evidence into `portfolio-transform.yml`.** The baseline workflow is now reusable (`workflow_call`), and the transform runs it first with `fail-on-baseline-failure: false`. Evidence comes from the same run as the migrated SHAs, and the case mismatch is fixed by downloading straight into `Artifacts/`. Download or produce `baseline-build-results.json` before assess. Fix the `artifacts/` vs `Artifacts/` case mismatch (Linux paths are case-sensitive).
- [x] **Make the Strategy Agent non-blocking for transform.** Deterministic artifacts are written first. The AI stage is skipped when `FOUNDRY_PROJECT_ENDPOINT` is unset; if it fails, a warning is logged and assess continues. An AI or auth failure must not stop deterministic migration. Assess still writes the deterministic artifacts, and the strategy outputs become optional in the workflow.
- [x] Fix the pinned OpenRewrite versions; the probe showed `rewrite-spring:6.43.0` and `rewrite-migrate-java:3.47.0` don't exist on Maven Central. Now 6.37.1 / 3.42.1, with plugin 6.46.1.
- [ ] Make the policy check fail when a pinned artifact doesn't exist (add a CI step that resolves each pinned recipe artifact). `OpenRewriteConfigurationValidator` currently only checks that a version is present.
- [x] **Verify end to end** (Spring modernisation run 7). Baseline: all 5 passed. Assess: all 5 Ready, waves 1 = `orders-service` + `platform-parent`, 2 = `shared-security`, 3 = `account-service` + `customer-service`; no `migration-analysis.json`, so the Strategy Agent was skipped or failed without blocking. Transform: the C# engine reproduced probe run 4 exactly (same SHA `b3986654`, both recipes, the same 7 files, `compile` failed). `platform-parent` didn't run because of stop-on-first-failure.
- [x] **Within-wave rule changed:** `MigrationWaveExecutor` now runs every repository in the active wave (peers are independent) and returns every result; a failure still blocks the next wave via the planner. Previously the wave stopped at the first failure.
- [ ] Re-run to confirm `platform-parent` now executes alongside the failing `orders-service`.
- [ ] Choose Foundry CI authentication: GitHub OIDC federated credential to Entra (workload identity, no long-lived secret).

## Phase 3: Preserve the transformed output

Today, `OpenRewriteExecutionCoordinator` deletes the workspace in `finally`, so the migrated code is lost.

- [ ] Before cleanup, capture a **binary-safe patch** (`git add -A` followed by `git diff --cached --binary` against the source SHA) via `IGitProcessRunner`.
- [ ] Capture the patch **per stage**: `openrewrite.patch` after the recipes run, and `remediation-attempt-N.patch` per AI attempt. This lets the summary attribute each change.
- [ ] Write the patches to `Artifacts/patches/<repo>/`, and reference their paths and SHA-256 hashes from `TransformationExecutionResult`.
- [ ] Upload `Artifacts/patches/**` in the workflow with `if: always()`.
- [ ] Tests: patch captured on success **and** on validation failure; still captured if cleanup throws; untracked files included.

## Phase 4: Remediation Agent (the headline)

A separate agent from the Strategy Agent, with its own narrow capabilities.

### 4.1 Evidence extraction (deterministic)

- [ ] `ValidationDiagnosticsExtractor`: from the Maven output, pull `[ERROR]` lines, compiler errors (file, line, message), and failing tests from `target/surefire-reports/*.xml`. Cap the size.
- [ ] Store the structured diagnostics in `TransformationValidationResult`, so the agent never receives raw multi-MB logs.

### 4.2 Read-only tools (scoped to the workspace)

- [ ] `ReadFile(path, startLine?, endLine?)`: the path must resolve inside the workspace root. Reject `..`, symlinks out of the workspace, `.git/` and `.github/`.
- [ ] `SearchSource(pattern, glob?)`: bounded result count.
- [ ] `GetChangedFiles()` / `GetOpenRewritePatch()`.
- [ ] `GetValidationDiagnostics()`.
- [ ] `ReadPom()` (effective dependency coordinates).
- [ ] No shell, no Maven, no git, no network, no write tool.
- [ ] Tests: path containment (traversal, similar-prefix, symlink), size limits.

### 4.3 Structured proposal (the AI never writes files)

- [ ] `RemediationProposal`: `Diagnosis`, `RootCause`, `Confidence`, `Decision` (`Fix` | `CannotFix` | `NeedsHumanDecision`), `Edits[]` (path plus exact search/replace blocks), `Rationale` per edit.
- [ ] `NeedsHumanDecision` and `CannotFix` are first-class outcomes, not failures. They power the "boundary holds" moment of the demo.

### 4.4 Controlled application (C#)

- [ ] `RemediationPatchApplier` guardrails:
  - Paths inside the workspace only.
  - Allowed file types: `.java`, `.kt`, `.xml` (including `pom.xml`), `.properties`, `.yml`/`.yaml`.
  - Denied: `.github/`, `mvnw*`, `.mvn/`, build scripts.
  - Max files and lines changed per attempt.
  - Each search block must match exactly once, or the whole proposal is rejected (no partial apply).
- [ ] Never delete or disable tests (reject edits that remove `@Test` methods or add `@Disabled`). Otherwise, "make the build green" can be gamed.
- [ ] Tests for every guardrail.

### 4.5 Loop (in the coordinator, before cleanup)

- [ ] On validation failure: extract diagnostics, ask the agent for a proposal, apply it, capture the patch, re-run the **same** fixed validation checks.
- [ ] Max attempts: 2 (configurable). Each attempt is recorded with its proposal, patch, applied/rejected status and validation result.
- [ ] Final success is still `transformation succeeded && validation passed`. The AI's confidence is never consulted.
- [ ] `TransformationExecutionResult` gains `RemediationAttempts[]` and `RemediatedByAi: bool`.
- [ ] Remediation is off unless explicitly enabled (`MIGRATION_REMEDIATION=enabled`), keeping the mutation boundary explicit.
- [ ] Tests with a fake agent: fix succeeds; first attempt fails then the second succeeds; `CannotFix`; guardrail rejection; cancellation; cleanup still runs.

## Phase 5: Reviewer summary

- [ ] `ChangeSummaryAgent` (or a second structured call): input is the OpenRewrite patch, the remediation patches, the diagnostics and the validation evidence. Output is a per-repo markdown summary covering what OpenRewrite changed, what the AI changed and why, what the reviewer should check, and residual risks.
- [ ] C# owns the facts section (files, checks, pass/fail); the AI writes only the narrative.
- [ ] Write it to `Artifacts/summaries/<repo>.md` and upload it.
- [ ] `DraftPullRequestPublisher` (behind an `IPullRequestPublisher` interface; fake in tests):
  - Only runs when the overall result is `Succeeded`.
  - Creates the branch from the plan's source SHA and applies the preserved patches as a commit, using a bot identity.
  - Opens the PR as a **draft** against the default branch.
  - Records the PR URL in the result.
  - Is idempotent: if the branch or PR already exists, update it rather than duplicating.
- [ ] The summary is the PR body. A failed or `NeedsHumanDecision` repo gets no PR, only artifacts.

## Phase 6: Strategy document polish (small)

- [ ] Add the agreed "AI contribution and control boundaries" table to `MigrationStrategyDocumentWriter`, now including remediation and the reviewer summary.

## Phase 7: Verification and rehearsal

- [ ] `dotnet test` green with zero new warnings.
- [ ] Full CI run against the demo organisation; confirm every success criterion above using the real artifacts.
- [ ] Re-run once to check repeatability; record any non-deterministic AI behaviour.
- [ ] Write a demo script: what to show at each step, which artifact to open, and the talking points.

---

## Suggested order

Phase 0, then Phase 1 (seeding) alongside Phase 2, then 3, then 4, then 5, then 6, then 7. Phase 1's findings decide what Phase 4 has to handle, so run OpenRewrite against the seeded repos early.

## Open decisions

1. ~~Demo estate~~: decided; a standalone repo in the existing organisation.
2. Remediation edit format: search/replace blocks (recommended; easy to validate) or unified diff?
3. ~~Draft PR~~: **in scope**. After a successful validation, push a `migration/spring-boot-3.5-java-21` branch and open a draft PR with the reviewer summary as the body. This needs `MIGRATION_GITHUB_TOKEN` with `contents: write` and `pull-requests: write` on the target repos. Never push to the default branch, and never merge.

## Review

_(to be completed after implementation)_
