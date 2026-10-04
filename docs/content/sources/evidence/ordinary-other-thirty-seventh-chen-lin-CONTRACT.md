# Current ordinary OL Chen Lin contract

Baseline: f373ef47ff50f735bed91cf74a2bc0d2c622d2a7. This is stage-only work. Main source, the protected PrepDiscardReceipts path already committed in this baseline, prior frozen deliveries and version sources are untouched. The current hero 113 page/API and advertised cover were each fetched once, HTTP200 with no redirects or retries. No further HTTP is needed. API identity is Wei, 3HP. No API gender field exists; Male is an explicitly labelled historical identity inference.

## Bifa

Current text: 结束阶段，你可以将一张手牌暗置于一名其他角色的武将牌上，其下回合开始时观看之并选择一项：1.交给你一张类型相同的手牌并获得此牌；2.移去此牌并失去1点体力。

7700 DepositBoundPrivateCardOffer consumes exactly one selected owner Hand entity and one other living target at the owner's actual Ending. It reuses the private turn-hold card zone with an additive nullable typed deferred-offer identity. Ordinary old holds keep their existing semantics. The new offer is consumed at its target's next actual turn start, including an extra or face-down skipped actual turn, before ordinary preparation/judgment/draw. Creation freezes source instance, hash, original binding, target and created turn. Deposit movement children finish before the original Ending returns.

7701 ResolveDeferredPrivateCardOffer runs only on the exact due lifecycle parent. The recipient privately watches the real held card and may give one printed category-matching Hand entity to the source, then obtain that held entity, or move the held entity to DiscardPile and lose one real HP. Basic/Trick/Equipment uses the mature printed category query, with delayed tricks classified as Trick. No use/response or accepted-action fact is invented. Each issued payment/reward segment has an owning receipt and drains its movement/HP/Recovery/Damage/Dying children before the next unissued segment. A paid exchange survives later skill loss; owner death or winner cancels unissued successors after paid children drain.

An already deposited obligation survives later source skill loss while its original source remains alive: the original deposit/source/hash/candidate proves the due binding without regranting an instance. This accepted engineering default supersedes the initial non-frozen proposal. Source or target death moves the real held entity to DiscardPile through mature death cleanup. Winner cancels unissued successors after already issued children drain.

The physical private hold stays source-owned for mature source-death cleanup, but its public snapshot count is projected on the logical target with explicit DeferredSourceSeat. Before due only the source sees the deposited card; at due the target gains a private view on the exact owning private prompt. Other viewers see source/target/count, without hidden ID/category/name. Ordinary holds preserve their existing owner-view and end-of-turn return; typed deferred holds explicitly opt out. New public facts omit private IDs. New collection-bearing receipts and views clone/freeze at constructor/init/with/JSON; all new events are scalar, with no new collection projection bypass.

## Songci

Current text: 每名角色限一次，出牌阶段，你可以选择一项：1.令一名手牌数小于体力值的角色摸两张牌；2.令一名手牌数大于体力值的角色弃置两张牌。

7702 ResolveGameTargetHandHpChoice is a zero-card own-Play activation with exactly one living target, including self. Eligibility uses current public HandCount versus HP, excludes equality and consumed targets. Issuance freezes the branch and consumes one owner+skill+state+target Game usage key. Its unique GameTargetHandHpChoiceIssuedEvent in the complete accepted-journal-derived game fact history is the durable quota ledger: ResetSkill removes ordinary usage records, but cannot remove this issued fact. CanSelectGameHandHpTarget uses that immutable issuance key before spending, so instance loss/regain, explicit skill reset and extra actual Play phases cannot reset it. Draw2 is real and may draw zero if supply is unavailable. The discard default is two legal HE entities, or all available legal entities if fewer. Equipment removal and all children drain before completion. Private selection exposes only the chooser's own cards.

## Integration and drafts

Reserved EffectOps 7700–7702. Missing nodes and nullable metadata preserve old paths. Ordinary content does not raise rules/schema/package versions. RegisterBundle uses its real three-argument configure form. Four focused real-command drafts cover private due exchange/cold replay, removal/LoseHp typed return, per-target game quota/draw and full discard/children. First method is the routine proposal. They remain uncompiled, unloaded and unrun; no runtime acceptance is claimed.
