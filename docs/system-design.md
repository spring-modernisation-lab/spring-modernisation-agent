# System design: the Spring modernisation pipeline as an agent-legible system

Status: design, 4 November 2026. Written after CI runs 11–23. Every principle here answers something that actually happened in those runs (see §12). Plans that implement it are in `tasks/todo.md` (Phase 8).

**Reading order for an agent:**
1. This page, §1–§5.
2. `README.md` for the commands.
3. `tasks/todo.md` for what is built.
4. `tasks/demo-traps.md` for what the runs showed.

Read code only after you know which layer (§2) and which identifier (§3) you are after.

---

## 1. The system in one sentence

> **Deterministic tools make most of the changes, a bounded AI proposes the rest, policy decides what may be applied, Maven decides what works, and a person decides what matters.**

Each clause is one layer, and each layer has a single owner:
- **Deterministic tools:** OpenRewrite.
- **A bounded AI:** the Remediation Agent.
- **Policy:** the rule registry and its knowledge base.
- **Maven:** the validation checks.
- **A person:** decisions, made in the draft PR.

No layer may do another layer's job. The AI never decides success. Maven never decides what is allowed. Policy never writes code. A person is never asked to make a decision that a fact already settles.

## 2. The tower of abstractions

Each layer consumes only the layer below it and produces one kind of artifact. An agent working at layer *n* should never need to read code from layer *n−2*.

