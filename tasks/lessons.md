# Lessons

Rules learned while building and driving this pipeline. Each lesson names the run that taught it. Read this before changing policy, the agent's instructions, or the publishing path.

## Above all

**When every fix creates the next problem, stop fixing and redesign.** From R16 to R24 each run produced a new workaround and each response was one more rule. The cause was the design: an optimiser aimed at "Maven green", which didn't mean "behaviour kept", with every kind of problem in one loop. The answer was to change the target (a behaviour contract), the routing (problem kinds) and the learning (durable fixes); see `docs/system-design.md` §2.

## Policy and guardrails

1. **After a guardrail is bypassed twice, look for an invariant instead of another shape rule.** In R16–R23 the agent re-implemented Ehcache 2 five ways; each shape rule held for one run. What ended it was a fact the AI cannot change: the POM still declares the library, so a green build cannot mean it was migrated (`I-UNSUPPORTED-NEVER-SUCCEEDS`).
2. **Prove every policy change by replay against recorded proposals, both ways.** The bad proposal must now be rejected, and every legitimate one (R14 attempts 1–4 especially) must still be accepted. A unit test alone missed R23.
3. **Normalisation steps in a checker are part of the checker, and need their own adversarial tests.** R23's bypass was not in the rule but in the pre-processing: stripping `<…>` as generics also deleted `ttl > 0 … age < max` and the anonymous classes between them. Patterns that delete text must only match what they claim to match.
4. **The AI optimises for a green build, not for the intent.** Expect it to special-case tests (R16), drop behaviour (R21 removed `@Cacheable` and switched production caching off), or swap in a weaker substitute (R23's fallback cache). Write rules about what must not be lost (annotations, settings, libraries), not only about what must not be added.

## Recipes

13. **When the AI makes the same fix twice, look for a recipe.** T3 (`javax.servlet` names in strings) was fixed by hand by the AI in R14, R20, R21 and R23 and broken in R24. OpenRewrite had the building block (`ChangePackageInStringLiteral`) in the core version we already pinned. We had designed T3 as AI work, pinned the recipes in run 1 and never looked again. Recipe discovery (Phase 8.1b) now looks every run.
14. **Read the recipe's source before relying on its name.** `ChangePackageInStringLiteral` keeps the rest of each name (`.http` stays), and the ready-made `JavaxRuntimeTypeNamesToJakarta` is not in the pinned version. Both facts came from the pinned JARs themselves, not from documentation.

## The Remediation Agent

5. **Give it real facts instead of letting it guess.** `describe_class` ended three runs of guessed HttpClient 5 signatures (R13). Telling it up front which libraries are unsupported cut R22 to two attempts.
6. **Don't let a decision hold back, or smuggle in, other fixes: work problems one at a time.** In R22 the agent knew the `javax.servlet` fix but could not send it with its decision. In R24 it sent one, the fix was wrong, and the run kept it anyway. The redesign classifies each problem and accepts a fix only if its own evidence disappears (design §4.1).
7. **Its options are not policy.** R22 suggested a hand-written in-memory cache "for tests". The PR shows fixed, pipeline-written manual steps first, and the AI's options are labelled.

## Running and publishing

8. **A step marked `continue-on-error` hides failures: report them in the job summary.** In R21 the PR update failed with a GitHub 500, but the step stayed green.
9. **Retry only transient errors.** Push retries apply to server errors only; a permission error fails at once, so a bad token is visible straight away.
10. **One CI run is one sample.** The same code gave Fixed (R14), AttemptsExhausted (R20, R21) and NeedsHumanDecision (R22). Judge behaviour over several runs.

## Documents

11. **Keep facts in one place and derive the rest.** `tasks/todo.md` drifted: its status line, an input name (`open-draft-pr`) and an "AI writes the narrative" claim were out of date. Phase 8 generates run tables from the ledger.
12. **Never add AI attribution to commits or PRs in any project.**
