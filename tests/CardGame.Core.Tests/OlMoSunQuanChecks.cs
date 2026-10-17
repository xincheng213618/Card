using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OlMoSunQuanChecks
{
    private const string Mq = "ol:mo-quanyu", Mt = "ol:mo-tianen", Mg = "ol:mo-qiangang", Mr = "ol:mo-rumo";
    private const string Md = "fixture:msq-driver", Mw = "fixture:msq-watch", Mn = "fixture:msq-neutral";
    private const string Mc = "fixture:msq-conditional", Mquiet = "fixture:msq-quiet", Mown = "fixture:msq-owner", Mmode = "identity:classic-mo-sun-quan";
    private static readonly string[] MsEffectNames = ["白虹", "青冥", "辟邪", "紫电", "百里", "流星"];

    public static void QuanyuSimultaneousPrivateRoundLifetimeHistoryAndNoneCold()
    {
        var s = MsScenario.Start("history", stopAtVote: true);
        for (var round = 1; round <= 6; round++)
        {
            s.Reach(() => s.IsVote && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MoQuanyu?.RoundNumber == round));
            var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.MoQuanyu is not null);
            var r = f.MoQuanyu!; var mask = (1 << (round - 1)) - 1;
            s.Need(r.Kind == MoQuanyuInvocationKind.Round && r.Participants.Count == 4 &&
                r.Participants is IList { IsReadOnly: true } && r.Votes is IList { IsReadOnly: true } &&
                r.Participants.All(p => p.HistoryMask == mask && p.AvailableMask == (63 & ~mask)) &&
                s.P!.Choices.Select(c => int.Parse(c.Parameters["effect"])).SequenceEqual(Enumerable.Range(round, 7 - round)) &&
                !s.E<MoQuanyuChoiceRevealedEvent>().Any(e => e.FrameId == f.Id),
                "Every living participant freezes the same exact unused lifetime history; nobody's current vote is revealed before all commit.");
            s.Need(Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players.All(player =>
                round == 1 ? player.Markers?.All(marker => marker.Kind != PlayerMarkerKind.MoQuanyuChoice) != false :
                    player.Markers?.Single(marker => marker.Kind == PlayerMarkerKind.MoQuanyuChoice).Name is { } name &&
                    name.Contains("本轮无可选项", StringComparison.Ordinal) && MsEffectNames.Take(round - 1).All(effect => name.Contains(effect, StringComparison.Ordinal)) &&
                    !name.Contains(MsEffectNames[round - 1], StringComparison.Ordinal))),
                "Public badges retain only the earlier revealed lifetime history; pending current-round votes cannot pre-reveal a current sword choice.");
            s.Private(); s.RejectVote(); var retired = s.P!; s.Cold(); s.Vote(round); s.Play(); s.RejectOld(retired);
            var votes = s.E<MoQuanyuChoiceRevealedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
            var reveal = s.E<MoQuanyuRevealCompletedEvent>().Single(e => e.FrameId == f.Id);
            s.Need(votes.Length == 4 && votes.Select(e => e.ParticipantSeat).Distinct().Count() == 4 &&
                votes.All(e => (int)e.Effect == round && e.HistoryBefore == mask && e.HistoryAfter == ((1 << round) - 1)) &&
                reveal.MatchCount == 4 && reveal.DrawCount == 3 &&
                s.E<MoQuanyuDrawReturnedEvent>().Single(e => e.FrameId == f.Id) is { RequestedCount: 3, ActualCount: 3, ReplacementCount: 0 } &&
                s.E<MoQuanyuChoicesCompletedEvent>().Count(e => e.FrameId == f.Id) == 1,
                "All four private votes reveal together and the literal matching participant count, including self, is capped at three actual native draws.");
            s.Need(Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players.All(player =>
                player.Markers?.Single(marker => marker.Kind == PlayerMarkerKind.MoQuanyuChoice).Name is { } name &&
                name.Contains(MsEffectNames[round - 1], StringComparison.Ordinal) && MsEffectNames.Take(round).All(effect => name.Contains(effect, StringComparison.Ordinal)))),
                "Once everyone commits, all viewers receive the same revealed current choice and complete public history.");
            if (round == 3)
            {
                s.Use("lose-quanyu"); s.Play(); s.Use("grant-quanyu"); s.Play();
                s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Count(e => e.IssuerSeat == 0) == 12,
                    "A real source loss and regrant does not erase the issuer's existing choice history.");
            }
            s.End();
        }
        s.Reach(() => s.E<MoQuanyuChoicesStartedEvent>().Any(e => e.RoundNumber == 7) &&
            s.E<MoQuanyuChoicesCompletedEvent>().Any(e => e.FrameId == s.E<MoQuanyuChoicesStartedEvent>().Last().FrameId));
        var exhausted = s.E<MoQuanyuChoicesStartedEvent>().Single(e => e.RoundNumber == 7);
        s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Where(e => e.FrameId == exhausted.FrameId).ToArray() is { Length: 4 } none &&
            none.All(e => e.Effect == MoQuanyuEffect.None && e.HistoryBefore == 63 && e.HistoryAfter == 63) &&
            s.E<MoQuanyuRevealCompletedEvent>().Single(e => e.FrameId == exhausted.FrameId) is { OwnerChoice: MoQuanyuEffect.None, MatchCount: 0, DrawCount: 0 } &&
            !s.E<MoQuanyuDrawRequestedEvent>().Any(e => e.FrameId == exhausted.FrameId),
            "After all six real choices, every participant automatically has None; None never matches and no empty private menu or fake Draw0 request is published."); s.Cold();
        s.Need(Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players.All(player =>
            player.Markers?.Single(marker => marker.Kind == PlayerMarkerKind.MoQuanyuChoice).Name is { } name &&
            name.Contains("本轮无可选项", StringComparison.Ordinal) && MsEffectNames.All(effect => name.Contains(effect, StringComparison.Ordinal)))),
            "Exhausted None does not erase any previously revealed sword history from the public badges.");

        s = MsScenario.Start("partial", stopAtVote: true);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.MoQuanyu is not null);
        s.Need(pending.OwnerSeat != 0 && s.V(pending.OwnerSeat).Role == Role.Lord &&
            pending.MoQuanyu!.Votes.Count > 0 && pending.MoQuanyu.Votes.Count < pending.MoQuanyu.Participants.Count &&
            !s.E<MoQuanyuChoiceRevealedEvent>().Any(e => e.FrameId == pending.Id),
            "The real AI Lord issuer has committed at least one hidden vote before the human participant answers; no trusted pending vote becomes a revealed fact.");
        s.Need(Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players.All(player =>
            player.Markers?.All(marker => marker.Kind != PlayerMarkerKind.MoQuanyuChoice) != false)),
            "Already committed but still hidden AI votes do not leak through public current-choice or history markers.");
        s.Private(); s.Cold(); s.Vote(2); s.Play();
        s.Need(s.E<MoQuanyuRevealCompletedEvent>().Single(e => e.FrameId == pending.Id) is { MatchCount: 3, DrawCount: 3 },
            "The human's different hidden choice does not alter the preceding AI choices or let AI inspect it while choosing.");
    }

    public static void TianenTrueUseTwoBranchesAndNativeSourceRightsCold()
    {
        var s = MsScenario.Start("same");
        var first = s.Dismantle(1); s.Reach(() => s.Hold("same-gain"));
        var issued = s.E<MoTianenBranchIssuedEvent>().Single();
        var obtained = s.E<MoTianenSlashObtainIssuedEvent>().Single(e => e.FrameId == issued.FrameId);
        s.Need(issued.Branch == MoTianenBranch.Same && issued.OwnerChoice == issued.TargetChoice && obtained.CardId is { } id &&
            obtained.From == CardLocation.DrawPile && s.V().Hand.Any(c => c.Id == id && c.SkillMarkLabel?.Contains("天恩", StringComparison.Ordinal) == true) &&
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.mo-sun-quan.tianen.obtain") == 1 &&
            !s.E<MoTianenCompletedEvent>().Any(e => e.FrameId == issued.FrameId),
            "A real converted original Use with one target obtains one printed Slash directly, marks only its original recipient, and waits for the genuine gain child.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.CardId == first && e.SourceSeat == 0 && e.CardKind == CardKind.Dismantlement) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == first && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.E<MoTianenCompletedEvent>().Count(e => e.FrameId == issued.FrameId) == 1,
            "The same native Use pays the real conversion material once and returns through the original target cursor.");
        _ = s.Dismantle(1, obtained.CardId); s.Play();
        s.Need(s.E<MoTianenBranchIssuedEvent>().Count(e => e.Branch == MoTianenBranch.Same) == 1,
            "The same branch is limited by the actual turn, independently of the second true Use and physical material.");
        s.Use("give", [obtained.CardId!.Value], [1]); s.Play();
        s.Need(s.V(1).Hand.Any(c => c.Id == obtained.CardId) &&
            s.E<MoTianenSlashExemptionIssuedEvent>().Single(e => e.CardId == obtained.CardId).Source.OwnerSeat == 0 &&
            s.G.CardMovements.Any(m => m.CardId == obtained.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)),
            "The real entity transfers, while its unique exemption receipt remains signed to the original recipient rather than being reissued to the new holder."); s.Cold();

        s = MsScenario.Start("different-loss", vote: 2); _ = s.Dismantle(1); s.Reach(() => s.Hold("different-discard"));
        issued = s.E<MoTianenBranchIssuedEvent>().Single(); var discard = s.E<MoTianenRandomDiscardIssuedEvent>().Single();
        var payment = s.E<MoTianenPaymentSealedEvent>().Single(e => e.FrameId == issued.FrameId);
        s.Need(issued.Branch == MoTianenBranch.Different && issued.OwnerChoice != MoQuanyuEffect.None &&
            issued.TargetChoice != MoQuanyuEffect.None && issued.OwnerChoice != issued.TargetChoice && discard.PoolCount == 4 &&
            discard.CardId is { } discarded && payment.MovementCount == 1 &&
            s.G.CardMovements.Any(m => m.CardId == discarded && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "skill-program.mo-sun-quan.tianen.discard") &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == issued.FrameId).MoTianen is
                { Stage: MoTianenStage.DiscardChildren, PaymentSealed: true, Movements: IList { IsReadOnly: true } } &&
            !s.E<MoTianenQuanyuRequestedEvent>().Any(),
            "The different branch makes one real random original HE discard and drains its child before issuing target-only Quanyu.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("after-source-loss"));
        s.Need(!s.V().Skills!.Any(skill => skill.ContentId == Mt) && !s.E<MoTianenCompletedEvent>().Any(),
            "A real HP-conditional Tianen source disappears inside the already-paid discard child without retiring the original parent.");
        s.Cold(); s.Continue(); s.Play();
        var returned = s.E<MoTianenQuanyuReturnedEvent>().Single(e => e.FrameId == issued.FrameId);
        s.Need(returned.Request.TargetSeat == 1 && returned.ChosenEffect == MoQuanyuEffect.Qingming && returned.DrawCount == 1 &&
            s.E<MoQuanyuChoiceRevealedEvent>().Count(e => e.FrameId == returned.ChildFrameId && e.ParticipantSeat == 1 && e.Effect == MoQuanyuEffect.Qingming) == 1 &&
            s.E<MoTianenCompletedEvent>().Count(e => e.FrameId == issued.FrameId && e.Branch == MoTianenBranch.Different) == 1 &&
            s.E<MoTianenPaymentSealedEvent>().Single(e => e.FrameId == issued.FrameId) == payment,
            "The original target's next unused private choice resolves under the paid original source, draws the issuer once on a real match, and never repeats the discard."); s.Cold();
    }

    public static void QiangangPermanentRumoOldRoundDamageAndHpChildrenCold()
    {
        var s = MsScenario.Start("rumo"); s.Enter(); var entry = s.E<OlMoRumoEnteredEvent>().Single();
        var paid = s.E<MoQiangangTianenLossPaidEvent>().Single();
        s.Need(paid.GrantCount > 0 && paid.RevisionAfter > paid.RevisionBefore &&
            s.E<MoQiangangTianenGrantRemovedEvent>().Count(e => e.FrameId == entry.EntryId) == paid.GrantCount &&
            !s.V().Skills!.Any(skill => skill.ContentId == Mt) && !s.Actions().Any(a => a.ProgramSkillId == Mg) &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[0].Markers?.Count(m => m.Kind == PlayerMarkerKind.OlMoRumo && m.Count == 1) == 1),
            "Entering Rumo really pays all original Tianen grants once and projects one public entered badge, without a fourth printed general skill or repeat activation.");
        s.Use("lose-qiangang"); s.Play(); s.Use("lose-quanyu"); s.Play(); s.End();
        s.Reach(() => s.Hold("rumo-hp")); var started = s.E<OlMoRumoRoundStartedEvent>().Single();
        s.Need(!started.HadRoundDamage && started.EntryId == entry.EntryId && started.RoundNumber == entry.EntryRound &&
            s.E<OlMoRumoHpLossAppliedEvent>().Single(e => e.FrameId == started.FrameId) is { RequestedAmount: 1, ActualAmount: 1 } &&
            s.G.ResolutionStack.First() is RoundEndingBoundaryFrame boundary && boundary.Id == started.BoundaryId &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId).OlMoRumo is { Stage: OlMoRumoStage.LossChildren, LossReturned: true } &&
            !s.E<OlMoRumoRoundCompletedEvent>().Any() && !s.E<RoundEndedEvent>().Any(e => e.RoundNumber == entry.EntryRound),
            "Permanent state survives losing both printed sources; actual old-round LoseHp1 is paid before its real HP child and before RoundEnded.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<OlMoRumoRoundCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.ActualLoss == 1) == 1 &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == started.FrameId && e.SkillId == Mr && e.Amount == 1) == 1 &&
            !s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && !e.SourceLess) && s.E<OlMoRumoEnteredEvent>().Single() == entry,
            "Returning the HP child completes the original obligation once, without turning HP loss into damage or replacing the entered state."); s.Cold();

        s = MsScenario.Start("pre-entry-damage"); s.Use("native-damage", targets: [1]); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1 && !e.SourceLess),
            "A genuine native program damage is recorded before the character enters Rumo."); s.Enter(); s.End();
        s.Reach(() => s.E<OlMoRumoRoundCompletedEvent>().Any());
        s.Need(s.E<OlMoRumoRoundStartedEvent>().Single().HadRoundDamage &&
            s.E<OlMoRumoRoundCompletedEvent>().Single() is { ActualLoss: 0, HadRoundDamage: true } &&
            !s.E<OlMoRumoHpLossAppliedEvent>().Any() && !s.E<ProgramSkillHpLostEvent>().Any(e => e.SkillId == Mr),
            "The whole original round includes real damage before entry; entering late does not reset the no-damage condition."); s.Cold();

        s = MsScenario.Start("rumo-dying"); s.Enter(); s.End(); s.Reach(() => s.Hold("rumo-dying"));
        started = s.E<OlMoRumoRoundStartedEvent>().Single(); s.Need(s.V().Hp == 0 && s.G.ResolutionStack.OfType<DyingFrame>().Any(d =>
                d.ParentFrameId == started.FrameId && d.VictimSeat == 0 && d.KillerSeat is null && d.Continuation == DyingContinuationKind.ProgramSkill) &&
            !s.E<OlMoRumoRoundCompletedEvent>().Any(),
            "One actual native round penalty enters the exact source-less ProgramSkill dying child and keeps its paid parent alive.");
        s.Private(); s.Cold(); s.Continue();
        s.Reach(() => !s.V().IsAlive && s.E<OlMoRumoRoundCompletedEvent>().Any(e => e.FrameId == started.FrameId));
        s.Need(s.G.State.Winner == Winner.None && s.E<OlMoRumoHpLossAppliedEvent>().Count(e => e.FrameId == started.FrameId) == 1 &&
            s.E<OlMoRumoRoundCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.ActualLoss == 1) == 1,
            "Nonterminal owner death drains the already-paid HP/dying subtree without another loss or a forged living source."); s.Cold();
    }

    public static void NativeSlashSixEffectsQuotaAndExtraRoundsCold()
    {
        static void Finished(MsScenario s, int material, long useId)
        {
            s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId)); s.Play();
            s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == useId && e.CardId == material && e.SourceSeat == 0) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId && e.CardId == material) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                "Every sword effect belongs to one native Use, one original Hand payment and one whole-use settlement."); s.Cold();
        }
        static void Hit(MsScenario s, long useId, int target, int amount, bool armor = false, bool noDodge = false)
        {
            s.Reach(() => s.Hold("six-damage"));
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId);
            s.Need(use.CardAttack is { Active: true } attack && attack.SourceSeat == 0 && attack.TargetSeat == target &&
                attack.IgnoresArmor == armor && attack.ProhibitsDodge == noDodge,
                "The real pending native attack retains this Use's exact target and armor/response policy.");
            var before = s.E<DamageAppliedEvent>().Length; s.Cold(); s.Continue();
            s.Reach(() => s.E<DamageAppliedEvent>().Length > before);
            s.Need(s.E<DamageAppliedEvent>().Skip(before).First() is { SourceSeat: 0, SourceLess: false } damage &&
                damage.TargetSeat == target && damage.Amount == amount,
                "The issued effect reaches the real native damage producer exactly once after its cold before-damage child.");
        }

        var s = MsScenario.Start("six-baihong", vote: 1); var material = s.Slash([1]);
        s.Reach(() => s.Hold("six-damage")); var issued = s.E<MoQuanyuSlashAppliedEvent>().Single();
        s.Need(issued.Mask == 1 && issued.AddedTargetSeat is null, "Baihong is the one actually revealed current sword, without a copied history bonus.");
        Hit(s, issued.UseId, 1, 2); Finished(s, material, issued.UseId);

        s = MsScenario.Start("six-qingming", vote: 2); material = s.Slash([1]); s.Reach(s.IsAdditionalTarget);
        var sole = s.E<MoQuanyuSoleUseCapturedEvent>().Single(e => e.Original.Kind == CardKind.Slash).Original;
        s.Need(sole.TargetSeat == 1 && s.P!.Choices.Any(c => c.Targets.SequenceEqual([3])), "Qingming starts from the real frozen sole designation and publishes a second legal recipient.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([3]));
        Hit(s, sole.UseId, 1, 1); Hit(s, sole.UseId, 3, 1); Finished(s, material, sole.UseId);
        s.Need(s.E<MoQuanyuTargetAddedEvent>().Single(e => e.UseId == sole.UseId) is { TargetIndex: 1, TargetSeat: 3 } &&
            s.E<MoQuanyuSlashAppliedEvent>().Single(e => e.UseId == sole.UseId) is { Mask: 2, AddedTargetSeat: 3 } &&
            s.E<MoTianenBranchIssuedEvent>().Where(e => e.UseId == sole.UseId).ToArray() is [{ TargetSeat: 1 }],
            "A real extra target receives its own native effect, while Tianen uses the original sole recipient rather than treating the added target as another qualifying Use.");

        foreach (var bypass in new[] { false, true })
        {
            s = MsScenario.Start(bypass ? "six-bixie" : "six-armor-baseline", vote: bypass ? 3 : 1);
            s.Use("equip-bagua", targets: [1]); s.Play();
            var armor = s.V(1).Equipment.Single(c => c.Kind == CardKind.BaguaFormation);
            s.Need(s.G.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(1)) == 1,
                "The comparison target actually equips one original Bagua from the real DrawPile through its native equipment Use.");
            s.Cold(); material = s.Slash([1]);
            if (bypass)
            {
                s.Reach(() => s.Hold("six-damage")); issued = s.E<MoQuanyuSlashAppliedEvent>().Single();
                s.Need(issued.Mask == 4 && s.V(1).Equipment.Any(c => c.Id == armor.Id && c.Kind == CardKind.BaguaFormation),
                    "Bixie faces the genuine equipped original Bagua armor entity.");
                Hit(s, issued.UseId, 1, 1, armor: true); Finished(s, material, issued.UseId);
                s.Need(!s.E<JudgmentRequestedEvent>().Any(), "Ignoring the real armor prevents its judgment rather than faking a failed armor result.");
            }
            else
            {
                s.Play(); var use = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == material).ResolutionId;
                s.Need(s.E<JudgmentRequestedEvent>().Any(e => e.TargetSeat == 1) &&
                    s.E<JudgmentResolvedEvent>().Any(e => e.TargetSeat == 1 && e.Suit == Suit.Heart && e.Succeeded) &&
                    !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1),
                    "The identical all-red physical deck makes the actual Bagua defense succeed without Bixie."); Finished(s, material, use);
            }
        }

        foreach (var prohibit in new[] { false, true })
        {
            s = MsScenario.Start(prohibit ? "six-zidian" : "six-dodge-baseline", vote: prohibit ? 4 : 1); material = s.Slash([1]);
            if (prohibit)
            {
                s.Reach(() => s.Hold("six-damage")); issued = s.E<MoQuanyuSlashAppliedEvent>().Single();
                s.Need(issued.Mask == 8 && s.V(1).Hand.Any(c => c.Kind == CardKind.Dodge), "Zidian is tested against an opponent holding real original Dodge entities.");
                Hit(s, issued.UseId, 1, 1, noDodge: true); Finished(s, material, issued.UseId);
                s.Need(!s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge),
                    "The target's native Dodge cannot be paid when this exact Slash prohibits the response.");
            }
            else
            {
                s.Play(); var use = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == material).ResolutionId;
                s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) &&
                    !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1), "The same real opponent actually pays a Dodge when Zidian is absent."); Finished(s, material, use);
            }
        }

        foreach (var fengzi in new[] { false, true })
        {
            s = MsScenario.Start(fengzi ? "six-baili-fengzi" : "six-baili", vote: 5); material = s.Slash([1]); int? cost = null;
            if (fengzi)
            {
                s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:fengzi" && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
                s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost") == true);
                cost = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "cost").Cards.Single();
                s.Need(cost != material && s.V().Hand.Any(c => c.Id == cost), "Fengzi pays a distinct real same-category Hand entity.");
                s.Cold(); s.Answer(c => c.Cards.SequenceEqual([cost.Value]));
            }
            s.Reach(() => s.Hold("six-damage")); issued = s.E<MoQuanyuSlashAppliedEvent>().Single();
            Hit(s, issued.UseId, 1, 1); Hit(s, issued.UseId, 1, 1); if (fengzi) Hit(s, issued.UseId, 1, 1);
            Finished(s, material, issued.UseId);
            s.Need(issued.Mask == 16 && s.E<MoQuanyuExtraRoundIssuedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
                s.E<MoQuanyuExtraRoundStartedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
                s.E<MoQuanyuExtraRoundCompletedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
                s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == (fengzi ? 3 : 2) &&
                !s.Actions().Any(a => a.Kind == LegalActionKind.Slash) &&
                (fengzi ? s.E<ExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == issued.UseId) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.extra-effect.cost") == 1 :
                    !s.E<ExtraCardEffectIssuedEvent>().Any()),
                "Baili adds exactly one full native round, composes with Fengzi's independently paid round, and retains the one ordinary Slash debit.");
        }

        s = MsScenario.Start("six-liuxing", vote: 6); var first = s.Slash([1, 3]);
        s.Reach(() => s.Hold("six-damage")); var firstUse = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == first).ResolutionId;
        Hit(s, firstUse, 1, 1); Hit(s, firstUse, 3, 1); Finished(s, first, firstUse);
        s.Need(!s.E<MoQuanyuSlashAppliedEvent>().Any(e => e.UseId == firstUse) && !s.E<MoQuanyuQuotaAcceptedEvent>().Any(e => e.Receipt.UseId == firstUse),
            "An initially multi-target real Slash uses ordinary quota and receives neither the singleton sword effects nor a Meteor ticket.");
        var action = s.SlashAction(1); s.Need(action.MaxTargetCount <= 1, "After ordinary quota is spent, Meteor publishes singleton capability rather than independent per-target allowances.");
        s.Reject(new PlayCardCommand(0, action.CardId!.Value, [1, 3], s.G.Revision, s.P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        material = s.Slash([1]); s.Reach(() => s.Hold("six-damage")); issued = s.E<MoQuanyuSlashAppliedEvent>().Single();
        Hit(s, issued.UseId, 1, 1); Finished(s, material, issued.UseId);
        s.Need(issued.Mask == 32 && s.E<MoQuanyuQuotaAcceptedEvent>().Single(e => e.Receipt.UseId == issued.UseId).Receipt is
            { OriginalTargetSeat: 1, ChoiceMask: 32, Entered: false }, "Meteor signs its exact accepted singleton and permits a real later Slash after the ordinary debit.");
        s.Use("lose-quanyu"); s.Play(); s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash), "Removing Meteor exposes the retained ordinary Slash debit; the earlier uses did not erase or skip it.");

        s = MsScenario.Start("six-history", vote: 2); s.End(); s.Reach(() => s.IsVote && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MoQuanyu?.RoundNumber == 2));
        s.Vote(1); s.Play(); s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Where(e => e.IssuerSeat == 0 && e.ParticipantSeat == 1).Last().HistoryAfter == 3,
            "The opponent has genuinely revealed Baihong and Qingming in two distinct native round choices.");
        s.Enter(); material = s.Slash([1]); s.Reach(s.IsAdditionalTarget); s.Cold(); s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Reach(() => s.Hold("six-damage")); issued = s.E<MoQuanyuSlashAppliedEvent>().Single(); var historyStart = s.E<MoQuanyuSlashStartedEvent>().Single();
        s.Need(historyStart is { Entered: true, Mask: 3 } && historyStart.Original.TargetSeat == 1 && issued.AddedTargetSeat == 3,
            "After real Qiangang entry, the original target's complete revealed history replaces the owner's current choice, without double-adding the owner's Baihong.");
        Hit(s, issued.UseId, 1, 2); Hit(s, issued.UseId, 3, 2); Finished(s, material, issued.UseId);
        s.Need(s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0).ToArray() is [{ Amount: 2 }, { Amount: 2 }],
            "The combined historical mask gives exactly one Baihong bonus and one real additional target on the same original Use.");
    }

    private sealed class MsScenario(string scene, GameEngine game, ContentRegistry registry, int vote)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool IsVote => P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-choice");
        public bool IsAdditionalTarget() => P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-extra-target");
        public bool Hold(string id) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Mw && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Vote(int effect) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-choice" && c.Parameters.GetValueOrDefault("effect") == effect.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The human answers a real exact published choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Md, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Enter() { Accept(new UseProgramSkillCommand(0, Mg, "lose-tianen-enter-rumo", [], [], G.Revision, P!.PromptId)); Play(); }
        public int Dismantle(int target, int? excluded = null)
        {
            var a = Actions().First(a => a.CardId != excluded && a.ConversionSource?.SkillId == "classic:qixi" && a.PlayedCardKind == CardKind.Dismantlement && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); return a.CardId.Value;
        }
        public LegalAction SlashAction(int target) => Actions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is not null &&
            a.TargetSeats.SequenceEqual([target]));
        public int Slash(int[] targets)
        {
            var a = Actions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is not null &&
                a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, a.CardId!.Value, targets, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); return a.CardId.Value;
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 420; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Fixture ended before native boundary at " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (IsVote)
                {
                    var wanted = vote.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("effect") == wanted)) Vote(vote);
                    else Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-choice");
                }
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Mw && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") is not null)) Continue();
                else if (p.Choices.Any(c => c.Parameters.ContainsKey("slot-index"))) Answer(c => c.Parameters.ContainsKey("slot-index"));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else throw new InvalidOperationException("Unexpected native frontier at " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native frontier at " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(viewer => G.CreateSnapshot(viewer).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0) &&
                !SnapshotJson.Serialize(G.CreateSnapshot(viewer)).Contains("\"votes\"", StringComparison.OrdinalIgnoreCase)), "Private choices and all pending votes remain out of every other viewer's snapshot.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains every viewer, original source and payment, private choice, native parent, RNG and accepted command prefix."); }
        public void RejectVote()
        {
            var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before &&
                !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-mo-vote"), G.Revision)).Accepted && State() == before, "Foreign and unpublished private votes cannot mutate the simultaneous commitment.");
        }
        public void RejectOld(PendingDecision old) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && State() == before, "A retired private vote cannot reveal or pay a second result."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "A forged multi-target Meteor command cannot borrow singleton quota or mutate the original payment and command prefix."); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(5) } : null,
            Players = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, V(seat).Hp, V(seat).MaxHp, V(seat).HandCount, V(seat).IsAlive,
                Equipment = V(seat).Equipment.Select(c => new { c.Id, c.Kind }) }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Starts = E<MoQuanyuChoicesStartedEvent>().TakeLast(2),
            Tianen = E<MoTianenBranchIssuedEvent>().TakeLast(2), Rumo = E<OlMoRumoRoundStartedEvent>().TakeLast(2),
            Judgments = E<JudgmentRequestedEvent>().TakeLast(3), JudgmentResults = E<JudgmentResolvedEvent>().TakeLast(3),
            Responses = E<CardRespondedEvent>().TakeLast(3), Damage = E<DamageAppliedEvent>().TakeLast(4),
            SwordEffects = E<MoQuanyuSlashAppliedEvent>().TakeLast(3) });
        public static MsScenario Start(string scene, int vote = 1, bool stopAtVote = false)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new MsFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
                HumanRole = scene == "rumo-dying" ? Role.Renegade : scene == "partial" ? Role.Rebel : Role.Lord,
                ModeId = Mmode, UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 34, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new MsScenario(scene, game, registry, vote); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Mown, game.Revision, s.P!.PromptId)); if (stopAtVote) s.Reach(() => s.IsVote); else s.Play();
            s.Need(s.V().Hp == (scene == "rumo-dying" ? 1 : scene == "partial" ? 12 : 13), "The real identity mode uses the selected general's HP and actual role bonus, rather than fixed custom-mode HP."); return s;
        }
    }

    private sealed class MsFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mo-sun-quan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual embedded bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            Production(builder, "ol-mo-sun-quan", Mq, Mt, Mg, Mr); Production(builder, "conversion-cutover", "classic:qixi");
            if (scene == "six-baili-fengzi") Production(builder, "ol-wang-rong", "ol:fengzi");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}").AsObject();
            static string Choice(string id, string target = "owner") => "{\"op\":\"chooseOption\",\"target\":\"" + target + "\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("native-damage", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("lose-quanyu", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Mq + "\"],\"sourceBind\":\"" + Mn + "\"}"),
                A("lose-qiangang", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Mg + "\"],\"sourceBind\":\"" + Mn + "\"}"),
                A("grant-quanyu", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Mq + "\"]}"));
            if (scene is "six-bixie" or "six-armor-baseline") acts.Add(A("equip-bagua", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"actual-bagua\"}", targets: 1));
            var defs = new JsonArray(new JsonObject { ["id"] = Md, ["revision"] = 1, ["activations"] = acts,
                    ["modifiers"] = N("[{\"id\":\"bounded-hand\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":80,\"priority\":0}]") },
                N("{\"id\":\"" + Mn + "\",\"revision\":1,\"modifiers\":[{\"id\":\"neutral\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":1,\"priority\":0}]}"),
                N("{\"id\":\"" + Mquiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var ownerSkills = new List<string> { Md }; if (scene != "partial") ownerSkills.Add(Mq);
            if (scene is not ("history" or "partial") && !scene.StartsWith("six-", StringComparison.Ordinal)) { ownerSkills.Add(Mg); ownerSkills.Add("classic:qixi"); ownerSkills.Add(scene == "different-loss" ? Mc : Mt); }
            if (scene == "six-qingming") ownerSkills.Add(Mt);
            if (scene == "six-history") { ownerSkills.Add(Mg); ownerSkills.Add(Mt); }
            if (scene is "six-zidian" or "six-dodge-baseline") ownerSkills.Add("standard:longdan");
            if (scene == "six-baili-fengzi") ownerSkills.Add("ol:fengzi");
            if (scene == "six-liuxing") defs[0]!["modifiers"]!.AsArray().Add(N("""{"id":"real-two-targets","query":"cardTargetCount","operation":"add","value":1,"cardKinds":["slash"],"priority":0}"""));
            if (scene == "different-loss") defs.Add(N("{\"id\":\"" + Mc + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-tianen\",\"skillId\":\"" + Mt + "\",\"minimumHp\":13}]}"));
            if (scene is "same" or "different-loss" or "rumo" or "rumo-dying" || scene.StartsWith("six-", StringComparison.Ordinal))
            {
                var triggers = new JsonArray();
                if (scene == "same") triggers.Add(N("{\"id\":\"same-native-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.mo-sun-quan.tianen.obtain\"],\"optional\":false,\"effects\":[" + Choice("same-gain") + "]}"));
                if (scene == "rumo") triggers.Add(N("{\"id\":\"actual-rumo-loss-child\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choice("rumo-hp") + "]}"));
                if (scene == "rumo-dying") triggers.Add(N("{\"id\":\"actual-rumo-dying-child\",\"window\":\"dyingEntering\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choice("rumo-dying") + "]}"));
                if (scene.StartsWith("six-", StringComparison.Ordinal)) triggers.Add(N("{\"id\":\"actual-sword-damage-child\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageSource\",\"optional\":false,\"effects\":[" + Choice("six-damage") + "]}"));
                if (scene == "different-loss") triggers.Add(N("{\"id\":\"different-native-discard\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.mo-sun-quan.tianen.discard\"],\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"}," + Choice("different-discard", "selectedTarget") + ",{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}," + Choice("after-source-loss", "selectedTarget") + "]}"));
                defs.Add(new JsonObject { ["id"] = Mw, ["revision"] = 1, ["triggers"] = triggers });
                if (scene != "different-loss") ownerSkills.Add(Mw);
            }
            var labels = defs.ToDictionary(node => node!["id"]!.GetValue<string>(), node => new { name = node!["id"]!.GetValue<string>(), description = "真实魔孙权母子票据",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => node[key]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var extra = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in extra.Programs) builder.AddSkill(new(id, id, "真实魔孙权行为夹具") { Program = p, ProgramPresentation = extra.Presentations[id] });
            const string ownPick = "fixture:msq-own-pick"; builder.AddSkill(new(ownPick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            builder.AddGeneral(new(Mown, "真实魔孙权本人", "supporter", ownPick, "wu", scene == "rumo-dying" ? 1 : 12, ownerSkills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:msq-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:msq-peer-pick-" + i; builder.AddSkill(new(pick, "其他角色", "固定真实候选")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => scene == "partial" ? role == Role.Lord ? i == 1 ? 100000000d : -100000000d : i == 1 ? -100000000d : 1000000d * (4 - i) : 1000000d * (4 - i)) });
                var skills = new List<string> { Mquiet }; if (scene == "partial" && i == 1) skills.Add(Mq); if (scene == "different-loss") skills.Add(Mw);
                builder.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pick, "shu", 12, skills));
            }
            var slashDeck = scene is "same" or "different-loss" || scene.StartsWith("six-", StringComparison.Ordinal) && scene is not ("six-zidian" or "six-dodge-baseline");
            var deck = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard(slashDeck ? "standard:slash" : "standard:dodge", scene is "six-bixie" or "six-armor-baseline" ? Suit.Heart : Suit.Spade, 7));
            if (scene is "six-bixie" or "six-armor-baseline") deck = deck.Concat(Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("standard:bagua", Suit.Heart, 7)));
            builder.AddDeck(new("fixture:msq-deck", "真实固定小牌堆", 4, 0, []) { PhysicalCards = deck.ToArray() });
            var roles = scene == "rumo-dying" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            builder.AddMode(new(Mmode, "真实魔孙权小局", 4, 4, roles, "fixture:msq-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Mown, .. peers]));
        }
    }
}
