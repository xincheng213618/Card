using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class WuLuXunChecks
{
    private const string Xm = "wumiao:xiongmu", Zc = "wumiao:zhangcai", Rx = "wumiao:ruxian";
    private const string Owner = "fixture:wulx-owner", Driver = "fixture:wulx-driver", Watch = "fixture:wulx-watch";
    private const string Quiet = "fixture:wulx-quiet", Mode = "identity:classic-wu-lu-xun", Conditional = "fixture:wulx-conditional";
    private const string XmDraw = "skill-program.wu-lu-xun.xiongmu.draw", XmPut = "skill-program.wu-lu-xun.xiongmu.put", XmObtain = "skill-program.wu-lu-xun.xiongmu.obtain";
    private const string ZcDraw = "skill-program.wu-lu-xun.zhangcai.draw", Zenrun = "ol:zenrun";

    public static void XiongmuRoundRandomInsertionOriginalRankEightAndNativeChildrenCold()
    {
        var s = WlScenario.Start("xm-round", true); s.Activate(); s.Reach(() => s.Hold("round-draw"));
        var first = s.E<XiongmuStartedEvent>().Single(); var drawing = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == first.FrameId).Xiongmu!;
        s.Need(first.OriginalHandCount == 0 && first.RequestedDraw == s.V().MaxHp && drawing is { Stage: XiongmuStage.DrawChildren, DrawOutcome.ActualDrawIds: IList { IsReadOnly: true } } &&
            s.E<XiongmuDrawReturnedEvent>().Single().ActualDrawCount == first.RequestedDraw && !s.E<XiongmuCompletedEvent>().Any(),
            "A real first-round empty Hand draws to actual maximum HP and retains the paid whole native draw while its gain child is open.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(s.Selecting);
        var old = s.P!; var selected = s.V().Hand.Take(2).Select(c => c.Id).ToArray();
        s.Pick(selected[0]); s.XmAnswer("unpick", selected[0]); s.Pick(selected[0]); s.Pick(selected[1]);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == first.FrameId).Xiongmu!;
        s.Need(pending.Selected.SequenceEqual(selected) && pending.Selected is IList { IsReadOnly: true } && !s.E<XiongmuPutPaidEvent>().Any(), "Private pick/undo selects actual distinct HE identities without moving cards or consuming RNG.");
        s.Private(); s.Cold(); s.XmAnswer("finish"); s.RejectOld(old); s.Reach(() => s.Hold("obtain"));
        var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == first.FrameId); var r = f.Xiongmu!;
        var order = r.DeckBefore.ToList(); foreach (var insert in r.Insertions) { s.Need(insert.PileCountBefore == order.Count && insert.Position >= 0 && insert.Position <= order.Count, "Every original insertion consumes a bounded native sample against the current real deck."); order.Insert(insert.Position, insert.CardId); }
        var obtained = s.E<XiongmuEntityObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray(); var got = obtained.Select(e => e.CardId).ToHashSet();
        s.Need(r is { Stage: XiongmuStage.ObtainChildren, InsertedCount: 2, ActualObtained: 2 } && r.PutMaterials.Select(m => m.CardId).SequenceEqual(selected) &&
            r.Insertions is IList { IsReadOnly: true } && r.PutMovements is IList { IsReadOnly: true } && r.ObtainMaterials is IList { IsReadOnly: true } &&
            order.Where(id => !got.Contains(id)).SequenceEqual(s.Deck()) && obtained.All(e => s.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.MovementSequence && m.From == e.From && m.To == CardLocation.Hand(0) && m.Reason.Value == XmObtain) == 1) &&
            !s.E<XiongmuCompletedEvent>().Any(), "Only the selected original cards are randomly interleaved; untouched deck order survives, and two genuine rank-eight obtains retain their original movement children.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<XiongmuCompletedEvent>().Single(e => e.FrameId == f.Id) is { InsertedCount: 2, ActualObtained: 2 } && s.E<XiongmuDrawReturnedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => selected.Contains(m.CardId) && m.Reason.Value == XmPut && m.To == CardLocation.DrawPile) == 2,
            "Cold return completes the one paid round process without redrawing, reinserting or fabricating replacement entities.");
        s.Use("stock-four"); s.Play(); var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.CardMovements.Count(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 2 &&
            got.All(id => s.V().Hand.Any(c => c.Id == id)), "The real discard phase exempts exactly the two obtained originals; the four ordinary cards still obey the actual HP hand limit."); s.Cold();

        foreach (var scene in new[] { "xm-lion", "xm-ox", "xm-loss" })
        {
            s = WlScenario.Start(scene); var equipment = s.V().Hand[0].Id; int? grain = null;
            if (scene != "xm-loss")
            {
                s.Native(equipment, []); s.Play();
                if (scene == "xm-lion") { s.Use("lose-one"); s.Play(); }
                else { grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
                s.End(); s.Reach(() => s.Optional(Xm));
            }
            else { s.End(); s.Reach(() => s.Optional(Xm)); }
            s.Activate(); s.Reach(s.Selecting); var issue = s.E<XiongmuStartedEvent>().Last();
            if (grain is { } g) s.Need(!s.P!.Choices.Any(c => c.Cards.Contains(g)), "Stored Wooden Ox grain is a genuine pile entity and is never an HE cost option.");
            if (scene == "xm-loss") equipment = s.V().Hand.First().Id;
            s.Pick(equipment); s.XmAnswer("finish");
            s.Reach(() => s.Hold(scene == "xm-lion" ? "lion" : scene == "xm-ox" ? "spill" : "obtain"));
            s.Need(!s.E<XiongmuCompletedEvent>().Any(e => e.FrameId == issue.FrameId) && s.G.CardMovements.Count(m => m.CardId == equipment && m.Reason.Value == XmPut && m.To == CardLocation.DrawPile) == 1,
                "The original equipment placement is paid once before Silver Lion recovery, Wooden Ox spill, or obtained-card children return.");
            s.Private(); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<XiongmuCompletedEvent>().Count(e => e.FrameId == issue.FrameId) == 1 &&
                (grain is not { } stored || s.G.CardMovements.Count(m => m.CardId == stored && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1) &&
                (scene != "xm-loss" || !s.V().Skills!.Any(k => k.ContentId == Xm)), "Native equipment children and paid source loss finish the original round receipt once."); s.Cold();
        }
        s = WlScenario.Start("xm-shortage", true); s.Activate(); s.Reach(s.Selecting); s.Pick(s.V().Hand[0].Id); s.XmAnswer("finish"); s.Play();
        s.Need(s.E<XiongmuCompletedEvent>().Single() is { InsertedCount: 1, ActualObtained: 0 } && !s.E<XiongmuEntityObtainedEvent>().Any(), "A real rank-eight shortage returns zero actual originals without reshuffling, cloning or treating the search as a draw."); s.Cold();
    }

    public static void XiongmuFirstActualTurnDamageHistoryAndThresholdCold()
    {
        var s = WlScenario.Start("first"); var hp = s.V().Hp; s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp && s.E<XiongmuFirstDamageReducedEvent>().Single().Receipt is { AmountBefore: 1, AmountAfter: 0 }, "The owner's first otherwise-positive actual damage is reduced to zero and consumes this turn's own reduction.");
        s.Use("hit-one"); s.Play(); s.Need(s.V().Hp == hp - 1 && s.E<XiongmuFirstDamageReducedEvent>().Length == 1, "Reducing the first hit to zero does not authorize a second reduction in the same actual turn."); s.Cold();
        s.Use("extra-turn"); s.Play(); s.End(); s.Play(); hp = s.V().Hp; s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp && s.E<XiongmuFirstDamageReducedEvent>().Length == 2 && s.E<XiongmuFirstDamageReducedEvent>().Select(e => e.Receipt.ActualTurn).Distinct().Count() == 2, "A genuine extra turn is a new damage ledger even within the same round."); s.Cold();

        s = WlScenario.Start("threshold"); s.Use("stock-twenty"); s.Play(); hp = s.V().Hp; s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp - 1 && !s.E<XiongmuFirstDamageReducedEvent>().Any(), "An oversized Hand makes the real first positive injury ineligible for reduction.");
        s.Use("discard", s.V().Hand.Select(c => c.Id).ToArray()); s.Play(); s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp - 2 && !s.E<XiongmuFirstDamageReducedEvent>().Any(), "Discarding down to the threshold cannot recover the first injury already recorded earlier in this actual turn."); s.Cold();

        s = WlScenario.Start("late"); hp = s.V().Hp; s.Use("hit-one"); s.Play(); s.Use("grant-xm"); s.Play(); s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp - 2 && !s.E<XiongmuFirstDamageReducedEvent>().Any(), "A legitimate late grant observes the table's existing positive-damage history instead of resetting it."); s.Cold();
        s = WlScenario.Start("prevent"); hp = s.V().Hp; s.Use("hit-one"); s.Play();
        s.Need(s.V().Hp == hp && !s.E<XiongmuFirstDamageReducedEvent>().Any(), "A different native prevention that stops the earlier attempt does not count as receiving damage.");
        s.Use("hit-one"); s.Play(); s.Need(s.V().Hp == hp && s.E<XiongmuFirstDamageReducedEvent>().Length == 1, "The next otherwise-positive attempt still receives Xiongmu's one actual first-injury reduction."); s.Cold();

        s = WlScenario.Start("two-hits"); hp = s.V().Hp; s.Use("two-hits"); s.Reach(() => s.Hold("attempt"));
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver && f.ActivationId == "two-hits");
        var first = s.E<XiongmuFirstDamageReducedEvent>().Single();
        s.Need(first.Receipt is { AmountBefore: 2, AmountAfter: 1 } && first.Receipt.AttackId == producer.Id && producer.XiongmuDamage == first.Receipt,
            "The first instruction retains its actual pending damage invoice on the one native Program producer."); s.Private(); s.Cold(); s.Continue();
        s.Reach(() => s.Hold("attempt") && s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0) == 1);
        var second = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == producer.Id);
        s.Need(second.XiongmuDamage is null && s.E<XiongmuFirstDamageReducedEvent>().Length == 1 && s.V().Hp == hp - 1,
            "A second damage instruction on the same Program ID cannot inherit the retired first attempt's reduction or transfer invoice."); s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.V().Hp == hp - 2 && s.E<DamageAppliedEvent>().Where(e => e.TargetSeat == 0).Select(e => e.Amount).SequenceEqual([1, 1]) &&
            s.E<XiongmuFirstDamageReducedEvent>().Length == 1 && !s.E<XiongmuCangxinChainRebasedEvent>().Any(),
            "Both real instructions resolve once while the actual-turn reduction is consumed only by the first."); s.Cold();
        WlChainReduction(false); WlChainReduction(true);
    }

    private static void WlChainReduction(bool xiongmuFirst)
    {
        var s = WlScenario.Start(xiongmuFirst ? "chain-xiongmu-first" : "chain-cangxin-first");
        foreach (var seat in new[] { 3, 0, 1 }) { s.Use("chain", targets: [seat]); s.Play(); }
        var hp = Enumerable.Range(0, 4).Select(i => s.V(i).Hp).ToArray(); s.Use("fire-three", targets: [3]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "lu-yu-sheng" && c.Parameters.GetValueOrDefault("option") == "pick") == true);
        var picker = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LuYuSheng?.Stage == LuYuShengStage.BottomChoice);
        var heart = picker.LuYuSheng!.BottomCards.First(id => s.G.CreateSnapshot(0).PublicRevealedCards.Any(c => c.Id == id && c.Suit == Suit.Heart));
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "lu-yu-sheng" && c.Parameters.GetValueOrDefault("option") == "pick" && c.Cards.SequenceEqual([heart]));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "lu-yu-sheng" && c.Parameters.GetValueOrDefault("option") == "finish"); s.Reach(() => s.Hold("chain-hit"));
        var xm = s.E<XiongmuFirstDamageReducedEvent>().Single(); var cx = s.E<LuYuShengDamageAdjustedEvent>().Single();
        s.Need(cx is { Applied: true, Hearts: 1 } && xm.Receipt.TargetSeat == 0 && cx.Receipt.TargetSeat == 0 &&
            xm.Receipt.AttackId == cx.Receipt.AttackId && xm.Receipt.WindowId == cx.Receipt.WindowId && xm.Receipt.ChainCursor == cx.Receipt.ChainCursor && xm.Receipt.ChainCursor > 0 &&
            xm.Receipt.AmountBefore == (xiongmuFirst ? 3 : 2) && xm.Receipt.AmountAfter == (xiongmuFirst ? 2 : 1) &&
            cx.Receipt.Before == (xiongmuFirst ? 2 : 3) && cx.Receipt.After == (xiongmuFirst ? 1 : 2) &&
            s.V().Hp == hp[0] - 1 && s.V(1).Hp == hp[1] && s.V(3).Hp == hp[3] - 3 && !s.E<XiongmuCangxinChainRebasedEvent>().Any(),
            "The later chained recipient pays one real bottom Heart and applies both native reductions in their actual order before any following recipient is advanced.");
        s.Need(s.G.CardMovements.Count(m => m.CardId == heart && m.From == CardLocation.DrawPile && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.lu-yu-sheng.cangxin.discard") == 1,
            "Cangxin's reduction is backed by the original publicly revealed bottom entity's actual discard."); s.Private(); s.Cold(); s.Continue(); s.Play();
        var restored = s.E<XiongmuCangxinChainRebasedEvent>().Single();
        s.Need(restored is { TargetSeat: 0, RestoredAmount: 3 } && restored.AttackId == xm.Receipt.AttackId && restored.ChainCursor == xm.Receipt.ChainCursor &&
            restored.XiongmuFrameId == xm.Receipt.ProgramFrameId && restored.XiongmuWindowId == xm.Receipt.WindowId && restored.CangxinFrameId == cx.FrameId && restored.CangxinWindowId == cx.Receipt.WindowId &&
            restored.XiongmuWasFirst == xiongmuFirst && s.E<DamageAppliedEvent>().Where(e => e.Nature == DamageNature.Fire).Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(3, 3), (0, 1), (1, 3)]) &&
            s.V(1).Hp == hp[1] - 3 && s.E<XiongmuFirstDamageReducedEvent>().Length == 1 && s.E<LuYuShengDamageAdjustedEvent>().Length == 1,
            "Exactly one strong combined rebase restores the earliest local base three, so the next chained recipient receives three in either reduction order."); s.Cold();
    }

    public static void ZhangcaiTrueUsePlayedEffectiveRanksAndPaidDrawCold()
    {
        var s = WlScenario.Start("zc-use"); var id = s.V().Hand[0].Id; s.Native(id, []); var issue = s.ResolveZhangcai(); s.Play();
        s.Need(issue is { EffectiveRank: 8, RemainingEights: 3, DrawCount: 3, Window: SkillProgramTriggerWindow.CardUseCommitted } &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == id) == 1 && s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "The true rank-eight equipment Use pays its original entity before the three remaining Hand eights are frozen for one native Draw3."); s.Cold();
        s = WlScenario.Start("zc-played"); s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        id = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([id])); issue = s.ResolveZhangcai(); s.Play();
        s.Need(issue.Window == SkillProgramTriggerWindow.CardResponseAccepted && issue.EffectiveRank == 8 && issue.RemainingEights == 3 &&
            s.E<CardUseDeclaredEvent>().All(e => e.CardId != id), "A genuine Duel Played Slash qualifies for Zhangcai without creating a fictional card Use."); s.Cold();
        s = WlScenario.Start("zc-dodge"); s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        id = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); s.Private(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([id])); issue = s.ResolveZhangcai(); s.Play();
        s.Need(issue.Window == SkillProgramTriggerWindow.CardResponseAccepted && issue.EffectiveKind == CardKind.Dodge && issue.DrawCount == 3 &&
            s.E<WuLuXunZhangcaiStartedEvent>().Count(e => e.ActionId == issue.ActionId) == 1, "The exact accepted true Dodge action is observed once, not again as an extra synthetic Use trigger."); s.Cold();

        s = WlScenario.Start("zc-effective"); var foreign = s.V(1).Hand[0].Id; s.Use("take", targets: [1]); s.Reach(() => s.P?.Kind == DecisionKind.SelectTargetCard || s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == "0"); s.Play(); s.Native(foreign, []); s.Reach(() => s.Hold("use"));
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == foreign);
        s.Need(use.Action?.EffectiveRank == 2 && !s.E<WuLuXunZhangcaiQualifiedEvent>().Any(), "A real foreign printed-eight Hand entity has Juewu effective rank two, so its native Use cannot borrow its old printed rank to trigger Zhangcai.");
        s.Private(); s.Cold(); s.Continue(); s.Play();

        foreach (var scene in new[] { "zc-loss", "zc-death" })
        {
            s = WlScenario.Start(scene); s.Native(s.V().Hand[0].Id, []); s.Reach(() => s.Optional(Zc)); s.Activate(); s.Reach(() => s.Hold("zhang-draw"));
            issue = s.E<WuLuXunZhangcaiStartedEvent>().Single(); var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == issue.FrameId);
            s.Need(parent.WuLuXunZhangcai is { Stage: WuLuXunZhangcaiStage.DrawChildren, DrawOutcome.ActualDrawIds: IList { IsReadOnly: true } } && !s.E<WuLuXunZhangcaiCompletedEvent>().Any(), "The accepted native gain child retains the once-paid rank snapshot before it removes the source or kills its owner.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(() => s.E<WuLuXunZhangcaiCompletedEvent>().Any(e => e.FrameId == issue.FrameId));
            s.Need(s.E<WuLuXunZhangcaiDrawReturnedEvent>().Single(e => e.FrameId == issue.FrameId) is { RequestedCount: 3, ActualCount: 3, ReplacementCount: 0 } &&
                s.E<WuLuXunZhangcaiCompletedEvent>().Count(e => e.FrameId == issue.FrameId) == 1 && (scene == "zc-death" ? !s.V().IsAlive : !s.V().Skills!.Any(k => k.ContentId == Zc)), "Paid source loss and death drain the issued native draw without recomputing X or drawing twice."); s.Cold();
        }

        s = WlScenario.Start("zc-replacement"); s.Native(s.V().Hand[0].Id, []); s.Reach(() => s.Optional(Zc)); s.Activate(); s.Reach(() => s.Optional(Zenrun)); s.Private(); s.Cold(); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        for (var i = 0; i < 3; i++) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("replacement"));
        s.Need(!s.E<WuLuXunZhangcaiDrawReturnedEvent>().Any(), "A real three-entity Zenrun replacement and its obtain child still belong to the outstanding Zhangcai draw attempt."); s.Private(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<WuLuXunZhangcaiDrawReturnedEvent>().Single() is { RequestedCount: 3, ActualCount: 0, ReplacementCount: 3 } && s.E<WuLuXunZhangcaiCompletedEvent>().Length == 1 &&
            !s.G.CardMovements.Any(m => m.Reason.Value == ZcDraw), "Native replacement preserves obtained identities separately from actual drawn cards and returns only after its compensation children."); s.Cold();

        s = WlScenario.Start("zc-supply"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
        var supplied = s.P!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"); s.Private(); s.Cold(); s.Answer(c => c.Id == supplied.Id); s.ResolveZhangcai(); s.Play();
        var supply = s.E<CardSupplyCompletedEvent>().Single();
        s.Need(supply.ProviderSeat == 0 && supply.RequesterSeat != 0 && s.E<WuLuXunZhangcaiStartedEvent>().Count(e => e.Source.OwnerSeat == 0 && e.ActionId == supply.ActionId && e.Window == SkillProgramTriggerWindow.CardSupplyCompleted) == 1 &&
            s.E<WuLuXunZhangcaiStartedEvent>().Count(e => e.Source.OwnerSeat == supply.RequesterSeat && e.ActionId == supply.ActionId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) == 1,
            "The actual provider Played and requester Use may share the native action ID but retain two distinct owner's accepted Zhangcai opportunities."); s.Cold();
    }

    public static void RuxianLimitedExpirySourceRightsAndExtraPhaseCold()
    {
        var s = WlScenario.Start("ruxian"); s.Accept(new UseProgramSkillCommand(0, Rx, "all-ranks-until-next-turn", [], [], s.G.Revision, s.P!.PromptId)); s.Play();
        var activation = s.E<WuLuXunRuxianActivatedEvent>().Single(); s.Native(s.V().Hand[0].Id, []); var first = s.ResolveZhangcai(); s.Play();
        s.Need(first is { EffectiveRank: 7, RemainingEights: 0, DrawCount: 1 } && first.RuxianFrameId == activation.FrameId, "Ruxian broadens the accepted rank while the primary-text X remains the number of remaining eights, with minimum one.");
        s.Use("lose-one"); s.Play(); s.Need(!s.V().Skills!.Any(k => k.ContentId == Rx), "The actual conditional Ruxian source is lost after activation.");
        s.Native(s.V().Hand[0].Id, []); var retained = s.ResolveZhangcai(); s.Play(); s.Need(retained.RuxianFrameId == activation.FrameId, "Loss of the original limited skill does not revoke its already issued until-next-own-turn rank permission.");
        s.Use("recover-one"); s.Play(); s.Need(s.V().Skills!.Any(k => k.ContentId == Rx), "A real HP recovery regrants the original limited source.");
        s.Reject(new UseProgramSkillCommand(0, Rx, "all-ranks-until-next-turn", [], [], s.G.Revision, s.P!.PromptId)); s.Cold();
        var actual = s.G.State.TurnNumber; var phases = s.E<ActualDiscardRecoveryPhaseStartedEvent>().Where(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.ActorSeat == 0).Select(e => e.Phase.Token).ToArray();
        s.End(); s.Play(); s.Need(s.G.State.TurnNumber == actual && s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.ActorSeat == 0).Phase.Token != phases.Last(), "A real inserted Play is a new phase but has not reached Ruxian's next actual own-turn deadline.");
        s.Native(s.V().Hand[0].Id, []); s.Need(s.ResolveZhangcai().RuxianFrameId == activation.FrameId, "The same issued effect remains valid in the extra Play."); s.Play();
        s.Use("face-down"); s.Play(); s.Use("extra-turn"); s.Play(); s.End(); s.Reach(() => s.E<TurnStartedEvent>().Count(e => e.ActorSeat == 0) == 2); s.Cold(); s.Play();
        var skipped = s.E<TurnStartedEvent>().Where(e => e.ActorSeat == 0).Skip(1).First(); var count = s.E<WuLuXunZhangcaiStartedEvent>().Length;
        s.Need(!s.E<ActualDiscardRecoveryPhaseStartedEvent>().Any(e => e.Phase.ActorSeat == 0 && e.Phase.TurnNumber == skipped.TurnNumber && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation), "The genuine next own turn was flipped and skipped; its TurnStarted still expires the old permission.");
        s.Native(s.V().Hand[0].Id, []); s.Play(); s.Need(s.E<WuLuXunZhangcaiStartedEvent>().Length == count && s.E<WuLuXunRuxianActivatedEvent>().Length == 1, "A later non-eight Use cannot restore the expired effect or refresh the limited activation through source regrant."); s.Cold();
    }

    private sealed class WlScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int[] Deck() => G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderBy(c => c.ZoneIndex).Select(c => c.CardId).ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Selecting() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-lu-xun-xiongmu" && c.Parameters.GetValueOrDefault("branch") == "finish") == true;
        public void Pick(int id) => XmAnswer("pick", id);
        public void XmAnswer(string branch, int? id = null) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "wu-lu-xun-xiongmu" && c.Parameters.GetValueOrDefault("branch") == branch && (id is null || c.Cards.SequenceEqual([id.Value])));
        public void Use(string binding, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(int id, int[] targets) { var action = G.GetHumanLegalActions().First(a => a.CardId == id && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets)); Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public WuLuXunZhangcaiStartedEvent ResolveZhangcai()
        {
            Reach(() => Optional(Zc)); Private(); Cold(); var count = E<WuLuXunZhangcaiStartedEvent>().Count(e => e.Source.OwnerSeat == 0); Activate();
            Reach(() => E<WuLuXunZhangcaiStartedEvent>().Count(e => e.Source.OwnerSeat == 0) > count);
            var issued = E<WuLuXunZhangcaiStartedEvent>().Last(e => e.Source.OwnerSeat == 0);
            Reach(() => Hold("zhang-draw") || E<WuLuXunZhangcaiCompletedEvent>().Any(e => e.FrameId == issued.FrameId));
            if (Hold("zhang-draw")) { Private(); Cold(); Continue(); }
            Reach(() => E<WuLuXunZhangcaiCompletedEvent>().Any(e => e.FrameId == issued.FrameId));
            Need(E<WuLuXunZhangcaiDrawReturnedEvent>().Count(e => e.FrameId == issued.FrameId && e.RequestedCount == issued.DrawCount) == 1 && E<WuLuXunZhangcaiCompletedEvent>().Count(e => e.FrameId == issued.FrameId) == 1, "The actual accepted action owns one requested draw and one completion.");
            return issued;
        }
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The human answers an actual published native choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Actual fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.SkillPrompt?.SkillId == "fixture:wulx-insert" && p.Choices.Count == 1 && p.Choices[0].Targets.SequenceEqual([0]) && p.Choices[0].Parameters.GetValueOrDefault("program-action") == "select-target") Answer(c => c.Id == p.Choices[0].Id);
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) && Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)), "Every chooser sees only their own private prompt and Hand; foreign views retain no private faces.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves all views, RNG order, readonly paid receipts, source rights and typed native returns."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "An unpublished, stale or foreign input cannot mutate the prepared result."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("unpublished-wu-lu-xun"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + " " + result.Error?.Code + "/" + result.Error?.Message); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Hp = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5) } : null, Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, Xm = (f as ProgramSkillFrame)?.Xiongmu, Zc = (f as ProgramSkillFrame)?.WuLuXunZhangcai }), Obtains = E<XiongmuEntityObtainedEvent>().TakeLast(5), Zhangcai = E<WuLuXunZhangcaiStartedEvent>().TakeLast(3), Reduced = E<XiongmuFirstDamageReducedEvent>().TakeLast(3) });
        public static WlScenario Start(string scene, bool firstRound = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new WlFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "zc-death" ? Role.Renegade : scene == "zc-supply" ? Role.Loyalist : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new WlScenario(scene, g, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId));
            if (firstRound) s.Reach(() => s.Optional(Xm));
            else if (scene == "zc-supply") s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
            else s.Play(); return s;
        }
    }

    private sealed class WlFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-lu-xun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var rules = JsonNode.Parse(Read("rules"))!;
                // Only scheduling priority differs: both cases execute the complete real Cangxin producer.
                if (bundle == "ol-lu-yu-sheng" && scene.StartsWith("chain-", StringComparison.Ordinal))
                    rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "ol:cangxin")!["triggers"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "first-damage-bottom")!["priority"] = scene == "chain-xiongmu-first" ? -100 : 100;
                var c = SkillProgramCatalog.Load(rules.ToJsonString(), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id], Tags = id == Rx ? SkillTag.Limited : SkillTag.None });
            }
            Real("wumiao-lu-xun", Xm, Zc, Rx); if (scene == "zc-replacement") Real("ol-sun-hong", Zenrun); if (scene == "zc-effective") Real("wumiao-guan-yu", "wumiao:juewu");
            if (scene.StartsWith("chain-", StringComparison.Ordinal)) Real("ol-lu-yu-sheng", "ol:cangxin");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => new() { ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["sourceZones"] = N("[\"hand\"]"), ["effects"] = N("[" + effects + "]") };
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"hold-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var discard = A("discard", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":20}", 20);
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var acts = new JsonArray(A("stock-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("stock-twenty", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), discard, duel,
                A("hit-one", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"), A("lose-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("extra-turn", "{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}"), A("face-down", "{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":true}"),
                A("two-hits", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"),
                A("chain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}", targets: 1), A("fire-three", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":3,\"nature\":\"fire\"}", targets: 1),
                A("grant-xm", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Xm + "\"]}"),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts }, N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var own = new List<string> { Driver, Watch };
            var xmScene = scene.StartsWith("xm-", StringComparison.Ordinal) || scene is "first" or "threshold" or "prevent" or "two-hits" || scene.StartsWith("chain-", StringComparison.Ordinal);
            if (xmScene && scene != "xm-loss") own.Add(Xm);
            if (scene.StartsWith("zc-", StringComparison.Ordinal)) own.Add(scene == "zc-loss" ? Conditional : Zc);
            if (scene == "ruxian") { own.Add(Zc); own.Add(Conditional); }
            if (scene == "xm-loss") own.Add(Conditional);
            if (scene is "xm-loss" or "zc-loss" or "ruxian")
                defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"" + (scene == "xm-loss" ? Xm : scene == "ruxian" ? Rx : Zc) + "\",\"minimumHp\":13}]}"));
            if (scene == "prevent") { const string prevent = "fixture:wulx-prevent"; own.Add(prevent); defs.Add(N("{\"id\":\"" + prevent + "\",\"revision\":1,\"triggers\":[{\"id\":\"prevent-first\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}")); }
            if (scene == "ruxian") { const string insert = "fixture:wulx-insert"; own.Add(insert); defs.Add(N("{\"id\":\"" + insert + "\",\"revision\":1,\"triggers\":[{\"id\":\"extra-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}")); }
            if (scene == "zc-replacement") own.Add(Zenrun); if (scene == "zc-effective") own.Add("wumiao:juewu");
            if (scene.StartsWith("chain-", StringComparison.Ordinal)) own.Add("ol:cangxin");
            var watch = new JsonArray();
            void Gain(string id, string reason, string after = "") => watch.Add(N("{\"id\":\"gain-" + id + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause(id) + (after.Length == 0 ? "" : "," + after) + "]}"));
            if (scene == "xm-round") Gain("round-draw", XmDraw);
            if (scene.StartsWith("xm-", StringComparison.Ordinal)) Gain("obtain", XmObtain, scene == "xm-loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "");
            if (scene.StartsWith("zc-", StringComparison.Ordinal) || scene == "ruxian") Gain("zhang-draw", ZcDraw, scene is "zc-loss" or "zc-death" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "zc-death" ? 20 : 1) + "}" : "");
            if (scene == "zc-replacement") Gain("replacement", "skill-program.sun-hong.zenrun.obtain");
            if (scene == "xm-lion") watch.Add(N("{\"id\":\"actual-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("lion") + "]}"));
            if (scene == "xm-ox") watch.Add(N("{\"id\":\"actual-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Pause("spill") + "]}"));
            if (scene == "zc-effective") watch.Add(N("{\"id\":\"actual-use\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"optional\":false,\"effects\":[" + Pause("use") + "]}"));
            if (scene == "two-hits") watch.Add(N("{\"id\":\"actual-attempt\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"priority\":-1000,\"optional\":false,\"effects\":[" + Pause("attempt") + "]}"));
            if (scene.StartsWith("chain-", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"actual-chain-hit\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamage\",\"optional\":false,\"effects\":[" + Pause("chain-hit") + "]}"));
            if (watch.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch }); else own.Remove(Watch);
            if (scene == "zc-dodge") defs.Add(N("{\"id\":\"fixture:wulx-foreign\",\"revision\":1,\"modifiers\":[{\"id\":\"range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":4,\"priority\":0}],\"triggers\":[{\"id\":\"real-slash\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}]}]}"));
            if (scene == "zc-supply") defs.Add(N("{\"id\":\"fixture:wulx-caller\",\"revision\":1,\"cardPolicies\":[{\"id\":\"lord-request\",\"kind\":\"factionResponseRequest\",\"requiredCardKinds\":[\"slash\"],\"factionId\":\"shu\",\"ownerRole\":\"lord\"}],\"triggers\":[{\"id\":\"real-use\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingSlashable\"},{\"op\":\"requestFactionCard\",\"target\":\"selectedTarget\",\"providerFactionId\":\"shu\",\"requiredKind\":\"slash\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Actual Wu Lu Xun native behavior fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Actual Wu Lu Xun fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:wulx-pick"; b.AddSkill(new(pick, "Owner", "Fixed human") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var baseHp = scene is "xm-round" or "xm-shortage" ? 3 : 12; b.AddGeneral(new(Owner, "武陆逊完整技能真人夹具", "supporter", pick, "shu", baseHp, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:wulx-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var key = "fixture:wulx-peer-pick-" + i; b.AddSkill(new(key, "Peer", "Fixed public peer") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); var peerSkills = new List<string> { Quiet }; if (i == 1 && scene == "zc-dodge") peerSkills.Add("fixture:wulx-foreign"); if (i == 1 && scene == "zc-supply") { peerSkills.Add("fixture:wulx-caller"); peerSkills.Add(Zc); } b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", key, "shu", baseHp, peerSkills)); }
            var kind = scene switch { "xm-lion" => CardKind.SilverLion, "xm-ox" => CardKind.WoodenOx, "zc-played" or "zc-supply" => CardKind.Slash, "zc-dodge" => CardKind.Dodge, _ => CardKind.Crossbow };
            var card = CardCatalog.Get(kind); const string cardId = "fixture:wulx-card"; b.AddCard(new(cardId, card.DisplayName, card.CategoryName, "Original printed card", kind));
            var rank = scene is "ruxian" or "xm-shortage" ? 7 : 8; var initial = scene is "xm-round" or "xm-shortage" or "first" or "late" or "threshold" or "prevent" or "two-hits" || scene.StartsWith("chain-", StringComparison.Ordinal) ? 0 : 4;
            b.AddDeck(new("fixture:wulx-deck", "Sixty-four original native cards", initial, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(cardId, scene.StartsWith("chain-", StringComparison.Ordinal) ? Suit.Heart : Suit.Club, rank)).ToArray() });
            var roles = scene == "zc-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : scene == "zc-supply" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "Full current Wu Lu Xun rules", 4, 4, roles, "fixture:wulx-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
