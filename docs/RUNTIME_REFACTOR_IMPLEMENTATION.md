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

## Extension boundaries consolidated (2026-10-13)

This batch extends the existing frame/runtime architecture. It addresses repeated
observer advancement, distributed continuation lists, three remaining temporary
program dictionaries, and duplicated physical-card submission cleanup. Earlier
sections describe their dated snapshots; the ownership changes below supersede
the remaining-work entries for the three named draft stores.

### Exact observer subtrees

`GameEngine.ObserverSubtrees.cs` supplies the executor now used by all 23 existing
`TryAdvance*Subtree` entry points. A caller still checks the pending decision and
its own observer root before invoking the executor. Root discovery shares the
scan in `FindProgramObserverRoot`; `HasObserverSuffix` shares traversal while
requiring explicit edge and completed-Dying-suffix predicates.

The helper is not a general permission to advance any frame of an allowed type.
Each mechanism retains its original first-child and receipt validation: payer,
skill instance, issued movement interval, parent identity, frozen participants
and paid cost remain domain responsibilities. The root scan retains its order,
and each suffix edge must be accepted before a rescue predicate can prove the
remaining suffix. No default edge policy or rescue bypass is supplied.
Paired-hand recast and outside-phase draw/discard reuse their identical suffix;
their distinct first-child/payment checks remain in their own observer files.

The executor preserves the existing differences. Recovery-replacement frames
advance here only for mechanisms that previously handled them. Owned-death
benefit and signed-damage payment retain their exceptions for an unfinished
damage child; the other applicable handlers decline that advance. Recipient
consequences retains its existing ordinary runtime route when an attack has no
return receipt. Native card uses, including the Dying-suit Peach return, retain
their own dispatch. A successful shared advance publishes through
`AdvanceRulesAndPublishState` once, after the actual transition.

### Continuations registered beside their handlers

`GameEngine.ProgramContinuations.cs` discovers `ProgramContinuationAttribute`
on the mechanism's `GameEngine` methods once, builds typed delegates and orders
them explicitly. `GameEngine.Runtime.cs` invokes the resulting catalog at three
distinct boundaries:

| Kind | Position and contract |
| --- | --- |
| `Resume` | Runs for an active `ProgramSkillFrame` before the ordinary `SkillProgramExecutor`. A handler accepts a frame ID or a `ProgramSkillFrame` and returns whether it handled the continuation. The frame adapter obtains the current active frame for each call, so a preceding declined handler cannot leave later handlers reading an older frame object. |
| `Movement` | Runs at the beginning of `ReturnRuntimeProgramMovement`, before the selected-card payment result and the retained obtain/mount payment branches. It receives the caller's captured `ProgramSkillFrame` receipt. |
| `MovementTail` | Runs only after those payment branches have declined, before the generic `PendingMovementContinuation` fallback. It receives the same captured movement-return receipt. It is not automatically called when a `Movement` handler declines. |

Orders must be non-negative and unique within each kind. Their values represent
rule execution precedence, not source-file, reflection or alphabetical order;
changing precedence requires an explicit behavior review. Discovery rejects
duplicate orders and invalid handler signatures. The first handler returning
`true` stops that phase, and rule exceptions propagate without reflection wrappers.
Handlers retain their exact ownership, stage, source and payment validation.
The catalog does not establish those facts on their behalf.

A mechanism using these existing continuation boundaries adds its attribute to
the implementation method, without adding another name to a central continuation
list. This does not remove the need to integrate a genuinely new shared capability
with its descriptor, validation, event projection, parent-return contract or
other affected runtime boundary. Dispatch still traverses the ordered handlers;
this change is an extension and maintenance improvement, not an O(1) lookup or
an asserted runtime speedup.

### Temporary state and physical-card submission

`ProgramChoiceDrafts.cs` places `CardDeclarationDraft`, `ParticipantTopView` and
`StrategicDraft` on their owning `ProgramSkillFrame`. The conversion, deferred
card, strategic and Cao Ren handlers no longer maintain separate dictionaries
for these drafts. Their card/seat collections copy and freeze supplied values.
Removing a completed strategic draft reads the current active frame so a newly
written card-set binding or target set survives the cleanup. The private top-card
view clears before cards leave Processing; remaining cards keep their original
top-first order when returned to the deck.

Player projections continue to filter the private view by its participant;
trusted diagnostic frames must not be sent as player snapshots. Checkpoints
continue to replay accepted commands rather than serializing these drafts as a
new checkpoint format. Longer-lived conversion state and deferred deposits retain
their existing owners.

In `MainViewModel.cs`, ordinary physical play, public target-card choices and
target-combination choices use `SubmitSelectedPlayAction` and `CreatePlayCommand`.
Accepted commands share `ResetPlayCardSelection` and their result refresh. Rejected
submissions retain the selection draft and the caller's applicable rejection
message. Conversion provenance, additional conversion sources, target identity,
prompt identity and revision remain part of command construction. The reset
only owns the physical-card selection; active skills, responses and discard
selection retain their separate lifecycles.

### Extending and verifying these boundaries

For another mechanism, first reuse its existing operation descriptor and typed
frame state. Register a continuation locally only at the applicable phase, after
checking the intended order against existing handlers. Keep the mechanism's
identity/receipt checks and define its exact subtree policy; do not copy a broad
union of other mechanisms' admitted edges. Store a new paused choice on the
owning frame and freeze its nested collections. Cleanup after another operation
replaces a frame must use the current frame to preserve that operation's result.

Ordinary content using existing capabilities still changes content and
registration. Add focused behavior coverage for uncovered behavior or a bug fix,
and shared mechanism coverage when a shared capability changes. Reuse the current
name filters rather than adding character-specific runner switches. Relevant
Core filters include `program continuation registration`, `Observer subtree`,
`frame-owned choices`, `Paired hand recast` and `Outside phase draw discard`;
include affected conversion, deferred-card and WPF checks for their actual scope.
The continuation check covers order, phase separation, short-circuiting, typed
receipts and rejection of invalid registrations. The subtree check exercises
real paid movement, unrelated-edge rejection, pending-choice stability and cold
return without paying again.

