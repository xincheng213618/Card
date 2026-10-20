namespace CardGame.Core.Tests;

internal static class MouXiahouShiQiaoshiChecks
{
    private const string Draw = "skill-program.mou-mobile:qiaoshi.Draw";

    public static void ForeignSourceChoiceActualDamageArmorAndTurnBudgetCold()
    {
        var s = MouXiahouShiScene.Start(new("source-choice-and-turn-budget") { HeroSeat = 1, Qiaoshi = true });
        s.Use("damage-one", 1); s.Reach(s.QiaoshiChoice); s.Private(); s.RejectPrompt(); s.Cold();
        var declined = s.E<MouXiahouShiQiaoshiStartedEvent>().Single();
        s.Need(s.P!.PlayerSeat == 0 && declined.Source.OwnerSeat == 1 && declined.DamageSourceSeat == 0 && declined.DamageAmount == 1 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == declined.FrameId).MouXiahouShiQiaoshi is { Stage: MouXiahouShiQiaoshiStage.SourceChoice },
            "The genuine foreign damage source, independently of the Qiaoshi owner, receives the private native after-damage decision.");
        s.ChooseBranch("decline"); s.Play();
        s.Need(s.E<MouXiahouShiCompletedEvent>().Single(e => e.FrameId == declined.FrameId) is { Accepted: false } &&
            !s.E<MouXiahouShiQiaoshiRecoveryIssuedEvent>().Any() && !s.E<MouXiahouShiDrawReturnedEvent>().Any(),
            "Declining neither recovers, draws nor consumes the owner's once-per-actual-turn acceptance.");
        s.Use("damage-one", 1); s.Reach(s.QiaoshiChoice); s.Cold();
        var accepted = s.E<MouXiahouShiQiaoshiStartedEvent>().Last(); s.ChooseBranch("recover-and-draw"); s.Play();
        s.Need(accepted.Turn == declined.Turn && s.E<MouXiahouShiQiaoshiRecoveryIssuedEvent>().Single(e => e.FrameId == accepted.FrameId) is
            { SourceSeat: 0, TargetSeat: 1, DamageAmount: 1, RequestedRecovery: 1 } &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 && s.V(1).Hp == 2,
            "A later actual damage in the same turn can be accepted after a decline and uses the original source and damage amount for native recovery.");
        DrawOnce(s, accepted.FrameId, 0);
        s.Use("damage-one", 1); s.Play();
        s.Need(s.E<MouXiahouShiQiaoshiStartedEvent>().Length == 2 && s.V(1).Hp == 1,
            "Acceptance locks the owner and named skill for the whole actual turn, preventing a second source decision on later damage.");
        s.Use("recover-target-two", 1); s.Play(); s.End(); s.Play();
        s.Use("damage-one", 1); s.Reach(s.QiaoshiChoice);
        var next = s.E<MouXiahouShiQiaoshiStartedEvent>().Last();
        s.Need(next.Turn != accepted.Turn, "A genuinely later actual turn receives a fresh Qiaoshi acceptance budget.");
        s.ChooseBranch("recover-and-draw"); s.Play(); DrawOnce(s, next.FrameId, 0); s.Cold();

