# Batch 25: ordinary OL boundary Chen Gong 597

Status: frozen static delivery only. No compilation, behavior check, benchmark, WPF check or complete-match acceptance was run by this agent. All files remain in this owned staging directory. Main source, test files, media, version authorities and Git state were not changed by this agent.

## Source

- Frozen source: `docs/content/sources/fenglin-twenty-fifth-597-source-2026-10-04.json`.
- Source SHA-256: `1b8dc3a45f48279062caf56f7d8967b0caf9a9c2fe29c0e488b0045d8d2504b7`.
- Ordinary OL official page: https://www.sanguosha.com/hero/597 . Official API: https://www.sanguosha.com/api/v1/hero/info?gid=597 . The frozen capture records both HTTP 200 responses and the raw page/API hashes.
- API faction/HP: Qun / 3. API `initial_hp:0` is not interpreted as a zero-HP starting character. Male is reused from the existing classic character metadata.
- Current Mingce: once per Play phase, give another character a Slash or equipment, then that character chooses either a virtual Slash at another character selected by the skill owner (if it causes damage, execute the other option), or both participants draw one card.
- Current Zhichi is the already-registered `classic:zhichi`; the new bundle does not redeclare it or change its historical behavior.

## NEW mapping

| Staged file under `new/` | Main destination |
| --- | --- |
| `BoundaryChenGongContent.cs` | `src/CardGame.Content.Standard/BoundaryChenGongContent.cs` |
| `boundary-chen-gong.rules.json` | `src/CardGame.Content.Standard/SkillPrograms/boundary-chen-gong.rules.json` |
| `boundary-chen-gong.presentation.json` | `src/CardGame.Content.Standard/SkillPrograms/boundary-chen-gong.presentation.json` |
| `ProgramSharedSlashOfferOperations.cs` | `src/CardGame.Core/ProgramSharedSlashOfferOperations.cs` |
| `GameEngine.SharedSlashOffers.cs` | `src/CardGame.Core/GameEngine.SharedSlashOffers.cs` |
| `BoundaryChenGongChecks.cs` | `tests/CardGame.Core.Tests/BoundaryChenGongChecks.cs` |

## Shared contract and compatibility

New opcode 5100 is `giveBoundCardThenOfferVirtualSlashOrSharedDraw`. It accepts only `op`, `target:selectedTarget`, `sourceBind` and the normal always condition. The composition contract requires an active binding with exactly one other living initial target, no initial input cards, and exactly two instructions: an exact one-card owner HE `selectOwnedCards`, followed by this terminal operation on that binding. Unsupported nested damage-trigger compositions are rejected. The existing `offerVirtualSlashOrDraw` is unchanged.

The owning `ProgramSkillFrame.SharedSlashOffer` freezes the exact source instance, recipient, instruction, physical cost and actual movement sequence before the gift. Its callback issues a scalar `SharedSlashGiftCommittedEvent` before removal hooks. The existing recovery, HP and movement program windows are drained before publishing the recipient's choice. Selection/payment revalidate current ownership and exact binding source locations. A canceled unissued tail does not refund or repeat a committed gift.

The accepted virtual Slash is a real `CardUseFrame`: zero physical entities, a real accepted action, actual recipient as actor/provider, one original target and a typed `SharedSlashBenefitReturn`. The immutable typed token and exact adjacent program producer establish its parent relationship; `CardUseFrame` and the base frame do not have a generic `ParentFrameId`. All target effects, damage, rescue, HP and card-use-completed children finish before the producer receives its typed completion. The result is the owning use's aggregate `CausedDamage`, never a last/global `DamageAppliedEvent` match.

`RecordYingboCardUse`, `RecordProgramUsedBasicCard`, the current Play history marker and actual Play-use history run exactly once at new true-use issuance. They do not add an ordinary Slash-limit debit to the historical assisted-use behavior. Existing committed/finalized/completed action windows and actual armor/dodge/damage machinery remain the producers.

After the full use returns true, or after the recipient selects the draw option, the owner draw is issued first, then its child windows drain, then the recipient draw is issued and drains. Each receipt stage is stored before the actual draw. Loss/suppression of the skill source after use issuance does not revoke an accepted benefit. Each draw checks the actual beneficiary's survival and current winner before giving cards; winner/dead beneficiaries produce an explicit count-zero issuance. These death/winner code paths are static only in this delivery, not separately exercised by the four drafts.

All new public events carry only immutable scalar records; none publishes the private gift entity ID or a collection-bearing payload. Private recipient/owner decisions use the existing per-view snapshot projection; choices are read-only. No new pending sidecar, parallel use-ID table, damage-window allowlist, general-specific engine branch, generic lifecycle widening or global version change is introduced.

## OLD integration

`old-wiring.patch` has one Update header per file and source-ordered hunks. `old-baselines.json` contains byte SHA-256 values of all 12 current main OLD inputs and of their diagnostic previews. `preview/` is limited to those 12 touched files; it is not a checkout or build output and must not overwrite later concurrent main edits.

The patch adds the opcode, nullable owning receipts, strict composition call, runtime continuation, opt-in entity-identity selection predicate, shared-offer prompt/AI dispatch, aggregate attack completion token, exact completed-use return and restore invariant. It also includes the one module registration, four existing-runner method registrations and one representative routine scope entry. Descriptor/handler discovery uses the existing reflection discovery; no explicit operation-registry hook is needed.