During development use the existing PowerShell filter arguments, for example
`./tools/Test-Changed.ps1 -CoreFilter @('program continuation registration', 'Observer subtree', 'frame-owned choices')`,
and the relevant `-WpfFilter` names for UI changes. After the batch run
`./tools/Test-Changed.ps1` without filters
for the measured routine scope in `tools/VerificationScopes.psd1`. Compare event
payloads/order, Prompt, Revision, commands, RNG where relevant, card zones and
every player's projection across the same inputs and checkpoint restores;
record intentional frame-representation differences separately. Verify visible
WPF state transitions for accepted, rejected and stale actions, and distinguish
focused UI checks from foreground-desktop acceptance.

Full verification and unfiltered direct runners remain manual-only and require
the user's explicit request. A routine failure or completion of this batch does
not authorize a full run. Do not increment rules/schema/package versions solely
for this structural consolidation; apply the compatibility criteria in
`AGENTS.md` and keep version sources in their existing implementation files.

Final focused verification passed 19/19 Core and 5/5 WPF checks in 34.689 seconds
(20.412 build, 3.581 Core, 10.373 WPF). The final routine passed 493/493 Core and
74/74 WPF checks in 218.743 seconds (15.134 build, 80.298 Core, 123.001 WPF),
exceeding the approximate one-minute routine target. The initial baseline took
260.188 seconds, with 482 passes and two failures among 484 Core checks, and
71 passes and two failures among 73 WPF checks. Those four opening-selection
and overlay failures were repaired by concurrent work, not this refactor.
Both source and selected check counts changed between these runs, so their
elapsed times do not establish a performance gain from this batch.

A separate control retained the same other source changes and removed only
this refactor. The paired-hand recast Normal scenario compared 27 state
boundaries and six cold restores; SourceLoss compared 22 boundaries and five
cold restores. Across all 49 boundaries, typed events, Prompt, Revision,
commands, movements, zones, all four safe player views, checkpoints and RNG
matched the control byte for byte. Internal frame representations were
deliberately excluded because their ownership and serialization shape changed.
These finite traces support equivalence for the exercised normal and source-loss
returns, not every possible program or game. Verification summaries, logs and
`trace-evidence.zip` are retained under
`docs/benchmarks/2026-10-13-architecture-extension`.

No manual Full run or foreground-desktop acceptance was performed. The routine
results and focused WPF checks establish their recorded scopes only.

Automatic deletion of this task's temporary outputs was rejected with
`blocked by policy`; no deletion was performed and no alternate cleanup was
attempted. The retained output directories below
`%TEMP%/Card-architecture-01a13336` are registered in `tools/cleanup_queue.json`.
The exact paths and retained size are in the evidence `summary.json`. The
pre-change source archive and manifest remain there as recovery material for
the already modified, concurrently used checkout.

## Follow-up simplification: permissions, choices and duplicate state

The generic executor now asks its host two semantic questions: whether the
issued program may continue after owner death, and whether it may continue
after losing its exact skill instance. It no longer exposes thirty separate
mechanism-specific continuation methods. `GameEngine.ProgramContinuationPermissions.cs`
keeps the ordered mechanism predicates; each mechanism still verifies its own
receipt, stage and owner. The original eighteen death predicates and thirty
source-loss predicates retain their order and short-circuiting. Concurrent HP
conditional-grant work adds its own exact receipt permission at the beginning
of the source-loss chain; that addition is preserved and is not attributed to
this structural change. Program lookup/hash validation, metadata permissions
and the paid-child barrier remain in the executor.

Draw advice, gifted-Slash rewards, public-pile cash-out and Liejie reuse the
same paid-damage observer suffix. They still prove different first-child
receipts before traversing it. An admitted edge is checked before a dying
rescue suffix can terminate the traversal. Five nullable per-engine capability
caches now use the existing immutable whole-catalog dependency index; queries
remain catalog-wide rather than depending on currently owned skills.

`PromptChoiceProjection.Freeze` is the common defensive-copy boundary for
prompt cards, targets, content IDs and parameter dictionaries. Snapshot,
committed-event and saved-choice paths use the same implementation. Every call
still creates a fresh choice and fresh read-only nested collections. Eight WPF
prompt-selection routes use `SubmitPublishedPromptChoice`, preserving their
own pending-state predicate and rejection text, current choice reference,
PromptId and Revision. Rejected prompt submissions retain their draft;
accepted submissions refresh from the command result.

Removing redundant state also revealed an actual Luochong bug. Its positional
`DiscardedCardIds` and separately initialized `Discarded` property diverged:
accepted payments updated one while accumulation and the final event read the
other. `LuochongPendingState` now owns one defensive-copy collection, used for
both accumulation and settlement. A real one-/two-card fixture covers each
physical payment, intermediate prompts, parent-frame cleanup and cold replay
before, between and after payments. This intentionally changes the resolution
event for identical content and commands, so the rules version is advanced in
`Replay.cs`. The structural changes do not independently change the rules JSON
schema or content package versions.

The WPF active-skill selection now stores its equipment/program/activation/
foreign-owner/target-contract identity in one immutable object. Seven cleanup
sites clear that object together, and tutorial suspension retains it safely
because the target contract is copied and frozen. Selection still matches a
freshly published legal action; it does not retain a stale `LegalAction` as
authority. An invalid selected identity does not fall back to the first action.
Empty-draft cancellation still retains the selected identity, while physical
card sets, ordered target selections, PromptId and zero-material selection
keep their existing lifecycles.