        foreach (var amount in new[] { 1, 3 })
        {
            var armor = MouXiahouShiScene.Start(new("armor-damage-" + amount) { HeroSeat = 1, Qiaoshi = true });
            armor.Use("armor-one", 1); armor.Play(); armor.Use(amount == 1 ? "damage-one" : "damage-three", 1); armor.Reach(armor.QiaoshiChoice);
            var start = armor.E<MouXiahouShiQiaoshiStartedEvent>().Single(); var damage = armor.E<DamageAppliedEvent>().Single();
            armor.Need(damage.Amount == amount && damage.ArmorPayment is { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0 } invoice &&
                invoice.HpLossAmount == amount - 1 && start.DamageAmount == amount && armor.V(1).Hp == 4 - amount,
                "Qiaoshi reads the real positive native damage invoice, including armor-only damage, rather than substituting the HP delta.");
            armor.Private(); armor.Cold(); armor.ChooseBranch("recover-and-draw"); armor.Play();
            armor.Need(armor.E<MouXiahouShiQiaoshiRecoveryIssuedEvent>().Single() is { DamageAmount: var frozen, RequestedRecovery: var requested, BaseRecovery: var clamped } &&
                frozen == amount && requested == amount && clamped == amount - 1 && armor.V(1).Hp == 3 &&
                armor.E<RecoveryAppliedEvent>().Where(e => e.TargetSeat == 1).Sum(e => e.Amount) == amount - 1,
                "The accepted damage amount remains immutable while native recovery clamps to missing HP; zero actual recovery still permits Draw2.");
            DrawOnce(armor, start.FrameId, 0); armor.Cold();
        }
        var own = MouXiahouShiScene.Start(new("self-damage-and-loss-negative") { Qiaoshi = true });
        own.Use("self-damage-one"); own.Play(); own.Use("loss-one"); own.Play();
        own.Need(own.E<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 0, Amount: 1 } && own.V().Hp == 1 &&
            own.E<MouXiahouShiQiaoshiStartedEvent>().Length == 0 && own.E<MouXiahouShiDrawReturnedEvent>().Length == 0,
            "The owner's own actual damage and genuine HP Loss do not masquerade as foreign-source Qiaoshi occurrences."); own.Cold();
    }

    public static void NativeRecoveryReplacementPaidChildrenSourceLossAndCold()
    {
        var loss = MouXiahouShiScene.Start(new("paid-recovery-child-source-loss")
            { HeroSeat = 1, Qiaoshi = true, ConditionalMinimum = 2, WatchRecovery = true, ChildLoss = 2 });
        loss.Use("damage-one", 1); loss.Reach(loss.QiaoshiChoice); loss.ChooseBranch("recover-and-draw"); loss.Reach(loss.Hold);
        var frame = loss.E<MouXiahouShiQiaoshiStartedEvent>().Single().FrameId;
        loss.Need(loss.P!.PlayerSeat == 1 && loss.V(1).Hp == 3 && loss.E<RecoveryAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 1 } &&
            !loss.E<MouXiahouShiDrawReturnedEvent>().Any() && loss.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frame).MouXiahouShiQiaoshi is
            { Branch: "recover-and-draw", Stage: MouXiahouShiQiaoshiStage.RecoveryChildren },
            "The true HP-recovery child pauses under the paid Qiaoshi frame before the source's draw, with the recipient independently controlling its own child.");
        loss.Private(); loss.Cold();
        loss.Reach(() => loss.E<MouXiahouShiCompletedEvent>().Any(e => e.FrameId == frame)); loss.Play();
        loss.Need(loss.V(1).Hp == 1 && !loss.Has(MouXiahouShiScene.Qiaoshi) && loss.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 1) == 1,
            "The NPC's real child answer and subsequent HP Loss remove the conditional source without replaying or cancelling its accepted recovery/draw tail.");
        DrawOnce(loss, frame, 0); loss.Cold();

        var death = MouXiahouShiScene.Start(new("paid-draw-source-death")
            { HeroSeat = 1, Qiaoshi = true, WatchDraw = true, ChildLoss = 6, Hand = [MouXiahouShiScene.C(CardKind.Dodge)] });
        death.Use("damage-one", 1); death.Reach(death.QiaoshiChoice); death.ChooseBranch("recover-and-draw"); death.Reach(death.Hold);
        var paid = death.E<MouXiahouShiQiaoshiStartedEvent>().Single().FrameId;
        death.Need(death.P!.PlayerSeat == 0 && death.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == paid) is
            { RecipientSeat: 0, RequestedCount: 2, ActualCount: 2 } && !death.E<MouXiahouShiCompletedEvent>().Any(e => e.FrameId == paid),
            "Draw2's actual cards are committed before its genuine gain child; the parent remains pending until that child returns.");
        death.Private(); death.RejectPrompt(); death.Cold(); death.Continue();
        death.Reach(() => death.E<MouXiahouShiCompletedEvent>().Any(e => e.FrameId == paid));
        death.Need(!death.V().IsAlive && death.V(1).IsAlive && death.G.State.Winner == Winner.None &&
            death.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
            !death.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == paid),
            "Actual nonterminal death of the source in its already-paid draw child returns once without new draws or a stranded Qiaoshi parent.");
        DrawOnce(death, paid, 0); death.Cold();

        var replacement = MouXiahouShiScene.Start(new("native-faction-recovery-replacement") { Qiaoshi = true, RecoveryReplacement = true });
        var provider = Enumerable.Range(1, 3).Single(seat => replacement.V(seat).Role == Role.Loyalist);
        replacement.Need(replacement.V().Role == Role.Lord && replacement.G.CreateSnapshot(provider).Players[0].Role == Role.Lord &&
            replacement.V(provider).Hp == 1 && replacement.V(provider).Hp < replacement.V(provider).MaxHp &&
            replacement.E<LordSkillProjectionChangedEvent>().Any(e => e.Added && e.OwnerSeat == provider && e.SkillId == "fixture:mxhs-redirect" && e.Source.LordSeat == 0),
            "A genuine loyalist's native lord-skill projection qualifies its recovery policy. Its actual source decision sees the real public Lord as the Qiaoshi owner.");
        replacement.Use("foreign-one", provider);
        replacement.Reach(() => replacement.P is { PlayerSeat: 0, Kind: DecisionKind.RecoveryReplacement });
        var redirect = replacement.E<MouXiahouShiQiaoshiStartedEvent>().Single();
        replacement.Need(redirect.Source.OwnerSeat == 0 && redirect.DamageSourceSeat == provider &&
            replacement.E<MouXiahouShiQiaoshiBranchAcceptedEvent>().Single() is { Branch: "recover-and-draw" } &&
            replacement.G.ResolutionStack.OfType<RecoveryReplacementFrame>().Single().Attempt is { TargetSeat: 0, Amount: 1 } &&
            replacement.E<MouXiahouShiDrawReturnedEvent>().Length == 0,
            "The actual foreign NPC source accepts through its own AI, and Qiaoshi's real native recovery pauses for the human beneficiary's legal replacement.");
        var hp = replacement.V().Hp; var hand = replacement.V().HandCount; replacement.Private(); replacement.RejectPrompt(); replacement.Cold();
        replacement.Answer(c => c.Parameters.GetValueOrDefault("recovery-action") == "redirect" && c.Targets.SequenceEqual([provider]));
        replacement.Play();
        replacement.Need(replacement.V().Hp == hp && hp == replacement.V().MaxHp - 1 && replacement.E<RecoveryReplacementChosenEvent>().Single() is
            { RecoveringSeat: 0, BeneficiarySeat: var beneficiary, ReplacedAmount: 1 } && beneficiary == provider &&
            replacement.E<MouXiahouShiQiaoshiRecoveryReturnedEvent>().Count(e => e.FrameId == redirect.FrameId) == 1 &&
            !replacement.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0) &&
            replacement.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == provider && e.Amount == 1) &&
            replacement.V().HandCount == hand + 1 && replacement.G.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.recovery-replacement.reward") == 1,
            "Replacing the real recovery preserves zero actual HP recovered by the Qiaoshi owner and returns through its exact native receipt.");
        DrawOnce(replacement, redirect.FrameId, provider); replacement.Cold();
    }

    private static void DrawOnce(MouXiahouShiScene s, long frame, int recipient)
    {
        var draw = s.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == frame);
        s.Need(draw is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } && draw.RecipientSeat == recipient &&
            s.E<MouXiahouShiCompletedEvent>().Count(e => e.FrameId == frame && e.Accepted) == 1 &&
            s.G.CardMovements.Count(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter &&
                m.To == CardLocation.Hand(recipient) && m.Reason.Value == Draw) == 2,
            "The original living source receives exactly the two native actual draws, and the owning Qiaoshi frame completes exactly once after its children.");
    }
}