## Engineering defaults, not official FAQ

1. The selection uses printed Slash/equipment identity and the already-issued intrinsic hand Alcohol-as-Slash policy. Optional Wusheng or other selectable ViewAs conversions are not intrinsic entity identity. Nontransferable general weapons are excluded only in the new 5100 selection/payment path. Historical classic selection remains unchanged.
2. The recipient's actual ordinary Slash target eligibility, distance and prohibitions reuse `GetProgramVirtualSlashOfferTargets`, plus the existing colorless card-target prohibition. The recipient cannot target itself. The skill owner may choose itself when ordinary eligibility permits. If no legal Slash target remains before an option is offered, only the shared-draw option is published. No new range/number exemption is invented.
3. The gift is committed before recipient choice. If the exact source instance, owner or recipient becomes invalid before a choice/use is accepted, only the unissued continuation is canceled. Once the real use has been accepted, its full completion and already-entitled living participants' rewards retain their exact origin despite subsequent source suppression.
4. Rewards are issued owner then recipient. This order and count-zero handling for dead characters or a determined winner are explicit implementation defaults; the supplied official source does not include a complex FAQ deciding those nested cases.
5. Source/prompt/card legality is revalidated before payment or issuance; payment material is never reconstructed from later current cards. Temporary card identity does not change the physical gift's printed kind.

## Behavior drafts and registered names

Four public methods, all fixed seed 31 and bounded four-player fixtures, use real serialized commands and four-view checkpoint/replay equality. No reflection state injection or seed search is used.

| Method | Existing runner display name | Intended behavior coverage, not an executed result |
| --- | --- | --- |
| `SharedGiftEquipmentRecoveryAndBothGainChildrenCold` | `Shared Slash gift equipment recovery both gain children cold` | Hand/real Silver Lion equipment gift; removal recovery and HP child; actual recipient gain; private choice, rejection atomicity; both sequential draw children; once-payment and real phase usage. This is the representative routine scope. |
| `VirtualSlashActualDamageRescueCompletedAndCommittedSourceLossCold` | `Shared Slash actual damage rescue Completed source loss cold` | Real zero-entity Slash; owner-selected target/distance; actual damage; real configured dying response and recovery HP child; completed-use observer; accepted source suppression; typed aggregate and two gain children. |
| `DodgedAndPreventedVirtualSlashCannotBorrowPriorDamage` | `Shared Slash false use cannot borrow prior damage` | Real Qingguo Dodge and real beforeDamage prevention; prior same-actor unrelated damage cannot supply this use's result; false completion gives no rewards. |
| `GiftIdentitySourceCancellationNativeAndClassicDefaults` | `Shared Slash identity cancellation native classic defaults` | Intrinsic Alcohol identity versus optional Wusheng; post-gift/pre-choice source cancellation; classic recipient-only draw compatibility; actual native choice/payment/return; unsupported 5100 composition rejection. |

Fixture base HP is validated before the engine's real Lord +1; the mode begins `identity:classic-`. Optional observer labels exist only for actual `chooseOption` instructions. The self-dying pause uses `usageScope:game/usageLimit:1` and the real recovery instruction, not injected frames. The 4-turn native fixture is a draft requiring actual future validation; no native AI success is claimed.

## 604 / 4901 complete-use tail: static proof and explicit gap

Current `GameEngine.CurrentCardEnhancements.cs` continues each additional target with the same `ProgramSkillCardUseFrameId` and carries the original use's aggregate `CardUseCausedDamage`. `GameEngine.CardAttackState.cs` reads/writes that aggregate on the original `CardUseFrame`. The new completion captures `SharedSlashBenefit` from that same use and bypasses the old selected-target parent rewrite only when this exact new token exists. It recaptures after the completed-card window, so its benefit cannot return after only the primary target.

The new invariant accepts multiple targets only with `HasIssuedOriginalTargetAdditionTail(use)`, while preserving the original one-target `TargetsConfirmedEvent` and accepted action/typed origin. This is a static cross-review, not a real 5100+4901 behavior check in this delivery.

The ordinary assisted-use recipient is outside its own turn. Existing 604 rights require its own Play phase and expire at that actual turn's end. The current small `insertPhase` capability is owner-only at TurnStart; no verified foreign-owner extra-Play fixture was found. Therefore these four methods do not manufacture rights or frames to claim a real combined extra-target check. This is an explicit remaining integration coverage gap for the parent to decide; generic existing 604 checks do not substitute for the new producer combination.

## Future acceptance

After the parent integrates this frozen draft, it must compile and run the four registered filters, relevant shared loader/composition and classic Chen Gong checks, then the appropriate batch routine/full lanes when the user authorizes CPU-heavy verification. Main registration/media/version/release work stays with the parent. No currently passing test total or timing is claimed for this new draft.
## 主区静态整合补记

root验证冻结manifest、来源SHA、六项NEW和十二项OLD原字节后，复制NEW并串行应用窄patch；所有旧文件的最终逻辑文本与完整预览一致。当前普通OL模块、既有runner四项名称及routine一个代表项已接线，官方原图已登记。严格加载器仅做源码复核，没有执行加载器、编译、测试或基准，实际5100＋604/4901组合及死亡收益者的缺口仍按本文记录保留。
