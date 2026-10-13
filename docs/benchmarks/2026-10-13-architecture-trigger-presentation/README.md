# Trigger-index reuse and national skill presentation

The whole-catalog dependency compiler now uses its existing trigger-operation
index to detect the ending-turn damage marker. The same index construction is
performed once, before the synthetic trigger value is compiled. Activation
operations remain separate. An unowned mandatory marker still enables the
damage value without a condition-value node; a separate plain registry does
not inherit it. These boundaries are asserted in the retained metadata check.

Current Slash-fire capability uses its owner's existing instance-trigger bucket
for `CardUseCommitted`, then the trigger's compiled operation facts. The nearby
offer query also uses those facts. Owner life, exact committed action, current
targets, attack, conversion policy and dynamic legality checks remain live.
The advanced activation target gate receives the caller's existing features
instead of scanning the activation again. It still checks current range and
per-target turn usage. The gate is retained because supported configurations
can use `DamageFarthestCharacter` with an initially selected target; a current
zero-target content entry alone does not make this supported path dead code.

National skill presentation removes the single-use `FilterOwnedSkillIds`
helper and its intermediate array copies. It filters directly with the same
ordinal owned-ID set before the final skill-card array is materialized. Null
owned skills still include all printed skills; an empty owned list includes
none. Null IDs, duplicates, case sensitivity and printed order keep the prior
semantics. Snapshot-reference refresh and unchanged-row reuse remain intact.
This is array materialization; no new immutable wrapper is claimed.

Five production files and one retained Core check changed. Production code
decreases by 13 lines. No new registered check, test framework, runner switch,
runtime state, persistence format or rules version was added by this batch.
The review's null/duplicate/order edge cases are static equivalence evidence;
the existing WPF checks provide real national privacy/reveal and row-stability
evidence, without claiming a separate direct assertion for every static case.

Twelve focused Core and two WPF checks passed in 21.394 seconds. The measured
routine passed 558 Core and all 75 WPF checks in 90.494 seconds. Build took
1.264 seconds, Core 79.864 and WPF 88.903; test scopes ran in parallel and their
durations must not be summed. All 2,325 captured inputs were unchanged during
that run, and the six owned sources match the run's manifest. The minute target
remains unmet. No isolated performance gain is attributed to this run duration.
Earlier concurrent named-ambush failures now pass in this captured routine.

`summary.json`, `evidence-manifest.json` and `evidence.zip` preserve recovery
sources, exact diffs, peer reviews, source manifests and measured logs. Existing
verification outputs were reused; their earlier `blocked by policy` deletions
were not retried. No new build-output directory, Clean, Rebuild, manual Full,
foreground desktop acceptance, release, commit or push was performed.

After ownership, link, consumer and archive checks, automatic approval rejected
deletion of this task's exact disposable iteration12 directory as
`blocked by policy`. Its 2,467,911 bytes remain on disk. The exact path is queued;
deletion was not executed or retried. Preconditions and rejection are recorded
alongside the summary, without claiming that space was reclaimed.
