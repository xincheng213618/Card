# Registry and trigger-index reuse

The production dependency index no longer stores trigger-operation membership
twice: the existing frozen operation-to-skill dictionary now answers membership
and ledger capability queries. The complete catalog scope and ordered immutable
skill identities are preserved, with no rules-version change.

The WPF response fixture shares only immutable registries, keyed by response kind
and all six option flags. Its original guards run before lookup, and only
successful builds enter the cache. Engines, options, checkpoints, views and save
stores remain independent. The successful routine log records 33 real fixture
creations across 12 tuples, eliminating 21 duplicate registry constructions. The
cache retains those 12 registries for the test process.

Focused checks passed: seven Core and five WPF checks covering all twelve fixture
tuples, followed by the shared compiled-catalog check. Measured routine checks
passed: 535 Core and 74 WPF, 205.777 seconds including build. All 2,292 recorded
source inputs remained unchanged. No Full run or foreground desktop acceptance
was performed. The construction count is verified; no isolated cache timing or
whole-run speedup is claimed, and routine duration remains above the one-minute
target.

`evidence.zip` contains the before/after source copies, narrow diffs, source hash
manifests, peer reviews and exact verification logs. `summary.json` records totals,
timings, archive integrity and retained temporary paths. A rejected first command
failed PowerShell argument binding before any build or checks; its stale copied
summary was superseded by the real passing invocation and the error is recorded.

Automatic approval rejected deletion of the archived earlier diagnostic output
at `C:\Users\17917\AppData\Local\Temp\Card-architecture-01a13336\iteration7\causal-output`
as `blocked by policy`. It remains registered, together with protected earlier
outputs. No deletion retry or space-reclamation claim was made.
