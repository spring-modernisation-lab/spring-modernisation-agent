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

Libraries Spring Boot 3 dropped, with their manual steps: `src/SpringModernisationAgent/Config/knowledge/unsupported-libraries.yaml`. Adding a library is one entry, with no code change; the file's header lists the fields and how each is used.
