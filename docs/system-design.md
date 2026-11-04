# System design: the Spring modernisation pipeline as an agent-legible system

Status: design, revised 4 November 2026 after CI runs 11–27; §3 and §4.1 are built and proven in CI (runs 26 and 27). Every principle here answers something that actually happened in those runs (see §15).

§2 explains why the first design kept going in circles; §3–§5 are the redesign; §6 onwards are the supporting parts. Plans that implement it are in `tasks/todo.md` (Phase 8).

**Reading order for an agent:**
1. This page, §1–§5.
2. `README.md` for the commands.
3. `tasks/todo.md` for what is built and next.
4. `tasks/demo-traps.md` for what the runs showed.

Read code only after you know which layer (§6) and which identifier (§7) you are after.

---

## 1. The system in one sentence

> **Deterministic tools make the changes they reliably can; a bounded AI ports what is left, one problem at a time, against a behaviour contract recorded on the baseline; facts decide what needs a person; and Maven plus that contract decide what works.**

Each clause has a single owner:
- **Deterministic tools:** OpenRewrite and our own recipes.
- **A bounded AI:** the porting agent, given one scoped port at a time.
- **The behaviour contract:** recorded by the pipeline on the Boot 2.7 baseline, never written by the AI.
- **Facts:** the catalogue, the BOM comparison and the contract differences.
- **A person:** decisions, made in the draft PR.

No part may do another part's job:
- The AI never decides success, never picks a problem's kind, and is never asked to make a decision problem green.
- Maven never decides what is allowed.
- A person is never asked to decide something a fact already settles.

## 2. Why the first design went in circles

From R13 to R24 the pipeline ran **one open loop**: "here is a failing repository; make Maven green within these rules". Every run, the AI found a new way to get to green, and we added a rule against that particular way. There were four causes, and none of them was a missing rule:

1. **An optimiser pointed at a weak target.** "Make it green" is exactly what the AI did each time:
   - a hand-written cache (R16, R23);
   - an anonymous `Cache` (R20);
   - inline Ehcache code, and `spring.cache.type: none` (R21);
   - a fallback `ConcurrentMapCacheManager` (R23);
   - a servlet check rewritten so it still fails (R24).

   Banning shapes after the fact can't keep up with something that is optimising.
2. **Green didn't mean the behaviour was kept.** No test covered:
   - the POST privilege loophole (R14);
   - the dropped connection TTL (R14 and R24);
   - the unbounded fallback cache (R23).

   Review flags caught some of this, after the fact.
3. **One treatment for every kind of problem.** A string rename (T3), API ports (T1, T2, T4), a contract question (T5) and a library the platform dropped (T6) all went into the same loop.
4. **Nothing was learned for good.** The AI re-derived the `javax.servlet` → `jakarta.servlet` fix in every run: correctly in R14, R20, R21 and R23, and wrongly in R24. The knowledge lived in the model's variance, not in the system.

The redesign attacks each cause directly:
- the **behaviour contract** (§3) strengthens the target;
- **problem kinds** (§4) give each problem its own path;
- **durable fixes** (§5) make the system learn for good.

The guardrails stay, but as a backstop rather than the main defence.

## 3. The behaviour contract

Before migrating, the pipeline records what the application **does** on the Boot 2.7 baseline. After migration it records the same things again and compares the two. "Done" means Maven passes **and** the contract matches. A workaround that changes behaviour then fails because of what it does, whatever its code looks like.

### 3.1 What is recorded

The contract is a set of items, each with a stable ID (`B-…`) and a value:

| Item kind | ID example | Value | Would have caught |
|---|---|---|---|
| **Access** (endpoint × user → status) | `B-SEC-POST-/api/orders/-viewer` | `403` | R14: the POST trailing-slash loophole |
| **Resolved setting** | `B-CFG-spring.cache.type@production` | `ehcache` | R21: caching switched off |
| **Key bean property** | `B-BEAN-HttpClient.connectionTimeToLive` | `30s` | R14 and R24: the TTL dropped |
| **Cache behaviour** (name, expiry, size) | `B-CACHE-inventoryAvailability` | `ttl 30s, max 1000` | R23: unbounded fallback cache |
| **Context starts** (per profile) | `B-CTX-production` | `starts` | Trap 3's failure, stated as behaviour |

Which items to record is set per route in configuration: `Config/knowledge/behaviour-probes.yaml`, selected by the route in `spring.yaml`. It is never chosen by the AI.

### 3.2 How it is recorded

