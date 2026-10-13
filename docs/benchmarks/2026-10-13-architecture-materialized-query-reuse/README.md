# Reuse materialized choices and movement queries

Three remaining committed event projections now wrap each newly materialized
choice array directly with Array.AsReadOnly. Per-choice Cards, Targets,
ContentIds and Parameters still pass through PromptChoiceProjection.Freeze;
other fields and the command preparation/commit/recovery boundary remain.
This removes a second outer reference-array copy for nonempty choices.

PlayerViewProjector reuses its private materialized program-reveal array at
the intermediate branch. Its final friend-card concatenation, distinct-card
materialization and FreezePlayerView remain. Pindian/program/public-draft/
FireAttack priority, ordering and viewer privacy are preserved. The private
array is not exposed as an unfrozen public result.

Actual hand gains compute the final ordered movement once per qualifying
card group. The incoming-source Any remains first, and OrderBy(Sequence).Last
retains its original tie behavior. Final hand/location qualification and
sorted original movement indices remain. No new cache or rule state was added.

The WPF prohibition label checks the three distinct Slash kinds through Count
and membership instead of building and sorting temporary arrays. Its fallback
reads the same catalog display name directly instead of constructing a Card.
Fallback order, duplicate/extra-kind handling and invalid-kind exceptions
are preserved. This label has static equivalence evidence; no direct
registered test for the three-Slash folding scenario is claimed.

These four production files add three lines while reducing repeated work.
The public ChoiceSnapshot getter and eager multi-target capture remain because
their binding, identity and evaluation boundaries have actual consumers.
No rules epoch, stored format, content version or registered check was added.
These are structural reductions, without an isolated allocation-byte or
latency measurement.

The initial focused run passed 20 Core and two WPF checks, with one WPF failure.
Its existing layout assertion counted every SkillChoice as a central button,
although target choices already use portraits and CenterSkillChoices excludes
them. The check now counts the actual central choices and also executes the
real portrait and confirmation bindings on a restored checkpoint. Draft
selection preserves all four safe views, revision, commands, events and card
movements; confirmation submits one exact AnswerPromptCommand and reaches
the unpaid discard follow-up. The original draw, public-face, quota, persistent
limit and cold-restoration assertions remain. No product routing changed.
The repaired check passed its isolated recheck in 27.486 seconds, including
23.959 seconds of build and 3.286 seconds of WPF execution.

The first routine passed 580 Core checks with one failure and all 76 WPF checks
in 94.985 seconds. This newly selected death-recipient scenario was not in the
preceding batch's routine scope. Its source had also changed between batches;
the preceding result does not establish that this scenario previously passed.
Core Program.cs changed during this first run, so final verification and
source attribution are recorded separately below.

The current parallel ExtraEffects fixture passed its death-recipient case in
a subsequent 14-check family run. That run passed 13 and failed the newly
registered claimed-global-material/cold-counterspell case, reporting
`Resolution frame 28 is not the top Damage frame` from ApplyAttackDamage's
finally block. The first routine did not include this new case. This work
made no ExtraEffects fixture or production repair; the changing source is
archived with explicit parallel attribution. The earlier enemy-AI cancellation
hypothesis was withdrawn: the current branch selects First without an enemy
preference, and the observed trace did not prove that hypothesis.

The completed routine result remains 580 Core passed/one failed and 76 WPF
passed in 94.985 seconds. The subsequent family recheck took 16.575 seconds
(13.983 build, 2.370 Core). No final all-green current-tree claim is made.
The five owned source files match both the routine's captured input/final
hashes and the current files. Whole-tree drift is recorded separately because
parallel registrations and fixtures continued changing. The remaining new
frame-ownership failure is retained for the next architecture slice; the
strict top-frame invariant has not been suppressed.

Recovery sources, owned diffs, peer reviews, failed-run logs and verification
input hashes are retained in the evidence archive. Existing verification
outputs are reused; previously policy-blocked deletions are never retried.
No manual Full, foreground desktop acceptance, publishing, commit or push
is part of this batch. Routine timing is not a controlled speedup measurement.

The final input discovery records ongoing parallel source changes and a new
GameEngine.ExtraEffectMaterialClaims.cs file. Those later changes are not
covered by the completed routine or family run; they require the next
focused verification. No all-green claim applies to that later tree.

After owner, strict path, link, consumer and verified-archive checks, automatic
approval rejected the single exact iteration16 directory deletion as
`blocked by policy`. Its 9,773,993 bytes remain. The exact retained path is
`C:/Users/17917/AppData/Local/Temp/Card-architecture-01a13336/iteration16` and
is registered for cleanup. Deletion was not executed or retried. Prior
blocked verification paths retain their no-retry queue reasons.
