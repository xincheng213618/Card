# Batch35 independent static cross-review

This report reviews SP Ma Chao first, then Yang Xiu. It makes no runtime, compiler, loader, test, native, game, UI, benchmark or network claim. Main, author stages and the frozen SP Pang De delivery were untouched. The accompanying manifest records each named file actually read and its captured raw/LF hashes; these are author drafts, not a final adoption manifest.

## Confirmed findings and closure

1. **MA-002 P2, corrected:** the original Zhangba branch of `GameEngine.HpLossSlashMaterials.cs` required an empty current conversion chain. Mature `GameEngine.CurrentSlashFireOffers.cs` legitimately changes the same committed Slash to FireSlash and appends its precise conversion source. The latest new helper lines 146-156 first uses `IsCurrentSlashFireChangedUse` and `AssertCurrentSlashFirePolicy`, then checks the original action for Zhangba/program material origin. It leaves current runtime action and the paid receipt unchanged. This is a static closure, not an executed combination.
2. **MA-003 P2, corrected:** `preview/SkillPrograms.cs:2218` originally contained an extra `+` before a boolean negation. The current preview and patch no longer contain that content character; the new optional modifier remains restricted to additive OwnerLostHp Slash target count.
3. **MA-004 P2, corrected:** `BeginHpLossMaterialSlash` originally called `BeginCardUse` before recomputing `HpLossSlashMaximum`. The real declaration synchronously consumes a genuine next-use target token (`GameEngine.NextActualUseTargetAdjustments.cs:55-80`). With full HP, new7341 plus that token, the valid two-target material Slash could therefore issue a frozen maximum of one and fail its own invariant. Latest new lines 99-107 freeze the maximum before the declaration, preserving the selected program maximum; the receipt matcher checks selection/issued maximum equality. It does not reread or repay the consumed token.

**MA-001 withdrawn:** complete preview `GameEngine.NextActualUseTargetAdjustments.cs:188` already returns null from the old600 freeze for the precise7341 material producer. The initially reported no-grant throw is not a current defect.

## Read scope

- SP Ma Chao: all six new Core mechanism files; all19 OLD diff sections, with complete affected previews and named producer/return/invariant contexts. Checked own paid movement and reward drainage, single generated discard versus two-material Processing, actor/provider and current color proof, target visits, exact old600 return, actual5403 conversion, processing membership, aggregate damage and typed completion. Pending author generated-material candidate filtering was treated as known work rather than an additional defect.
- Yang Xiu: all four new Core mechanism files; all26 OLD normalized changed sections. Checked own-trick target identity and BorrowedSword holder count, paid Draw1 ledger/first child and exact target nullification, early paid resume, category issuance/actual turn expiry, private owned selector and `SelectionActorSeat`, all material use/response restriction, self-Hand discard intent versus foreign choices, hand-limit excess versus the legal discard pool, scalar public events and read-only player collection projection. No definite additional P1/P2 or API gap was found in that bounded scope.
- Mature named dependencies are listed separately in the manifest. Checks still being authored were not evaluated. Unchanged portions of large previews were hash-bound but not represented as a full source-line semantic audit.

## Limits

All execution fields are false. No behavior or compatibility acceptance is implied by this static read. Author stages were drafts; later final manifests or root combined previews require their own exact byte review. The unrelated dirty PrepDiscardReceipts file and frozen SP Pang De deliverable are excluded.