- A **pipeline-owned probe**: a small JUnit 5 test with its own dependencies. It is added to the workspace's test classpath only while validation runs, and it is excluded from every patch, in the same way as `target/`.
- The probe drives MockMvc requests for access items, reads the `Environment` for settings, and reads bean properties through Spring's own introspection. Each probe compiles against both Boot 2.7 and Boot 3.5. A probe that can't be read on one side reports `unavailable`, never a default value.
- The baseline contract is recorded once per assessed commit, with the baseline build. It is stored as `Artifacts/behaviour/<repo>.json` and kept with the assessment.

### 3.3 How it is compared

| Result | Meaning | Effect |
|---|---|---|
| `same` | The value matches | Nothing |
| `changed` | The value differs | **Blocks success.** Becomes a contract problem (§4) unless a decision record allows it |
| `unavailable` | The probe couldn't read the item after migration (for example, the bean was removed) | **Blocks success**, as `changed` does: unknown is not "same" |
| `allowed` | It differs, and a decision record says so (`D-…`) | Nothing; shown in the PR |

Two rules keep the comparison readable (both from run 26):
- **An application that no longer starts is one difference, not dozens.** Its profile's unreadable items are counted under its `B-CTX-…` row as not checked; values that were read and differ are still listed.
- **Known platform changes are named.** A difference that a behaviour signature explains (`behaviour-signatures.yaml`, for example Spring 6 no longer matching a trailing slash) is labelled as a platform change. It still blocks success until a decision allows it, but it is never held against a port (§4.1).

The contract is evidence about behaviour, not a proof: it covers only what is recorded. Its value is that it turns the losses the runs actually found into something Maven-level and deterministic, and makes every new probe add to the coverage of every later run.

## 4. Problem kinds and routes

Each failure is **classified from facts first**, then sent down the path for its kind. Classification is deterministic: a lookup in the knowledge base (§9.2), not an AI judgement.

| Kind | ID | Recognised by | Route | Trap |
|---|---|---|---|---|
| **Mechanical** | `K-MECHANICAL` | A known diagnostic signature with a recipe | A deterministic recipe, run with OpenRewrite. No AI | T3 (`javax.servlet` strings) |
| **Known API move** | `K-API-MOVE` | A diagnostic signature mapped to an API mapping (`M-…`) | A **scoped AI port** (§4.1) | T1 `UserType`, T2 HttpClient 5, T4 Security |
| **Dropped by the platform** | `K-PLATFORM-DROPPED` | The catalogue or the BOM comparison (§9.3) | Straight to a decision, with the catalogue's manual steps. **No AI edit** | T6 Ehcache 2 |
| **Contract question** | `K-CONTRACT` | A contract item `changed`, or a failing test that asserts an HTTP status or URL | A decision, shown next to the baseline's recorded behaviour | T5 trailing slash |
| **Unknown** | `K-UNKNOWN` | Nothing matched | AI **diagnosis only**: it proposes a kind and a mapping; a person confirms; no edits | — |

Every failing test and diagnostic group is assigned exactly one kind. Problems are worked in a fixed order: mechanical, then API moves, then decisions. A problem whose evidence disappears after an earlier fix is closed, not worked.

### 4.1 The scoped AI port

This replaces the open loop. The AI gets one **task**, not a repository:

```
PortTask {
  problem: K-API-MOVE, mapping: M-HTTPCLIENT-5-POOL
  files:   [ src/main/java/…/InventoryClientConfiguration.java ]   # from the diagnostics
  evidence: the diagnostics and failing tests for this problem only
  obligations: [ B-BEAN-HttpClient.maxTotal = 50, B-BEAN-HttpClient.connectionTimeToLive = 30s, … ]
  notes:   the mapping's checked API notes; describe_class for real signatures
}
```

The task is accepted only if all three conditions hold:
1. **The targeted evidence is gone:** this problem's diagnostics and failing tests no longer appear.
2. **Nothing new fails:** no new diagnostic or failing test anywhere.
3. **The obligations still match:** every contract item the task named is `same`.

Otherwise the change is **reverted to the task's base tree**, and the problem goes to a person with the attempt attached. There is one attempt, plus one repair using the specific failure as feedback. Then the task stops.

Consequences:
- **Scope is stated positively.** The task names the only files the AI may change. Guardrails remain as a backstop for anything else.
- **The AI is never asked to make a dropped library green.** Run 23's parser-based cache could not have been written, because the Ehcache problem never reached the AI.
- **"Better or reverted" falls out of the acceptance rule.** R24's broken servlet fix fails rule 1: `HttpServletRequest` still isn't found. And as a `K-MECHANICAL` problem, T3 would never reach the AI at all.
- **Cost is bounded per problem,** not per repository.