| Layer | Name | Produces | Owner | Deterministic? | Lives in |
|---|---|---|---|---|---|
| L0 | **Evidence** | Git trees, patches, Maven output, Surefire XML, token counts | git, Maven, the model API | Yes (the model's text is recorded, not trusted) | `Artifacts/`, `Workspace/`, `Build/`, `Maven/` |
| L1 | **Facts** | Diagnostics, failing tests, unsupported libraries, review flags, changed files | Extractors | Yes | `Transformation/*Extractor*`, `SurefireReportReader`, `UnsupportedLibraries`, `ReviewFlagDetector` |
| L2 | **Policy** | Rule verdicts (accept or reject, with rule ID and reason) and outcome invariants | Rule registry and knowledge base | Yes | `RemediationPatchApplier*`, `Config/` (target: `Config/knowledge/`) |
| L3 | **Actors** | **Changes**: proposed edits with provenance | OpenRewrite, Remediation Agent, a person (decision records) | OpenRewrite yes; AI no; person no | `Transformation/OpenRewrite*`, `Remediation/` |
| L4 | **Verdicts** | One verdict per repository, with reasons | The verdict function | Yes | `Models/TransformationExecutionResult` (target: `Verdict`) |
| L5 | **Hand-off** | Reviewer summary, draft PR, results document | Publishers and reporters | Yes, except sections labelled AI-generated | `Publishing/`, `Reporting/` |
| L6 | **Control plane** | Run requests, run digests, the ledger, replays | The driver: a person or an agent like me | Yes | `Commands/` (target: `run`, `digest`, `replay`, `ledger`) |

Two rules make the tower legible:
- **Upward data, downward control.** Facts flow up. Only L6 starts things, and only L2 can stop an L3 change.
- **AI output is evidence, never a fact.** A model's diagnosis is stored in L0 with its tokens and tool calls. It becomes a fact only if a deterministic L1 extractor can confirm it. For example, "Ehcache 2 is the cause" counts as a fact because `UnsupportedLibraries.FindIn` found Ehcache 2 in the POM, not because the model said so.

## 3. Concepts and stable identifiers

Everything an agent needs to reason about has a **stable, greppable ID**. That ID appears identically in:
- the code (a constant or enum),
- every message (rejection reasons, PR flags, the job summary),
- the tests (test names cite it),
- the fixtures (the replay corpus labels it),
- the docs.

Grepping an ID should find all of them.

| Concept | ID scheme | Example | Meaning |
|---|---|---|---|
| Run | `R<number>` | `R23` | One CI execution of `portfolio-transform.yml` |
| Wave | `W<n>` | `W1` | Repositories that can migrate together |
| Route | route id | `spring-boot-application-2.7-to-3` | Recipes plus validation checks for a repository kind (`Config/spring.yaml`) |
| Stage | `NN-name` | `03-remediation-attempt-2` | One step in a repository's change chain |
| Tree | git tree id | `4b2f…` | Content address of the workspace after a stage |
| Change | `<repo>@<stage>` | `orders-service@03-remediation-attempt-2` | Edits from tree A to tree B, by one actor |
| Rule | `G-<area>-<name>` | `G-DESIGN-ANON`, `G-LIB-UNSUPPORTED`, `G-CFG-VALUE` | One guardrail (L2) |
| Invariant | `I-<name>` | `I-UNSUPPORTED-NEVER-SUCCEEDS` | An outcome rule over facts (L4) |
| Fact kind | `F-<name>` | `F-UNSUPPORTED-LIBRARY`, `F-FAILING-TEST` | A deterministic finding (L1) |
| Flag | `FLAG-<name>` | `FLAG-LARGE-ADDITION` | A review flag in the PR |
| Trap | `T<n>` | `T6` (Ehcache 2) | A seeded demo problem (`tasks/demo-traps.md`) |
| Decision | `D-<repo>-<n>` | `D-orders-service-1` | A person's recorded choice (§9.3) |

Today most of these exist only as prose: rejection reasons are free text, and traps are table rows. Giving them IDs is Phase 8.1. It's the cheapest change with the biggest payoff, because it turns "read and remember" into "grep".

## 4. The Change model

OpenRewrite, the AI and a person all do the same kind of thing: they turn one tree into another. The system should model them uniformly:

```
Change {
  repository, stage, actor: OpenRewrite | RemediationAgent | Person | Recipe(id)
  baseTree, resultTree, patch (path, sha256), changedFiles
  policy: [ { rule, verdict, reason } ]      # L2, empty for deterministic actors
  validation: { checks[], failingTests[] }   # L0/L1, after the change
  provenance: { proposalId?, tokens?, toolCalls?, decisionId? }
}
```

Patches are already chained by tree id (Phase 3), so a repository's history is a **linear change chain**. The cumulative patch is the chain's endpoint. Three things follow:
- **Attribution is free.** Every line in the draft PR can be traced to one Change, and therefore to one actor.
- **Replay is exact.** Any Change can be re-checked against today's policy by rebuilding its `baseTree` from the chain. The replay in §7.3 does exactly this.
- **Human work fits.** A person's decision (§9.3) becomes another Change with `actor: Person` or `actor: Recipe(id)`. It isn't a side channel.

## 5. Verdicts: one function, with reasons

Runs 22 and 23 showed what happens when "did it work?" is derived in several places: `RemediationOutcome`, `Succeeded`, PR eligibility and the summary icon could all disagree. The target is **one pure function**:

```
verdict(result) -> { state: Succeeded | NeedsDecision | Failed | NotRun,
                     reasons: [ I-… | F-… ] }
```

| State | When | Wave gate | PR |
|---|---|---|---|
| `Succeeded` | OpenRewrite succeeded, Maven passed, and no invariant objects | Opens the next wave | Draft PR |
| `NeedsDecision` | The agent chose NeedsHumanDecision, **or** an invariant holds, e.g. `I-UNSUPPORTED-NEVER-SUCCEEDS` | Blocks | `[Needs decision]` draft PR |
| `Failed` | Maven failed and no decision is pending | Blocks | None |
| `NotRun` | OpenRewrite failed or the repository was skipped | Blocks | None |

**Invariants (L4) beat guardrails (L2) wherever they can.** A guardrail asks "does this edit look like a workaround?", which means pattern-matching code shapes. An invariant asks "can this outcome be true, given the facts?" Across four runs the agent found five code shapes:
- named classes (R16);
- an anonymous class (R20);
- inline Ehcache code and changed settings (R21);
- a fallback cache manager and a hand-written XML-parsing cache (R23).

One invariant ended the series: the AI cannot edit `pom.xml`, and the POM still declares an unsupported library, so a green build **cannot** mean it was migrated.

Design rule: **when a guardrail has been bypassed twice, look for the invariant.**

Every verdict carries its reasons. Every surface prints them: the job summary, the PR, the results document and the digest (§7.2). "Needs decision: `I-UNSUPPORTED-NEVER-SUCCEEDS` (Ehcache 2)" tells an agent exactly where to look, where "❌" tells it nothing.

## 6. Policy as data

### 6.1 Rule registry

Every L2 rule is a registered object, not an `if` statement:

```
Rule { id: "G-DESIGN-ANON", layer: L2, appliesTo: [".java", ".kt"],
       check(before, after, facts) -> null | reason,
       motivatedBy: ["R20", "R23"], fixtures: ["corpus/R20-a2.json", "corpus/R23-a3.json"] }
```

- **One rule, one ID, one reason template.** Rejection messages start with the ID: `G-CFG-VALUE: changes spring.cache.type from 'ehcache' to 'none'`. The Remediation Agent then sees the same ID in feedback that I see in the digest.
- **Every rule has fixtures.** These are real historical proposals that it must reject, plus legitimate ones it must accept (§9.1). A rule without a fixture that once failed hasn't been tested.
- **`preview_edits` runs the registry.** The agent's dry run and the real application share one code path. That's true today, and must stay true.

### 6.2 Knowledge base

The facts the rules depend on are currently hard-coded in C#: the unsupported-library list, the behaviour annotations, the settings rule and the manual steps. They belong in versioned data under `Config/knowledge/`, next to the routes:

| File | Holds | Used by |
|---|---|---|
| `unsupported-libraries.yaml` | Name, package, POM markers, replacements, recommended route, manual steps, alternatives | L1 detection, `G-LIB-UNSUPPORTED`, `I-UNSUPPORTED-NEVER-SUCCEEDS`, the agent prompt, the PR manual steps |
| `behaviour-annotations.yaml` | Annotations whose removal deletes behaviour | `G-BEHAVIOUR-ANNOTATION` |
| `settings.yaml` | Protected settings (any existing value) and known renames (old key → new key) | `G-CFG-VALUE`, `G-CFG-REMOVED` |
| `api-notes.yaml` | Short, checked notes on moved APIs (HttpClient 4 → 5 pool methods, Hibernate 6 `UserType`) | Retrieved by the agent on demand (§8) |

Adding knowledge must never need a code change. It's one YAML entry plus one fixture. That makes the system **accretive**: each run's lesson becomes data that every later run uses.

## 7. The control plane: the driver's interface

Driving the pipeline means starting runs, knowing what each one showed, and checking a change before spending a CI run on it. Today the inputs of a run are not recorded alongside its results, artifacts must be fetched by hand, and the history lives only as narrative in `tasks/demo-traps.md`. The control plane makes each of these one command, with recorded inputs and a compact, stable output.

### 7.1 Commands

Each command is either **read-only** or **side-effecting**, and labelled as such in `--help`. Side-effecting commands take a request file and are idempotent.

| Command | Kind | What it does |
|---|---|---|
| `run start --request run-request.yaml` | side-effecting | Dispatches the workflow through the API with the request's inputs, and prints the run id. The request is uploaded with the results, so every run records exactly how it was started. |
| `run status <id>` | read-only | Shows the state of the run, plus the step currently running and its last progress line |
| `run fetch <id>` | read-only | Downloads the artifacts into `runs/R<id>/`, verifies the hashes, and writes a manifest |
| `digest <run-dir>` | read-only | The **run digest** (§7.2), as JSON plus Markdown, about 2 KB |
| `explain <run-dir> <repo> [--attempt n]` | read-only | One repository's change chain with rule verdicts, failing tests and the decision, without the patch bodies |
| `replay <run-dir> [--policy HEAD]` | read-only | Re-checks every recorded proposal against current policy and diffs the verdicts against the recorded ones (§7.3) |
| `ledger add <run-dir>` / `ledger show` | side-effecting / read-only | Appends the digest to `runs/ledger.jsonl` and shows trends |

Replay, digest and explain already exist in prototype form, in the trial harness used to verify the guardrail changes in runs 20–23. They should become product commands with tests.

### 7.2 The run digest: read first, read little

The digest is the single entry point to a run. It's designed to answer, in one read, what happened, why it stopped and what it cost. Illustrative example, using R23's real token totals as the digest would report them once the invariant applies:

```json
{ "run": "R23", "commit": "99adb50", "request": { "remediation": true, "maxAttempts": 5, "pullRequests": true },
  "repositories": [
    { "name": "orders-service", "verdict": "NeedsDecision",
      "reasons": ["I-UNSUPPORTED-NEVER-SUCCEEDS:Ehcache 2"],
      "facts": { "unsupportedLibraries": ["Ehcache 2"], "failingTests": [] },
      "attempts": [
        { "n": 2, "decision": "Fix", "policy": "accepted", "after": "unit-tests failed",
          "fixed": ["T3"], "flags": ["FLAG-LARGE-ADDITION"], "tokens": [41578, 3660] } ],
      "pr": { "number": 1, "title": "[Needs decision] …", "created": false } } ],
  "cost": { "inputTokens": 372414, "outputTokens": 28275 },
  "schema": "digest/1" }
```

The digest follows four rules:
- **Facts, IDs and counts only.** No patch bodies, no stack traces. Those are one `explain` away.
- **Schema-versioned** (`schema: digest/1`). It never renames a field silently.
- **Deltas against the previous ledger entry** for the same request: what improved, what regressed, and how cost changed.
- **Published three ways from one source:** printed to the job summary, uploaded as `run-digest.json`, and returned by `digest`.

### 7.3 Replay: the cheapest experiment

Replay rebuilds each attempt's base tree from the change chain and re-runs **today's** policy on the recorded proposal. It costs seconds and no tokens. In this session it was the strongest tool I had:
- it proved that each guardrail change rejected the bad proposal and kept every legitimate one (run 14 stayed fully accepted throughout);
- in run 23 it showed in one step that the guardrail itself had a bug, not the model.

Replay output is a verdict diff, for example:

```
R23 a3  accepted -> rejected  G-DESIGN-ANON  (Cache, CacheManager)
R14 a1  accepted -> accepted
```

A policy change is only finished when replay over the whole corpus shows exactly the intended diffs.

## 8. The inner agent's ergonomics

The Remediation Agent is also an agent driving a system. The same principles apply:

| Principle | Today | Next |
|---|---|---|
| **Facts up front, not discovered** | The failed check, diagnostics, earlier attempts and unsupported libraries | Plus the rule list by ID, and the relevant `api-notes` entries |
| **Real signatures, never guesses** | `describe_class` (javap); fixed run 13's HttpClient guessing | Keep it. Cache results across attempts |
| **Dry run on the real policy** | `preview_edits` runs the full applier | Return rule IDs; show "would be rejected by G-…" before the agent answers |
| **Feedback names the rule** | Rejection reason text | Start it with the rule ID, so the agent can map a rejection to an instruction |
| **Decisions carry independent fixes** | Since `99adb50` | Keep. Run 22 showed the cost of not allowing it |
| **Budgets** | 40 tool calls, 5 previews, 1–5 attempts | Record cost per attempt in the digest; stop early on repeated rejection by the same rule |
| **Narrow, honest options** | Options are AI-written and labelled | The PR leads with the knowledge base's manual steps; AI options are listed after them |

The prompt is part of policy. Every instruction in `RemediationAgent.Instructions` maps to a rule ID. An instruction with no enforcing rule is a hope, not a rule, and the prompt should say which is which.

## 9. Accretion: how the system gets better on its own record

A run should leave the system more capable, not just a set of artifacts. There are five loops, each with an owner and an artifact:

1. **Guardrail corpus (every run).** `ledger add` copies each attempt's proposal, its base-tree reference and the verdict it received into `corpus/`. Any proposal a person later judges wrong gets the expected verdict and becomes a regression fixture. Runs 16, 20, 21 and 23 are the first entries.
2. **Recipe candidates (recurring AI fixes).** If the AI makes the same kind of fix in several runs, it shouldn't be AI work any more. Examples: `javax.servlet` strings → `jakarta` (T3 in runs 14, 20, 21 and 23), the Hibernate 6 `UserType` port (T1), and the security adapter (T4). The ledger counts recurring fixes by trap and file, and proposes an OpenRewrite recipe once a fix recurs three times. The AI's share should shrink over time, and the ledger shows that trend.
3. **Decision records (people → system).** A `[Needs decision]` PR ends with the person's choice, recorded as `decisions/<repo>.yaml`, for example `D-orders-service-1: Ehcache 2 → Ehcache 3 via JCache`. The next run reads it. The chosen route becomes a Change by `actor: Recipe(...)` or `Person`, and the invariant that blocked the repository is satisfied by a fact, not by the AI.
4. **Knowledge entries (lessons → data).** A run that teaches something about a library or API ends with one YAML entry in `Config/knowledge/` and one fixture. Code changes only when a new kind of rule is needed.
5. **Ledger (history → docs).** `runs/ledger.jsonl` is the record of runs. `tasks/demo-traps.md` keeps the narrative and the demo talking points, but its tables are generated from the ledger, so the docs can't drift from the evidence. In this session `tasks/todo.md` drifted: its status line, the PR input name and a "the AI writes the narrative" claim were all out of date.

## 10. Economy: the cost ladder

Climb a rung only when the rung below can't answer the question:

| Rung | Costs | Answers |
|---|---|---|
| 1. `digest` / `explain` | Nothing | What happened, and why it stopped |
| 2. `replay` | Seconds, no tokens | Does today's policy treat recorded proposals correctly? |
| 3. Local trial (agent against a reconstructed workspace, no Maven) | Tokens, about 2 minutes | Does the agent's behaviour change with these instructions or tools? |
| 4. CI run | About 15 minutes, 0.3–0.8M tokens, a person to fetch artifacts (until `run fetch`) | Does Maven agree? Does the PR look right? |

Guidelines:
- **One hypothesis per CI run,** written in the run request (`hypothesis:`) and checked by the digest.
- **Expect variance.** The same code gave Fixed in 4 attempts (R14), AttemptsExhausted (R20, R21) and NeedsHumanDecision in 2 attempts (R22). Judge behaviour over several runs in the ledger, not one.
- **Stop early by policy, not by budget.** R22's early decision cost 0.3M tokens where R21's five attempts cost 0.7M. An agent that knows the invariants should decide sooner.

## 11. Evidence contract

- Every artifact is content-addressed (`sha256` in the manifest) and comes from one run and one commit.
- Every JSON file carries `schema: <name>/<version>`, and readers accept older versions (as `Patches` and `UnsupportedLibraries` already do).
- Each run directory is self-describing: it contains the request, the digest, the manifest, results, patches, summaries and PR records, so an agent can analyse it with no outside context.
- The model's text is stored verbatim with its tokens and tool calls, and is always labelled as AI-generated wherever a person reads it.

## 12. What the runs taught, and where the design answers it

| Run | What happened | Design answer |
|---|---|---|
| R13 | The agent guessed HttpClient 5 signatures three times | §8: real signatures (`describe_class`); `api-notes` |
| R14 | Fixed, but a review found a privilege-escalation risk and a dropped TTL | §5: green is not enough; review flags; draft PR only |
| R16 | Hand-written cache that games the test | `G-DESIGN-TYPES`, `G-TEST-REFERENCE`; §5: look for the invariant |
| R20 | Anonymous class got past the type rule | `G-DESIGN-ANON`; §7.3: replay proved it |
| R21 | Removed `@Cacheable`; switched production caching off; GitHub 500 on push | `G-BEHAVIOUR-ANNOTATION`, `G-CFG-VALUE`; push retry; publish failures in the summary |
| R22 | Correct decision, but a known fix was left out | §8: decisions carry independent fixes |
| R23 | "Fixed" with a parser-based fake cache; guardrail bug in the generics pattern | `I-UNSUPPORTED-NEVER-SUCCEEDS`; §6.1: rules need fixtures; §7.3: replay found the bug |
| All | Run inputs not recorded with results; artifacts fetched by hand; history kept only as narrative | §7: `run start` / `run fetch` / `digest` / `ledger` |

## 13. Non-goals

- **Autonomy beyond proposals.** The AI never applies, merges, pushes or decides success, and never edits tests, POMs or CI.
- **Agent-written policy.** An agent (including me) may *propose* a rule or knowledge entry. A rule becomes policy only with a fixture, a replay diff and human review in a commit.
- **Hiding non-determinism.** Variance between runs is recorded and reported, never averaged away.
