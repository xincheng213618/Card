# Activation selection and movement classifier reuse

Activation card filtering now receives the caller's existing execution plan at
all three entry points: action generation, command validation and active-frame
validation. Five full operation scans use that plan's frozen operation facts.
The conditional discard query searches only the `DiscardSelected` bucket and
still requires `Condition.Kind == Always`. This preserves the distinction
between an unconditional payment and an unchosen conditional branch. Current
card locations, category restrictions, payment legality, effective suits and
the original short-circuit order remain live. The single-consumer forwarding
helper is removed.

Movement classification reuses the existing cached trigger facts for eighteen
pure operation queries. The five-line action-color reward branch is removed
from ordinary movement matching: its descriptor requires
`DiscardPileReceived`, the composition validator rejects other windows, and
every ordinary movement matcher entry excludes that discard-pile path. The
real discard-pile branch remains, including its live history, batch and actor
matching. Existing Lu Zhi checks cover actual response/discard behavior and
reject moving the operation into a `CardsGained` window.

Three production files changed with zero net line growth. No new runtime
state, registered check, test framework, runner switch, persistence format or
rules version was added. This is a measured verification result and a
structural simplification; no isolated command speedup is claimed.

Seventeen focused Core checks passed in 32.922 seconds, including the build.
The measured routine passed 558 Core and 75 WPF checks in 97.878 seconds.
Build took 5.896 seconds; Core and WPF ran in parallel for 80.049 and 91.632
seconds respectively. The one-minute target remains unmet. Of 2,325 captured
inputs, only the cleanup queue changed through the runner's normal output
registration. All three owned sources match their captured test hashes.

The review also records policy and WPF candidates for later work; those
candidate files were not changed in this batch. Before/candidate sources,
exact patches, peer reviews, input manifests and actual logs are preserved in
`evidence.zip` with entry hashes in `evidence-manifest.json`. Verification
outputs were reused. Earlier policy-blocked deletions were not retried. No
manual Full, foreground desktop acceptance, publish, commit or push was run.

After verified ownership, link, consumer and archive checks, automatic approval
rejected deletion of the exact iteration13 directory as blocked by policy.
Its 2,321,086 bytes remain on disk. The exact path is queued; deletion was
not executed or retried. Preconditions are preserved in cleanup-preconditions.json.
