# Current ordinary OL 627 integration contract

This stage implements both current skills together. The complete primary source is `docs/content/sources/fenglin-twenty-seventh-627-source-2026-10-04.json` with SHA256 `4c93bf6cf1d09c9e064a7f60e1b9724febf8cfdc66b7fcf9e587e2ea3b069b19`. The official page and API both contain full skill texts and no unresolved font references. Current API gender is absent; Female is an inference from the current primary biography, explicitly identified in the content code and manifest. No historical or other-client skill definition is substituted.

## New generic operations

- 5800 `DonateAllEquipmentAndOfferRecipientBenefits`: exactly one standalone owner operation in a zero-card, one-other-living-target activation. The operation owns its post-payment game-wide opportunity. It pays every actual Equipment entity through one owner batch, freezes actual delivered X, and waits for the original movement/recovery children before the recipient chooses. No generated-weapon subset payment is legal.
- 5801 `ChooseEquipmentOrDrawAfterOtherActualTurn`: exactly one optional owner operation at `AfterTurnEnded` with `OtherLiving` scope. It freezes the original actual-turn qualification independently from collection-bearing Facts, then issues each distinct equipment/draw option at most once. Equipment placement uses current actual equipment and native replacement, without a new equipment card use.

## Owning frames and returns

All pending progress resides on `ProgramSkillFrame.EquipmentDonation` or `.ActualEndedEquipment`. Equipment donation owns its paid IDs, frozen recipient, actual X, chosen damage targets and cursor. Issuing each native damage increments the cursor before child resolution; the native `AttackAttempt`/`AttackReturn` own all damage, prevention, Dying, rescue and transfer state. The original recipient remains the parent program's selected participant and native damage source. Source loss, recipient loss and winner terminate the unpaid tail without returning any paid entity or usage.

The new native-damage observer admission proves the issued donation root and contiguous native BeforeDamage/Damage/ordered-candidate edges before accepting nested movement, HP loss or Dying. It uses exact DamageRequested facts and typed owning attack state, never whichever unrelated damage happens to be globally current. The existing native damage invariants still validate normal damage progress; only a fully proved donation subtree opts into the new automatic Dying and ordered-window observer union.

Both actual movement paths fetch the current program frame after native moves before merging their receipt. Silver Lion can append queued recovery attempts during an equipment move; the local awaited-movement dispatcher starts queued replacement, then HP, then movement children. It never overwrites that queue with a stale pre-movement frame. The first child is proved against the exact actual movement interval, original source/candidate and native return kind. Subsequent observer edges use the existing exact Dying, rescue, Jiushi and movement proofs; a whole-tail proof is accepted only after its incoming Dying edge is proved.

Equipment placement freezes its exact replaced entity ID and generated flag. A replaced generated weapon alone may leave Equipment for OutsideGame with EquipmentReplace; every ordinary replacement is uniquely tied to that frozen ID and DiscardPile. Incoming chosen equipment remains non-generated and truly placed. The replacement can remove the source skill, which cancels only the unpaid next option.

The original-turn context records only scalar turn owner/number and qualification. The new opt-in branches in `AfterTurnEnded` match its exact current candidate, parent, source instance/hash and scalar context. They bypass collection-reference `Facts` equality only for 5801; the existing non-opt-in path remains unchanged.

## Events, privacy and restoration

New built-in public facts contain scalars only. Per-entity donation payment uses separate scalar facts, never an exposed mutable list. Frame-owned `PaidCardIds` and `DamageTargets` are copied to read-only collections at construction and `init`, including JSON reconstruction. New scalar context/receipt equality is safe after JSON round trip; no collection-bearing `Facts` record equality is used. Existing `CommittedEventProjection` continues to freeze the mature movement/event payloads before history and observers. There is no new event collection to add to that projection.

Actual use qualification observes the exact current CardUse frame at `TargetsConfirmed`, backed by its earlier `CardUseDeclared`. Actor/provider and original actual-turn number/owner are separate. Action-null uses require the mature exact typed legacy producer; no accepted action is invented. Ordinary Slash/Dodge responses are excluded. Nullification has a card target, so its requester/opponent does not become a synthetic character target. Actual positive source-attributed damage is read from the original turn's public DamageApplied facts.

## Parent integration and verification

Final OLD baselines must be refreshed after 642 has been committed on top of 621; provisional main bytes are not final authority. The final assembler retains one physically ordered narrow Update per shared file, preserves all old enum numeric values, and records raw-byte SHA separately from normalized UTF-8/LF text SHA.

Required shared hooks are: two operation enum values/descriptors discovered by the existing catalog, strict activation/trigger validation, nullable owning receipt/context fields, exact activation legality before payment, choice and AI dispatch, the opt-in pending-movement/Dying proof, the actual-use scalar observer, and new 5801 scalar AfterTurnEnded context/candidate/child matching. Complete content registration and four existing-runner method registrations are parent-owned. Only the first generic behavior name is proposed for routine scope.

The four fixed real-command drafts cover actual whole equipment payment/X/recovery and restored HP children; recipient range/damage plus source loss and restored gain-Dying return; original actual-turn tier/distinct equipment/draw choices and refusal; ordinary response exclusion, true action-null Shensu, native AI and strict composition rejection. They deserialize a real checkpoint into a new engine and continue that restored instance, comparing four private snapshots and exact committed state. No manual state mutation, reflection, fake accepted action or seed scan is used.

No compiler, C# loader, behavior check, native simulation or benchmark has run for this stage. All actual runtime acceptance, including queued Silver Lion/Jiushi/transfer interactions, remains deferred under the user's quiet-development request. Root owns later builds, tests, content/art registration, version decisions and commits.

## 主线静态整合记录

2026-10-04：基于 2e805f43 共享 Core 的确切原字节接入 8 个 NEW、9 个 OLD 窄接线，主 manifest SHA-256 为 f77e1ff8091efe5a194aec85874ee9e2bc58bfc8e8f2817c0b21bf602688f104；另应用只改 NEW 变量命名的独立 followup（manifest 4177a54ca74743f1c16e77895b655d91a463aade56207c08984ac3047a537408）。最终 Observers 原字节 SHA 为 f196676e6850407afcc33225db54b19162bf87c5e375dbc613a53b36c0b0cea0。OLD 的统一 LF SHA、NEW 与源记录散列核对一致，原313枚举值保留。正式登记完整 boundary:cai-fu-ren、四项行为检查草稿和唯一 routine prefix，原图登记。未构建、未执行生产 loader、任何测试或基准；冷恢复、生成武器替换、真实Dying等运行验收继续待用户统一验证。普通人物增量不抬全局规则、schema 或内容包版本。