Ninety-four identical zero-amount owner-only operation parsers now delegate to
`ProgramOperationDescriptorBase.ParseOwnerAlways`. This helper keeps the exact
order of unknown-field validation, reading the operation identity, owner-target validation,
condition parsing and always-condition enforcement. The operation-specific
handlers, resources, capabilities, compilation and AI policies stay on their
existing descriptors. Parsers with a different amount, allowed fields or
validation contract are excluded from this consolidation.

### Follow-up verification and retained evidence

The final focused run passed 56 Core and 10 WPF checks in 59.351 seconds
(19.011 build, 13.196 Core, 26.868 WPF). The measured routine passed 509 Core
and 74 WPF checks in 200.113 seconds (2.784 build, 90.221 Core, 106.791 WPF).
It still exceeds the approximate one-minute routine target. Concurrent source
and routine-scope changes prevent attributing the elapsed-time difference from
the preceding batch to this refactor.

The first Luochong check compiled successfully and failed on the missing
accumulated cards in its final event before the product fix. The final check
passes both one-card exhaustion and two-card completion, mutation rejection
and cold replay. An intermediate focused run had 48 Core passes, one failure
in the concurrently developed HP conditional-grant fixture, and five WPF
passes. That failure preceded its paid continuation; concurrent work repaired
the inserted-phase return to normal flow. The final focused and routine runs
include the repaired HP path. Its behavior change and separate rules-version
increment are not attributed to this simplification.

The owner-parser consolidation removes 450 product lines net; the WPF identity
consolidation removes 37 product lines net. These are differences against the
task's immediately preceding file snapshots, not a count of all changes in
the shared dirty checkout. The parser review records every one of the 94
before/after method mappings and their normalized-body proof. Independent
reviews also checked continuation predicate mapping, observer suffix order,
immutable prompt copies and WPF identity lifetime. The only source changes
after the final verification snapshot are this report's documentation updates.

Focused/routine summaries, the failing pre-fix Luochong log, intermediate
failures, source hashes, narrow backups and static review evidence are under
`docs/benchmarks/2026-10-13-architecture-simplification`. No manual Full run,
unfiltered direct runner or foreground-desktop acceptance was performed.
The WPF results are their recorded automated control and hidden-window scopes.

Earlier automatic cleanup in this task was rejected with `blocked by policy`.
Those same verification outputs were reused, and their deletion was not
retried. The retained outputs and this batch's recovery/diagnostic directory
are registered by exact path in `tools/cleanup_queue.json`; the evidence
summary records their actual disk use. The old pre-change source archive is
also retained as recovery material for the concurrent checkout.

## Follow-up simplification: catalog opt-ins and repeated child state

Four more per-engine catalog capability caches now reuse
`SkillProgramDependencies`: foreign equipment discard, ordinary-trick
nullification protection, range/damage-nature prevention, and the actual Play
use ledger. The ledger opt-in retains its descriptor trait instead of adding
a second list of operation IDs. Policy, trigger-only and activation-only
queries retain their original scopes, including the concurrently added
damage-source hand-category restriction. A shared metadata check compares
the compiled answers with the original catalog predicates and independently
checks unowned policy, activation and trigger opt-ins plus registry isolation.

Tongxie follow-up results now have one canonical list each for used responders,
declined responders and consumed cards. Those lists, remaining responders and
guard preventers copy and freeze inputs at construction and `with` updates.
The existing provenance check also covers accumulated results across actual
use/response children, unchanged earlier state instances, mutation rejection
and cold recovery. Its formal event records and command checkpoint format
are unchanged. The public follow-up constructor/Deconstruct arity and raw
diagnostic frame JSON do change: the three redundant aliases and optional
shadow constructor lists are removed. Historical diagnostic JSON is not a
supported checkpoint migration path; official restore replays accepted commands.

The new Zhuihuan regression uses a four-player, eight-card fixed fixture with
the real skill. It arms a secret choice, records three actual attributed
damages, pays two real discard cards, then pauses both reflected-damage
children through real after-damage prompts. The initial product fails its
active-program invariant because it rewinds the owning instruction to zero
before issuing the first child. That is a behavior defect, separate from
removing redundant list fields, and requires a rules-version change.

The fix uses the existing `ReexecuteParticipantInstruction` path. The parent
retains its committed instruction while the damage child is live; the executor
handles post-instruction windows and eligibility before rewinding and executing
the same instruction. A mechanism-specific receipt check validates the original
turn-start boundary, chosen seat and cumulative list/count structure. It does
not reject dead owners, lost skills, redirected damage or repeated damagers
from different arms; those retain their original rule handling. There is no new
continuation registration, scheduler or parallel pending state. Finalization
clears the receipt and repeat flag together.

Zhuihuan now also owns one frozen list each for remaining damagers, damaged
seats and discard-paying seats. Input copies preserve order and repeated
damagers from different arms. Its record constructor/Deconstruct changes from
five parameters to three and the two old diagnostic aliases are removed;
the formal result event remains unchanged. Construction, `with` replacement,
current diagnostic JSON round trips, attempted mutations and every cold
boundary are checked. The rules-version gate still rejects earlier development
checkpoints after the continuation defect is fixed; no migration or format
change is introduced.

The fault reproduces with a live parent at instruction zero under an actual
damage/window child. After the continuation fix, the two observable boundaries
preceding that fault remain identical to the failing implementation. Then,
with the same rules version and unchanged fixture commands, six boundary
SHA-256 digests match between the fix-only implementation and the collection
simplification. They cover events with typed payloads, all four safe viewer
snapshots, commands, revision, physical movements, zones, checkpoints and RNG.
Raw diagnostic frames are deliberately excluded from the cross-implementation
comparison; same-implementation cold recovery separately includes them. This
is finite regression evidence for this scenario, not proof of all possible
Zhuihuan interactions. The final rules-version increase happens after that
structural comparison and is verified in the final checks.

### Catalog/state follow-up verification and retained evidence

