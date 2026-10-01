# Runtime refactor implementation record

This file records the migration from the 2026-10-01 architecture plan. The source
of truth for rules and content compatibility remains the code and the content
fingerprint. Version numbers are intentionally not repeated here.

## Ownership and execution path

`GameEngine` remains the public command facade. `Submit` validates the command
boundary. `CommandSessionState` owns revisions, the accepted journal, the command
awaiting commit, and fault recovery metadata. The command pipeline prepares its
final projection and event envelopes before committing the command and events;
observers receive that commit and the caller receives the prepared result.
A preparation or rules exception faults the session; `LastTrustedCheckpoint` exposes
the preceding accepted command prefix when the session was driven through
`Submit`. A faulted session refuses commands and ordinary checkpoint capture.

An infrastructure failure during delivery preserves the already committed
journal and revision. Ordinary observer exceptions remain isolated.

`FrameStore` owns the one resolution-frame list. `GameEngine.Runtime.cs` owns
step dispatch, program and rule-window continuations, and stack transitions.
Pushes require unique identities and live typed parents; replacements preserve
identity and type; completion removes only the expected top frame. Domain
handlers retain their original order. Migrated domains read and write their
frame and have no old pending field or synchronization helper. `CardZoneStore` remains the sole
physical-card movement store.

`ResolutionStack` exposes frames for trusted-host diagnostics only. Frames can
contain private prompt data or hidden skill candidates. Player payloads must use
`CreateSnapshot(viewerSeat)`, never a serialized resolution stack.

`AdvanceEventRulesAndQueueFact` runs the old internal event reactions in their
existing order, then queues a fact for commit. `QueueGameEvent` only queues that
fact. `AdvanceRulesAndPublishState` runs the old deferred cleanup and rule
windows before `PublishState` captures a snapshot. Notification callbacks are
not rules steps.

Player views are produced by the projection code behind the unchanged
`CreateSnapshot` API. Content loading continues through
`EmbeddedSkillProgramCatalog`; operation descriptors and binding validation
remain the authoring and execution authority. Typed instruction slices compile
Draw, Recover, ChangeMaximumHp, and GrowMaximumHpAndHp parameters from existing
JSON without changing the authoring format or content hash.

## Migration ledger

