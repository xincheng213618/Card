using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Static command drafts. This batch deliberately has not loaded or run them.
internal static class BoundaryBuLianShiChecks
{
    private const string Anxu = "boundary:anxu-current", Zhuiyi = "boundary:zhuiyi-current";
    private const string Driver = "fixture:bls-driver", Gain = "fixture:bls-gain", Gift = "fixture:bls-gift", Hp = "fixture:bls-hp", Reward = "fixture:bls-reward";
    private const string NestedGain = "fixture:bls-nested-gain", NestedHp = "fixture:bls-nested-hp", NestedEntry = "fixture:bls-nested-entry", NestedRescue = "fixture:bls-nested-rescue", NestedCommitted = "fixture:bls-nested-committed";
    private const string Mode = "identity:classic-bu-lian-shi-fixture";
    private const string ObtainReason = "skill-program." + Anxu + ".ObtainOneFromEachSelectedTarget";
    private const string GiftReason = "skill-program." + Anxu + ".GiveShownCardToLeastOriginalTarget";
    private const string BenefitDrawReason = "skill-program." + Zhuiyi + ".Draw";

    public static void OrderedOpaquePairPaysBeforePublicGiftAndReward()
    {
        foreach (var suit in new[] { Suit.Spade, Suit.Heart })
        {
            var (g, r) = Create(suit: suit); Play(g);
            var firstBefore = V(g, 1).HandCount; var secondBefore = V(g, 2).HandCount;
            AnxuPair(g, 1, 2); Reach(g, p => Action(p, "pair-obtain"));
            var rootId = PairRoot(g).Id;
            Require(P(g)!.TargetSeat == 1 && P(g)!.ValidCardIds.Count == 0 && P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Label.Contains("手牌", StringComparison.Ordinal)),
                "The original first foreign hand publishes opaque slots, without identities or a filtered card name.");
            Private(g, 0); Reject(g); g = RestoreAfterCold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
            var root = PairRoot(g); var paid = root.PairObtain!.FirstPayment!;
            Require(root.Id == rootId && root.PairObtain is { Cursor: 0, AwaitingMovement: true, SecondPayment: null } &&
                paid.From == CardLocation.Hand(1) && paid.Delivered && !paid.SameHand && Moves(g, ObtainReason) is [var first] && first.CardId == paid.CardId &&
                first.To == CardLocation.Hand(0) && V(g, 1).HandCount == firstBefore - 1 && V(g, 2).HandCount == secondBefore &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.ParentFrameId == rootId &&
                    (w.Batch.AwaitingProgramFrameId is null || w.Batch.AwaitingProgramFrameId == rootId)),
                "One real first obtain owns the gain child before the second original slot is selected; no future target has paid.");
            g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => Action(p, "pair-obtain"));
            Require(P(g)!.TargetSeat == 2 && PairRoot(g).PairObtain is { Cursor: 1, AwaitingMovement: false }, "The exact ordered cursor advances only after the first gain children return.");
            Private(g, 0); g = RestoreAfterCold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Reach(g, p => Action(p, "select-owned-cards"));
            var shown = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
            Private(g, 0); g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([shown])); Reach(g, p => Action(p, "shown-pair-gift"));
            root = PairRoot(g); var gift = root.ShownPairGift!;
            Require(root.PairObtain is { Cursor: 2, AwaitingMovement: false } && Moves(g, ObtainReason).Length == 2 && gift.CardId == shown && gift.FrozenSuit == suit &&
                gift.FrozenFirstHandCount == V(g, 1).HandCount && gift.FrozenSecondHandCount == V(g, 2).HandCount &&
                P(g)!.Choices.All(c => c.Targets is [1] or [2]) &&
                root.CardSetBindings.Single(b => b.Name == "shown-gift").Visibility == SkillProgramCardSetVisibility.Public &&
                F<ProgramCardsRevealedEvent>(g).Any(e => e.FrameId == rootId),
                "The public single-card reveal freezes the effective suit and real post-gain hand counts of the original pair.");
            g = RestoreAfterCold(g, r); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => p.SkillPrompt?.SkillId == Gift && p.PlayerSeat == 1);
            Require(Moves(g, GiftReason) is [var delivered] && delivered.CardId == shown && delivered.From == CardLocation.Hand(0) && delivered.To == CardLocation.Hand(1) &&
                !F<ShownPairGiftRewardIssuedEvent>(g).Any() && PairRoot(g).ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingGift, Delivered: true },
                "The shown entity really reaches the original least-hand recipient before any non-Spade reward.");
            g = RestoreAfterCold(g, r); Continue(g);
            if (suit != Suit.Spade)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Reward && p.PlayerSeat == 0);
                Require(F<ShownPairGiftRewardIssuedEvent>(g).Single() is { RequestedCount: 1, ActualCount: 1 } && PairRoot(g).ShownPairGift is { Stage: ProgramShownPairGiftStage.AwaitingReward },
                    "The actual one-card reward owns its own gain child after the gift, using the frozen revealed suit.");
                g = RestoreAfterCold(g, r); Continue(g);
            }
            Play(g);
            Require(Moves(g, ObtainReason).Length == 2 && Moves(g, GiftReason).Length == 1 && F<ShownPairGiftCommittedEvent>(g).Length == 1 &&
                F<ShownPairGiftRewardIssuedEvent>(g).Length == (suit == Suit.Spade ? 0 : 1) && !g.GetHumanLegalActions().Any(a => a.SkillId == Anxu) &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PairObtain is not null),
                "Both real payments, the shown gift and its optional reward complete once; the actual play-phase usage remains consumed."); Cold(g, r);
        }
    }

    public static void SelfHandSelectionIsNoMoveAndEquipmentObtainOwnsRecovery()
    {
        var (same, sr) = Create(); Play(same); AnxuPair(same, 0, 1); Reach(same, p => Action(p, "pair-obtain"));
        var ownId = P(same)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); var count = same.CardMovements.Count;
        Private(same, 0); same = RestoreAfterCold(same, sr); Answer(same, c => c.Cards.SequenceEqual([ownId])); Reach(same, p => Action(p, "pair-obtain"));
        Require(same.CardMovements.Count == count && PairRoot(same).PairObtain is { Cursor: 1, FirstPayment: { SameHand: true, Delivered: false } } &&
            F<PairObtainStepCommittedEvent>(same) is [var noMove] && noMove.SameHand && noMove.SequenceBefore == noMove.SequenceAfter &&
            !same.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(e => e.Batch.Movements.Any(m => m.Reason.Value == ObtainReason)),
            "Selecting an already owned hand card advances the exact private cursor without manufacturing a loss, gain or intermediate Processing move.");
        same = RestoreAfterCold(same, sr); Answer(same, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Reach(same, p => p.SkillPrompt?.SkillId == Gain); same = RestoreAfterCold(same, sr); Continue(same);
        CompleteShownGift(same); Play(same); Require(Moves(same, ObtainReason).Length == 1 && (V(same, 0).Hand.Any(c => c.Id == ownId) ||
            Moves(same, GiftReason).Any(m => m.CardId == ownId)), "The original self-hand entity only leaves its hand if it is the later real shown gift."); Cold(same, sr);

        var (g, r) = Create(equipment: true); Play(g); Use(g, "equip", [0]); Play(g);
        var lion = V(g, 0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id; var hpBefore = g.State.Players[0].Hp;
        Require(hpBefore < g.State.Players[0].MaxHp, "The formal Lord starts genuinely wounded, rather than an injected HP state.");
        AnxuPair(g, 0, 1); Reach(g, p => Action(p, "pair-obtain")); Private(g, 0); g = RestoreAfterCold(g, r);
        Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0);
        var root = PairRoot(g); var payment = root.PairObtain!.FirstPayment!;
        Require(root.PairObtain is { Cursor: 0, AwaitingMovement: true, SecondPayment: null } && payment.From == CardLocation.Equipment(0) && payment.Delivered &&
            Moves(g, ObtainReason) is [var actual] && actual.CardId == lion && actual.To == CardLocation.Hand(0) &&
            g.State.Players[0].Hp == hpBefore + 1 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w => w.Change.ParentFrameId == root.Id &&
                w.ResumeFrameId == root.Id && w.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "The real Equipment-to-Hand obtain removes Silver Lion once and pauses in its precise recovery before the next original target.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
        Require(PairRoot(g).PairObtain is { Cursor: 0, AwaitingMovement: true }, "The gain child still belongs to the first equipment obtain after its HP child returns.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => Action(p, "pair-obtain")); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        CompleteShownGift(g); Play(g);
        Require(Moves(g, ObtainReason).Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0)) == 1 &&
            F<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "Cold continuation finishes the original pair, with one actual armor removal and one actual armor recovery."); Cold(g, r);

        var (lost, lr) = Create(removePaidSource: true); Play(lost);
        var untouched = V(lost, 2).HandCount; AnxuPair(lost, 1, 2); Reach(lost, p => Action(p, "pair-obtain"));
        Answer(lost, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Reach(lost, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
        var lostRootId = PairRoot(lost).Id; var retained = PairRoot(lost).PairObtain!.FirstPayment!.CardId;
        lost = RestoreAfterCold(lost, lr); Continue(lost); Play(lost);
        Require(!V(lost, 0).Skills!.Any(s => s.Id == Anxu) && V(lost, 0).Skills!.Any(s => s.Id == "fixture:bls-noop") &&
            F<SkillsAcquiredEvent>(lost).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Gain && e.SkillIds.Contains("fixture:bls-noop")) == 1,
            "The real first-gain child acquires one independent suppression source and removes Anxu from the qualified snapshot; this does not physically remove its grant.");
        Require(Moves(lost, ObtainReason) is [var retainedMove] && retainedMove.CardId == retained && retainedMove.To == CardLocation.Hand(0) &&
            V(lost, 0).Hand.Any(c => c.Id == retained) && V(lost, 2).HandCount == untouched &&
            F<SkillsAcquiredEvent>(lost).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Gain && e.SkillIds.Contains("fixture:bls-noop")) &&
            !F<ProgramCardsRevealedEvent>(lost).Any(e => e.FrameId == lostRootId) && !F<ShownPairGiftCommittedEvent>(lost).Any() &&
            !lost.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == lostRootId) && !lost.GetHumanLegalActions().Any(a => a.SkillId == Anxu),
            "The genuine first-gain program suppresses the original source's qualification after payment: the paid entity remains, while no second obtain, reveal or gift is invented."); Cold(lost, lr);
    }

    public static void EndingIssuanceAndOwnerDeathRepeatOnlyOriginalRecipient()
    {
        var (g, r) = Create(deathOwner: true); Play(g); Require(g.State.Players[0].Role == Role.Renegade && g.State.Players[0].Hp == 1, "The fixed genuine death owner is a non-Lord with one actual HP.");
        End(g); ActivateZhuiyi(g); Reach(g, p => Action(p, "select-target")); Private(g, 0); g = RestoreAfterCold(g, r); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 2);
        var issued = F<FixedRecipientBenefitIssuedEvent>(g).Single(); var root = FixedRoot(g);
        Require(root.FixedRecipient is { DeathReplay: false, DrawIssued: true, ActualDrawCount: 3, RecipientSeat: 2 } && issued.ProgramFrameId == root.Id &&
            Moves(g, BenefitDrawReason).Count(m => m.To == CardLocation.Hand(2)) == 3 && root.WindowContext?.Window == SkillProgramTriggerWindow.TurnEnding,
            "The real own Ending issues one original beneficiary, and all three actual draw ledger entries belong to its typed program.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 2);
        Require(FixedRoot(g).FixedRecipient is { RecoveryHpBefore: not null, RecoveryAmount: 1 } && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w =>
            w.Change.ParentFrameId == issued.ProgramFrameId && w.ResumeFrameId == issued.ProgramFrameId && w.Continuation == PostEventContinuation.Program && w.Change.TargetSeat == 2),
            "Recover1 happens after all real Draw3 children, with the same original beneficiary and exact HP return.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g); Use(g, "hurt", [2]); Play(g);
        Use(g, "hurt", [0]); Reach(g, p => p.SkillPrompt?.SkillId == Zhuiyi && Action(p, "activate"));
        var death = g.ResolutionStack.OfType<DeathFrame>().Single(d => d.VictimSeat == 0);
        var deathWindow = g.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(w => w.DeathFrameId == death.Id);
        Require(!g.State.Players[0].IsAlive && g.State.Winner == Winner.None && deathWindow.OwnerSeat == 0,
            "Actual LoseHp, native rescue refusal and real death produce an OwnerDied window without prematurely ending the match.");
        g = RestoreAfterCold(g, r); ActivateZhuiyi(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 2);
        root = FixedRoot(g); var repeated = F<FixedRecipientDeathBenefitStartedEvent>(g).Single();
        Require(root.FixedRecipient is { DeathReplay: true, DrawIssued: true, ActualDrawCount: 3, RecipientSeat: 2 } receipt &&
            receipt.IssuanceProgramFrameId == issued.ProgramFrameId && receipt.Source == issued.Source && receipt.OwnerDeathWindowFrameId == deathWindow.Id &&
            repeated.RecipientSeat == 2 && repeated.IssuanceProgramFrameId == issued.ProgramFrameId &&
            !P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"),
            "Death reuses only the exact issued original recipient and instance; no unrelated target selection or second limited debit appears.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 2); g = RestoreAfterCold(g, r); Continue(g);
        Until(g, engine => !engine.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.FixedRecipient is not null) && !engine.ResolutionStack.OfType<DeathFrame>().Any(d => d.Id == death.Id));
        Require(F<FixedRecipientBenefitIssuedEvent>(g).Length == 1 && F<FixedRecipientDeathBenefitStartedEvent>(g).Length == 1 &&
            F<FixedRecipientBenefitDrawIssuedEvent>(g).Length == 2 && F<FixedRecipientBenefitDrawIssuedEvent>(g).All(e => e.RecipientSeat == 2 && e.ActualCount == 3) &&
            F<FixedRecipientBenefitRecoveryRequestedEvent>(g).Length == 2 && F<FixedRecipientBenefitRecoveryRequestedEvent>(g).All(e => e.RecipientSeat == 2 && e.Amount == 1),
            "Native beneficiary observers and real cold-restored commands complete Draw3/Recover1 twice for the same recipient and then return the original death."); Cold(g, r);
        NestedOriginalDeathBenefit("damage");
        NestedOriginalDeathBenefit("hp-loss");
    }

    public static void UnissuedOrDeadRecipientNeverPublishesReplacementDeathTarget()
    {
        var (empty, er) = Create(deathOwner: true); Play(empty); Use(empty, "hurt", [0]);
        Until(empty, engine => F<PlayerDiedEvent>(engine).Any(e => e.VictimSeat == 0) && !engine.ResolutionStack.OfType<DeathFrame>().Any(d => d.VictimSeat == 0));
        Require(!F<FixedRecipientBenefitIssuedEvent>(empty).Any() && !F<FixedRecipientDeathBenefitStartedEvent>(empty).Any() &&
            !F<ProgramBindingStartedEvent>(empty).Any(e => e.SkillId == Zhuiyi && e.Window == SkillProgramTriggerWindow.OwnerDied),
            "An owner who never issued the limited own-Ending benefit cannot choose a new arbitrary death beneficiary."); Cold(empty, er);

        var (g, r) = Create(deathOwner: true); Play(g);
        var recipient = Enumerable.Range(1, 3).First(seat => g.State.Players[seat].Role != Role.Lord);
        End(g); ActivateZhuiyi(g); Reach(g, p => Action(p, "select-target")); Answer(g, c => c.Targets.SequenceEqual([recipient]));
        Play(g); End(g); Play(g);
        Require(F<FixedRecipientBenefitIssuedEvent>(g).Length == 1, "The second real own Ending cannot issue another beneficiary from the named game usage.");
        while (g.State.Players[recipient].Hp > 1) { Use(g, "hurt", [recipient]); Play(g); }
        Use(g, "hurt", [recipient]); Play(g); Require(!g.State.Players[recipient].IsAlive && g.State.Winner == Winner.None, "Real commands kill the original non-Lord recipient while retaining a live Lord and other players.");
        g = RestoreAfterCold(g, r); Use(g, "hurt", [0]);
        Until(g, engine => F<PlayerDiedEvent>(engine).Any(e => e.VictimSeat == 0) && !engine.ResolutionStack.OfType<DeathFrame>().Any(d => d.VictimSeat == 0));
        Require(F<FixedRecipientBenefitIssuedEvent>(g).Single().RecipientSeat == recipient && F<FixedRecipientDeathBenefitStartedEvent>(g).Length == 0 &&
            !F<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Zhuiyi && e.Window == SkillProgramTriggerWindow.OwnerDied),
            "A dead original recipient cancels the death opportunity and does not turn it into a new target or a second issuance."); Cold(g, r);
    }


    private static void NestedOriginalDeathBenefit(string mode)
    {
        var (g, r) = Create(deathOwner: true, deathNesting: mode); Play(g);
        End(g); ActivateZhuiyi(g); Reach(g, p => Action(p, "select-target")); Answer(g, c => c.Targets.SequenceEqual([2]));
        Play(g);
        // Actual commands lower the beneficiary to 1, never reflection/state injection.
        for (var i = 0; i < 7 && g.State.Players[2].Hp > 1; i++) { Use(g, "hurt", [2]); Play(g); }
        Require(g.State.Players[2].Hp == 1, "The fixed real beneficiary has 1 HP before the original owner dies.");
        var issued = F<FixedRecipientBenefitIssuedEvent>(g).Single();
        Use(g, "kill-damage", [0]); Reach(g, p => p.SkillPrompt?.SkillId == Zhuiyi && Action(p, "activate"));
        var originalDying = g.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == 0);
        var originalDeath = g.ResolutionStack.OfType<DeathFrame>().Single(d => d.VictimSeat == 0);
        var frozenOriginalDeath = JsonSerializer.Serialize(originalDeath);
        var window = g.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(w => w.DeathFrameId == originalDeath.Id);
        var originalAttack = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.AttackAttempt is not null);
        Require(originalDeath.ParentFrameId == originalDying.Id && originalDying.Continuation == DyingContinuationKind.Damage &&
            g.ResolutionStack.OfType<DamageFrame>().Single(d => d.Id == originalDying.ParentFrameId).ParentFrameId == originalAttack.Id,
            "A true source-owned Damage -> Dying -> Death chain remains the original parent.");
        g = RestoreAfterCold(g, r); ActivateZhuiyi(g);
        Reach(g, p => p.SkillPrompt?.SkillId == (mode == "damage" ? NestedGain : NestedHp) && p.PlayerSeat == 2);
        var root = FixedRoot(g); var returnReceipt = root.OwnedDeathBenefitReturn!;
        Require(returnReceipt.ProgramFrameId == root.Id && returnReceipt.OriginalDeath.FrameId == originalDeath.Id &&
            returnReceipt.OriginalDying?.FrameId == originalDying.Id && returnReceipt.OwnerDeathWindowFrameId == window.Id &&
            returnReceipt.OriginalAttackOwnerFrameId == originalAttack.Id && root.FixedRecipient!.IssuanceProgramFrameId == issued.ProgramFrameId &&
            returnReceipt.OriginalDeath.CleanedUpCardIds is System.Collections.IList { IsReadOnly: true } &&
            returnReceipt.OriginalDying!.ResponderSeats is System.Collections.IList { IsReadOnly: true } &&
            returnReceipt.OriginalDying!.AttemptedSelfDyingBindings is System.Collections.IList { IsReadOnly: true },
            "The opt-in return freezes only this original dead victim, its exact cursor/source and original paid benefit.");
        Private(g, 2); Reject(g); g = RestoreAfterCold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == NestedEntry && p.PlayerSeat == 2);
        var nestedDying = g.ResolutionStack.OfType<DyingFrame>().Single(d => d.VictimSeat == 2);
        Require(g.State.Players[2].IsAlive && g.State.Players[2].Hp == 0 && nestedDying.Id != originalDying.Id &&
            g.ResolutionStack.OfType<DyingFrame>().Count() == 2 &&
            JsonSerializer.Serialize(g.ResolutionStack.OfType<DeathFrame>().Single(d => d.Id == originalDeath.Id)) == frozenOriginalDeath &&
            g.ResolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Single(w => w.Id == window.Id).CandidateIndex == window.CandidateIndex &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.ResumeDyingFrameId == nestedDying.Id &&
                w.Window == SkillProgramTriggerWindow.DyingEntering && w.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry),
            "Actual gain Damage or recovery LoseHp reaches a distinct live DyingEntering pause without advancing or removing the original death.");
        g = RestoreAfterCold(g, r); Continue(g);
        if (mode == "hp-loss")
        {
            Reach(g, p => p.SkillPrompt?.SkillId == NestedCommitted && p.PlayerSeat == 2);
            var rescue = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.DyingResponse?.ResolutionId == nestedDying.Id);
            var materialIds = rescue.PhysicalCardIds!.ToArray();
            Require(rescue.CardKind == CardKind.Peach && rescue.SourceSeat == 2 && materialIds.Length == 2 &&
                rescue.Action!.PhysicalCards.Select(c => c.CardId).SequenceEqual(materialIds) &&
                materialIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing) == 1),
                "The newly granted response issues one real Peach from two real black materials, only after the original owner is already dead.");
            g = RestoreAfterCold(g, r); Continue(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 2 && g.ResolutionStack.OfType<DyingFrame>().Any(d => d.Id == nestedDying.Id));
            Require(g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Continuation == PostEventContinuation.CardUse &&
                h.ResumeFrameId == rescue.Id && h.Change.ParentFrameId == rescue.Id && h.Change.TargetSeat == 2),
                "Actual Peach HP observers retain the exact rescue use and both original and new Dying ancestors.");
            g = RestoreAfterCold(g, r); Continue(g);
            Until(g, engine => !engine.ResolutionStack.OfType<DyingFrame>().Any(d => d.Id == nestedDying.Id));
            Require(materialIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1),
                "Cold-restored two-material rescue pays and cleans each entity once.");
        }
        Until(g, engine => !engine.ResolutionStack.OfType<DeathFrame>().Any(d => d.Id == originalDeath.Id) &&
            !engine.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id || f.Id == originalAttack.Id));
        Require(g.State.Players[2].IsAlive && !g.State.Players[0].IsAlive &&
            F<OwnedDeathBenefitReturnIssuedEvent>(g).Length == 1 && F<OwnedDeathBenefitReturnedEvent>(g) is [var returned] && returned.Completed &&
            returned.ProgramFrameId == root.Id && returned.OriginalDeathFrameId == originalDeath.Id && returned.OriginalDyingFrameId == originalDying.Id &&
            F<FixedRecipientBenefitDrawIssuedEvent>(g).Length == 2 && F<FixedRecipientBenefitDrawIssuedEvent>(g).All(e => e.ActualCount == 3 && e.RecipientSeat == 2) &&
            F<FixedRecipientBenefitRecoveryRequestedEvent>(g).Length == 2 &&
            F<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 0) == 1 &&
            F<PlayerDyingEvent>(g).Count(e => e.ResolutionId == nestedDying.Id && e.VictimSeat == 2) == 1,
            "The original recipient's two benefits, nested real Dying/rescue, original damage/death cursor and typed return complete once.");
        Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Create(Suit suit = Suit.Spade, bool equipment = false, bool deathOwner = false, bool removePaidSource = false, string? deathNesting = null)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(suit, equipment, deathOwner, removePaidSource, deathNesting));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = deathOwner ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Require(P(g)!.ValidContentIds.Contains("fixture:bls-owner"),
            "The fixed published general selection actually retains the owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:bls-owner", g.Revision, P(g)!.PromptId));
        if (removePaidSource)
        {
            Play(g);
            Require(!V(g, 0).Skills!.Any(s => s.Id == Anxu), "The source-loss fixture does not print the skill whose acquired qualification will be suppressed.");
            Use(g, "grant-anxu", []); Play(g);
            Require(V(g, 0).Skills!.Any(s => s.Id == Anxu) &&
                V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Anxu).IsAcquired &&
                F<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Anxu)) == 1,
                "One actual activation independently grants Anxu before its real pair payment and later qualification suppression.");
        }
        return (g, r);
    }
    private static void AnxuPair(GameEngine g, int a, int b) => Accept(g, new UseProgramSkillCommand(0, Anxu, "pair-obtain-show-give", [], [a, b], g.Revision, P(g)!.PromptId));
    private static void ActivateZhuiyi(GameEngine g) { Reach(g, p => p.SkillPrompt?.SkillId == Zhuiyi && Action(p, "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Zhuiyi); }
    private static void CompleteShownGift(GameEngine g)
    { Reach(g, p => Action(p, "select-owned-cards")); Answer(g, c => c.Cards.Count == 1); Reach(g, p => Action(p, "shown-pair-gift")); Answer(g, c => c.Targets.Count == 1); }
    private static ProgramSkillFrame PairRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PairObtain is not null);
    private static ProgramSkillFrame FixedRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FixedRecipient is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g)
    { var p = P(g)!; Require(p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"), "The actual child publishes its continue option.");
        if (p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> expected) => Until(g, engine => P(engine) is { } p && expected(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> expected)
    { for (var i = 0; i < 240; i++) { if (expected(g)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Bu Lian Shi command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Events = g.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Gain or Gift or Hp or Reward) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die")) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four prepared views, exact typed frames, scalar facts, real movement ledger and accepted-command history cold-restore identically.");
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "Cold JSON restoration preserves the paid parent and resumes this actual restored instance."); return restored; }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished private answer cannot pay or alter the original cursor."); }
    private static void Private(GameEngine g, int chooser)
    { foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "Only the original chooser sees its private card choices or opaque slots.");
        var p = g.CreateSnapshot(chooser).PendingDecision!; Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }), "Prepared outer and nested choice lists expose only frozen collections."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Suit suit, bool equipment, bool deathOwner, bool removePaidSource, string? deathNesting) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:boundary-bu-lian-shi", "1.0.0", "当前OL机制真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:bls-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                  {"id":"fixture:bls-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:bls-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.ObtainOneFromEachSelectedTarget","skill-program.boundary:zhuiyi-current.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-gift","revision":1,"triggers":[{"id":"gift","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.GiveShownCardToLeastOriginalTarget"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:bls-reward","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:anxu-current.GiveShownCardToLeastOriginalTarget.reward"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (removePaidSource)
            {
                ((JsonArray)rules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"grant-anxu","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:anxu-current"]}]}"""));
                ((JsonArray)rules["skills"]![2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-noop"]}"""));
            }

            if (deathNesting is not null)
            {
                ((JsonArray)rules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"kill-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}"""));
                // This is a real first-Ending HP observer's skill grant. The new
                // candidates are absent from that already-frozen HP window.
                ((JsonArray)rules["skills"]![4]!["triggers"]![0]!["effects"]!).Add(JsonSerializer.SerializeToNode(new
                    { op = "grantSkills", target = "owner", skillIds = new[] { deathNesting == "damage" ? NestedGain : NestedHp } }));
                var nested = JsonNode.Parse("""
                    [
                     {"id":"fixture:bls-nested-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"priority":20,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:zhuiyi-current.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"nested-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-nested-entry"]},{"op":"damage","target":"owner","amount":1}]}]},
                     {"id":"fixture:bls-nested-hp","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"priority":20,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"nested-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:bls-nested-entry","fixture:bls-nested-rescue","fixture:bls-nested-committed"]},{"op":"loseHp","target":"owner","amount":2}]}]},
                     {"id":"fixture:bls-nested-entry","revision":1,"triggers":[{"id":"entering","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
                     {"id":"fixture:bls-nested-rescue","revision":1,"viewAs":[{"id":"two-spades","inputKinds":["slash"],"inputSuits":["spade"],"outputKind":"peach","forPlay":false,"forResponse":true,"inputCount":2,"sameSuit":true,"sourceZones":["hand","equipment"],"extendedUse":true}]},
                     {"id":"fixture:bls-nested-committed","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["peach"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-seen","options":[{"id":"continue"}]}]}]}
                    ]
                    """)!.AsArray();
                if (deathNesting == "damage")
                    ((JsonArray)nested[2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}"""));
                foreach (var definition in nested.ToArray()) ((JsonArray)rules["skills"]!).Add(definition!.DeepClone());
            }

            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "固定实体与真实HP" }, ["fixture:bls-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Gain] = Pause("真实获得"), [Gift] = Pause("展示后赠牌"), [Hp] = Pause("真实回复"), [Reward] = Pause("非黑桃奖励") } });
            if (deathNesting is not null)
            {
                var p = JsonNode.Parse(presentation)!;
                foreach (var id in new[] { NestedGain, NestedHp, NestedEntry, NestedCommitted }) p["skills"]![id] = JsonSerializer.SerializeToNode(Pause("死亡收益实际子链"));
                p["skills"]![NestedRescue] = JsonSerializer.SerializeToNode(new { name = "两实体真实救援", description = "全黑实体真实转换桃" });
                presentation = p.ToJsonString();
            }

            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:bls-quiet", Gain, Gift, Hp, Reward }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            if (deathNesting is not null) foreach (var id in new[] { NestedGain, NestedHp, NestedEntry, NestedRescue, NestedCommitted }) b.AddSkill(new(id, id, "死亡收益实际子链") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:bls-noop", "已付来源资格抑制", "真实Lord的2HP使原来源失去资格") { SuppressionRule = new(2), Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:bls-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:bls-owner", "界步练师机制", "supporter", removePaidSource ? "fixture:bls-selection" : Anxu, "wu", 3, [Zhuiyi, Driver, Gain, Gift, Hp, Reward], Gender: GeneralGender.Female) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:bls-target-{i}", "固定受益者", "supporter", "fixture:bls-selection", "qun", 6,
                ["fixture:bls-quiet", Gain, Gift, Hp], Gender: GeneralGender.Male) { InitialHp = 5 });
            b.AddDeck(new("fixture:bls-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : "standard:slash", suit, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界步练师", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:bls-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:bls-owner", "fixture:bls-target-1", "fixture:bls-target-2", "fixture:bls-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