The final focused scope passed 26 Core checks in 22.973 seconds (15.224 build,
7.449 checks). The measured routine ran 519 Core and 74 WPF checks in 193.989
seconds (7.188 build, 87.012 Core, 99.527 WPF): 517 Core passed, two failed,
and all 74 WPF passed. The two failures belonged to concurrently added
forced-phase Wine fixtures. Their latest four-check scope passed in 6.154
seconds (4.033 build, 1.838 checks) after that content work corrected the
Lord's extra initial HP and removed a cancellation fixture whose window the
simple Wine path does not dispatch. The replacement covers real commit,
quota, paid source loss, actor death and cold return; it does not establish
live Wine-effect cancellation coverage. No source in that feature was edited
by this architecture batch. The routine result remains recorded as failed;
the focused recheck is a separate result.

Fourteen of the fifteen recorded product/test/scope files still match the
final verification hashes. The shared test-registration file changed during
concurrent work; the unchanged hashes and current registration evidence are
retained with the logs. The routine remains above the approximate one-minute
target, and these timings do not demonstrate a performance gain. No manual
Full run, unfiltered direct runner or foreground-desktop acceptance was done.

The summaries, red cursor diagnostics, narrow recovery archives, static
reviews, source hashes and six-boundary comparison are saved under
`docs/benchmarks/2026-10-13-architecture-state-ownership`. Its evidence archive
contains 64 files. Previous automatic deletion was rejected as
`blocked by policy`; those same outputs were reused and deletion was not
retried. The exact retained directories and measured bytes are in
`summary.json`, and the verification outputs plus this batch's recovery
directory are registered in `tools/cleanup_queue.json`.

## Follow-up simplification: committed child continuations

Lord-skill projection and gain-phase qualification now compile their exact
whole-catalog opt-ins on `SkillProgramDependencies`. The engine no longer
stores a second copy or rescans those definitions per match. Projection's
dynamic source/HP/death dependencies and invalidation remain in the match;
movement timing keeps its legacy null opt-out and the scheduled Draw actor.
The shared metadata check compares the original predicates and distinguishes
an unowned qualified gain trigger from the same window/operation without its
optional qualification. The unowned pure Lord projection fixture has no
trigger or operation, so those unrelated indexes cannot substitute for it.
The loader fixture construction is shared with the existing ledger checks.

The always-false integer comparison in Liang Xing's counter-Slash guard is
removed. Its singleton selection, owner distinction, liveness, active-use,
prohibition and range checks remain in place.

Two real continuation regressions exposed the remaining host-side cursor
rewinds. A first-node `DamageOtherLiving` program failed during its actual
damage window. Native Luochong's lethal one-HP loss failed while its first
instruction was suspended below the dying flow. Both paths now retain the
committed instruction and use `ReexecuteParticipantInstruction`. The existing
executor drains post-instruction windows, checks eligibility and then atomically
reexecutes the instruction. Typed receipt validation binds each repeat to its
own operation and state; no extra scheduler, pending sidecar or continuation
registration is added. The global committed-cursor invariant is unchanged.

The strategic regression exercises three genuine pauses before damage and
three after damage, using the restored engine after every checkpoint round
trip. It verifies the frozen target suffix, ordered applied damage, all four
safe views, typed events, movements, zones, commands, checkpoints, RNG and a
single later draw. The existing source-loss cancellation and changed-condition
batch-retirement checks are retained. The receipt permits frozen targets that
die and relies on existing damage-return validation for redirection and prevention.

The Luochong fixture reaches one HP through actual game-start HP loss and then
uses the native preparation binding. It covers the owner with an Aichen child
and a different target with real rescue, paying one physical converted Peach.
It checks the pending parent at each dying/rescue boundary, cold recovery,
one HP payment, one settlement, both frame retirements and the next native
damage window's same-round exclusion of the paid option and target. That later
window also exposed a missing `frame-id` on the published decline choice; the
choice now carries the same identity required by the existing strict resolver.
The regression continues through the real decline rather than ending early.

These three fixes change execution with the same content fingerprint, so the
rules version advances in `Replay.cs`. The equivalent catalog moves and dead
condition removal do not separately change content package versions or the
rules JSON schema. Checkpoint/command formats remain unchanged; the existing
version gate rejects older development checkpoints.

### Committed-continuation verification and concurrent source state

The final focused run passed 28 Core checks in 19.966 seconds (15.080 build,
4.650 checks). The measured routine passed 521 Core and 74 WPF checks in
169.049 seconds (6.289 build, 75.705 Core, 86.805 WPF). It still exceeds the
approximate one-minute routine target; concurrent source and scope changes
prevent treating this timing as a performance gain.

The strategic first-operation regression initially failed the real command's
committed-cursor invariant. The Luochong fixture's first attempt failed an
initial-HP assumption; after using the correct native general-HP mode, it
reproduced the cursor defect in the accepted lethal choice. The first
continuation fix then exposed the published decline's missing identity, which
was repaired without weakening the resolver or the subsequent ledger assertions.

After the green routine snapshot, two shared product files changed during
concurrent content work. Thirteen of fifteen recorded files still matched
their verified hashes at closeout. A separate delayed-placement/Core/WPF
recheck could not build: the in-progress sole-target protection branch still
referenced unavailable members, so no checks ran. The successful routine is
evidence for its saved source snapshot, not a claim that the newest entire
checkout is buildable. These integration drafts were preserved; their active
chat status and build failure were inspected rather than overwriting the work.

Evidence is saved under
`docs/benchmarks/2026-10-13-architecture-committed-continuations`, including
red and green logs, source recovery archives, independent reviews, the
concurrent-source diff and the failed recheck summary. No manual Full run,
unfiltered direct runner or foreground-desktop acceptance was performed.
The exact retained temporary directories and measured disk use are in its
`summary.json`. Previous output deletion was rejected as `blocked by policy`;
those outputs were reused without another deletion attempt, and this batch's
recovery/diagnostic directory is registered separately in `tools/cleanup_queue.json`.

## Follow-up simplification: registry-owned catalog facts

