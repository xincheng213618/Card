# Empty binding storage and one seat-cache entry

`MatchSkillBindingIndex.cs` removes 28 production lines. The shard and its
successful rebuild count now share one seat-keyed cache entry. The count is
read after the rebuild succeeds, preserving nested successful rebuilds and
failure behavior. Trigger buckets share one construction pipeline; numeric
modifier buckets use the same freezing helper. Instance order, the first
ordinal skill instance used by unique triggers, and read-only bucket arrays
remain unchanged.

Truly empty shards now allocate only a shard with its own state stamp and
share immutable empty collections. Ordinary enabled skills without programs
still use the normal constructor, retain their definitions and grant identity,
and remain distinct from empty shards. Null string-key failures still use the
dictionary's original argument boundary. Previously prepared shards remain
unchanged when a current owner's grants change. This structural batch does
not change the rules version, replay format or stored configuration.

The retained shared Core fixture adds assertions for empty owner separation,
read-only definitions, null keys, plain grants, unsuccessful rebuild counts
and nested successful rebuilds. Five architecture checks passed in 17.201
seconds. Seven concurrently developed phase-control checks and the same five
checks subsequently passed together. Peer review found no behavior regression.
Failure-cache object identity and an inner success followed by an outer failure
were inspected statically and are not directly asserted by the new helper.
No new registered check, fixture framework or runner switch was added.

An eight-sample component probe on .NET 8.0.31 used the same warmed reflection
invocation of the internal empty factory, with 1,000 calls per sample. All
baseline samples allocated 1,520 bytes per call; all candidate samples allocated
208 bytes per call, saving 1,312 bytes (86.32%). Median time per 1,000 calls was
1.045 ms and 0.11685 ms respectively. These measurements cover that factory,
including identical invocation overhead, and do not measure a complete match,
screen frame rate or routine verification speed. Exact assembly identities,
probe source and samples are archived in the evidence.

The initial routine recorded 545 Core passes and 10 failures, plus 73 WPF
passes and two failures, in 107.005 seconds. Those results remain separate
from later rechecks. One compile attempt had previously stopped on an invalid
raw interpolated fixture string; the string assembly was minimally corrected
without changing the test JSON intent.

Four phase-control failures executed an older compiled lifecycle dispatcher:
the actual test-directory Core PDB's lifecycle checksum differed from the
current source, while its binding-index checksum matched this batch. Refreshing
only the lifecycle file's last-write timestamp and performing a normal build
made its PDB checksum match the source; all seven phase-control checks then
passed. No Clean, Rebuild, source replacement or blocked-output deletion was
used. The exact dependency hashes and checksums are in the peer report.

The WPF opening fixture now answers a real private faction-selection prompt
through its existing UI command before waiting for opening cards. It asserts
the same engine and exactly one command with the actual actor, revision,
prompt and choice. The existing navigation, timer and Ctrl+Enter assertions
remain intact. Its focused recheck passed. The unchanged actual mouse-capture
check also passed alone; the initial failure's transient cause remains unknown.
This is loaded WPF evidence and not foreground hardware acceptance.

The final routine passed 554 of 556 Core checks and all 75 WPF checks in
116.186 seconds. Its two remaining named-ambush failures concern delayed/card
completion and paid source removal after real owner death. Build took 17.109
seconds, Core 86.806 and WPF 98.702; the two test scopes ran in parallel and
their durations must not be summed. The one-minute target remains unmet.
All three owned production/test files match that run's inputs; a concurrently
edited named-ambush fixture changed during the run and needs subsequent
validation. The routine is not claimed to be fully green.

`summary.json` records the final routine scope, timings, remaining failures,
source drift and retained paths. `evidence.zip` contains recovery sources,
source hashes, exact diffs, peer reviews, probe instrumentation and logs.
Concurrent named-ambush work and its failures are preserved as integration
evidence. No manual Full run, release, commit or push was performed.

Automatic approval rejected deletion of this batch's allocation-probe output
as `blocked by policy`. Its exact path is queued and its parent is protected
against recursive deletion. Earlier rejected verification outputs were reused
without retrying their deletion. Necessary recovery evidence is retained for
the continuing architecture goal; no reclaimed-space claim is made.