| Area | Current owner | Retired path / remaining work |
| --- | --- | --- |
| Viewer-specific snapshot | `Projection/PlayerViewProjector.cs` | Original method body removed from `GameEngine.cs`; deeper state capture can follow the runtime migration. |
| Embedded skill bundles | `EmbeddedSkillProgramCatalog` | Repeated per-package JSON resource readers removed; package registration and metadata retained. |
| Resolution stack and scheduler | `FrameStore` and `GameEngine.Runtime.cs` | Raw list writes retired; push, identity-preserving replace and top completion use restricted runtime transitions. Actual step/window dispatch and program completion live at this seam. |
| Opaque target-card selection | `TargetCardSelectionFrame` | `_pendingTargetCardSelection`, its resolution class and synchronization helper removed. |
| Judgment and replacement | `JudgmentFrame` | `_pendingJudgment`, its resolution class and step/candidate synchronization removed; card identity is an ID resolved from the current zone. Lightning damage stays on its owning judgment frame until completion. |
| Nullification response window | `NullificationWindowFrame` | `_pendingNullification`, its resolution class and synchronization helper removed; physical cards are resolved by ID from `CardZoneStore`. |
| Fire Attack reveal and suit payment | `CardUseFrame.FireAttackSelection` | `_pendingFireAttack` and its resolution class removed; the revealed card stays in the target hand, with only its ID published after reveal. |
| Completed response return | `ProgramCompletedResponseReturn` on the existing card-action window | `ProgramCardContinuation.CompletedResponse` and its second continuation field removed; Dodge and Nullification return through a typed result after accepted-response observers. |
| Damage trigger window | `DamageTriggerWindowFrame` | `_pendingDamageTrigger`, its resolution class and candidate/step synchronization removed; nested damage resumes the outer frame by ID. |
| Death trigger and cleanup | `DeathFrame` | `_pendingDeath` and `DeathResolution` removed; owner-death and killer-death windows, cleanup card IDs, and typed parent return stay on the frame. |
| Dying response and parent return | `DyingFrame` and `DyingCompletionReceipt` | `_pendingDying`, `DyingResolution`, legacy continuation enum and cursor synchronization removed; completed debt pauses retain only a typed receipt. |
| Cardless program damage | `ProgramSkillFrame.AttackAttempt` | `ProgramAttackHandle` holds only the engine and owner ID; damage modifiers, transfer, chain cursor and dying occurrence live in the frame. Nested damage and faction recovery debt return through typed frame IDs; the old nested-damage and debt sidecars are removed. |
| Card attacks and assistance | `CardUseFrame` and typed continuation records | Attack, Duel, group card, Borrowed Sword, weapon and faction-request pending objects retired. Handles contain only engine and owner ID. Per-occurrence prepared target state, the target cursor, damage facts, and suspension return data are frame-owned. |
| Use lifecycle and target return | `CardUseFrame` | Twelve use-ID sets/dictionaries retired: once-only windows, target adjustment and Yingbo flags, two damage bases, adjusted simple-card continuation and frozen actual-damage participant IDs. State retires when its owning use completes. |
| Selected-card payment and movement pause | `ProgramSkillFrame.SelectedCardPayment` and typed child result | GiveSelected/DiscardSelected no longer use the generic `PendingMovementContinuation` branch; remaining operations keep their unmigrated continuation until their own batch. |
| Internal event reactions | `AdvanceEventRulesAndQueueFact` | Rules no longer advance inside `QueueGameEvent`; the original reaction order is retained. |
| Deferred rule windows | `AdvanceRulesAndPublishState` | Rules no longer advance inside `PublishState`; the original short-circuit order is retained. |
| Command commit and failure | `CommandSessionState` and command pipeline | Projection and event preparation precede commit. Rules/preparation faults expose the preceding trusted prefix; delivery failures preserve visible commits. |
| Command input freezing | `Submit` plus `CloneCommand` | Chained card-conversion source lists are copied before validation/execution and into the accepted journal, so later caller mutations cannot change replay input. |
| Skill operation parameters | existing descriptors plus typed Draw/Recover/max-HP instructions | Other operations still consume `SkillProgramEffect` parameters and should migrate only where a concrete boundary is improved. |

## Verification boundary

The frozen source baseline passed all 198 retained Core checks. Its WPF full
check failed at the missing or invalid original face for Scarlet Blood Sword
(赤血青锋). The initial baseline run took 61.7 seconds: 9.1 seconds to build,
52.2 seconds for Core, and 0.3 seconds until the WPF failure. This is an
existing asset failure, not a runtime migration regression.

Focused checks for projection privacy, embedded loading and hashes, target-card
selection, nested post-movement windows, typed operation parameters, committed
journal visibility, and fault recovery passed during development. A complete
post-migration verification result belongs in the final entry after all domain
changes and integration checks are complete.

After the death and dying frame batch, `./tools/Test-Changed.ps1 -Full` built
successfully and passed all 203 retained Core checks. The wrapper reported
59.1 seconds total: 6.6 seconds build, 52.2 seconds Core, and 0.3 seconds to
the same pre-existing Scarlet Blood Sword artwork failure in WPF. This is an
intermediate batch measurement; later domain and main-worktree integration
changes still require their own verification.

The cardless program damage batch passed all 203 retained Core checks. Its nested
damage fixture now reaches an actual paused child damage instruction before
capturing and restoring a checkpoint.

After card attack, Runtime and command-session integration, all 206 Core checks
passed. This intermediate full run took 83.2 seconds: 8.3 seconds build, 74.5
seconds Core and 0.3 seconds to the existing WPF artwork failure. It exceeds the
daily one-minute target. Card-use lifecycle ledger cleanup and integration with
concurrent main-worktree content still require final verification.

## Final integration and retained scope

The separate combined checkout integrated the concurrent main snapshot captured
on 2026-10-01 at 12:09:44 UTC. Its 263 retained Core registrations and this
worktree's 206 registrations form an exact union of 271, with no missing,
duplicate or restored historical checks. The combined Release build succeeded;
all 271 Core checks passed. The full wrapper took 177.8 seconds (7.5 seconds
build, 170.0 seconds Core), exceeding the daily one-minute target. The frozen
main baseline itself took 203.6 seconds; the differing loads do not establish a
performance improvement caused by this refactor.