`SkillProgramDependencies` now owns the maximum required program rules version
and the two immutable HP-related skill-id sets. They are compiled from the
registry's already frozen definitions once. `GameEngine` no longer scans the
whole skill catalog three times at construction or stores another HP capability
set per match. The original engine rules rejection still happens before match
setup, with the same comparison, exception and message; an empty catalog still
requires version zero. Registry construction itself does not reject future
programs.

The HP sets retain different contracts. The broad sensitivity set includes
suppression-only definitions even when they have no program. The strict
capability set includes only programs with HP-conditioned grants. Both use
ordinal identities and frozen membership. Binding invalidation still tests raw
enabled grants against the broad set. Grant synchronization still tests the
strict set and processes retained source instances after their grants disappear.
Current HP, source identity, quota, qualification, suppression, projection and
shard caches remain match-owned state.

The existing shared metadata check compares all three original predicates in
separate registries. Its small fixtures distinguish an unowned HP-only
capability from a suppression-only definition without a program, check ordinal
identity and empty catalogs, and exercise the original engine rejection with a
program requiring the next supported rules version. Existing HP-threshold,
source-lifetime, paid-child, privacy and cold-recovery checks are retained. This
equivalent metadata relocation does not change command semantics, content
fingerprints or saved formats and needs no separate rules/schema/package bump.

### Registry-fact verification

The focused scope passed 17 Core and one WPF check in 29.129 seconds (19.380
build, 4.196 Core, 5.243 WPF). The measured routine passed 521 Core and 74 WPF
checks in 172.624 seconds (2.119 build, 78.870 Core, 91.336 WPF). The previous
concurrent missing-member build blockage had been resolved before these runs.
Eleven of twelve recorded source/test/scope files matched the routine hashes at
closeout; the separate sole-target feature changed again afterward. All four
metadata implementation/test files and the rules-version source remained
unchanged. The successful results belong to their saved source snapshot.

Evidence is saved under `docs/benchmarks/2026-10-13-architecture-catalog-facts`:
the archive contains nineteen recovery, review, source-hash and verification
files. The routine still exceeds the approximate one-minute target; neither
these timings nor the removed scans establish an application performance gain.
No manual Full run or foreground-desktop acceptance was performed. Task-owned
recovery/diagnostic material is retained while simplification continues. Prior
verification output deletion was blocked by policy and was not retried; exact
retention and measured disk use are recorded in `summary.json` and the queue.

## Follow-up simplification: one authoritative Pindian policy cursor

The maximum-Slash Pindian claim branch now advances only its frame-owned
`PolicyClaims`. It no longer rebuilds a second claimant-seat list at each
prompt or copies the policy index into the ordinary claim index. The shared
claim invariant validates the policy's frozen collection and index first,
then reads that claim's owner directly. It retains the original result,
prompt-choice, revealed-card, processing-zone, parent, privacy and status
constraints.

Ordinary claims still own and use `ClaimSeats` and `ClaimIndex`; their record
API and path are retained. A policy acquired partway through an ordinary claim
can leave those earlier fields populated, but every subsequent policy prompt
and answer uses `PolicyClaims` until completion. There is no fallback to the
earlier cursor, new pending object or synchronization helper. The public
diagnostic field names remain available, while unused ordinary values in a
fresh policy frame remain null/zero. Production checkpoints restore accepted
commands rather than importing trusted diagnostic frames. This equivalent
state consolidation requires no rules/schema/package bump.

### Pindian cursor verification and concurrent legacy regression

Eleven focused Core checks passed in 25.319 seconds (15.702 build, 9.290
checks), covering random and ordinary Pindian, legacy ordering, tied maximum
Slashes, effective ranks, top-card selection and cold returns. A temporary
diagnostic retains the three existing maximum-Slash/random checks and every
original assertion. Seventeen observation digests match exactly between the
saved before/after assemblies: safe views, typed event envelopes, commands,
revision, content hash, checkpoints, RNG state, movements and zones. Raw
diagnostic frames are excluded from the cross-implementation comparison; the
original checks still compare their full frame JSON during same-implementation
cold recovery. This is bounded evidence for those real command scenarios.

The measured routine completed in 192.192 seconds (18.260 build, 80.459 Core,
93.174 WPF): 520 of 521 Core passed, one failed, and all 74 WPF passed. The
failure is the separately changing actual-target/sole-target integration's
legacy Shensu producer assertion. A latest-source focused recheck also failed.
A second temporary diagnostic copies that existing check without altering any
assertion or command. The earlier green assembly preserves a null ActionId and
legacy producer 18; the later assembly issues ActionId 1 with no legacy
producer. Both apply exactly the expected HP change from three to two. This
isolates the failed facts to provenance rather than HP payment. The later
virtual-Slash source has a whole-catalog sole-target capability condition that
also creates actions for legacy instructions; source-level causal isolation
and a compatible integration fix remain outstanding. Neither the new test nor
the in-progress product feature is weakened by this architecture batch.

All seven recorded Pindian implementation/model/version/test files remain at
their focused hashes. Five of 2,280 wider routine inputs changed during the
concurrent work. The successful Pindian checks do not make the newest checkout
or the failed routine green. Detailed source hashes, independent reviews,
recovery files, diagnostics, logs and per-check timing are retained under
`docs/benchmarks/2026-10-13-architecture-pindian-cursor`.

The routine's existing verbose timings contain no duplicated check names.
Core's ten slowest checks account for 15.8 percent of its timed bodies; WPF's
ten account for 41.7 percent. The failure itself takes only 0.135 seconds in
the routine. Reducing its test count does not address the preparation cost.
The next concrete candidates reuse already frozen weapon registries and the
same hand-response Play checkpoint while retaining real loads, independent
engines, cold comparisons and every behavior assertion. No speedup is claimed
for those unimplemented candidates. No manual Full run or foreground-desktop
acceptance was performed.

## Follow-up simplification: reuse immutable verification preparation

