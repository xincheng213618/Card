# Current ordinary OL Cao Hong 114 — full Yuanhu

Authority is `source/114-page.raw.html` (HTTP200, SHA026aa6d29af9539c074538c0ea0ef1b116b233498f2b3b262c49eaf73229ffcf). The page advertises the exact info API; its raw HTTP200 is `source/114-info.raw.json` (SHA21649496bbc5607edf32d6a586705f213b371e8b5fd70cf29d2ddc738b76452a). There is one complete skill and no font/granted-skill gap. Current biography “曹操从弟” supports a male identity inference; the API has no gender field. API `initial_hp:0` is a placeholder, not an instruction to start at zero HP.

> 结束阶段，你可以将一张装备牌置入一名角色的装备区，若为：武器牌，你弃置其距离为1的一名角色区域里的一张牌；防具牌，其摸一张牌；坐骑牌，其回复1点体力。

New identity `ol:cao-hong`, skill `ol:yuanhu`, character `character:cao-hong`, variant `ordinary`, ruleset `sanguosha-ol`, portrait key `ol-cao-hong`, module `OrdinaryCaoHongContent`, bundle `ordinary-cao-hong`. The old registered `standard:yuanhu` is the separate after-damage discard-hand recovery rule and remains byte-preserved.

## Minimal generic capability

7500 `PlaceOwnedEquipmentThenResolveSlotBenefit` is one optional Owner/Own actual TurnEnding standalone instruction with an Always effect. Resource validation requires TurnEnding; composition rejects other scopes, subjects, mandatory bindings, multiple effects and activation placement. Candidates without actually transferable equipment are not offered. All state is scalar on the original ProgramSkillFrame; no use-ID map, pending sidecar or character-ID dispatch exists.

The original TurnEndingBoundary item, current candidate, occurrence, binding, skill-instance and gameplay hash stay exact. Whole Facts record/dictionary equality is not used. Before payment, the original source must remain qualified and the equipment/recipient must remain eligible. A private HE choice captures one real printed equipment entity; recipient selection permits all eligible living characters including the owner for a Hand source. Existing CanPlaceOwnedEquipment supplies real slot capacity, abolition and same-area constraints. Actual replacement and EquipmentEnter movement, EquipmentChangedEvent and an immutable scalar paid receipt are issued once. Placement creates no CardActionAcceptedEvent or Use ledger entry.

Replacement/source SilverLion HP/recovery-replacement and all movement children drain before inspecting the frozen printed slot. The owning frame is re-read after both actual moves, so queued recovery/replacement fields cannot be overwritten by a stale frame. WoodenOx grain transfer and replacement destruction retain native ledger rules.

Weapon: owner chooses a living character using the recipient’s *current final directed combat distance*, exactly one, then one actual eligible HEJ entity of that character. Foreign hand choices expose slots only; they do not expose IDs, kind or suit. Current payment eligibility includes shared foreign/self-discard restrictions (including Yang Xiu). Each resulting discard is an actual native MoveCard; generated equipped weapons follow exact OutsideGame ledger semantics.

Armor: recipient actually attempts Draw1 after placement children, with an exact DrawPile→Hand ledger interval and actual count. Recipient-dead/winner cancellation issues no Drawn event; an actual empty-deck attempt may record actual0. Mount: recipient recovery uses the mature Program producer, queued replacement, HP observer and typed return; the eligible amount is clamped to current lost HP. Full-HP recovery creates no RecoveryApplied event.

Already paid continuation bypasses the generic live-source gate only through the new exact standalone receipt/parent/ledger Resume. It drains children and finishes its original binding; it cannot issue a new cost or substitute a reacquired instance. All first-child prefix and continuous HP/movement/Damage/Dying/Death/rescue edges are proven locally under this receipt. Mature physical rescue, bound/round-priced Alcohol and virtual Alcohol whole-tail helpers are only accepted after the incoming Dying edge. New nested-Damage admission only recognizes the paused actual Damage effect in this paid subtree; no old generic helper is widened.

All new committed events contain scalars and immutable scalar records only (payment, invoice, source). No exposed nested list/dictionary or new PlayerSnapshot collection is added; thus no new CommittedEventProjection collection case is needed. Private selection ID stays only in the trusted owning frame until the equipment is publicly placed. Checkpoint restore is accepted-journal re-execution; drafts restore a new engine and assign it before continuing commands.

## Named focused drafts (never executed)

1. `OrdinaryCaoHongChecks.RealPlacementReplacementAndArmorDraw`: actual equipped SilverLion transfer, actual replacement, two recovery observers, restored continuation, one armor draw, no second Use.
2. `DirectedRecipientDistanceOpaqueHejAndEquipmentSource`: true Indulgence entity in Judgment, recipient Mashu directed distance, foreign opaque Hand choice, actual Hand/Judgment discard and cold continuation.
3. `MountRecoveryPaidSourceLossAndGainDying`: both mount slots, restored recovery; real source suppression after payment; actual Armor Draw→Gain LoseHp→DyingEntering→zero Alcohol/HP child and restored paid completion.
4. `NativeOptionalEndingAndStrictComposition`: unpaid cancel invariance, bounded native real Ending payment, strict standalone rejection drafts.

Only the first existing-runner prefix is added to the routine scope. No duplicate character-definition snapshot, runner switch or test framework is added. These are *source/API checked drafts*, not evidence that parsing, compilation, gameplay, native behavior or assertions have passed. Generated weapon replacement/discard, WoodenOx, recovery replacement, nested Damage/AttackHpLoss/death and all special rescue combinations are supported by static exact producer/edge contracts but have no new execution evidence in this delivery.
