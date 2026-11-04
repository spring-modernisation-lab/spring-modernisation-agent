# spring-modernisation-agent

AI-assisted migration of a Spring estate from Spring Boot 2.7 / Java 11 to Spring Boot 3.5 / Java 21.

> **Deterministic tools make the changes they reliably can; a bounded AI ports what is left, one problem at a time, against a behaviour contract recorded on the baseline; facts decide what needs a person; and Maven plus that contract decide what works.**

**Status:** the pipeline below is built and proven in CI (runs 11–24). Its remediation step is being redesigned (Phase 8): the open "make Maven green" loop kept finding new workarounds. It is being replaced by a recorded behaviour contract, problems classified by kind, scoped AI ports and durable fixes. See [`docs/system-design.md`](docs/system-design.md) §2–§5.

## Where to start

| You want to… | Read |
|---|---|
| Understand the system: layers, identifiers, verdicts, policy, accretion | [`docs/system-design.md`](docs/system-design.md) |
| Know what is built and what is next | [`tasks/todo.md`](tasks/todo.md) |
| See what each CI run showed, and the demo traps | [`tasks/demo-traps.md`](tasks/demo-traps.md) |
| Avoid mistakes already made | [`tasks/lessons.md`](tasks/lessons.md) |

## How it works

| Step | What happens | Who decides |
|---|---|---|
| **Assess** (`assess`) | Scans the estate, works out dependencies and waves, and checks the baseline builds. The AI writes the strategy document. | Deterministic rules; the AI only explains |
| **Transform** (`transform`) | Clones each repository in the next wave at its assessed commit, runs the OpenRewrite recipes for its route, and validates with Maven. Every stage is recorded as a patch. | OpenRewrite, then Maven |
| **Remediate** (part of `transform`, opt-in) | When validation fails, the Remediation Agent reads the evidence through read-only tools and proposes edits, or a decision. Guardrails decide what may be applied; Maven re-validates. | The proposal comes from the AI; policy and Maven decide |
| **Report** (`report`) | Writes the migration results document. | Deterministic, plus labelled AI analysis |
| **Hand over** (`publish`, opt-in) | Writes a reviewer summary and opens or updates a **draft** pull request on `migration/spring-boot-3.5-java-21`. It never pushes to the default branch and never merges. | A person |

Each repository ends in one of four states: **Succeeded**, **Needs decision**, **Failed** or **Not run**. Only *Succeeded* opens the next wave. A repository that still uses a library Spring Boot 3 dropped (for example Ehcache 2) is *Needs decision*, whatever Maven says.

## Key findings (CI runs 11–24)

The demo repository, `orders-service`, has six seeded traps (T1–T6, catalogued in [`tasks/demo-traps.md`](tasks/demo-traps.md)).

**1. OpenRewrite does most of the work, and says where it stops.** It changed 7–8 files, reported about 3 hours of work saved, and wrote its own "Migrate manually" marker where it gave up. It left 12 compile errors in three areas (Hibernate `UserType`, HttpClient 5, the Spring Security adapter) and two problems that only appear at runtime.

**2. The AI can do the long tail.** In run 14 it fixed all five code traps in 4 attempts (about 138k input tokens) and Maven passed. It needed real library signatures to do it: without a `describe_class` tool it guessed HttpClient 5 methods three times (run 13).

**3. Asked to "make the build green", the AI repeatedly found ways around the rules instead of stopping.** T6 uses Ehcache 2, which Spring Boot 3 dropped. The right answer is "a person must choose a replacement". Instead the AI found a new workaround in almost every run, each time getting past the rule written after the previous one:

| Run | What the AI did | Why it was a problem |
|---|---|---|
| 16 | Wrote its own 146-line cache. Its expiry was short only when the **test's** config file was loaded | It gamed the test: production would have behaved differently |
| 20 | After "no new classes" was added: made an existing class implement `CacheManager` and returned an **anonymous** `Cache` | The same component, in a shape the rule didn't see |
| 21 | After "no anonymous classes": **deleted `@Cacheable`** and wrote caching by hand inside the service, then set **`spring.cache.type: none` in production** | Removed behaviour, and switched production caching off |
| 23 | Added a fallback cache with no expiry or size limit, then a hand-written cache that parsed `ehcache.xml` itself. Reported **"Fixed"** | A green build with the library still unsupported. It also exposed a bug in our own guardrail (comparisons like `ttl > 0 … age < max` were mistaken for generics, hiding the code between them) |
| 24 | Stopped for a decision correctly, but included a `javax.servlet` "fix" that still failed | The run kept a change that fixed nothing |

A green build also hid real losses: in run 14, a `POST` to `/api/orders/` (trailing slash) bypassed the operator-only rule, and the HTTP connection time-to-live was dropped, in runs 14 and 24 alike.

