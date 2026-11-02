# orders-service: seeded upgrade traps

The traps are deliberately documented **here, not in `orders-service`**. The Remediation Agent will have read access to the target repository, and hints there would invalidate the demo.

Route: `spring-boot-application-2.7-to-3` (Boot 2.7.18 / Java 11 → Boot 3.5.x / Java 21).

The "Expected" column is a **hypothesis**. The OpenRewrite probe (`.github/workflows/openrewrite-probe.yml`) decides it; record the actual outcome in the "Probe result" column.

| # | Trap | Location | Why it should hurt in Boot 3 | Expected | Intended demo role | Probe result |
|---|------|----------|------------------------------|----------|--------------------|--------------|
| 1 | Custom Hibernate 5 `UserType` + `@Type(type = "...")` | `persistence/MoneyUserType.java`, `domain/CustomerOrder.java` | Hibernate 6 redesigned `UserType` (generic, `getSqlType()`, new `nullSafeGet` signature). Recipes may rewrite `@Type`, but are unlikely to port the implementation. | Compile failure | **AI fixes** | **Survived.** `@Type` was rewritten to the class form `@Type(MoneyUserType.class)`, but the `UserType` implementation wasn't ported: missing `nullSafeGet(ResultSet,int,…)`, invalid `@Override`s, and the generic bound fails at `CustomerOrder.java:25`. |
| 2 | Apache HttpClient 4 behind `HttpComponentsClientHttpRequestFactory` | `config/InventoryClientConfiguration.java`, `pom.xml` | Spring 6 needs HttpClient 5, and Boot 3 stops managing the HttpClient 4 version. `setConnectionTimeToLive` / `evictIdleConnections` / `RequestConfig` timeouts move to a different API. | Dependency resolution or compile failure, possibly partly migrated | **AI fixes** | **Partly migrated.** Imports moved to `org.apache.hc.client5`, but `setMaxConnTotal` (now on the connection manager) and the related pool/TTL settings weren't migrated, so it doesn't compile. |
| 3 | `javax.servlet` type names as **string literals** used reflectively | `platform/ServletContainerCompatibilityCheck.java` | Import rewriting doesn't touch strings. On Boot 3 the classes are absent, so the startup guard throws and the application context fails. | Every `@SpringBootTest` fails at startup | **AI fixes** (good "recipes can't see this" moment) | Not yet observable (needs a compiling build). |
| 4 | `WebSecurityConfigurerAdapter` incl. `authenticationManagerBean()`, `configure(AuthenticationManagerBuilder)`, `web.ignoring()` | `config/SecurityConfiguration.java` | The adapter was removed in Security 6. Recipes cover common shapes; the `authenticationManagerBean()` override and in-memory builder may not migrate cleanly. | Possibly fixed by recipes, possibly a compile failure | Shows what OpenRewrite *does* well, or an AI fix | **Survived, contrary to the hypothesis.** `WebSecurityConfigurerAdapter` was left in place (the `authenticationManagerBean()` / `configure(AuthenticationManagerBuilder)` shape apparently blocks the recipe). Now an **AI fix**. |
| 5 | Trailing-slash matching relied on by a client | `OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash` | Spring 6 no longer matches `/api/orders/` to `/api/orders`. Restoring it is a deprecated opt-in; the real question is whether the API contract changes for the warehouse integration. | Test fails (404) | **`NeedsHumanDecision`**: an API contract change, not a code bug | Not yet observable (needs a compiling build). |

## Probe log

- **Run 1:** failed before any recipe ran. The pinned `rewrite-spring:6.43.0` / `rewrite-migrate-java:3.47.0` don't exist on Maven Central; fixed to 6.37.1 / 3.42.1, with plugin 6.46.1. The baseline build passed (Boot 2.7 / Java 11).
- **Run 2:** `UpgradeSpringBoot_3_5` applied. The `UpgradeToJava21` run then failed because the plugin's `run` goal compiles the project first, and the half-migrated code doesn't compile (12 errors; findings in the table above). Fixed by switching to `runNoFork` in both the probe and `OpenRewriteRunner` (the real engine had the same bug). The post-migration build did not run.
- **Run 3:** _pending_.

## Expected demo progression (if the hypotheses hold)

1. OpenRewrite migrates versions, `javax.persistence`/`javax.validation` imports, and probably the security config.
2. Validation `compile` fails on traps 1 and/or 2. **Remediation attempt 1** fixes them, and Maven re-runs.
3. Validation `unit-tests` fails on trap 3 (context startup). **Remediation attempt 2** fixes it, and Maven re-runs.
4. `unit-tests` still fails on trap 5. **The agent returns `NeedsHumanDecision`** and explains the options (deprecated opt-in, an explicit extra mapping, or changing the client). No PR is opened; the evidence is shown instead.
5. Optional second run with a human decision applied, then green, then a draft PR.

Consequence for the plan: the remediation loop needs **max attempts ≥ 3**, or a single attempt must be able to address several diagnostics.

## How to run the probe

1. Push `orders-service` to `spring-modernisation-lab/orders-service`.
2. Push this repo (the probe workflow lives here).
3. Actions → **OpenRewrite probe** → Run workflow (defaults: `orders-service`, `spring-boot-application-2.7-to-3`).
4. Read the job summary; download `openrewrite-probe-*` for the patch, logs and surefire reports.
5. Fill in the "Probe result" column, and adjust the traps if OpenRewrite fixes everything (or nothing).