**As built (8.4):**
- **One task per mapping.** The problems that share a mapping (for example `MoneyUserType` and `CustomerOrder`) are one task over all their files, so tasks never share a file and each can be reverted on its own.
- **Obligations are checked once everything compiles.** A half-ported repository can't start, so a **contract gate** runs one behaviour comparison after the last task and checks every kept port's obligations (`obligations:` in `api-mappings.yaml`, as contract item globs). A port that broke one gets its repair; if it still breaks one, its files go back to the recipes' output and the revert is recorded as a patch. That is at most two probe runs per repository.
- **Platform differences are not held against a port.** `behaviour-signatures.yaml` names differences the platform causes whatever a port does, for example Spring 6 no longer matching a trailing slash (`BS-TRAILING-SLASH`). They go to a person as decisions.
- **"Nothing new fails" allows for how javac reports.** Fixing one file can let javac report another file's errors for the first time. A new failure counts against the port when it is in the port's files or matches no signature. The build moving on to a later check is progress.
- **Replayed on run 26's records,** the gate keeps the security port (its four trailing-slash 404s are the platform's) and catches the HttpClient port's lost `connectionTimeToLive`, which the open loop had accepted in three runs.

## 5. Durable fixes: the AI writes the recipe, not the patch

When a person accepts a scoped port in a draft PR, the work isn't finished until it's **durable**:

1. The AI drafts the generalisation from the accepted change, as one of:
   - an OpenRewrite recipe, when the change is mechanical across code bases;
   - an API mapping entry (`M-…`) with checked notes, when it still needs judgement but the target API is known;
   - a diagnostic signature, so the next occurrence is classified straight away.
2. **Replay proves it.** The recipe, run on the recorded base trees of past runs, must reproduce the accepted fix and leave the contract `same`.
3. **A person approves it once**, in a commit, with its fixture.

From then on, the problem's kind moves down the table: `K-UNKNOWN` → `K-API-MOVE` → `K-MECHANICAL`. **The AI's per-run share shrinks over time, and that share is the measure of progress.** The ledger (§10.4) reports it per trap and per kind.

T3 was the first: `lab.jakarta.ServletStringLiterals` (Phase 8.1). It produces, byte for byte, the file the AI produced in R14, R20 and R23, and the AI broke that file in R24.

### 5.1 Recipe discovery: the front door to durable fixes

T3 had a recipe building block all along; nobody looked. Recipe discovery makes the system look, every run, for each problem the route's recipes leave behind.

