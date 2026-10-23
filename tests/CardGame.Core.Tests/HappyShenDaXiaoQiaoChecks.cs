using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

public static class HappyShenDaXiaoQiaoChecks
{
    private const string Hero = "happy:shen-da-qiao-xiao-qiao", Peer = "fixture:happy-qiao-peer", Driver = "fixture:happy-qiao-driver",
        NoDraw = "fixture:happy-qiao-no-draw", Quiet = "fixture:happy-qiao-quiet", Deck = "fixture:happy-qiao-deck", GainHold = "fixture:happy-qiao-gain",
        MaximumCounter = "fixture:happy-qiao-maximum-counter", MaximumHold = "fixture:happy-qiao-maximum-hold";

    public static void IndependentPoolsNativeRecoveryAndDyingResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 3, 3) && s.V.HpUndefined && s.V.Hp == 0 && s.V.MaxHp == 0 && s.V.HandLimit == 6,
            "The one real seat exposes two independent 3/3 pools and a six-card hand limit, with no fake six-HP game scalar.");

        s.Use("damage", [0]); s.PoolBoundary(); s.Cold(); s.Pool(HappyQiaoPool.XiaoQiao); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3), "Native damage subtracts only the selected Xiao Qiao pool.");
        var handBeforeQuery = s.V.Hand.Count;
        s.Use("draw-current-hp", []); s.PoolBoundary();
        var queryFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        var hpQuery = queryFrame.HappyQiaoQueries!.Single();
        s.Need(hpQuery.TargetSeat == 0 && hpQuery.Before == new HappyQiaoDualHealth(3, 2, 3) && hpQuery.Pool is null &&
            queryFrame.HappyQiaoHealth is null, "A real HP-valued Draw owns a query receipt rather than a health payment or an invented aggregate.");
        s.Cold(); s.Pool(HappyQiaoPool.XiaoQiao); s.Play();
        s.Need(s.V.Hand.Count == handBeforeQuery + 2 && s.Health == new HappyQiaoDualHealth(3, 2, 3) &&
            s.E<HappyQiaoHealthPoolSelectedEvent>().Count(e => e.RequestId == hpQuery.Id && e.Pool == HappyQiaoPool.XiaoQiao && e.HpBefore == 2) == 1 &&
            !s.E<HappyQiaoHealthAppliedEvent>().Any(e => e.RequestId == hpQuery.Id), "The selected Xiao Qiao query draws exactly two real cards once after cold restore without modifying either pool.");
        s.Use("damage", [0]); s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(2, 2, 3), "A separate native request may select Da Qiao without rewriting Xiao Qiao.");

        s.Use("recover", []); s.PoolBoundary();
        var recovering = s.G.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
        s.Need(recovering.Attempt.HpBefore is null && recovering.Attempt.HappyQiaoBefore == new HappyQiaoDualHealth(2, 2, 3),
            "An unselected real recovery preserves both previous values and has no invented scalar HP-before.");
        s.Cold(); s.Pool(HappyQiaoPool.DaQiao); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3), "A real recovery returns through its original request and changes the chosen pool once.");
        s.Use("damage", [0]); s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao); s.Play();

        s.Use("loss-three", []); s.PoolBoundary(); s.Pool(HappyQiaoPool.XiaoQiao);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.Health == new HappyQiaoDualHealth(2, -1, 3) && s.V.HandLimit == 1 && s.G.ResolutionStack.OfType<DyingFrame>().Single().HappyQiaoPool == HappyQiaoPool.XiaoQiao,
            "Only the selected minus-one pool is dying even while the other sister remains at two HP.");
        s.Cold();
        s.Peach(); s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.Health == new HappyQiaoDualHealth(3, -1, 3) && s.G.ResolutionStack.OfType<DyingFrame>().Single().HappyQiaoPool == HappyQiaoPool.XiaoQiao,
            "Recovering the other sister cannot finish the original pool's dying continuation.");
        s.Peach(); s.PoolBoundary(); s.Pool(HappyQiaoPool.XiaoQiao);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.Health == new HappyQiaoDualHealth(3, 0, 3), "The original zero-HP pool still needs real rescue.");
        s.Peach(); s.PoolBoundary(); s.Cold(); s.Pool(HappyQiaoPool.XiaoQiao); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 1, 3) && !s.G.ResolutionStack.OfType<DyingFrame>().Any() && s.V.IsAlive,
            "The exact dying pool's real positive recovery finishes rescue without paying any restored cost twice.");
        s.Need(s.E<HappyQiaoHealthAppliedEvent>().Count(e => e.Kind == HpChangeKind.Loss && e.Pool == HappyQiaoPool.XiaoQiao && e.HpBefore == 2 && e.HpAfter == -1) == 1 &&
            s.E<HappyQiaoHealthAppliedEvent>().Count(e => e.Kind == HpChangeKind.Recovery && e.Pool == HappyQiaoPool.XiaoQiao) == 2,
            "The native HP ledger retains one original loss and two actual same-pool rescue payments across cold returns.");

        s.Need(new[] { "recover", "recover-to-two" }.All(id => s.P!.Choices.Count(c =>
                c.Parameters.GetValueOrDefault("skill-id") == Driver && c.Parameters.GetValueOrDefault("activation-id") == id) == 1),
            "Different pure recovery AI estimates for the two explicit HP hypotheses must not duplicate one legal skill activation or its prompt identity.");
        s.Use("recover-to-two", []); s.PoolBoundary();
        var recoverTo = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        s.Need(recoverTo.HappyQiaoRecoverTo is { TargetHp: 2, Before.DaQiaoHp: 3, Before.XiaoQiaoHp: 1, RecoveryAttemptId: null } intent &&
            recoverTo.HappyQiaoHealth is { ReturnKind: HappyQiaoHealthReturnKind.RecoverTo, Pool: null } actual && actual.Id == intent.HealthRequestId &&
            recoverTo.HappyQiaoQueries is null && !s.G.ResolutionStack.OfType<RecoveryReplacementFrame>().Any(),
            "RecoverTo freezes the target HP on its original instruction and requests an actual pool before manufacturing any recovery amount.");
        s.Cold(); s.Pool(HappyQiaoPool.XiaoQiao); s.Play();
        var queuedTo = s.E<HappyQiaoRecoverToQueuedEvent>().Single();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3) && queuedTo is { TargetHp: 2, Pool: HappyQiaoPool.XiaoQiao, RequestedAmount: 1 } &&
            s.E<ProgramRecoveryRequestedEvent>().Count(e => e.RecoveryFrameId == queuedTo.RecoveryAttemptId && e.RequestedAmount == 1 &&
                e.HpBefore is null && e.HappyQiaoBefore == new HappyQiaoDualHealth(3, 1, 3)) == 1 &&
            s.E<HappyQiaoHealthAppliedEvent>().Count(e => e.ParentFrameId == queuedTo.RecoveryAttemptId && e.Pool == HappyQiaoPool.XiaoQiao && e.HpBefore == 1 && e.HpAfter == 2) == 1,
            "After cold restore the chosen Xiao Qiao pool both determines the one-point difference and receives its single native recovery, without a second selection.");
        var recoveriesBeforeNoEffect = s.E<RecoveryAppliedEvent>().Length;
        s.Use("recover-to-two", []); s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3) && s.E<HappyQiaoRecoverToQueuedEvent>().Length == 1 &&
            s.E<RecoveryAppliedEvent>().Length == recoveriesBeforeNoEffect &&
            s.E<HappyQiaoRecoverToNoRecoveryEvent>() is [{ TargetHp: 2, Pool: HappyQiaoPool.DaQiao }],
            "Choosing the already higher Da Qiao pool produces no healing, no HP loss and no invented zero-valued recovery invoice.");
        CompleteSkillsExtraDrawAndOrderedTongxin();
        CardMaximumReplacementPreservesBothClamps();
    }

    private static void CardMaximumReplacementPreservesBothClamps()
    {
        var s = Scene.Start(maximum: true); s.Play();
        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var nativeUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.SourceSeat == 1 && f.CardKind == CardKind.Slash).Id;
        s.Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == MaximumHold);
        var first = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Last();
        var replaced = s.E<CardDamageReplacedWithMaximumHpLossEvent>().Single();
        s.Need(replaced.ParentResolutionId == nativeUse && replaced.Receipt is { HpBefore: null, HpAfter: null,
                MaximumHpBefore: 3, MaximumHpAfter: 2, HappyQiaoMaximumProof: { Before.DaQiaoHp: 3, Before.XiaoQiaoHp: 3, After.DaQiaoHp: 2, After.XiaoQiaoHp: 2 } } &&
            s.Health == new HappyQiaoDualHealth(2, 2, 2) && first.Change is { Kind: HpChangeKind.MaximumHp, HappyQiaoPool: HappyQiaoPool.DaQiao, HpBefore: 3, HpAfter: 2 } &&
            first.Change.ParentFrameId == nativeUse && !s.E<DamageAppliedEvent>().Any() && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == nativeUse),
            "A true external native Slash replaces damage with one common maximum reduction, retaining both exact clamps and the first pool's original native HP window without fake scalar HP.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == MaximumHold);
        var second = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Last();
        s.Need(second.Change is { Kind: HpChangeKind.MaximumHp, HappyQiaoPool: HappyQiaoPool.XiaoQiao, HpBefore: 3, HpAfter: 2 } &&
            second.Change.ParentFrameId == nativeUse && second.Id != first.Id && s.E<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1,
            "After cold restore Xiao Qiao retains her own second clamp window; the native card and its round replacement cost are not issued again.");
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == nativeUse));
        s.Need(s.Health == new HappyQiaoDualHealth(2, 2, 2) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == nativeUse) == 1 &&
            s.E<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == 0 && e.Delta == -1 && e.MaximumHp == 2 && e.SkillId == MaximumCounter) == 1 &&
            s.E<HappyQiaoMaximumChangedEvent>().Count(e => e.ParentFrameId == nativeUse && e.Before == new HappyQiaoDualHealth(3, 3, 3) && e.After == new HappyQiaoDualHealth(2, 2, 2)) == 1 &&
            s.E<SkillUsageConsumedEvent>().Count(e => e.SkillId == MaximumCounter && e.Scope == SkillUsageScope.Round) == 1,
            "Both native health windows return once to the already paid card; one replacement receipt publishes one maximum fact and consumes one genuine round allowance.");
    }

    private static void CompleteSkillsExtraDrawAndOrderedTongxin()
    {
        var s = Scene.Start(fullSkills: true); s.Play();
        s.Need(s.E<ProgramPhaseScheduledEvent>().Count(e => e.SkillId == "happy:shuangshu" && e.Phase == TurnPhase.Draw && e.Started) == 1 &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.SkillId == "happy:shuangshu" && e.Phase == TurnPhase.Draw && !e.Started) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == NoDraw && e.BindingId == "no-draw" && e.OwnerSeat == 0) == 2,
            "Shuangshu inserts one true separate Draw phase with its own normal-draw window before the ordinary Draw; it is not a raw two-card bonus.");
        var clubSlash = s.V.Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Club).Id;
        var diamondSlash = s.V.Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Diamond).Id;
        s.Card(clubSlash, LegalActionKind.Slash, 1); s.Play();
        s.Use("damage", [0]); s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao); s.Play();
        s.Use("damage", [0]); s.PoolBoundary(); s.Pool(HappyQiaoPool.XiaoQiao);
        s.ActivateShutu(); s.Menu(HappyQiaoStage.UseChoice);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Cards.SequenceEqual([diamondSlash]) && c.Targets.SequenceEqual([1]));
        s.Need(s.P is not { Kind: DecisionKind.PlayCard }, "Shutu's actual red Slash must retain its source Use while opening Tongxin; causal diagnostics: " +
            JsonSerializer.Serialize(new { Qualified = s.E<HappyQiaoRedHealthQualifiedEvent>(), Issued = s.E<HappyQiaoUseIssuedEvent>(),
                Returned = s.E<HappyQiaoUseReturnedEvent>(), Damage = s.E<DamageAppliedEvent>(),
                Native = s.G.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action).Where(a => a.ActorSeat == 0).Select(a => new { a.ActionId, a.Type, a.EffectiveKind, a.EffectiveSuit, a.EffectiveIsRed }) }));
        s.Menu(HappyQiaoStage.HealChoice); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "heal");
        s.PoolBoundary(); s.Pool(HappyQiaoPool.DaQiao);
        s.ActivateShutu(); s.Menu(HappyQiaoStage.SuitChoice); s.Answer(c => c.Parameters.GetValueOrDefault("value") == "DD");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        var gain = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HappyQiao?.Stage == HappyQiaoStage.GainChildren);
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.HappyQiaoShutuUse is not null);
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3) && gain.HappyQiao!.Gained.Count == 2 &&
            gain.HappyQiao.Gained.All(id => s.V.Hand.Any(c => c.Id == id && c.Suit == Suit.Diamond)) &&
            use.UnlimitedUse && use.IssuedNoResponse?.CardActionId == use.Action?.ActionId && use.CardKind == CardKind.Slash &&
            use.PhysicalCardIds!.SequenceEqual([diamondSlash]) && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
            "The spent-quota Shutu Slash is one actual unrespondable native Use; its red damage opens Tongxin healing, whose Da Qiao recovery then takes two chosen real Diamond entities before returning.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.V.Hand.Count(c => c.Suit == Suit.Heart) == 2 && s.V.Hand.Count(c => c.Suit == Suit.Diamond) == 3 &&
            s.E<HappyQiaoBranchEvent>().Count(e => e.Stage == HappyQiaoStage.HealChoice && e.Branch == "heal") == 1 &&
            !s.E<HappyQiaoCostPaidEvent>().Any() && s.E<HappyQiaoUseIssuedEvent>().Length == 1 && s.E<HappyQiaoUseReturnedEvent>().Length == 1,
            "Tongxin checks the live red hand counts only after the native recovery and both Shutu gains return; the now-unequal counts cannot use the earlier equality.");
        var discard = s.V.Hand.Where(c => c.Suit == Suit.Diamond).Take(2).Select(c => c.Id).ToArray();
        s.Accept(new UseProgramSkillCommand(0, Driver, "discard-two", discard, [], s.G.Revision, s.P!.PromptId)); s.Play();
        var peach = s.V.Hand.Single(c => c.Kind == CardKind.Peach).Id;
        s.Card(peach, LegalActionKind.Peach, 0); s.PoolBoundary(); s.Pool(HappyQiaoPool.XiaoQiao);
        s.Menu(HappyQiaoStage.DiscardChoice);
        var redCost = s.V.Hand.Single(c => c.Suit == Suit.Heart).Id;
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([redCost]));
        s.Menu(HappyQiaoStage.TargetChoice); s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        var paid = s.E<HappyQiaoCostPaidEvent>().Single();
        var facts = s.G.Events.Select(e => e.Payload).ToArray();
        var paidAt = Array.FindIndex(facts, e => e is HappyQiaoCostPaidEvent p && p.FrameId == paid.FrameId);
        var completedAt = Array.FindIndex(facts, e => e is HappyQiaoCompletedEvent done && done.FrameId == paid.FrameId);
        s.Need(s.Health == new HappyQiaoDualHealth(3, 3, 3) && paid.CardId == redCost && paid.From == CardLocation.Hand(0) &&
            s.G.CardMovements.Count(m => m.CardId == redCost && m.Sequence == paid.Sequence && m.To == paid.To) == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == paid.FrameId && e.ExecutorSeat == 0 && e.HandCount == 1) == 1 &&
            completedAt > paidAt && facts.Skip(paidAt + 1).Take(completedAt - paidAt - 1).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1,
            "A later true red Peach heals only Xiao Qiao, skips the impossible full-HP first reward, then really pays one red hand card and applies the second Tongxin damage exactly once.");

        var blackDuel = s.V.Hand.Single(c => c.Kind == CardKind.Duel && c.Suit == Suit.Club).Id;
        s.Card(blackDuel, LegalActionKind.Duel, 1);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var blackUseId = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == blackDuel && f.CardKind == CardKind.Duel).Id;
        s.Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "take-duel-damage");
        s.PoolBoundary(); s.Pool(HappyQiaoPool.XiaoQiao);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "happy:shutu" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var blackHealth = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Last();
        var blackDamage = s.G.ResolutionStack.OfType<DamageFrame>().SingleOrDefault(f => f.Id == blackHealth.Change.ParentFrameId);
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3) && blackHealth.Change is { Kind: HpChangeKind.Damage, HappyQiaoPool: HappyQiaoPool.XiaoQiao, TargetSeat: 0 } &&
            blackDamage is { SourceSeat: 1, TargetSeat: 0, Amount: 1 } && blackDamage.ParentFrameId == blackUseId &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == blackUseId) && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == blackUseId),
            "A true black Duel's selected Xiao Qiao damage opens Shutu immediately on the original DamageFrame before its native Use can finish, without needing Tongxin's red-use qualification.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.Health == new HappyQiaoDualHealth(3, 2, 3) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == blackUseId) == 1 &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "Declining the original black-damage Shutu opportunity returns once to the same native Duel.");
    }

    private sealed class Fixture(bool fullSkills = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-qiao-health", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            if (fullSkills) CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":true,"isSkillInvocation":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.happy.shutu.chosen-red"],"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{MaximumCounter}}","revision":1,"cardPolicies":[{"id":"native-first-round-slash-maximum","kind":"firstRoundCardDamageBecomesMaximumHpLoss","cardKinds":["slash"]}],"triggers":[{"id":"real-other-card","window":"playEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"isSkillInvocation":false,"effects":[{"op":"useVirtualSlash","target":"owner","targetRef":{"kind":"eventTarget"} }]}]},
              {"id":"{{MaximumHold}}","revision":1,"triggers":[{"id":"actual-pool-maximum","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"maximumHp"},"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"maximum","options":[{"id":"continue"}]}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
                {"id":"recover-to-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":2,"clampToMaxHp":true}]},
                {"id":"draw-current-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","numberExpression":"currentHp"}]},
                {"id":"loss-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
                {"id":"discard-two","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":2}]}
              ]}
            ]}
            """;
            var presentations = new[] { NoDraw, Quiet, Driver, GainHold, MaximumCounter, MaximumHold }.ToDictionary(id => id, id => new { name = id, description = "真实双池原生健康与三技能原帧子返回",
                optionLabels = id == MaximumHold ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = p, ProgramPresentation = catalog.Presentations[id] });
            if (!fullSkills) b.AddGeneral(new(Hero, "双池原生健康角色", "", NoDraw, "god", 3, [Driver]) { Gender = GeneralGender.Female });
            b.AddGeneral(new(Peer, "安静健康旁观角色", "", NoDraw, "wei", 12, [Quiet]));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", fullSkills && i >= 40 ? i % 2 == 0 ? Suit.Heart : Suit.Diamond : Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐双池健康小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PlayerSnapshot V => G.CreateSnapshot(0).Players[0];
        public HappyQiaoDualHealth Health => V.HappyQiaoHealth!;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Menu(HappyQiaoStage stage) => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "happy-qiao-skills") == true &&
            G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappyQiao is not null)?.HappyQiao?.Stage == stage);
        public void ActivateShutu()
        {
            Reach(() => P?.SkillPrompt?.SkillId == "happy:shutu" && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        }
        public void Card(int id, LegalActionKind kind, int? target = null)
        {
            var action = G.GetHumanLegalActions().Single(a => a.CardId == id && a.Kind == kind && a.TargetSeats.SequenceEqual(target is { } seat ? [seat] : Array.Empty<int>()));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void PoolBoundary() => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "happy-qiao-health") == true);
        public void Pool(HappyQiaoPool pool) => Answer(c => c.Parameters.GetValueOrDefault("pool") == pool.ToString());
        public void Peach() => Answer(c => c.Cards.Count == 1 && V.Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Peach));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing exact dual-pool health choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 100; n++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 }) Need(false, "Unexpected Qiao health boundary " + JsonSerializer.Serialize(new
                {
                    P, Qualified = E<HappyQiaoRedHealthQualifiedEvent>(), Started = E<HappyQiaoDamageHealthStartedEvent>(),
                    Returned = E<HappyQiaoDamageHealthReturnedEvent>(), NativeIssued = E<HappyQiaoUseIssuedEvent>(), NativeReturned = E<HappyQiaoUseReturnedEvent>(),
                    Actions = E<CardActionAcceptedEvent>().Select(e => e.Action).Where(a => a.ActorSeat == 0)
                        .Select(a => new { a.ActionId, a.Type, a.EffectiveKind, a.EffectiveSuit, a.EffectiveIsRed }),
                    Damage = E<DamageAppliedEvent>()
                }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missed bounded Qiao health flow.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold preserves both pools, original selection invoice and same-pool dying cursor.");
        }
        public void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected dual-pool health command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Health = V.HappyQiaoHealth, Facts = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool fullSkills = false, bool maximum = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(fullSkills));
            var peer = new ShanheCombatantDefinition(Peer, Hp: 12) { StartingHand = fullSkills ? [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Club, 3)] : [] };
            var duelPartner = fullSkills ? peer with { StartingHand = [.. peer.StartingHand ?? [], new(CardKind.Slash, Suit.Club, 8)] } : peer;
            if (maximum) duelPartner = duelPartner with { ExtraSkillIds = [MaximumCounter] };
            var setup = new ShanheBattleSetup(47, 1, "happy-qiao-health", Hero, 3, 0, 0, 0, 0, [],
                fullSkills ? [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Slash, Suit.Diamond, 6), new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Duel, Suit.Club, 9)] :
                    [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Peach, Suit.Heart, 6)], [], [peer, peer], [duelPartner], new string('a', 64))
            { DefaultGodFactionId = "wu" };
            if (fullSkills) setup = setup with { ExtraSkillIds = [NoDraw, Driver, GainHold] };
            if (maximum) setup = setup with { ExtraSkillIds = [MaximumHold], StartingCards = [new(CardKind.Dodge, Suit.Club, 2)] };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
