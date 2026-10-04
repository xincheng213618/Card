# Current ordinary OL SP Pang De (gid 105)

The exact current detail page says:

- 马术：锁定技，你计算与其他角色的距离-1。
- 决死：出牌阶段，你可以弃置一张【杀】并令攻击范围内一名其他角色弃置一张牌，若弃置牌不为【杀】且其体力值不小于你，你视为对其使用【决斗】。

The current page and its actually advertised info request were each read once. The info gives Wei and HP 4, has no gender/skill fields and uses initial_hp=0 as a placeholder. Male is a same-person supplement from the already registered classic Pang De, not an API field. `ol:sp-pang-de` / `ol:juesi` use CharacterId `character:pang-de`, VariantId `sp`, RulesetId `sanguosha-ol`; this ordinary OL SP variant does not replace the Qun classic or Qun boundary variants. Existing `classic:mashu` supplies the first complete skill. No granted-skill text is missing.

## New generic capability 7320

`discardSlashThenOtherCardAndUseDuel` is an unconditional standalone unlimited zero-input activation with exactly one other living target currently in attack range. The initial activation selects the target; the capability then publishes the owner's private own-HE Slash payment. Unpublished input is rejected before payment. Cancellation remains available until this first real payment. The target subsequently chooses its own HE entity, through the mature own-card slot selection mechanism. The owner never chooses the target's hidden hand card.

Both actual payments are frozen scalar receipts on the original ProgramSkillFrame: original entity, printed kind, original zone/owner, exact sequence interval, instruction, source instance/hash and original actual-turn/target. `MoveCard` pays each entity once. PendingMovementContinuation and fresh owning-frame merges preserve SilverLion recovery/replacement queues; all HP, movement and Dying children drain before advancing the stage. The paid identity is not a current zone-membership test: subsequent legitimate card movements do not repay or erase the original payment.

After every target-discard child has returned, the capability compares the target's frozen discarded identity and the two current HP values. If the discarded kind is not in the Slash family and target HP >= owner HP, a legal true zero-entity Duel is issued against that same original target. A standalone origin locks the reserved real CardUse id, original program/instruction/source/hash, actual turn and HP comparison. The old `CanIssueSelectedActorDuel` legality and canonical actual BeginCardUse producer are reused; this is not skill damage and has no extra optional refusal. Both already discarded costs remain outside Processing. The actual Duel supports mature committed/finalized/before-target windows, Nullification, damage, Dying and Completed returns.

Only this exact new origin joins `IsIssuedZeroEntityDuel`, the zero-material Nullification/attack Processing guards and the typed AttackCompletionReceipt/no-attack return. All other Card0 attacks retain existing gates. No source-actor/player-id branch, pending sidecar, fake AcceptedAction or fake Card0 movement is introduced. Original costs are never cleaned up as Duel materials.

Unissued work drains paid children and then cancels if the original source/owner/target/game is no longer valid. An already issued actual Duel completes its original typed return even after suppression of the old skill instance. No source reacquisition changes that old receipt. Current attack/source actor replacements remain the mature actual-use behavior; the receipt preserves the initial actor/provider rather than overwriting prior ownership facts.

All new receipt/event payloads are scalar, including CardConversionSource and CardLocation. No new list-bearing public event or snapshot field is added; existing private prompts and runtime frame collections use the mature freeze path. Nullable frame properties are ignored when null, preserving the old default serialized shape. No comparison depends on collection-bearing record reference equality.

## Check drafts and execution boundary

Four existing-runner methods cover: two real costs and true Duel Damage/Completed returns; printed Slash versus non-Slash with actual post-payment HP children; actual Dying/Peach/Completed and source loss before/after issuance; native public scoring plus strict standalone composition rejection. The first method is the only added routine prefix. The fixture is fixed seed 31 with a small actual physical deck and real preparation commands. Cold helpers journal-restore a new engine through JSON and continue commands on that returned instance; they do not claim arbitrary checkpoint frame-field deserialization.

These are uncompiled, unloaded, unexecuted drafts. Static API and data inspection is the evidence. No seed search, game simulation, native execution, compiler, production loader or test runner has run. The draft does not claim runtime coverage of generated weapons, actor replacement, added Duel targets, nested observer Damage, or every rescue skill/armor combination. Their production paths reuse the precisely scoped existing mechanisms; later focused validation remains required.
