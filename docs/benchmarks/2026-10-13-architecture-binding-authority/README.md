# Enabled-program authority and compiler index reuse

Skill binding queries now use their existing enabled-definition dictionary,
removing the copied program dictionary. They retain owner qualification, exact
program references, ordinal ID/hash queries and source-removal invalidation.
Execution plans detect duplicate triggers through their existing dictionary,
removing a temporary set while preserving early duplicate and null-ID failures.
Maximum policy kind is compiled from the existing whole-catalog policy set.
The owned production change removes five lines and three redundant structures
or traversals.

Five focused checks passed using the retained fixtures, with added assertions
for query and failure boundaries. After concurrent Liuli episode correction,
the five checks plus eight sole-target checks all passed. The final measured
routine passed 542 Core and 74 WPF in 120.339 seconds. Core and WPF ran in
parallel through a concurrently changed runner; this is not an isolated speedup
measurement for the architecture change. The one-minute target remains unmet.

An earlier completed routine had 535 Core passes, one new Liuli episode failure
and 74 WPF passes. Its failure remains recorded separately from the later
passing recheck. Incomplete concurrent end-refill and phase-start-control builds
are also recorded as build failures, with no checks started. Each summary is
the authority for its actual scope; old selected logs copied during a failed
build are not evidence of that failed attempt executing tests.

All five owned source files match the final build inputs. The concurrent
`StopUsingAfterTrickOrHpCount` ledger opt-in was preserved. Seven other Core
files changed during the passing run and still need later validation. No Full
run or foreground desktop acceptance was performed.

`evidence.zip` contains recovery sources, diffs, source hashes, peer reports and
exact logs. `summary.json` records scopes, timings, integrity and retained
temporary paths. Copies of the invoked runner and scope file are source
snapshots; rerun the repository's actual `tools/Test-Changed.ps1`.

Previously rejected output deletion remains protected as `blocked by policy`.
Outputs were reused and their queue reasons restored; no deletion was retried.
