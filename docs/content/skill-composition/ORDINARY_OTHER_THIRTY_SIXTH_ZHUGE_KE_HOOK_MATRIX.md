# 118 exact route and ownership matrix (static, not executed)

`AddRequestedDeckBasicChoices` only decorates a genuine existing typed need on its producer. Nil ordinary choices reach the same producer by explicit opt-in gates; no global prompt/use-ID set exists. `RequestedDeckBasicFrame` parents to the actual request frame and stores the producer ID, original prompt/revision, responder cursor, static source/hash and ordered top entities. Selection attaches one nullable receipt to that producer. `CaptureRequestedDeckBasicPaid` records only its actual DrawPile→Processing ledger entry.

| Need | Nil-choice entrance and parent | Selected native dispatch |
|---|---|---|
| Slash Dodge | GameEngine ResolveAttack human and native conditions include HasRequestedDeckBasicSource; response window retains CardUse parent | ResolveDodgeResponse, same response cursor/action |
| Group Dodge/Slash | BeginGroupAttackResponse no-card gate includes new source | ResolveGroupResponse, native response material |
| Duel Slash | BeginDuelResponse no-card gate includes new source | ResolveDuelResponse, native response |
| Dying Peach / self Alcohol | Human exposure and RunOneDyingStep native entry include new source before old no-card fallback | ApplyDyingResponse→ResolvePeach/ResolveDyingAlcohol→ResolveRecoveryCard; exact DyingResponse original token, HP/recovery/card windows |
| 护驾 Dodge provision | FactionDefense no-card gate includes alive qualified faction provider; actual candidate cursor frozen | ResolveFactionDefenseCandidateResponse→native provision/payment |
| 激将 Slash provision | FactionCardRequest no-card gate includes alive qualified provider; actual candidate cursor frozen | ResolveFactionSlashCandidateResponse→same provided Slash producer, original user/provider tuple |
| 借刀 actual Slash | BeginBorrowedSwordSlashChoice no-card gate includes new source | ResolveBorrowedSwordSlashChoice→native Slash use to actual required victim |
| 青龙 actual Slash | TryBeginQinglongCrescentBladeChoice no-card gate includes new source | ResolveQinglongCrescentBladeChoice with exact response-card-kind |
| Program requested Slash | ProgramSlashRequest/nearest no-card gate includes new source; original requester program owns selected material | ResolveProgramRequestSlashChoice / ResolveProgramRequestSlashByNearestChoice; exact paused instruction and target |
| Assisted physical Slash | AssistedSlashRequest existing decline-only genuine actor need is decorated; its canonical prompt comparison strips only new choices | ResolveAssistedPhysicalSlashChoice uses existing GetPlayableCards native material |
| Nearest legal Slash | NearestLegalSlashRequest existing decline-only genuine actor need is decorated; canonical comparison strips only new choices | ResolveNearestLegalSlashChoice, same actor/nearest target and parent return |

Global GetPlayableCards appends only the exact selected still-unpaid DrawPile material; with no receipt it returns the old collection. Program forced-Slash Hand-only enumeration has its own exact selected-material union. FindOwnedCardLocation returns DrawPile only for that authorized ID. Rescue Alcohol enumeration receives that exact material. Native recovery is not routed through FindOwnedPlayableCard's ordinary Play lookup: ApplyDyingResponse directly selects GetDyingPeaches/GetDyingAlcohols and ResolveRecoveryCard pays via FindOwnedCardLocation.

Native private view invariants locally project the frozen original decision/top. Real private state stays pushed. Runtime AI sees and consumes the private view first. For native rescue refusal or prohibited/no-activate publication, exact original Dying/cursor runs mature fallback with the Aocai entry skipped once; later responders are separate requests. Other native refusal returns to the original existing response AI dispatch.

7601's Paid PendingMovement continuation admits only exact paused effect/receipt/selected target and atomic original ledger. Both immediate return and resumed Paid tail first drain queued recovery→HP→remaining original CardsMoved, then clear the continuation. Paid source suppression bypass is confined to this receipt. DamageRequested, direct PlayerDying and DyingResolved tokens are captured before their parent pops; an explicit completion fact validates the final stage. Mature HalfHandPaidDamageObserverEdge and strict rescue suffix proofs are used only after locking this root and first child. Runtime pending attacks use mature CompleteDamageAttack; RecoveryReplacement retains earlier priority.

Focused drafts cover Dodge, request Slash, real Peach, SilverLion cost, direct surviving/dead Dying, self virtual wine, native decline and cost source suppression. Remaining route combinations, short/reshuffled deck, redirected/damage-as-HP and winner cancellation have static wiring only. No compiler/loader/Core/WPF/native/UI/benchmark was invoked.
