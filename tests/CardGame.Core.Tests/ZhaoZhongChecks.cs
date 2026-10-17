using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class ZhaoZhongChecks
{
    private const string Pengbi = "ol:pengbi", Dici = "ol:dici", Yintian = "ol:yintian", Biri = "ol:biri";
    private const string Driver = "fixture:zz-driver", Watch = "fixture:zz-watch", Quiet = "fixture:zz-quiet", Regrant = "fixture:zz-regrant";
    private const string Owner = "fixture:zz-owner", Mode = "identity:classic-zhao-zhong", Prevent = "fixture:zz-prevent", Transfer = "fixture:zz-transfer", Mid = "fixture:zz-mid";
    private const string Give = "skill-program.zhao-zhong.dici.give", NextGive = "skill-program.zhao-zhong.dici.next-give", BiriDraw = "skill-program.zhao-zhong.biri.draw";

    public static void PengbiFirstActualTurnPermanentGrantsAndRegrantCold()
    {
        foreach (var chosen in new[] { Yintian, Biri })
        foreach (var share in new[] { false, true })
        {
            var s = Scenario.Start("pengbi"); var old = s.P!; s.Private(0); s.Reject(); s.Cold();
            s.Answer(c => c.Parameters.GetValueOrDefault("skill-id") == chosen); s.Reach(() => s.Hold("grant"));
            var own = s.Own(); var source = own.ZhaoZhong!.Source; var self = s.E<PengbiSkillGrantedEvent>().Single();
            s.Need(own.ZhaoZhong is { Paid: true, Stage: ZhaoZhongStage.SelfGrantChildren } && self.RecipientSeat == 0 && self.SkillId == chosen &&
                s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f => f.Window == SkillProgramTriggerWindow.SkillsChanged) && !s.E<PengbiOtherChosenEvent>().Any(),
                "The first ownership mutation is the real permanent self-grant, with an actual SkillsChanged child before the other-player decision.");
            s.RejectOld(old); s.Private(0); s.Cold(); s.ContinueAny(); s.Reach(() => s.Branch("pengbi-target"));
            var other = chosen == Yintian ? Biri : Yintian;
            if (share) s.Answer(c => c.Parameters.GetValueOrDefault("zhao-zhong") == "pengbi-target" && c.Targets.SequenceEqual([1]));
            else s.BranchAnswer("pengbi-skip");
            s.Play();
            s.Need(s.Has(chosen) && s.E<PengbiCommittedEvent>().Single().Source == source && s.E<PengbiSkillGrantedEvent>().Length == (share ? 2 : 1) &&
                (!share || s.Has(other, 1)) && s.E<PengbiOtherChosenEvent>().Single().RecipientSeat == (share ? (int?)1 : null),
                "Both permanent-grant directions and the genuine optional skip preserve their distinct source instances.");
            s.Use("lose-pengbi"); s.Play(); s.Need(!s.Has(Pengbi) && s.Has(chosen), "Losing the issuer skill does not revoke its permanent leaf grant.");
            s.Use("grant-pengbi", skill: Regrant); s.Play();
            s.Need(s.Has(Pengbi) && s.E<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Regrant && e.SkillIds.Contains(Pengbi)), "A genuine later native acquisition restores the lost skill rather than mutating its old source.");
            s.NextOwnPlay(); s.Need(s.E<PengbiCommittedEvent>().Length == 1 && s.E<PengbiSkillGrantedEvent>().Count(e => e.RecipientSeat == 0) == 1 && s.Has(chosen),
                "A later actual turn and a new source instance cannot repeat the once-ever first-turn allocation."); s.Cold();
        }

        var extra = Scenario.Start("pengbi-extra");
        extra.Need(extra.E<ProgramExtraTurnPendedEvent>().Any(e => e.Seat == 0) && extra.E<TurnStartedEvent>().Count(e => e.ActorSeat == 0) == 1,
            "A real foreign opening program schedules this actor's first actual turn as an extra turn.");
        extra.Answer(c => c.Parameters.GetValueOrDefault("skill-id") == Yintian); extra.Reach(() => extra.Branch("pengbi-skip")); extra.BranchAnswer("pengbi-skip"); extra.Play();
        extra.NextOwnPlay(); extra.Need(extra.E<PengbiCommittedEvent>().Length == 1 && extra.E<TurnStartedEvent>().Count(e => e.ActorSeat == 0) >= 2,
            "First-own-turn allocation follows actual extra turns rather than a normal-seat counter."); extra.Cold();

        var skipped = Scenario.Start("pengbi-facedown");
        var first = skipped.E<TurnStartedEvent>().First(e => e.ActorSeat == 0); var allocated = skipped.G.State.TurnNumber;
        skipped.Need(skipped.E<TurnStartedEvent>().Count(e => e.ActorSeat == 0) == 2 && !skipped.V().IsFaceDown && first.TurnNumber < allocated &&
            !skipped.E<PengbiCommittedEvent>().Any() && !skipped.E<ActualDiscardRecoveryPhaseStartedEvent>().Any(e => e.Phase.TurnNumber == first.TurnNumber && e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation),
            "A real setup face-down state skips the scheduled first turn before Preparation, preserving the first actual start's allocation opportunity.");
        skipped.Cold(); skipped.Answer(c => c.Parameters.GetValueOrDefault("skill-id") == Biri); skipped.Reach(() => skipped.Branch("pengbi-skip")); skipped.BranchAnswer("pengbi-skip"); skipped.Play();
        skipped.Need(skipped.E<PengbiCommittedEvent>().Single().ActualTurn == allocated && skipped.E<PengbiSkillGrantedEvent>().Length == 1, "Only the later genuine started turn grants the permanent skill, once."); skipped.Cold();
    }

    public static void DiciOriginalHandGiftIndependentNextChainLeasesCold()
    {
        foreach (var scene in new[] { "dici", "dici-full", "dici-loss" })
        {
            var s = Scenario.Start(scene); if (scene != "dici-full") { s.Use("hurt", targets: [1]); s.Play(); }
            s.Use("chain", targets: [1]); s.Play(); var hp = s.V(1).Hp;
            var gift = s.BeginDici(); s.Reach(() => s.Hold("gift")); var paid = s.Own(); var id = paid.Id; var source = paid.ZhaoZhong!.Source;
            s.Need(paid.ZhaoZhong is { Paid: true, Stage: ZhaoZhongStage.GiftChildren } && s.E<DiciGiftEntityEvent>().Single().CardId == gift &&
                s.G.CardMovements.Count(m => m.CardId == gift && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == Give) == 1 && !s.E<DiciLeaseIssuedEvent>().Any(),
                "The actual donor's original private Hand entity is paid once, before recovery, state changes or any delayed lease.");
            s.Private(0); s.Cold(); s.ContinueAny();
            if (scene != "dici-full")
            {
                s.Reach(() => s.Hold("health")); s.Need(s.V(1).Hp == hp + 1 && !s.E<DiciLeaseIssuedEvent>().Any() && s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),
                    "The genuine positive recovery child precedes unlinking and debt issuance."); s.Private(1); s.Cold(); s.ContinueAny();
            }
            s.Reach(() => s.Hold("chain") && s.P!.PlayerSeat == 0);
            s.Need(!s.V(1).IsChained && s.V().IsChained && !s.E<DiciLeaseIssuedEvent>().Any() && s.Own(id).ZhaoZhong!.Neighbors.Order().SequenceEqual([0, 2]),
                "Recovery, even when it is zero, is followed by real unlinking and the frozen living-neighbor queue.");
            s.Frozen(s.Own(id).ZhaoZhong!.Neighbors); s.Private(0); s.Cold(); s.Play();
            var lease = s.E<DiciLeaseIssuedEvent>().Single();
            s.Need(lease.LeaseId == id && lease.Source == source && !s.V(1).IsChained && s.V().IsChained && s.V(2).IsChained &&
                s.V(1).Hp == (scene == "dici-full" ? hp : hp + 1) && (scene != "dici-loss" || !s.Has(Dici)) &&
                !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Dici),
                "Only the fully completed initial sequence issues its original-source lease and spends this actual Play phase.");
            var before = s.V().HandCount; s.Use("chain", targets: [1]); s.Reach(() => s.Branch("next-give"));
            s.Need(s.P!.PlayerSeat == 1 && s.E<DiciLeaseDueEvent>().Single().LeaseId == id && s.E<DiciLeaseStartedEvent>().Single().LeaseId == id,
                "A genuine false-to-true transition addresses the marked target as chooser, including after source loss.");
            s.Private(1); s.Cold(); s.Advance(); s.Play();
            s.Need(s.V().HandCount == before + 1 && s.E<DiciNextChoiceEvent>().Single().Branch == "give" && s.E<DiciGiftEntityEvent>().Count(e => e.LeaseId == id) == 1 &&
                s.E<DiciGiftEntityEvent>().Where(e => e.LeaseId == id).All(e => s.G.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == NextGive) == 1),
                "The real AI chooses its original Hand gift; issuer does not answer another player's private menu.");
            s.Use("chain", targets: [1]); s.Play(); s.Use("unchain", targets: [1]); s.Play(); s.Use("chain", targets: [1]); s.Play();
            s.Need(s.E<DiciLeaseStartedEvent>().Length == 1 && !s.E<DiciThunderIssuedEvent>().Any(), "Neither repeated set-true nor a later genuine entry reuses a consumed lease."); s.Cold();
        }

        var overlap = Scenario.Start("dici-overlap"); overlap.BeginDici(); overlap.Play(); var turn = overlap.G.State.TurnNumber;
        overlap.End(); overlap.Reach(() => overlap.P?.SkillPrompt?.SkillId == "fixture:zz-insert" && overlap.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        overlap.Need(overlap.P!.Choices.Count == 1 && overlap.P.Choices[0].Targets.SequenceEqual([0]), "The actual Play-ending insertion first selects its current turn beneficiary through the real native prompt.");
        overlap.Private(0); overlap.Cold(); overlap.Answer(c => c.Targets.SequenceEqual([0])); overlap.Play(); overlap.BeginDici(); overlap.Play();
        var debts = overlap.E<DiciLeaseIssuedEvent>();
        overlap.Need(debts.Length == 2 && debts.Select(e => e.LeaseId).Distinct().Count() == 2 && overlap.G.State.TurnNumber == turn &&
            overlap.E<DiciAcceptedEvent>().Select(e => e.Phase).Distinct().Count() == 2 && overlap.E<ProgramPhaseScheduledEvent>().Any(e => e.Started),
            "A real inserted Play phase pays its own quota and appends an independent obligation instead of overwriting the first.");
        overlap.Use("chain", targets: [1]); overlap.Reach(() => overlap.Branch("next-give")); overlap.Cold(); overlap.Play();
        overlap.Need(overlap.E<DiciLeaseStartedEvent>().Length == 2 && overlap.E<DiciLeaseDueEvent>().Select(e => e.ChangeId).Distinct().Count() == 1 &&
            overlap.E<DiciNextChoiceEvent>().Count(e => e.Branch == "give") == 2 && overlap.E<DiciGiftEntityEvent>().Count(e => debts.Any(d => d.LeaseId == e.LeaseId)) == 2,
            "One actual entry settles both independent leases in stable order, each with its own real payment."); overlap.Cold();

        var thunder = Scenario.Start("dici-thunder"); thunder.TakeAll(3);
        var allowed = thunder.G.GetHumanLegalActions().Where(a => a.ProgramSkillId == Dici).SelectMany(a => a.SelectableTargetSeats).ToArray();
        thunder.Need(thunder.V(3).HandCount == 0 && thunder.V(1).HandCount > 0 && thunder.V(2).HandCount > 0 && allowed.Contains(1) && allowed.Contains(2) && !allowed.Contains(0) && !allowed.Contains(3),
            "The published Dici target set excludes only the genuinely empty donor and self while other real Hand donors remain available.");
        thunder.RejectDiciTarget(3); thunder.Need(!thunder.E<DiciAcceptedEvent>().Any() && thunder.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Dici && a.SelectableTargetSeats.Contains(1)),
            "Rejected empty-Hand input neither spends the actual phase quota nor removes the remaining legal donor."); thunder.Cold(); thunder.BeginDici(); thunder.Play();
        while (thunder.V(1).HandCount > 0) { thunder.Use("take", targets: [1]); thunder.Reach(() => thunder.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); thunder.Answer(c => c.Cards.Count == 0); thunder.Play(); }
        var beforeHp = thunder.V(1).Hp; thunder.Use("chain", targets: [1]); thunder.Reach(() => thunder.Branch("next-damage"));
        thunder.Need(thunder.P!.Choices.Count == 1 && thunder.P.PlayerSeat == 1, "An actually empty marked Hand cannot supply a fictitious gift."); thunder.Cold(); thunder.Advance();
        thunder.Reach(() => thunder.Hold("damage")); thunder.Private(1); thunder.Cold(); thunder.Play();
        thunder.Need(thunder.V(1).Hp == beforeHp - 1 && thunder.E<DiciThunderIssuedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1 } &&
            thunder.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Nature == DamageNature.Thunder && e.Amount == 1) == 1,
            "The alternative issues one real native thunder-damage subtree with the frozen issuer as source."); thunder.Cold();

        var dead = Scenario.Start("dici-death"); dead.BeginDici(); dead.Reach(() => dead.Hold("gift")); var deadId = dead.Own().Id; dead.Cold(); dead.ContinueAny();
        dead.Reach(() => !dead.V().IsAlive && dead.E<DiciLeaseIssuedEvent>().Any(e => e.LeaseId == deadId));
        dead.Need(dead.E<DiciGiftEntityEvent>().Count(e => e.FrameId == deadId) == 1 && dead.E<ZhaoZhongCompletedEvent>().Count(e => e.FrameId == deadId && e.Paid) == 1,
            "Real issuer death drains the already-paid gift/recovery/state children and issues the promised obligation once.");
        dead.Reach(() => dead.Branch("next-damage")); dead.Need(dead.P!.Choices.All(c => c.Parameters.GetValueOrDefault("zhao-zhong") == "next-damage"), "A dead issuer cannot receive a new Hand gift.");
        dead.Cold(); dead.Advance(); dead.Reach(() => dead.Hold("damage")); dead.Cold(); dead.Reach(() => dead.E<DiciThunderIssuedEvent>().Any() && dead.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1));
        dead.Need(dead.E<DiciThunderIssuedEvent>().Length == 1 && dead.E<DiciLeaseStartedEvent>().Length == 1 && !dead.V().IsAlive,
            "The issued next-entry lease preserves a genuinely dead source for its real thunder damage without replaying the original gift."); dead.Cold();

        var targetDead = Scenario.Start("dici-target-death"); targetDead.BeginDici(); targetDead.Play(); var cancelled = targetDead.E<DiciLeaseIssuedEvent>().Single();
        targetDead.Use("kill", targets: [1]); targetDead.Reach(() => !targetDead.V(1).IsAlive && targetDead.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        targetDead.Need(targetDead.E<DiciLeaseCancelledEvent>().Count(e => e.LeaseId == cancelled.LeaseId) == 1 && !targetDead.E<DiciLeaseStartedEvent>().Any() && !targetDead.E<DiciThunderIssuedEvent>().Any(),
            "The genuinely dead marked target cancels only its unstarted obligation, without a dead-player choice or new damage."); targetDead.Cold();
    }

    public static void YintianFirstPositiveRecoveryNonstackingNativeDamageCold()
    {
        var s = Scenario.Start("yintian"); s.Use("heal", targets: [1]); s.Play();
        s.Need(!s.E<YintianFirstRecoveryEvent>().Any() && !s.E<YintianChargeIssuedEvent>().Any(), "A real zero recovery at full HP is not the global first positive recovery.");
        s.RecoverPeer(1); var charge = s.E<YintianChargeIssuedEvent>().Single(); s.RecoverPeer(2);
        s.Need(s.E<YintianChargeIssuedEvent>().Length == 1 && s.E<YintianFirstRecoveryEvent>().Length == 1 && charge.Source.OwnerSeat == 0,
            "Only the first global positive recovery in the actual turn issues the nonstacking charge.");
        s.NextOwnPlay(); s.RecoverPeer(2);
        s.Need(s.E<YintianChargeIssuedEvent>().Length == 1 && s.E<YintianChargeRetainedEvent>().Any(e => e.ExistingGrantId == charge.GrantId), "An unspent charge survives the turn and a later first recovery cannot stack it.");
        s.Use("lose-yintian"); s.Play(); var hp = s.V(1).Hp; s.NativeSlash(1); s.Reach(() => s.Hold("injury")); s.Cold(); s.Play();
        s.Need(!s.Has(Yintian) && s.V(1).Hp == hp - 2 && s.E<YintianChargeConsumedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 2 } used && used.GrantId == charge.GrantId,
            "A real physical Slash spends the same cross-turn charge once after its source skill disappears.");
        hp = s.V(2).Hp; s.Use("hit", targets: [2]); s.Play(); s.Need(s.V(2).Hp == hp - 1 && s.E<YintianChargeConsumedEvent>().Length == 1, "A later Program damage cannot reuse the consumed charge."); s.Cold();

        var prevented = Scenario.Start("yintian-prevent"); prevented.RecoverPeer(1); var grant = prevented.E<YintianChargeIssuedEvent>().Single();
        prevented.Use("two-hits", targets: [2]); prevented.Reach(() => prevented.Optional(Prevent)); prevented.Cold(); prevented.Activate();
        prevented.Reach(() => prevented.Hold("between")); var mother = prevented.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        prevented.Need(prevented.E<ProgramDamagePreventedEvent>().Length == 1 && !prevented.E<YintianChargeConsumedEvent>().Any() && !prevented.E<DamageAppliedEvent>().Any(),
            "The first actual attempt in this same Program is prevented and preserves its charge before the next instruction."); prevented.Cold(); prevented.ContinueAny(); prevented.Play();
        prevented.Need(prevented.E<YintianDamageIssuedEvent>().Length == 2 && prevented.E<YintianDamageIssuedEvent>().All(e => e.AttackId == mother.Id) &&
            prevented.E<YintianChargeConsumedEvent>().Single().GrantId == grant.GrantId && prevented.E<DamageAppliedEvent>().Single() is { TargetSeat: 2, Amount: 2 },
            "Two damage instructions reuse the genuine Program owner ID but acquire distinct attempts; prevented damage cannot leave a stale invoice."); prevented.Cold();

        var transfer = Scenario.Start("yintian-transfer"); transfer.RecoverPeer(2); var transferHp = transfer.V().Hp; transfer.NativeSlash(1);
        transfer.Reach(() => transfer.P?.SkillPrompt?.SkillId == Transfer); transfer.Private(1); transfer.Cold(); transfer.Play();
        transfer.Need(transfer.V().Hp == transferHp - 2 && transfer.E<ProgramDamageTransferredEvent>().Single() is { SourceSeat: 0, TargetSeat: 0 } &&
            transfer.E<YintianDamageTransferredEvent>().Single() is { SourceSeat: 0, OriginalTargetSeat: 1, TargetSeat: 0, Amount: 2 } &&
            transfer.E<YintianChargeConsumedEvent>().Single() is { SourceSeat: 0, TargetSeat: 0, Amount: 2 },
            "Native damage transfer preserves the real source's already-computed increment and consumes it at the actual positive recipient."); transfer.Cold();

        var chain = Scenario.Start("yintian-chain"); chain.Use("chain", targets: [1]); chain.Play(); chain.Use("chain", targets: [2]); chain.Play(); chain.Use("chain", targets: [3]); chain.Play();
        chain.NativeSlash(1); chain.Reach(() => chain.Hold("middle"));
        chain.Need(chain.E<DamageAppliedEvent>().Single() is { TargetSeat: 1, Amount: 1 } && !chain.E<YintianChargeIssuedEvent>().Any(), "The first chain hit actually lands before the mid-chain recovery arms a new charge.");
        chain.Private(1); chain.Cold(); chain.ContinueAny(); chain.Reach(() => chain.Hold("health")); chain.Cold(); chain.Play();
        var hits = chain.E<DamageAppliedEvent>();
        chain.Need(hits.Where(e => e.SourceSeat == 0).Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(new[] { (1, 1), (2, 2), (3, 1) }) &&
            chain.E<YintianChargeConsumedEvent>().Single() is { TargetSeat: 2, Amount: 2, ChainCursor: >= 0 } && chain.E<YintianDamageRebasedEvent>().Any(),
            "A charge gained during real chain propagation affects only the second recipient: the third returns to the inherited one-point base rather than spreading the consumed increment."); chain.Cold();

        var late = Scenario.Start("yintian-late"); late.RecoverPeer(1); late.Use("grant-yintian"); late.Play(); late.RecoverPeer(2);
        late.Need(late.Has(Yintian) && late.E<YintianFirstRecoveryEvent>().Length == 1 && !late.E<YintianChargeIssuedEvent>().Any(), "Gaining the skill after the global first recovery cannot retroclaim another recovery in the same actual turn.");
        late.NextOwnPlay(); late.RecoverPeer(1); late.Need(late.E<YintianChargeIssuedEvent>().Length == 1, "The next actual turn has its own genuine first-recovery opportunity."); late.Cold();
    }

    public static void BiriActualNonDrawGainsAndNativeDrawChildrenCold()
    {
        foreach (var scene in new[] { "biri-gift", "biri-loss" })
        {
            var s = Scenario.Start(scene); s.Use("draw"); s.Play();
            s.Need(!s.E<BiriDrawIssuedEvent>().Any() && s.E<NativeDrawInvocationRecordedEvent>().Any(e => e.OwnerSeat == 0), "Actual normal and Program draws carry native invocation proof and never count as non-draw acquisitions.");
            var before = s.V().HandCount; var card = s.V().Hand[0].Id; s.Use("give", cards: [card], targets: [1]); s.Reach(() => s.Hold("biri"));
            var frame = s.Own(); var issued = s.E<BiriDrawIssuedEvent>().Single(); var outcome = frame.ZhaoZhong!.DrawOutcome!;
            var moved = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
            s.Need(issued.Source.OwnerSeat == 0 && frame.ZhaoZhong is { Paid: true, Stage: ZhaoZhongStage.DrawChildren } && outcome.ActualDrawIds.Count == 1 && outcome.ReplacementGainedIds.Count == 0 &&
                moved.NativeDrawInvocation is { } native && native.ParentFrameId == frame.Id && native.Materials.Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds) &&
                s.E<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == native.InvocationId && e.ActualCount == 1) == 1 && !s.E<ZhaoZhongCompletedEvent>().Any(e => e.FrameId == frame.Id),
                "A foreign player's first actual Hand gain issues this observer's Draw1, whose exact native material child must return before completion.");
            s.Private(0); s.Cold(); s.Play();
            s.Need(s.V().HandCount == before && s.E<BiriDrawIssuedEvent>().Length == 1 && s.E<BiriDrawReturnedEvent>().Single().ActualCount == 1 &&
                s.E<ZhaoZhongCompletedEvent>().Count(e => e.FrameId == frame.Id && e.Paid) == 1 && (scene != "biri-loss" || !s.Has(Biri)),
                "The actual Biri draw does not recursively trigger itself and paid source loss does not cancel its own children.");
            s.Use("give", cards: [s.V().Hand[0].Id], targets: [2]); s.Play(); s.Need(s.E<BiriDrawIssuedEvent>().Length == 1, "A second unrelated Hand gift in the actual turn cannot issue another benefit.");
            if (scene == "biri-gift") { s.NextOwnPlay(); s.Use("give", cards: [s.V().Hand[0].Id], targets: [1]); s.Play(); s.Need(s.E<BiriDrawIssuedEvent>().Length == 2, "A new actual turn opens exactly one new global non-draw opportunity."); }
            s.Cold();
        }

        var top = Scenario.Start("biri-top"); top.Use("top-obtain", targets: [1]); top.Reach(() => top.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "participant-top-view") == true);
        top.Private(1); var viewed = top.G.CreateSnapshot(1).PrivateRevealedCards!.Select(c => c.Id).ToArray();
        top.Need(viewed.Length == 1 && Enumerable.Range(0, 4).Where(i => i != 1).All(i => top.G.CreateSnapshot(i).PrivateRevealedCards is null), "Only the actual recipient privately sees the real DrawPile-to-Processing candidate.");
        top.Cold(); top.Advance(); top.Reach(() => top.Hold("biri")); var topIssue = top.E<BiriDrawIssuedEvent>().Single();
        top.Need(top.G.CardMovements.Any(m => m.CardId == viewed[0] && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) &&
            top.G.CardMovements.Any(m => m.CardId == viewed[0] && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Sequence == topIssue.GainSequence) &&
            !top.E<NativeDrawInvocationRecordedEvent>().Any(e => e.SequenceBefore < topIssue.GainSequence && e.SequenceAfter >= topIssue.GainSequence && e.OwnerSeat == 1),
            "A selected real DrawPile entity is a non-draw obtain despite its origin; it is not mislabeled as an invocation material."); top.Cold(); top.Play();

        var replacement = Scenario.Start("biri-replacement"); replacement.Use("replace-draw"); replacement.Reach(() => replacement.Optional("ol:zenrun"));
        var attempt = replacement.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(); replacement.Cold(); replacement.Activate();
        replacement.Reach(() => replacement.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Zenrun?.Stage == ZenrunStage.Target));
        replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        replacement.Need(replacement.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0), "The replacement publishes opaque foreign Hand slots, never their private entity IDs.");
        replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        var obtained = replacement.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null).Zenrun!.Selected.Single().CardId;
        replacement.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); replacement.Reach(() => replacement.Hold("biri"));
        replacement.Need(replacement.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Id == attempt.Id).Replacement!.GainedIds.SequenceEqual([obtained]) &&
            replacement.E<BiriDrawIssuedEvent>().Length == 1 && !replacement.E<NativeDrawAttemptDecidedEvent>().Any(e => e.AttemptFrameId == attempt.Id),
            "The replacement's real non-draw gain triggers Biri before the original request can return, without forging an actual DrawPile result."); replacement.Cold();
        replacement.ContinueAny(); replacement.Reach(() => replacement.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Zenrun?.Stage == ZenrunStage.VictimChoice));
        replacement.Need(replacement.P!.PlayerSeat == 1, "Only the robbed actual recipient chooses native compensation."); replacement.Private(1); replacement.Cold(); replacement.Advance();
        replacement.Reach(() => replacement.Hold("draw-ended")); var producer = replacement.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        replacement.Need(producer.CardSetBindings.Single(b => b.Name == "actual").CardIds.Count == 0 && replacement.E<NativeDrawAttemptDecidedEvent>().Single(e => e.AttemptFrameId == attempt.Id).Replaced &&
            replacement.E<BiriDrawReturnedEvent>().Single() is { ActualCount: 1, ReplacementCount: 0 } && replacement.E<BiriDrawIssuedEvent>().Length == 1 && replacement.E<ZenrunCompensationReturnedEvent>().Single().ActualDrawCount == 1,
            "Replacement entities remain separate from actual draw IDs; the nested Biri draw and victim compensation complete once without recursion."); replacement.Cold(); replacement.Play();

        var late = Scenario.Start("biri-late"); late.Use("give", cards: [late.V().Hand[0].Id], targets: [1]); late.Play(); late.Use("grant-biri"); late.Play();
        late.Use("give", cards: [late.V().Hand[0].Id], targets: [2]); late.Play();
        late.Need(late.Has(Biri) && !late.E<BiriDrawIssuedEvent>().Any(), "Gaining Biri after the first global non-draw gain cannot retroclaim a later batch in the same actual turn.");
        late.NextOwnPlay(); late.Use("give", cards: [late.V().Hand[0].Id], targets: [1]); late.Play(); late.Need(late.E<BiriDrawIssuedEvent>().Length == 1, "The next actual turn, rather than a new source instance, opens Biri again."); late.Cold();
    }

    private sealed class Scenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == skill) == true;
        public ProgramSkillFrame Own(long? id = null) => G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.ZhaoZhong is not null && (id is null || f.Id == id));
        public bool Branch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("zhao-zhong") == branch) == true;
        public bool Hold(string option) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void BranchAnswer(string branch) => Answer(c => c.Parameters.GetValueOrDefault("zhao-zhong") == branch);
        public void ContinueAny() { if (P!.PlayerSeat == 0) Answer(c => c.Parameters.ContainsKey("option-id")); else Advance(); }
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id, int[]? cards = null, int[]? targets = null, string skill = Driver) => Accept(new UseProgramSkillCommand(0, skill, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void TakeAll(int seat)
        {
            while (V(seat).HandCount > 0)
            {
                Use("take", targets: [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
                Answer(c => c.Cards.Count == 0); Play();
            }
        }
        public void RejectDiciTarget(int target)
        {
            var state = State(); var command = new UseProgramSkillCommand(0, Dici, "give-recover-chain", [], [target], G.Revision, P!.PromptId);
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(!result.Accepted && state == State(), "A forged unavailable donor is rejected without changes to source, payment, history, quota or decision.");
        }
        public void RecoverPeer(int seat) { Use("hurt", targets: [seat]); Play(); Use("heal", targets: [seat]); Reach(() => Hold("health")); Cold(); Play(); }
        public void NextOwnPlay() { var turn = G.State.TurnNumber; End(); Reach(() => G.State.TurnNumber > turn && P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); }
        public void NativeSlash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && a.ConversionSource is null);
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public int BeginDici()
        {
            Use("give-recover-chain", targets: [1], skill: Dici); Reach(() => Branch("initial-give"));
            Need(P!.PlayerSeat == 1 && P.Choices.All(c => c.Cards.Count == 1 && V(1).Hand.Any(h => h.Id == c.Cards[0])), "The real initial donor chooses one of its own original Hand entities.");
            var id = P.Choices[0].Cards.Single(); Private(1); Cold(); Advance(); return id;
        }
        public void Private(int chooser) => Need(P is { IsPrivate: true } p && p.PlayerSeat == chooser && Enumerable.Range(0, 4).Where(i => i != chooser).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the actual chooser sees this private native decision.");
        public void Reject()
        {
            var p = P!; var state = State(); Need(!G.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == state &&
                !G.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished-zhao-zhong"), G.Revision)).Accepted && State() == state, "Wrong actor and unpublished input cannot mutate an unpaid choice or frozen receipt.");
        }
        public void RejectOld(PendingDecision old) { var state = State(); Need(!G.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && State() == state, "A stale consecutive prompt cannot spend or redirect the current decision."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var c = P?.Choices.FirstOrDefault(predicate); Need(P is { PlayerSeat: 0 } && c is not null, "Human must choose a genuinely published canonical choice."); Accept(new AnswerPromptCommand(0, P!.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 260; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded fixture ended before boundary at " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance();
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) ContinueAny();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                { Need(p.SkillPrompt?.SkillId != Pengbi && p.SkillPrompt?.SkillId != Dici, "Unexpected native Zhao Zhong opportunity must not be silently skipped."); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary at " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary at " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Frozen<T>(IReadOnlyList<T> list) { var refused = false; try { ((IList)list).Add(default(T)); } catch (NotSupportedException) { refused = true; } Need(refused, "The native receipt owns a detached immutable collection, including empty lists."); }
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold replay preserves every viewer, private decision, typed child cursor, original material and once-issued entitlement."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount, Skills = V(i).Skills?.Select(s => s.ContentId) }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(14).Select(e => e.Payload), Moves = G.CardMovements.TakeLast(8) });
        public static Scenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var foreign = scene is "dici-death" or "pengbi-extra";
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = foreign ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId));
            if (scene.StartsWith("pengbi", StringComparison.Ordinal)) s.Reach(() => s.Branch("pengbi-skill")); else s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhao-zhong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            Production(b, "ol-zhao-zhong", Pengbi, Dici, Yintian, Biri); if (scene == "biri-replacement") Production(b, "ol-sun-hong", "ol:zenrun");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, string more = "") => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + more + "\"effects\":[" + effects + "]}");
            static string Choose(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            static JsonNode T(string id, string window, string subject, string effects, string more = "") => N("{\"id\":\"" + id + "\",\"window\":\"" + window + "\",\"subject\":\"" + subject + "\",\"optional\":false," + more + "\"effects\":[" + effects + "]}");
            var acts = new JsonArray(A("hurt", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1), A("heal", "{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1), A("kill", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}", targets: 1),
                A("hit", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1), A("chain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}", targets: 1),
                A("unchain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":false}", targets: 1), A("draw", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1, "\"sourceZones\":[\"hand\"],"),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("top-obtain", "{\"op\":\"viewTopCardsAndObtainMatchingCards\",\"target\":\"selectedTarget\",\"amount\":1,\"cardCategories\":[\"basic\"]}", targets: 1),
                A("replace-draw", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1,\"resultBind\":\"actual\"}," + Choose("draw-ended")),
                A("two-hits", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}," + Choose("between") + ",{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1));
            foreach (var (name, skill) in new[] { ("pengbi", Pengbi), ("yintian", Yintian), ("biri", Biri), ("dici", Dici) })
            {
                acts.Add(A("lose-" + name, "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + skill + "\"],\"sourceBind\":\"" + Regrant + "\"}"));
                acts.Add(A("grant-" + name, "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + skill + "\"]}"));
            }
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts,
                ["modifiers"] = new JsonArray(N("{\"id\":\"range\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0}")) });
            if (scene == "pengbi-facedown") defs[0]!["triggers"] = new JsonArray(T("real-opening-face", "gameStarting", "owner", "{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":true}"));
            defs.Add(new JsonObject { ["id"] = Regrant, ["revision"] = 1, ["activations"] = new JsonArray(A("grant-pengbi", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Pengbi + "\"]}")) });
            var quiet = new JsonArray(T("skip-play", "afterNormalDraw", "owner", "{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}"));
            if (scene == "dici-death") quiet.Add(T("real-later-entry", "afterNormalDraw", "owner", "{\"op\":\"setChainedState\",\"target\":\"owner\",\"chained\":true}"));
            if (scene == "pengbi-extra") quiet.Add(T("real-extra", "turnStartBeforeNormalFlow", "owner", "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"pendExtraTurn\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"selectedTarget\"}}", "\"usageScope\":\"game\",\"usageLimit\":1,"));
            defs.Add(new JsonObject { ["id"] = Quiet, ["revision"] = 1, ["triggers"] = quiet });
            if (scene == "pengbi-extra") defs[0]!["modifiers"]!.AsArray().Add(N("{\"id\":\"first-hand\",\"query\":\"initialHandSize\",\"operation\":\"add\",\"value\":4,\"priority\":0}"));
            var watch = new JsonArray();
            watch.Add(T("recovery", "afterHpRecovered", "owner", Choose("health")));
            watch.Add(T("positive-injury", "afterDamageApplied", "owner", Choose("injury"), "\"damageOccurrence\":\"perDamage\","));
            watch.Add(T("before-thunder", "beforeDamageApplied", "damageTarget", Choose("damage"), "\"condition\":{\"kind\":\"damageNatureIs\",\"damageNature\":\"thunder\"},"));
            if (scene.StartsWith("pengbi", StringComparison.Ordinal)) watch.Add(T("first-native-grant", "skillsChanged", "owner", Choose("grant"), "\"usageScope\":\"game\",\"usageLimit\":1,"));
            if (scene.StartsWith("dici", StringComparison.Ordinal))
            {
                var effect = scene == "dici-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scene == "dici-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
                watch.Add(T("actual-gift", "cardsGained", "owner", Choose("gift") + effect, "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + Give + "\"],"));
                watch.Add(T("actual-entry", "characterEnteredChain", "owner", Choose("chain")));
            }
            if (scene.StartsWith("biri", StringComparison.Ordinal)) watch.Add(T("actual-biri-draw", "cardsGained", "owner", Choose("biri") + (scene == "biri-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : ""), "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + BiriDraw + "\"],"));
            defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch });
            defs.Add(N("{\"id\":\"" + Prevent + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-prevention\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":true,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}"));
            defs.Add(new JsonObject { ["id"] = Transfer, ["revision"] = 1, ["triggers"] = new JsonArray(T("real-transfer", "beforeDamageApplied", "damageTarget", "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"minimumCards\":1,\"maximumCards\":1,\"resultBind\":\"cost\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"cost\",\"destination\":\"discardPile\"},{\"op\":\"redirectCurrentDamage\",\"target\":\"selectedTarget\",\"sourceBind\":\"cost\",\"drawLostHpAfterDamage\":false}", "\"usageScope\":\"game\",\"usageLimit\":1,")) });
            defs.Add(new JsonObject { ["id"] = Mid, ["revision"] = 1, ["triggers"] = new JsonArray(T("mid-chain-recovery", "afterDamageApplied", "owner", Choose("middle") + ",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}", "\"damageOccurrence\":\"perDamage\",\"usageScope\":\"game\",\"usageLimit\":1,")) });
            if (scene is "dici-loss" or "biri-loss") defs.Add(N("{\"id\":\"fixture:zz-conditional\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-leaf\",\"skillId\":\"" + (scene == "dici-loss" ? Dici : Biri) + "\",\"minimumHp\":13}]}"));
            if (scene == "dici-overlap") defs.Add(new JsonObject { ["id"] = "fixture:zz-insert", ["revision"] = 1, ["triggers"] = new JsonArray(T("actual-extra-play", "playEnding", "owner", "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}", "\"usageScope\":\"game\",\"usageLimit\":1,")) });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Small genuine native command fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Real native fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string Pick = "fixture:zz-pick"; b.AddSkill(new(Pick, "Human", "Public fixture selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var ownSkills = new List<string> { Driver, Watch };
            if (scene.StartsWith("pengbi", StringComparison.Ordinal)) ownSkills.Add(Pengbi);
            else if (scene.StartsWith("dici", StringComparison.Ordinal)) ownSkills.Add(scene == "dici-loss" ? "fixture:zz-conditional" : Dici);
            else if (scene.StartsWith("yintian", StringComparison.Ordinal) && scene != "yintian-late") ownSkills.Add(Yintian);
            else if (scene.StartsWith("biri", StringComparison.Ordinal) && scene != "biri-late") ownSkills.Add(scene == "biri-loss" ? "fixture:zz-conditional" : Biri);
            if (scene == "biri-replacement") ownSkills.Add("ol:zenrun"); if (scene == "yintian-prevent") ownSkills.Add(Prevent); if (scene == "dici-overlap") ownSkills.Add("fixture:zz-insert");
            b.AddGeneral(new(Owner, "赵忠真实技能夹具", "ol-zhao-zhong", Pick, "qun", 12, ownSkills, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:zz-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3))
            {
                var pick = "fixture:zz-pick-" + i; b.AddSkill(new(pick, "Peer", "Public fixture selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { Quiet, Watch }; if (scene == "yintian-transfer") skills.Add(Transfer); if (scene == "yintian-chain") skills.Add(Mid);
                b.AddGeneral(new(peers[i - 1], "原生其他角色", "supporter", pick, "qun", 12, skills, GeneralGender.Male));
            }
            var kind = scene == "yintian-chain" ? CardKind.ThunderSlash : scene.StartsWith("yintian", StringComparison.Ordinal) || scene == "biri-top" ? CardKind.Slash : CardKind.Crossbow;
            const string Physical = "fixture:zz-entity"; b.AddCard(new(Physical, "原实体" + kind, CardCatalog.Get(kind).CategoryName, "Printed native entity", kind));
            b.AddDeck(new("fixture:zz-deck", "80 fixed physical entities", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(Physical, new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            var roles = scene is "dici-death" or "pengbi-extra" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "赵忠真实原生边界", 4, 4, roles, "fixture:zz-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + part + ".json") ?? throw new InvalidOperationException("Missing actual embedded skill " + bundle + "." + part); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