**4. Adding a rule per workaround never converged; facts did.** Each new rule held for one run. What ended the Ehcache series was an invariant the AI can't argue with: it may not edit `pom.xml`, so if the POM still declares an unsupported library, a green build cannot mean it was migrated. The repository is "Needs decision", whatever Maven says. Every rule change is now checked by replaying the real proposals from past runs, both ways: the bad ones must be rejected and the good ones still accepted.

**5. Recurring AI fixes should become recipes, but only after measuring them.** The AI fixed T3 (`javax.servlet` names in strings) by hand in four runs and broke it in a fifth. An OpenRewrite recipe now does it deterministically, producing a file byte-identical to the AI's correct fixes. Recipe discovery then found an existing recipe for T1 (`MigrateUserType`), but measuring it showed it fixes 3 errors and introduces 4. A recipe that looks like a perfect match still has to be measured.

**6. Results vary between runs.** The same code gave Fixed (run 14), AttemptsExhausted (runs 20 and 21) and Needs decision (runs 22 and 24). A run costs roughly 0.17–0.7M input tokens. One run is one sample.

**What changed as a result** (the Phase 8 redesign, [`docs/system-design.md`](docs/system-design.md)):
- a behaviour contract is recorded on the baseline, so "green" has to mean "behaviour kept";
- problems are classified from facts, so a dropped library goes straight to a person and never reaches the AI;
- the AI ports one scoped problem at a time, and its fix is reverted unless that problem's errors disappear;
- accepted AI fixes are turned into recipes.

## What the AI may and may not do

- **May:** read the workspace, OpenRewrite's patch, diagnostics and real library signatures; preview its edits against the same guardrails that apply them; propose edits to `src/main/**` (`.java`, `.kt`, `.properties`, `.yml`, `.yaml`); ask for a decision.
- **May not:** edit tests, `pom.xml`, CI or build files; add new types (named or anonymous) or new supertypes; write code against unsupported libraries; remove behaviour annotations such as `@Cacheable` or `@Transactional`; change existing settings' values; refer to test files from production code; decide that anything succeeded.

## Running it

CI: run the **Spring modernisation** workflow (`.github/workflows/portfolio-transform.yml`) with these inputs:

| Input | Default | Effect |
|---|---|---|
| `remediation` | off | Run the Remediation Agent on validation failures (Azure AI Foundry over GitHub OIDC) |
| `remediation-max-attempts` | 3 | 1–5 attempts per repository |
| `open-draft-pull-requests` | off | Publish reviewer summaries as draft PRs (uses the `MIGRATION_GITHUB_TOKEN` secret) |

Artifacts per run:
- `transformation-results-<run>`: results JSON, `transformation-summary.md`, `patches/<repo>/*.patch`, `summaries/<repo>.md`, `pull-requests.json` and the results document;
- `spring-modernisation-assessment`;
- `baseline-build-results`.

Locally: `dotnet test`, then `dotnet run --project src/SpringModernisationAgent -- <assess|transform|report|publish>`. The commands read and write `Artifacts/` in the working directory.

The planned control-plane commands (`run start`, `run status`, `run fetch`, `digest`, `explain`, `replay`, `ledger`) are described in `docs/system-design.md` §10 and tracked as Phase 8 in `tasks/todo.md`.

## Configuration

| Variable | Used by | Meaning |
|---|---|---|
| `FOUNDRY_PROJECT_ENDPOINT`, `FOUNDRY_MODEL`, `FOUNDRY_AUTH_MODE` | AI agents | Azure AI Foundry project, deployment (`spring-migration-strategy`, currently gpt-5-mini) and auth mode |
| `MIGRATION_REMEDIATION`, `MIGRATION_REMEDIATION_MAX_ATTEMPTS` | `transform` | `enabled` to run the Remediation Agent; its attempt limit |
| `MIGRATION_PUBLISH_PULL_REQUESTS`, `GITHUB_TOKEN`, `GITHUB_ORGANISATION`, `MIGRATION_WORKSPACE_ROOT` | `publish` | Draft PR publishing |

Routes, recipes and validation checks per repository kind: `src/SpringModernisationAgent/Config/spring.yaml`.

Pipeline-owned OpenRewrite recipes (durable fixes, for example `lab.jakarta.ServletStringLiterals` for `javax.servlet` names in string literals): `src/SpringModernisationAgent/Config/recipes/lab-recipes.yml`. A route uses one with `configuration: recipes/lab-recipes.yml` instead of an artifact.

Libraries Spring Boot 3 dropped, with their manual steps: `src/SpringModernisationAgent/Config/knowledge/unsupported-libraries.yaml`. Adding a library is one entry, with no code change; the file's header lists the fields and how each is used.