Native weapon feedback loads now use the immutable registry already owned by
their real weapon witness. The two weapon paths no longer rebuild an identical
whole-content registry before restoring the same genuine checkpoint. Weapon
identity, native payment/decline, actual loads, cold restores and all five safe
viewer comparisons remain in place.

Hand-response checks prepare their original Play prefix once and share only its
frozen checkpoint and initial serialized boundary. Each route still loads its
own engine and compares it with an independently commanded reference engine.
The earlier reference is later changed by a real Slash, so the later draft
routes compare with the initial frozen string rather than that mutated engine.
All seventeen actual loads are retained; the existing reference-identity cache
now needs two independent baseline preparations instead of three.

A bounded component benchmark built each weapon registry ten times after
warmup. Median redundant construction costs were 6.7232 ms/5,530,852 allocated
bytes for Stone Axe and 7.6115 ms/5,534,744 bytes for Qinglong. The two removed
builds therefore account for about 14.3 ms and 11.1 MB of preparation work in
that snapshot. A thousand reads of the already frozen registry allocated zero
bytes. Both paths retained the same exact content fingerprints. This is a
registry-preparation measurement, not a whole-run or screen-performance gain.

The two focused WPF checks passed, retaining their real hidden-HWND focus,
input, visible-state, privacy, command, payment and cold-recovery assertions.
The measured routine ran 532 Core and 74 WPF checks in 180.679 seconds (14.326
build, 76.649 Core, 89.462 WPF): 530 Core and all 74 WPF passed; two newly
registered sole-target integration checks failed. The earlier legacy Shensu
check and the zero-entity eligibility check passed in this later snapshot.
The two remaining failures concern substitution continuing after game end and
a source-loss fixture not producing its expected actual recovery replacement.
Those concurrently edited feature files and assertions are preserved.

An isolated Core copy independently reproduced the legacy provenance failure
by changing only the action-creation condition from qualified participants
back to the catalog-wide capability. Directly removing the capability path
also prevented the real zero-entity protection receipt. The participant
condition itself was corrected by concurrent project work; this batch did not
overwrite it or duplicate its rules-version change. The first protection
fixture copy had an invalid activation context; the later copy retains the
corrected original method and helpers, while excluding unrelated uncalled
methods that require newer in-progress frame APIs. Its earlier parent-return
failure remains recorded separately from the later passing routine check.

Summaries, narrow recovery sources, exact isolated inputs, causal results and
component measurements are saved under
`docs/benchmarks/2026-10-13-architecture-preparation-reuse`. No assertion,
registered check or routine filter was removed. The routine remains above the
approximate one-minute target; unrelated source/scope changes prevent treating
its total duration as a measured speedup. No manual Full run or foreground
desktop acceptance was performed. Automatic approval rejected the exact
component-output cleanup command as `blocked by policy`; that output was
registered and deletion was not retried. Previously blocked outputs also
remain protected from retries.

## Follow-up simplification: one trigger index and frozen response registries

The trigger-operation membership set duplicated the keys of the frozen
operation-to-skill dictionary. Membership queries and actual-Play ledger
capability detection now use that existing dictionary. The whole-catalog
scope, trigger/activation distinction, ordered frozen skill lists and public
query behavior remain unchanged. The shared compiled-metadata check still
compares every operation with the original definition predicates, including
unowned programs and independent catalogs. This structural change does not
change the rules version.

Stable hand-response fixtures now reuse a frozen registry for each exact
response kind and all six option flags. The original invalid-combination
guards run before lookup; failed builds are never inserted. Every call still
creates its own engine, options, checkpoint, save store and view model and
performs genuine commands, private-view assertions, replay and manual load.
The private cache retains twelve immutable registries for the check process.

The five focused WPF checks covered all twelve tuples and passed, together
with seven sole-target Core checks. The subsequent compiled-catalog focused
check passed. The measured routine then passed all 535 Core and 74 WPF checks
in 205.777 seconds: 7.126 build, 96.871 Core and 101.411 WPF. All 2,292 recorded
source inputs were unchanged through verification. The earlier sole-target
integration failures are resolved in this later concurrent-work snapshot;
their historical failing evidence remains intact.

The routine log confirms all thirty-three independent fixture creations
completed across twelve tuples. Registry reuse therefore removes twenty-one
duplicate constructions without deleting checks or assertions. No isolated
cache timing benchmark was run, so this is a construction-count result rather
than an end-to-end speedup claim. Routine verification remains above the
approximate one-minute target. No manual Full run or foreground desktop
acceptance was performed.

Recovery sources, narrow diffs, peer reviews, source hashes and measured logs
are archived under
`docs/benchmarks/2026-10-13-architecture-registry-index-reuse`. Automatic approval
rejected cleanup of the completed earlier causal diagnostic output as
`blocked by policy`, after its owner, links, consumers and three archived
assembly hashes were checked. That exact path was registered for retained
cleanup; neither its deletion nor earlier blocked deletions were retried.

## Follow-up simplification: one enabled-program authority

Skill binding shards no longer copy their enabled programs into a second
dictionary. Program queries use the already frozen, owner-qualified definition
dictionary, preserving reference identity, ordinal IDs and gameplay-hash checks.
Removing the last source changes the current shard while previously prepared
shards remain intact; an enabled non-program skill still has no program.
Running issued frames retain their separate registry-definition resolver.

Execution-plan construction no longer allocates a second set of trigger IDs.
The existing ordinal trigger dictionary detects duplicates before compiling
the duplicate's instructions. A null ID still reaches the original plan
argument validation. The catalog's maximum card-policy kind is now compiled
from its existing complete policy-kind set, preserving the nullable empty
result and avoiding another scan of every program's policies. Together these
changes remove one retained dictionary, one temporary set and one catalog
scan, with five fewer production lines. They do not change rule behavior.