WPF has 36 registrations in that snapshot. Its first check failed at the same
Scarlet Blood Sword original artwork, and the remaining 35 checks passed using
the existing start-after runner. This is not a fully passing WPF run.

Standard input (4 boundaries, 114 events) and two real skill fixtures (3 games,
58 accepted boundaries) produced traces byte-identical to frozen main. These
include events, Prompt, RNG, commands, card zones and every viewer projection;
internal frames were retained separately as diagnostics. Content identity was
also byte-identical. The owned patch passed a read-only application check on
the current main worktree. Main was not modified, committed or published.

Integration retained main's response-completion dying/rescue checks, entity
exchange, deck operations, public piles, target contests and runtime returns.
The cancel-all-target fixture found an empty-target Fangtian cursor advance;
its bounded advance is fixed in both combined and original refactor sources.
The original fixture's paid-cost and replay assertions remain intact.

Later main WPF, artwork, documentation and UI-check changes were recorded but
are outside this snapshot's compilation and test acceptance. Core/Core.Tests
had no semantic drift at the final source audit. The patch preserves those
concurrent changes; application checks are not runtime acceptance for them.

The card-use lifecycle markers, adjusted-target return and completed-damage
participants belong to `CardUseFrame` and retire with it. Phase-instance
Slash debit/refund records, skill usage
counts, turn history and grants waiting for the next use have longer lifetimes
and retain their existing owners.

Selected-card Give/Discard payment is the completed operation pilot. Other
program operations still use their existing frame continuation, and named-card
declaration, private top-card viewing and deferred-provider reward chains retain
their existing program-ID stores. Those operations may migrate in later slices;
this batch does not replace every skill operation or the JSON authoring model.
Private top-card data remains subject to viewer projection and must not be
treated as a disposable cache.

Further operation migrations must compare actual event payload and order,
Prompt, Revision, RNG, card zones and every viewer's projection on the same
input; keep intentional internal frame representation differences in separate
diagnostics. A completed child must never repay an already committed cost after
a movement-trigger pause.

## Review repairs and final verification

The repair checkout uses the exact main cutoff captured on 2026-10-01 at
13:30:52 UTC: snapshot `6c3841553689426a50be085dc4fe73b5217d65dd`, tree
`47bdc4e6cd4d3f8f7ef964db1993423b7464fdbc`. It includes the later Lu Ji,
Hao Zhao and Wang Ping content and the deferred-hand-alignment WPF checks.
Changes after this cutoff are recorded separately for the integrating reviewer.

Two review defects were repaired. Prepared player projections now detach and
freeze their nested collections before commit; delivery observers and the
command result share that immutable projection. A selected-card gift resumes
the living owner's program after the recipient dies in a movement reaction.
The new regression replays from a checkpoint before the gift; the existing
atomic-batch check retains coverage for restoring during a payment pause.

The measured no-argument `tools/Test-Changed.ps1 -Verbose` routine passed
91 Core and 11 WPF checks in 55.814 seconds, including its incremental build
(5.138 seconds build, 16.595 seconds Core, 33.975 seconds WPF). It selects the
shared mechanisms and representative UI checks listed in
`tools/VerificationScopes.psd1`. Multiple existing substring filters run their
union once, and explicitly empty filters or an empty scope fail before build.
Routine coverage does not include every general or all AI scenarios. Direct
Core/WPF runners without filters and the wrapper's `-Full` remain complete.

The complete wrapper passed all 293 Core and 38 WPF checks in 223.948 seconds
(1.633 seconds build, 166.629 seconds Core, 55.639 seconds WPF). It retains
all 283 cutoff-main Core registrations plus ten new behavior checks; all
38 cutoff-main WPF registrations remain. This full lane still exceeds the
daily one-minute target. After that run, only the artwork gallery test layout
and its bounds assertions changed. A subsequent WPF build and focused artwork
check passed, and the exported 50-card gallery was visually inspected without
edge or last-row clipping. The routine timing was measured before that
test-layout-only correction.