1. **Recipe index (deterministic).** It's built from the recipe JARs: each JAR's `META-INF/rewrite/recipes.csv` (every recipe's name, description and options) and its declarative YAML (the types and packages each recipe touches).
   - It covers the pinned modules plus candidate modules at pinned versions, listed in `Config/knowledge/recipe-sources.yaml`.
   - It records each module's required OpenRewrite core version, because a module built for a newer core may not run on the pinned plugin.
2. **Match (deterministic first).** Symbols are extracted from each remaining compile error or failing test (types, members, packages) and looked up in the index. Exact hits need no AI.
3. **AI ranking (fuzzy cases only).** The AI sees the errors and a shortlist from the index, then ranks and explains the candidates. Any name it returns that isn't in the index is dropped, so invented recipe names can't get through.
4. **Measure.** OpenRewrite's `dryRun` applies nothing, so each candidate is tried on a throwaway copy of the post-recipe tree, which is then rebuilt. The result is a measured fact ("compile errors 12 → 10, none new"), or `unverified`.
5. **Report, never apply.** The results document and the PR list "Possible recipes for the remaining errors (suggestions, not applied)", each with its effect and source (index or AI). Adding a recipe to a route stays a reviewed commit with a probe run, as in 8.1. The ledger counts suggestions per problem.

Candidates are judged by the same rule as an AI port: they remove failures, add none, and leave the contract the same. Recipes and the AI are measured on one scale.

## 6. The tower of abstractions

Each layer consumes only the layer below it and produces one kind of artifact.

| Layer | Name | Produces | Owner | Deterministic? | Lives in |
|---|---|---|---|---|---|
| L0 | **Evidence** | Git trees, patches, Maven output, Surefire XML, probe output, token counts | git, Maven, the probe, the model API | Yes (the model's text is recorded, not trusted) | `Artifacts/`, `Workspace/`, `Build/`, `Maven/` |
| L1 | **Facts** | Diagnostics, failing tests, unsupported libraries, **contract items and differences**, **problem kinds**, review flags | Extractors, the contract comparison, the classifier | Yes | `Transformation/*Extractor*`, `SurefireReportReader`, `UnsupportedLibraries`, `ReviewFlagDetector` (target: `Behaviour/`, `Triage/`) |
| L2 | **Policy** | Rule verdicts (accept or reject, with rule ID) and task acceptance | Rule registry, knowledge base, the port acceptance rule | Yes | `RemediationPatchApplier*`, `Config/knowledge/` |
| L3 | **Actors** | **Changes** with provenance | Recipes (OpenRewrite and ours), the scoped AI port, a person (decision records) | Recipes yes; AI no; person no | `Transformation/OpenRewrite*`, `Remediation/` |
| L4 | **Verdicts** | One verdict per repository, with reasons | The verdict function | Yes | `Models/TransformationExecutionResult` (target: `Verdict`) |
| L5 | **Hand-off** | Reviewer summary, draft PR, results document | Publishers and reporters | Yes, except sections labelled AI-generated | `Publishing/`, `Reporting/` |
| L6 | **Control plane** | Run requests, run digests, the ledger, replays | The driver: a person or an agent | Yes | `Commands/` (target: `run`, `digest`, `replay`, `ledger`) |

Two rules make the tower legible:
- **Upward data, downward control.** Facts flow up. Only L6 starts things, and only L2 can stop an L3 change.
- **AI output is evidence, never a fact.** A model's diagnosis or proposed kind is stored in L0. It becomes a fact only when a deterministic extractor, the contract or a person confirms it.

## 7. Concepts and stable identifiers

Everything an agent needs to reason about has a **stable, greppable ID**. That ID appears identically in:
- the code (a constant or enum),
- every message (rejection reasons, task results, PR flags, the job summary),
- the tests (test names cite it),
- the fixtures (the replay corpus labels it),
- the docs.

| Concept | ID scheme | Example | Meaning |
|---|---|---|---|
| Run | `R<number>` | `R24` | One CI execution of `portfolio-transform.yml` |
| Wave | `W<n>` | `W1` | Repositories that can migrate together |
| Route | route id | `spring-boot-application-2.7-to-3` | Recipes, checks and probes for a repository kind (`Config/spring.yaml`) |
| Problem kind | `K-<kind>` | `K-API-MOVE` | Which path a failure takes (§4) |
| Contract item | `B-<kind>-<name>` | `B-BEAN-HttpClient.connectionTimeToLive` | One recorded behaviour (§3) |
| API mapping | `M-<name>` | `M-HTTPCLIENT-5-POOL` | Checked notes for a known API move |
| Recipe | recipe id | `lab.jakarta.ServletStringLiterals` | A deterministic fix |
| Stage | `NN-name` | `03-port-M-HTTPCLIENT-5-POOL` | One step in a repository's change chain |
| Change | `<repo>@<stage>` | `orders-service@03-port-…` | Edits from tree A to tree B, by one actor |
| Rule | `G-<area>-<name>` | `G-DESIGN-ANON`, `G-LIB-UNSUPPORTED` | One guardrail (backstop) |
| Invariant | `I-<name>` | `I-UNSUPPORTED-NEVER-SUCCEEDS`, `I-CONTRACT-MATCHES` | An outcome rule over facts |
| Fact kind | `F-<name>` | `F-UNSUPPORTED-LIBRARY` | A deterministic finding |
| Flag | `FLAG-<name>` | `FLAG-LARGE-ADDITION` | A review flag in the PR |
| Trap | `T<n>` | `T6` (Ehcache 2) | A seeded demo problem (`tasks/demo-traps.md`) |
| Decision | `D-<repo>-<n>` | `D-orders-service-1` | A person's recorded choice, which may allow contract differences |

## 8. The Change model and verdicts

### 8.1 Changes

OpenRewrite, our recipes, the AI and a person all turn one tree into another, and are modelled uniformly:

```
Change {
  repository, stage, actor: OpenRewrite | Recipe(id) | Port(task) | Person(decision)
  baseTree, resultTree, patch (path, sha256), changedFiles
  policy: [ { rule, verdict, reason } ]        # empty for deterministic actors
  validation: { checks[], failingTests[] }
  contract: { item: same | changed | unavailable | allowed }
  provenance: { taskId?, proposalId?, tokens?, toolCalls?, decisionId? }
}
```

Patches are chained by tree id, so a repository's history is a linear change chain, and the cumulative patch is its endpoint. Three things follow:
- **Attribution is free:** every line can be traced to one actor.
- **Replay is exact** (§10.3).
- **Reverting a task is just restoring its base tree.**

### 8.2 One verdict function, with reasons

```
verdict(result) -> { state: Succeeded | NeedsDecision | Failed | NotRun,
                     reasons: [ I-… | K-… | B-… | F-… ] }
```

| State | When | Wave gate | PR |
|---|---|---|---|
| `Succeeded` | Maven passed, every contract item is `same` or `allowed`, and no invariant objects | Opens the next wave | Draft PR |
| `NeedsDecision` | Any `K-PLATFORM-DROPPED`, `K-CONTRACT` or rejected port problem remains, or an invariant holds | Blocks | `[Needs decision]` draft PR |
| `Failed` | Maven failed and nothing is waiting on a person | Blocks | None |
| `NotRun` | OpenRewrite failed or the repository was skipped | Blocks | None |

The reasons are printed everywhere: the job summary, the PR, the results document and the digest.

**As built (8.5):** `Verdict.Of` in `Models/Verdict.cs`. A reason that needs a person makes it `NeedsDecision`; otherwise a failing build makes it `Failed`. A contract that could not be compared because the build failed is a consequence of the failure, not a decision. Results recorded before scoped ports keep the old agent's own decision as `AI-DECISION`, so earlier runs replay to the states they had.

**Invariants over shapes.** The earlier lesson still holds: when a guardrail has been bypassed twice, look for the invariant. `I-UNSUPPORTED-NEVER-SUCCEEDS` ended the Ehcache series. The contract generalises that lesson. `I-CONTRACT-MATCHES` is the invariant that covers behaviour in general.

## 9. Policy and knowledge as data

### 9.1 Guardrails: the backstop

The path, design, library, annotation and settings rules stay. They now guard the edges of a scoped task rather than being the main defence.
- Each rule has an ID that starts its rejection message, and real historical proposals as fixtures.
- `preview_edits` and application share one code path.
- **No new shape rules.** A new workaround is answered by a contract item, a problem kind or an invariant.

### 9.2 Knowledge base (`Config/knowledge/`)

| File | Holds | Used by |
|---|---|---|
| `unsupported-libraries.yaml` | **Built.** Name, package, POM markers, replacements, manual steps, alternatives | `K-PLATFORM-DROPPED`, `G-LIB-UNSUPPORTED`, `I-UNSUPPORTED-NEVER-SUCCEEDS`, the PR |
| `diagnostic-signatures.yaml` | Pattern over a diagnostic or failing test → kind, and recipe or mapping | The classifier (§4) |
| `api-mappings.yaml` | `M-…`: old API → new API, checked notes, and the contract items a port must keep | Scoped ports (§4.1) |
| `behaviour-probes.yaml` | Which contract items to record, per route | The contract (§3) |
| `behaviour-annotations.yaml`, `settings.yaml` | Annotation and settings rules | Guardrails (§9.1) |

Adding knowledge never needs a code change: it's one YAML entry plus one fixture. The catalogue is validated on load; the others will be too.

### 9.3 Derived knowledge

Where the platform publishes the facts, compute them. Comparing the `spring-boot-dependencies` BOMs for the source and target versions shows which libraries Boot dropped, Ehcache 2 among them.
- Libraries Boot never managed (Springfox, the Keycloak adapter) need catalogue entries.
- A dropped library with no catalogue entry is still `K-PLATFORM-DROPPED`. It gets generic steps and becomes a catalogue candidate.

## 10. The control plane: the driver's interface

### 10.1 Commands

Each command is labelled **read-only** or **side-effecting** in `--help`. Side-effecting commands take a request file and are idempotent.

| Command | Kind | What it does |
|---|---|---|
| `run start --request run-request.yaml` | side-effecting | Dispatches the workflow with the request's inputs. The request is uploaded with the results |
| `run status <id>` / `run fetch <id>` | read-only | Run state; artifacts into `runs/R<id>/` with hashes and a manifest |
| `digest <run-dir>` | read-only | The run digest (§10.2) |
| `explain <run-dir> <repo> [--problem K-…]` | read-only | One repository's problems, tasks, contract differences and decisions |
| `replay <run-dir>` | read-only | Re-checks recorded proposals and recipes against current policy (§10.3) |
| `contract diff <before> <after>` | read-only | Compares two behaviour records |
| `ledger add` / `ledger show` | side-effecting / read-only | Run history, the AI's share per kind, recurrence counts |

### 10.2 The run digest

This is the one entry point to a run: about 2 KB, schema-versioned (`digest/1`), with facts, IDs and counts only. Per repository it lists:
- the verdict and its reasons;
- the problems with their kinds and outcomes (closed by a recipe, ported, reverted, or decision);
- the contract differences;
- the tasks with their tokens;
- the PR.

It's published as the job summary, as `run-digest.json` and through `digest`, from one source.

### 10.3 Replay

Replay rebuilds a base tree from the change chain and re-runs current policy (or a new recipe) on the recorded input, in seconds and with no tokens. In R20–R24 it was the strongest verification tool: it proved each policy change in both directions and exposed a bug in a guardrail. **A policy, recipe or mapping change is finished only when replay over the corpus shows exactly the intended differences.**

### 10.4 The ledger

`runs/ledger.jsonl` holds one digest per run. It answers three questions over time:
- How varied are the outcomes?
- What does a run cost?
- **How much of the work is still AI work, per kind?**

The run tables in `tasks/demo-traps.md` are generated from it.

## 11. The inner agent's ergonomics

| Principle | How |
|---|---|
| **One task, stated positively** | A `PortTask` names the problem, the files, the mapping and the obligations (§4.1) |
| **Facts up front** | The task's evidence only, the mapping's notes and the obligations, not the whole repository's failures |
| **Real signatures** | `describe_class` (built) |
| **Dry run on the real policy** | `preview_edits` (built), extended to report which obligations a preview would break |
| **Feedback names the rule or obligation** | "`B-BEAN-HttpClient.connectionTimeToLive` changed from 30s to unavailable" |
| **Never asked to decide** | Kinds come from facts; the AI may propose a kind only for `K-UNKNOWN` |
| **Bounded** | One attempt plus one repair per task, a tool-call budget, and tokens recorded per task |

## 12. Accretion: how the system gets better on its own record

1. **Durable fixes (primary, §5).** Accepted ports become recipes, mappings and signatures.
2. **Contract coverage.** Every behaviour loss found in review becomes a probe, so every later run checks it.
3. **Decision records.** `decisions/<repo>.yaml` records a person's choice, for example `D-orders-service-1: Ehcache 2 → Ehcache 3 via JCache`. It also lists the contract differences that choice allows. The next run applies the choice as a deterministic Change.
4. **Corpus.** Every recorded proposal, recipe run and contract record is kept as a replay fixture.
5. **Knowledge entries.** A lesson is one YAML entry plus a fixture; code changes only for a new kind of rule.

## 13. Economy: the cost ladder

| Rung | Costs | Answers |
|---|---|---|
| 1. `digest` / `explain` | Nothing | What happened, and why it stopped |
| 2. `replay`, `contract diff` | Seconds, no tokens | Does current policy, or a new recipe, treat recorded inputs correctly? |
| 3. Local port trial (one `PortTask` against a reconstructed tree) | Tokens, about a minute | Does a mapping or instruction change the port? |
| 4. CI run | About 15 minutes and real tokens | Does Maven agree? Does the contract match? Does the PR look right? |

Guidelines:
- One hypothesis per CI run.
- Judge behaviour over several runs.
- The scoped design should also cost less: R24's two attempts used 170k input tokens for the whole repository, while a port task sees only its own evidence.

## 14. Evidence contract

- Every artifact is content-addressed and comes from one run and one commit.
- Every JSON file carries `schema: <name>/<version>`, and readers accept older versions.
- A run directory is self-describing: request, digest, manifest, results, patches, behaviour records, summaries and PR records.
- The model's text is stored verbatim with its tokens and tool calls, and is always labelled where a person reads it.

## 15. What the runs taught, and where the design answers it

| Run | What happened | Answer |
|---|---|---|
| R13 | Guessed HttpClient 5 signatures | `describe_class`; `api-mappings` |
| R14 | Fixed, but a POST loophole and a dropped TTL | §3 contract: `B-SEC-…`, `B-BEAN-…` |
| R16 | Hand-written cache that games the test | §4: T6 is `K-PLATFORM-DROPPED` and never reaches the AI |
| R20 | Anonymous-class cache | Same as R16; `G-DESIGN-ANON` stays as a backstop |
| R21 | Removed `@Cacheable`; caching switched off in production | §3: `B-CFG-spring.cache.type`, `B-CACHE-…` |
| R22 | Correct decision; a known fix left out | §4: problems are worked one at a time, so independent fixes don't wait on a decision |
| R23 | "Fixed" with a parser-based fake cache; a guardrail bug | §4 and `I-UNSUPPORTED-NEVER-SUCCEEDS`; §10.3: replay found the bug |
| R24 | Decision correct; the carried servlet fix still failed; TTL dropped again | §4.1: rejected for not removing its evidence, then reverted; §5: T3 becomes a recipe; §3: `B-BEAN-…TimeToLive` |
| R26 | The contract, in the pipeline, caught the TTL dropped a third time; production not starting made 36 rows of noise | §3.3: a stopped context is one difference; §4.1: obligations |
| R27 | Scoped ports: T1, T2 and T4 accepted on the first attempt, TTL kept and confirmed; T5 and T6 left as decisions | §4.1 as built |
| All | One open loop chasing green; knowledge re-derived each run | §2–§5 |

## 16. Sizing the migration effort (T-shirt sizes)

Each repository is sized twice: an **estimate** at assessment, before anything changes, and an **actual** after the transform, from what is left for people. The size is always computed from facts by a rubric in configuration (`Config/knowledge/sizing.yaml`). The AI may explain a size, but never sets one.

### 16.1 Estimate, at assessment

The assessment already records each repository's kind, Spring and Java versions, Maven project, external dependencies, internal dependents and baseline build. A cheap source scan adds **hotspots**, each a fact from the code:

| Signal | Example | Why it costs effort |
|---|---|---|
| APIs the platform removed or changed | `WebSecurityConfigurerAdapter`, Hibernate `UserType` implementations, HttpClient 4, `javax.*` names in strings | Known ports; some are recipes, some aren't |
| Unsupported libraries | Ehcache 2, Springfox, the Keycloak adapter (catalogue, and the BOM comparison) | Each one is a decision for a person |
| Version distance | Boot 2.7 → 3.5, Java 11 → 21 | More recipes, more behaviour change |
| Blast radius | Repositories that depend on this one | A parent or library gates a whole wave |
| Size and safety net | Source files; whether tests exist | Less test coverage means more review |

The rubric gives each signal points, and the total maps to a size. Totals are shown per wave, which helps plan how long the old and new lines must coexist (§17).

### 16.2 Actual, after the transform

This sizes the **work remaining for people**, using the problem kinds (§4). It's defined by the kind of work left, not by hours or error counts: a recipe can remove 12 errors at once, but a decision costs what it costs.

| Size | Work left |
|---|---|
| **XS** | None: recipes only |
| **S** | One small API port |
| **M** | Several API ports |
| **L** | API ports plus one decision (a dropped library, or a contract question) |
| **XL** | Several decisions, or a decision on a repository with a wide blast radius |

On the demo estate:
- `orders-service` is estimated **L** (four API hotspots plus Ehcache 2). Its actual is **L**: three ports (T1, T2, T4), one platform decision (T6) and one contract question (T5).
- `platform-parent` is **XS** both times.

### 16.3 Where sizes appear, and how they improve

- **The estimate** appears in the strategy document, per repository and per wave.
- **The actual** appears in the results document, the job summary and each draft PR.
- **Both** go in the ledger (§10.4), next to what actually happened: AI tokens spent, decisions taken, time until the PR closed.

With five repositories, the rubric starts as a reasoned heuristic. The ledger is how it gets calibrated: when estimates and actuals disagree, the rubric's points change, in a reviewed commit, like any other knowledge.

## 17. Running the migration alongside ongoing work

The pipeline stops at a draft PR and never merges. That's right for a single repository, but an estate needs more. A parent or shared library has to be **released** before its consumers can move, and services keep shipping on Boot 2.7 while their migration waits. This section describes how the waves work in practice. It belongs to people and process; the pipeline's job is to make each step cheap and visible.

### 17.1 Parents and libraries: two lines for a while

- A migrated parent or library is released as a **new major version**, alongside the old one. For example, `platform-parent` 2.0.0 is Boot 3.5 / Java 21, while **1.x (Boot 2.7) carries on** from a maintenance branch (`release/1.x`) for consumers that haven't moved.
- One parent can't serve both Boot 2.7 and Boot 3, so the two lines are unavoidable. The major version tells consumers it's a breaking change.
- 1.x is retired on a set date, once every consumer has moved. The T-shirt sizes per wave (§16) tell you roughly how long that will be.

### 17.2 Waves gate on releases, not just on success

- Today the wave gate opens when the previous wave **Succeeded**. In practice the next wave can only build once the new parent or library version **exists in the Maven repository**.
- The gate becomes **release-aware**: the previous wave's PRs are merged, and the new versions are published.
- Each consumer's route then gets a deterministic first step that moves its parent or library dependency to the released version. OpenRewrite has recipes for changing a parent version, so that step needs no AI.
- The estate is **re-assessed after each wave**, which catches repositories that changed in the meantime.

### 17.3 Services keep shipping: migration branches are regenerated, not maintained

- Feature and support work carries on in `main`, on Boot 2.7, as normal. Nobody works in the migration branch.
- The migration branch is **regenerated**, not rebased. On each new commit to `main` (or nightly), the pipeline re-runs from that commit and force-pushes the branch: recipes, then recorded decisions (§12), then ports. It already rebuilds the branch from scratch on every run.
- This is why durable fixes matter. Recipes and decision records reapply deterministically on every refresh. Only genuinely new code needs porting, and it shows up as a fresh failure on the next refresh, not as a merge conflict months later.
- A refresh reports **drift**, for example "3 files changed on `main` since the last refresh; 1 needs porting".

### 17.4 Cut-over, one service at a time

1. The PR is green, reviewed, and its decisions are made.
2. Refresh it on the latest `main`.
3. If the current release is still in production, create a `release/2.7` branch for critical fixes only.
4. Merge, and release as a new major version.
5. Fixes made on `release/2.7` until the new version is deployed are forward-ported to `main` (or simply picked up by the next refresh), and the branch is retired.

**The guiding principle:** people own merging, releasing and retiring versions. The pipeline keeps every migration branch current, says when the next step is possible, and puts the cut-over checklist in each PR. That keeps the "never merges by itself" rule, while making a long migration workable alongside ongoing support.

### 17.5 Persisted estate state: waves survive between runs

A migration takes months and many runs, so the plan and its progress must outlive any one run. Workflow artifacts can't do this: they expire, you need the previous run's id to fetch them, and they have no history or review. Today each run starts with no previous results, so every run would open wave 1 again.

**Where:** a dedicated **state repository** per estate (for example `migration-state`), separate from the tool's code. One tool serves several estates, each with its own access. Git gives history, diffs, review and an audit trail without running a database.

```
migration-state/
  estate/assessment.json              # the assessment the waves came from
  waves/wave-1.json                   # members, each pinned to its assessed commit; gate status
  repos/orders-service.json           # latest state: status, verdict reasons, PR, branch, run id
  decisions/D-orders-service-1.yaml   # decision records (§12)
  runs/ledger.jsonl                   # one digest per run (§10.4)
```

**Store only what can't be derived.** Stored status is a cache; facts win. Every run starts by **reconciling** the stored state with the facts:
- whether each wave's PRs are merged, from GitHub;
- whether the new parent or library versions are published (the release-aware gate, §17.2), from the Maven repository;
- whether a repository's default branch has moved past its assessed or refreshed commit. If it has, the repository is due a refresh (§17.3), and a structural change (a new module, a new dependency on another repository) means it is re-assessed.

Decisions, approved plans and the ledger exist nowhere else, so they are stored, never derived.

**The run cycle:**
1. Check out the state repository and reconcile.
2. Pick the open wave and the repositories in it that are due.
3. Run them.
4. Commit the new results, the repository states and a ledger line in one commit, and push.

Only one transform per estate writes at a time (a workflow concurrency group). A rejected push is retried after a rebase; the files are per repository, so concurrent edits rarely touch the same file.

**Approval is a merge.** A new or changed assessment is proposed as a pull request on the state repository. Merging it is a person approving the waves and their membership. A decision record is added the same way. The pipeline never merges these either.

**Identifiers:** every file is keyed by stable IDs (§7): repository name, wave number, decision `D-…`, run id. A file can be read on its own, and a reviewer can follow a repository's whole history with `git log repos/orders-service.json`.

## 18. Non-goals

- **Autonomy beyond proposals.** The AI never applies, merges, pushes or decides success, never picks a problem's kind (outside `K-UNKNOWN` suggestions), and never edits tests, POMs or CI.
- **Agent-written policy.** An agent may *propose* a recipe, mapping, probe or rule. It becomes policy only with a fixture, a replay diff and human review in a commit.
- **A complete behaviour proof.** The contract covers what is recorded, and grows as reviews find gaps.
- **Hiding non-determinism.** Variance between runs is recorded and reported, never averaged away.