The retained shared fixtures now assert program reference/hash/ID queries,
duplicate-source retention, final-source removal, old-shard immutability,
non-program queries and duplicate/null trigger failure boundaries. Five
focused checks passed in 18.606 seconds. No new fixture, registered check,
runner switch or test framework was added.

During integration, a newly registered real Liuli reentry check failed after
the third visit to an earlier target. Concurrent feature work corrected its
episode candidate handling and changed the rules version; this batch
preserved that work. All eight sole-target checks and the five owned checks
then passed together. Initial incomplete end-refill and phase-start-control
builds are recorded separately, with checks not started in those attempts.

The final measured routine passed 542 Core and 74 WPF checks in 120.339 seconds.
Its concurrently changed runner executes Core and WPF in parallel: build took
20.214 seconds, Core 90.963 and WPF 99.682, so those steps must not be summed.
This total is not an isolated speedup measurement for these storage changes.
The approximate one-minute target remains unmet. All five owned source files
match the final build inputs, including a preserved concurrent ledger opt-in;
seven other Core inputs changed during verification and need subsequent
validation. No latest-whole-tree green claim, manual Full run or foreground
desktop acceptance is made.

Before/after sources, exact diffs, peer reviews, source manifests, red-green
integration records and measured logs are archived under
`docs/benchmarks/2026-10-13-architecture-binding-authority`. Existing verification
outputs remain protected after their earlier `blocked by policy` cleanup
rejections. This batch reused them and created no new disposable build-output
directory. Necessary recovery evidence remains registered for the continuing
goal; no blocked deletion was retried.

## Follow-up simplification: empty bindings and one seat-cache entry

The binding index now stores a shard and its successful rebuild count in one
seat-keyed entry, removing the parallel count dictionary. It reads the current
count after construction succeeds, preserving nested successful rebuilds.
Trigger buckets share one construction pipeline, and numeric modifiers use
the same read-only bucket freezing helper. Instance ordering and the selected
first instance for unique skill triggers remain unchanged.

Truly empty shards share immutable empty collections while keeping a separate
state stamp for each owner. Plain enabled skills retain their definitions and
grant identities through the normal constructor. Null-key validation, old
shard immutability, failed rebuild counts and nested successful rebuilds are
covered by the retained shared fixture. These changes remove 28 production
lines without changing rules or persistence formats.

Five architecture checks passed. A component probe measured empty-factory
allocation at 1,520 bytes before and 208 bytes after, saving 1,312 bytes per
call across all eight samples. This is a factory measurement through the same
warmed reflection call, not a whole-match or routine-run speed claim.

Integration uncovered a stale compiled lifecycle dispatcher: the actual test
dependency's PDB checksum differed from the current lifecycle source, while
the owned binding-index checksum matched. Refreshing the lifecycle file's
last-write timestamp without changing its content and using a normal build
made the checksum current. All seven phase-control checks and the five
architecture checks then passed together. No Clean or Rebuild was used.

The WPF opening fixture now completes a real private faction-choice prompt
through the existing UI command before waiting for opening cards. It asserts
the same engine and one exact accepted prompt command; existing shortcut and
timer coverage stays intact. The unchanged actual mouse-capture check passed
alone and in the final routine, without establishing the initial failure's
transient cause or foreground hardware acceptance.

The final routine passed 554 Core checks, failed two named-ambush checks, and
passed all 75 WPF checks in 116.186 seconds. The remaining failures concern
delayed/card completion and paid removal after owner death in parallel feature
work. The initial routine's 10 Core and two WPF failures remain recorded
separately. Core and WPF ran in parallel; the one-minute target remains unmet.
The three owned source files match the final build inputs. A parallel
named-ambush fixture changed during that run and needs later validation.

Recovery sources, exact diffs, peer reviews, compiled-source checksums,
allocation samples and red/green logs are archived under
`docs/benchmarks/2026-10-13-architecture-empty-bindings`. Automatic approval
rejected deletion of this batch's probe output as `blocked by policy`; its
exact path and protected parent are queued. Earlier blocked outputs were
reused without retrying their deletion. No manual Full or release was run.

## Follow-up simplification: shared compiled queries and gallery metadata

Registry Set-conflict and expanded-card-domain validation now share one frozen
program array. Both validations retain their full-catalog scope and order.
Hand-comparison quotas and eligibility use the existing compiled operation
buckets; all hand counts, HP, owner identity, history and usage remain live.
The quota query preserves the single-matching-item exception boundary. Public
pile projection resolves a paused instruction once, still using its issued
frame and registry definition rather than the current enabled-owner shard.

Present-empty snapshot dictionaries now use the shared framework read-only
empty dictionary. Null remains null and populated values remain detached
copies. Retained checks cover old-view isolation after input mutation,
rejected dictionary mutation and null-key errors. The gallery removes its
duplicate series DTO and eleven copied records plus one array per view model,
sharing the explicitly frozen presentation catalog. Existing names, IDs,
selection tags and string commands are unchanged; the retained focus check
also verifies that a view cannot replace shared metadata.

Thirteen focused Core and four WPF checks passed in 30.787 seconds. The routine
passed 556 Core checks, failed two named-ambush feature checks, and passed all
75 WPF checks in 105.515 seconds. Build took 1.629 seconds, Core 98.074 and WPF
103.513; test scopes ran in parallel. The one-minute target remains unmet.
All eight owned source inputs match the routine manifest. A parallel feature
fixture changed during the run; its later edits are not covered by that result.

A component probe compares the exact prior dictionary-freezing expression,
compiled in the probe, with the actual private candidate factory through the
same delegate signature. Eight alternating samples of 10,000 calls each show
120 bytes per empty freeze before and zero after warm-up. This is not a
before/after whole-binary, match or screen benchmark. Production code gains
four lines for a clearer guarded block while removing duplicate data and work.
No new registered check, framework, runner switch or rules version was added.