The two missing weapon assets now use authenticated official sources:
Scarlet Blood Sword uses an official square weapon illustration, uniformly
scaled with transparent padding and a runtime title; it is not a complete
printed card face. Xingtian Axe uses an official 93-by-130 UI face enlarged
twofold without redrawing, so the source's low resolution remains visible.
The 103 original atlas files and four existing project-generated files are
unchanged. Artwork loading, dimensions, identity, interactions and runtime
title placement passed; a separate packaging run was not performed.

Final standard input (four boundaries, 114 events) and real Taoluan/Xin Xianying
fixtures (three games, 58 accepted boundaries) match the exact cutoff byte for
byte in events, Prompt, Revision, RNG, commands, card zones and all viewers'
projections. Content identity also matches byte for byte. Internal frames are
separate diagnostics. These finite fixtures establish their observed behavior;
they do not prove every possible game trace equivalent.

The final report records the owned patch, released source hashes, current-main
drift and read-only application check. The original coordinating chat performs
the final review and integration; this checkout does not write, commit or push
the main workspace.

## Coordinating review and main integration

The coordinating chat independently reviewed both repaired defects, ran their
two focused regressions and the explicit-empty-filter rejection check, and
verified the released raw trace hashes. The approved 182-path patch was applied
to the main workspace on 2026-10-01 at 22:14 Shanghai time. Main HEAD and the
Git index remained unchanged. Existing tracked changes and affected files were
backed up before application; no commit or push was performed.

The guard observed concurrent writes to two unowned WPF files during application.
Neither file is named by the patch; their resulting contents match the validated
cutoff. Their preceding contents remain recoverable from the tracked backup.
All 182 owned paths matched the released implementation after application,
with three documentation/tool files differing only in line endings.

The actual main Release build passed. A clean compilation reported two existing
nullable warnings in GaoDaYiHaoChecks. Main's routine validation then passed
91 Core and 11 WPF checks in 55.763 seconds including the incremental build
(1.779 seconds build, 16.019 seconds Core, 37.884 seconds WPF). A further
unfiltered WPF run passed all 38 checks against the actual main output to cover
the UI/resource changes after the cutoff. The complete 293-Core acceptance
remains the released integration run; it was not redundantly repeated on main.

Review and application evidence is retained outside the repository under
`C:\Users\17917\AppData\Local\Temp\CardRuntimeIntegration-01a0f6ca-20261001T113220-ab6725ea\ROOT_REVIEW_FINDINGS.json`
and `C:\Users\17917\.codex\tmp\card-review-root-20261001`.

## Post-integration review and next slices

The subsequent review found a remaining committed-event output defect: a
subscriber could replace an element of the Five Grains reveal array, changing
the stored event and the payload delivered to a second subscriber. Commit
preparation now detaches and freezes collection-bearing built-in facts through
`Projection/CommittedEventProjection.cs`; rule reactions still receive their
original input before this output projection. Nested policy and deposit lists
are included. Existing immutable `CardActionContext` collections are reused.
The shared real-command regression checks mutation rejection, both observers,
history, exact replay event bytes, all viewer projections and nested source-array
isolation. Rules, revision, RNG, JSON shape and version sources are unchanged.

This review also checked the downloaded, fixed-commit open-source implementations.
Bundle caching, event-window binding buckets and reference-keyed execution plans
already exist in this project and are not unfinished roadmap items. Continue
inside the current solution. The next useful slices, in priority order, are:

1. **Prepare one final player view per command.** `ExecuteCommandOperation`
   discards its private operation's result, while `AdvanceOneStepCore`, `Accept`
   and final preparation can each build a complete projection. `PublishState`
   also prepares a view later replaced before commit. Replace the pre-commit
   pending snapshot with a publication marker, then change the private execution
   contract to avoid unused intermediate results. Preserve the single visible
   commit, final revision, fault-prefix recovery, reentry rejection and observer
   order. Instrument projection counts and compare the same commands/events,
   every viewer and replay; no speedup has yet been measured for this proposal.
   This follows the separation between rule execution and
   [player-view filtering in boardgame.io](https://github.com/boardgameio/boardgame.io/blob/5e9a2c94bde803fae8b081958c406c4d0a7be8ae/src/master/filter-player-view.ts#L19).
2. **Compile registry-wide dependency summaries once.** Trigger-fact capture,
   damage prevention, attributed-marker events and strategic activation checks
   repeatedly scan the complete skill catalog for static condition/value/op
   kinds. Store the same answers on the frozen registry, keeping the current
   whole-catalog meaning. Owner/window-only narrowing can change null fields or
   historical event shapes and requires a separate compatibility review. Compare
   catalog identity, facts and raw replay traces before replacing scans. See
   [freekill-core event buckets](https://github.com/Qsgs-Fans/freekill-core/blob/c19441690711b73ffb427b3e7974ec7e92e33bea/lua/server/gamelogic.lua#L86)
   and noname's registration-time event-interest table.
3. **Reuse compiled bindings and centralize representative legality policies.**
   `ProgramInstructionResolver` already holds binding dictionaries, but the
   executor and program host still perform repeated activation/trigger searches
   and effect scans. Extend the current execution plan with source bindings and
   static features, preserving per-instance eligibility and relative seat order.
   Then add typed legality policies to the existing operation descriptors for a
   small pilot such as hand-required Pindian or self-excluding gifts. Legal action
   generation, final validation and AI should share those policies. Preserve
   priority ties, ChoiceGroup order, death continuation and reference-keyed cache
   isolation. Do not introduce arbitrary content scripts. See
   [freekill-core active skill contracts](https://github.com/Qsgs-Fans/freekill-core/blob/c19441690711b73ffb427b3e7974ec7e92e33bea/ltk/core/skill_type/active.lua#L34).
4. **Finish frame ownership in bounded cancellation slices.** The strategic
   mass-damage queue and deferred-provider rewards can remain in frame-ID stores
   after owner death or skill invalidation cancels their parent program. The
   review identified orphan state, without demonstrating visible wrong damage
   or replay divergence. Move these two stores onto `ProgramSkillFrame` and
   release them on both completion and cancellation; test nested death/skill loss,
   no orphan state and no repeated payment with small fixtures. Afterwards,
   migrate typed Damage/LoseHp parameters and remaining movement returns by
   mechanism. See
   [FreeKill event-owned cleanup](https://github.com/Qsgs-Fans/FreeKill/blob/671b0ad698c36b703808d8b29f3483ce54b36631/lua/server/gameevent.lua#L198).
5. **Declare each content module once.** New general modules still need a central
   Register call, a separate general-pool entry and repeated bundle skill IDs.
   Derive registration and pool membership from one explicit ordered module
   collection; reuse the current catalog loader while retaining explicit skill
   metadata overrides. This improves authoring cost and early validation rather
   than changing the skill JSON model. Prove the same fingerprint, general pool,
   labels, AI weights and duplicate/reference rejection. See
   [FreeKill standard-package composition](https://github.com/Qsgs-Fans/FreeKill/blob/671b0ad698c36b703808d8b29f3483ce54b36631/packages/standard/pkg/init.lua#L10).

The proposals above are static review conclusions, not completed migrations or
measured performance results. Ordinary content still uses content/registration
changes and existing shared checks. The routine lane and complete lane retain
their distinct purposes; reducing runtime projection work is preferable to
removing additional behavior, replay, privacy or movement assertions.

The current submission includes the completed content already integrated with
the reviewed runtime. The next Fenglin batch (Zhuge Zhan, Chen Dao and Sun Liang)
remains in its developing chat. Its three portraits, three pending documents and
four mapping/catalog hunks are retained in the working tree without staging;
mixed files are staged only for the completed content. No whole-file rollback
is used to separate those changes.

## Five follow-up optimizations implemented (2026-10-02)

The five proposals in the preceding review are implemented in the existing
solution. The completed Zhuge Zhan, Chen Dao and Sun Liang batch was first
validated and committed separately, then integrated without replacing its
shared runtime, committed-event freezing or retained coverage.

- Command operations now return no intermediate result. A publication marker
  requests one final output view; command result, publication and observer
  reentry rejection share that prepared immutable view. AI decisions and
  explicit viewer reads still construct their necessary views.
- The frozen registry compiles whole-catalog condition, value, operation,
  trigger-window and card-policy dependencies. Unowned and disabled definitions
  retain their historical contribution to nullable facts and event shape.
- Reference-keyed execution plans retain activation/trigger bindings and static
  operation features. Dynamic eligibility, skill-instance identity, priority,
  relative seat ordering and ChoiceGroup behavior remain runtime decisions.
  Operation descriptors share hand-contest and other-recipient prerequisites
  between legal actions, submitted-input validation and public AI estimation.
- Strategic damage batches and deferred provider rewards belong to their
  ProgramSkillFrame. Child return preserves remaining targets and already-paid
  costs; completion, owner death and skill loss retire the frame's state. A
  repeat whose condition becomes false clears its instruction-owned batch before
  a later damage instruction starts. Damage and LoseHp parameters are compiled
  typed instructions. Remaining movement mechanisms are separate future slices;
  this batch does not claim to eliminate every movement continuation.
- A single ordered list declares 23 general modules. Their actual AddGeneral
  declarations supply their pool membership; RegisterBundle loads each catalog's
  skills without a second membership list. Explicit metadata overrides and the
  historical Fame 2015, Ji Kang and Zhuge Zhan ordering exceptions remain.
  A new module needs its content declaration and one GeneralModules entry, with
  no separate AddedGeneralIds list or per-character version increment.

Independent content probes compared all 17 StandardContentRegistry factories
and four duplicate/reference rejection results against the completed-content
baseline. The complete ordered JSON signatures match byte for byte, including
fingerprints, metadata, presentation, AI weights and mode pools. The classic
catalog has 365 skills, 200 generals and 176 pool entries; the boundary pool
retains 49 entries. Evidence is outside the repository at
`C:\Users\17917\.codex\tmp\card-modules-ed3789e-20261002\equivalence.json`.

TEMP-only instrumentation compared an identical standard command prefix before
and after final-output deduplication. Start used seven player projections before
and one after; the next step used three before and one after. Result JSON, typed
event bytes, notifications, every viewer and replay were identical. These are
counts for the observed commands, not a promise that all AI-driven commands
construct only one snapshot or a controlled elapsed-time speedup estimate.
Instrumentation never entered production source.

Cross-review checked all 66 Accept call sites and the preparation/commit/delivery
failure boundaries. A real conditional-repeat regression was reproduced before
repair: owner HP loss in the first damage child invalidated its parent condition,
then an independent mass-damage node encountered the old batch. The repair
retired that batch, preserving the expected ordered targets [1,1,2,3], four HP
payments and one final draw. Existing normal and skill-loss cancellation checks
remain, with exact checkpoint frame restoration, event bytes and every viewer.
Only seven shared behavior registrations were added; no historical suite was
restored and no rules/schema/package version was raised by these optimizations.

The final combined-source Full wrapper passed 323/323 Core and 41/41 WPF
checks in 111.563 seconds: build 7.747, Core 57.949, WPF 45.828. The subsequent
routine wrapper passed 98/98 Core and 11/11 WPF checks in 38.904 seconds,
including the incremental build (1.267 build, 9.975 Core, 27.601 WPF). This
meets the approximate one-minute daily target on this observed run. Full
coverage still has its separate batch acceptance role. A clean compilation
reported only the two existing GaoDaYiHaoChecks nullable warnings; the
incremental routine build reported none. All tracked source, tests and tools
were SHA-256 checked against the verification snapshot after both runs.

The complete immutable summary copies are
`C:\Users\17917\.codex\tmp\card-runtime-next-20261002\final-full.json` and
`C:\Users\17917\.codex\tmp\card-runtime-next-20261002\final-routine.json`.
The latter does not replace the former; the full build log is also preserved.

An independent TEMP-only trace probe then compared the completed-content
baseline with the final repaired candidate: standard Start/Step, Taoluan,
Xin Xianying and Zhuge Zhan private quota/top fixtures. All 59 accepted
boundaries captured result JSON, exact typed event envelopes, every viewer
and checkpoint replay events/views. The complete raw trace files match byte
for byte (SHA-256 43B0C647EA7DA17C86555E2B31F79930525B282E7C85348D43369462F3E2391E).
Their four final event counts are 5, 74, 55 and 46. Static cross-review of the
whole-catalog summaries, binding caches, ordering and shared legality pilots
found no additional actionable defect. These finite traces establish their
observed behavior, not equivalence of every possible game or future program.
Evidence: `C:\Users\17917\.codex\tmp\card-runtime-next-20261002\final-trace-verification.json`.
