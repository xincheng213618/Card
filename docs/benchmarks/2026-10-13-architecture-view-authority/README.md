# UI query authority and policy reuse

The WPF view now retains one frozen `HumanActionView` instead of separately
storing its revision, legal actions and hand guidance. The engine reference
remains part of the refresh key, so a replacement game with the same revision
still invalidates the preview. Both query methods read the same view. A failed
query leaves the previous cache intact; no extra collection copies are made.
Existing action identities and nested collection freezing remain unchanged.

Active-card selection resolves its current action once after the PlayCard
guard, then uses that action for eligibility, maximum cost and equipment
choices. The single-consumer CardViewModel forwarding overload is removed.
Two old presentation properties and their notifications are removed after
repository searches found no consumers. The actual interface continues to use
`SupplementalActiveSkillActions`, the skill rail and existing button text.
These application view-model properties have no repository plugin contract;
unknown external reflection consumers were not independently verified.

The suit-distance query reads the existing instance-policy bucket instead of
scanning every program and policy. Suit, card kind and condition evaluation
remain lazy. Duplicate grants may repeat an identical false condition; no new
deduplication collection was introduced. The first matching evaluation still
initializes public program boolean states through the existing context query;
later repeated reads are idempotent. The shown-entity offer receives the
trigger already validated by its sole caller, retaining source, instance,
gameplay-hash, window, marker-cost and live turn/hand checks.

Six production files changed, decreasing production code by 13 lines and
reducing the preview cache from four fields to two. No runtime rule state,
registered check, runner switch, rules version or persistence format was added.
No isolated command speedup is claimed.

Ten focused Core and four WPF checks passed in 41.624 seconds. They include
real selection/confirmation, equipment payment, condition costs, action and
guidance refresh after commit, effective-suit distance and cold replay.
The measured routine passed 558 Core and 75 WPF checks in 94.789 seconds.
Build took 3.622 seconds; Core and WPF ran in parallel for 83.934 and 90.823
seconds. The one-minute target remains unmet. Among 2,325 captured inputs,
only the cleanup queue changed through normal runner registration; all six
owned source hashes match the verified run, and no additional input appeared.

Before/candidate sources, exact diffs, peer reviews, manifests and actual logs
are preserved in `evidence.zip` with verified entry hashes. Existing build
outputs were reused; earlier policy-blocked deletions were not retried. No
manual Full, foreground desktop acceptance, publish, commit or push was run.

After ownership, link, consumer and archive checks, automatic approval rejected
deletion of the exact iteration14 directory as blocked by policy. Its
2,924,239 bytes remain on disk. The exact path is queued; deletion was not
executed or retried. Preconditions are recorded in cleanup-preconditions.json.