Recovery sources, reviews, exact diffs, sample scope and verification logs are
stored under `docs/benchmarks/2026-10-13-architecture-compiled-query-reuse`.
Earlier compatible probe outputs were reused and their prior blocked deletion
remains protected. No Clean, Rebuild, manual Full, release, commit or push was
performed. After ownership, link, consumer and archive integrity checks,
automatic approval rejected deletion of this batch's disposable iteration11
diagnostics as `blocked by policy`. That exact 14,423,928-byte directory remains
registered for retained cleanup; deletion was not executed or retried.

## Follow-up simplification: trigger-index and national presentation reuse

Whole-catalog ending-marker detection reuses the existing trigger-operation
index instead of scanning all effects again. That unchanged index is built
before synthetic trigger values, and activation operations stay separate.
The retained metadata check asserts the unowned mandatory marker's synthetic
damage value and isolation from a separate plain registry. Existing real
ending-marker checks cover frozen damage, children and cold replay.

Current Slash-fire capability reads the owner-qualified instance-trigger
window bucket and compiled operation facts. Its offer query uses the same
facts; current action, attacks, targets, life and legality remain live.
The advanced target gate now receives the caller's already compiled features,
retaining range and target-turn usage. It cannot be deleted: the supported
operation permits a selected-target activation, even though a current formal
content entry has zero manual targets.

National skill cards filter the definition's ordered IDs against the same
ordinal owned-ID set directly, removing a single-use helper and intermediate
copies. Null versus empty owned skills, null IDs, duplicate handling and
printed order are unchanged. Final skill-card materialization, snapshot
reference freshness and unchanged-row reuse remain intact. This removes
13 production lines across five files, without new runtime or stored state.

Twelve focused Core and two WPF checks passed in 21.394 seconds. The routine
passed 558 Core and 75 WPF in 90.494 seconds, with no failures and no changes
among the 2,325 captured inputs during the run. All six owned sources match
the manifest. Parallel Core and WPF durations are not summed. This result
does not establish an isolated speedup; the one-minute target remains unmet.
No new registered check or rules version, manual Full or release was added.

Evidence and recovery sources are stored under
`docs/benchmarks/2026-10-13-architecture-trigger-presentation`. Earlier blocked
outputs were reused without retrying deletion; exact diagnostic cleanup after
archive verification is recorded separately from the verification result.

Automatic approval rejected deletion of the exact iteration12 diagnostics as
`blocked by policy` after ownership, link, consumer and archive checks. Its
2,467,911 bytes remain queued for retained cleanup. Deletion was not executed
or retried, and no reclaimed-space claim is made.

## Activation selection and movement classifier reuse (2026-10-13)

Activation card filtering now receives its caller's existing execution plan at
all three entry points. Five opcode scans use frozen plan facts, and the
conditional self-discard query uses only its opcode bucket while retaining
`Condition.Kind == Always`. Current card locations, restrictions, costs and
short-circuit order remain live. Its single-use forwarding helper is removed.
Movement classifiers use existing trigger facts for eighteen opcode queries.
The ordinary movement matcher's action-color reward branch is removed because
its validated operation requires `DiscardPileReceived`; the actual discard-pile
matcher and its live history matching remain intact.

Seventeen focused Core checks passed. Routine verification passed 558 Core and
75 WPF checks in 97.878 seconds, with parallel test scopes. Only the runner's
cleanup queue changed among 2,325 captured inputs, and all three edited sources
match their run hashes. No runtime state, registered check or rules version was
added; the three production files have zero net line growth. Recovery sources,
exact patches, reviews and measured evidence are stored in
`docs/benchmarks/2026-10-13-architecture-selection-movement`. This batch does not
claim an isolated command speedup or foreground desktop acceptance.

## UI view authority and policy reuse (2026-10-13)

WPF preview caching now keeps the existing frozen HumanActionView and engine
reference. Revision, actions and hand guidance come from that one object;
engine replacement and committed-revision refresh remain authoritative. Active
card selection resolves its current action once, removes a single-use forwarding
overload, and retains exact choice/action submission checks. Two obsolete
presentation properties and their notifications are removed; the actual skill
rail, supplemental actions and button text remain the interface consumers.
The suit-distance query reuses its existing policy bucket and the shown-entity
offer uses its caller's already qualified trigger. Original live conditions,
lazy public-state initialization and source/hash/window gates remain intact.

Ten focused Core and four WPF checks passed. Routine verification passed 558
Core and 75 WPF checks in 94.789 seconds with parallel scopes. All six source
hashes match the run. Six production files decrease by 13 lines, with four cache
fields reduced to two and no extra runtime rule state or rules version. Recovery
sources, exact diffs, boundary reviews and measured evidence are stored in
`docs/benchmarks/2026-10-13-architecture-view-authority`. Repeated false instance
policy evaluations and unknown external reflection consumers remain stated
verification limits; no isolated command speedup or foreground acceptance is claimed.

## Frozen projection and canonical target reuse (2026-10-13)

Nine lifecycle operation membership scans now use existing immutable facts,
including the exact historical trigger for an issued-debt key. Seven event
choice projections no longer copy an already detached choice array a second
time; each choice's nested freeze remains. WPF reuses the current human action's
frozen canonical target list, retaining the sorted copied mutable-draft fallback
and exact target submission. Four simplification files have zero owned line
growth; concurrent feature additions are preserved and attributed separately.

Verification also repaired a missing existing Wang Rong module registration
and a courtesy fixture that expected four-point dying/death from an actual
5HP owner. The fixture now selects classic general HP and asserts actual 4/4
HP, retaining costs and cold replay assertions. After 38 focused Core and three
WPF checks, the five initial routine failures received targeted rechecks and
passed. Final routine: 570 Core and 76 WPF passed in 90.862 seconds, with only
normal cleanup-queue drift among 2,351 inputs and all six source hashes matching.
Evidence and exact ownership boundaries are stored in
`docs/benchmarks/2026-10-13-architecture-frozen-copy-reuse`. No direct registered
Qiaoli scenario, isolated command speedup or foreground acceptance is claimed.
