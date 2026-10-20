using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouChenGongMingceChecks
{
    private static ShanheStartingCard C(CardKind kind) => MouChenGongScene.C(kind);
    private static long PayGift(MouChenGongScene s, int target = 1)
    {
        s.BeginGift(target); var root = s.GiftFrame().Id; s.Gift(s.V().Hand.First().Id);
        s.Reach(() => s.E<MouChenGongMingceCompletedEvent>().Any(e => e.FrameId == root)); s.Play(); return root;
    }
    private static void CheckGift(MouChenGongScene s, long root)
    {
        var paid = s.E<MouChenGongGiftPaidEvent>().Single(e => e.FrameId == root);
        s.Need(s.G.CardMovements.Count(m => m.CardId == paid.CardId && m.Sequence == paid.Sequence && m.From == paid.From && m.To == paid.To && m.Reason.Value == MouChenGongScene.GiftReason) == 1 &&
            s.E<MouChenGongMingceCompletedEvent>().Count(e => e.FrameId == root) == 1 && !s.G.ResolutionStack.Any(f => f.Id == root),
            "The exact real gift and original accepted parent complete once after all native payment descendants.");
    }
    public static void RealHandEquipmentGiftChoicesExactPlayQuotaAndPaidChildrenCold()
    {
        foreach (var kind in new[] { CardKind.Dodge, CardKind.SilverLion, CardKind.ThunderChariot })
        {
            var s = MouChenGongScene.Start(new("real-HE-" + kind) { OwnerHp = kind == CardKind.SilverLion ? 2 : 3, Hand = [C(kind)], GiftChild = "pause", PauseLoss = true, PauseDraw = true });
            var card = s.V().Hand.Single().Id; var equipment = kind != CardKind.Dodge;
            if (equipment) { s.Native(kind, []); s.Play(); }
            s.BeginGift(1); var root = s.GiftFrame().Id; s.Private(); s.RejectPrompt(); s.Cold(); s.Gift(card, equipment ? CardZoneKind.Equipment : CardZoneKind.Hand);
            s.Reach(() => s.Hold("gift-child")); var paid = s.E<MouChenGongGiftPaidEvent>().Single();
            var destination = kind == CardKind.ThunderChariot ? CardLocation.OutsideGame : CardLocation.Hand(1);
            s.Need(paid.CardId == card && paid.From == (equipment ? CardLocation.Equipment(0) : CardLocation.Hand(0)) && paid.To == destination &&
                s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == destination && s.E<MouChenGongBranchAcceptedEvent>().Length == 0 && s.Ce() == 0 &&
                (kind != CardKind.SilverLion || s.V().Hp == 3),
                "A genuine owned entity pays once, including SilverLion departure recovery and actual ThunderChariot destruction OutsideGame, before any recipient choice or reward.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("loss-child"));
            s.Need(s.V(1).Hp == 11 && s.E<MouChenGongBranchAcceptedEvent>().Single().Branch == "lose-hp" && s.Ce() == 0 && s.E<MouChenGongDrawReturnedEvent>().Length == 0 &&
                s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w => w.Change is { Kind: HpChangeKind.Loss, TargetSeat: 1, Amount: 1 }),
                "The high-HP real ally independently chooses Loss1; its genuine HP child precedes both ownerDraw2 and Ce1.");
            s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("draw-child"));
            s.Need(s.Ce() == 0 && s.G.CardMovements.Count(m => m.Reason.Value == MouChenGongScene.DrawReason && m.To == CardLocation.Hand(0)) == 2,
                "Both real owner draw entities are paid before their movement child returns; Ce is still ungranted.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<MouChenGongDrawReturnedEvent>().Single(e => e.FrameId == root) is { RecipientSeat: 0, RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } && s.Ce() == 1 &&
                s.E<MouChenGongLossReturnedEvent>().Single(e => e.FrameId == root) is { RecipientSeat: 1, ActualAmount: 1, HpBefore: 12, HpAfter: 11 },
                "The original loss branch returns one exact native Loss receipt, actual Draw2 outcome and one earned Ce.");
            s.PublicCe(1); s.PublicBudget(1); CheckGift(s, root); s.Cold();
        }
        foreach (var branch in new[] { "lose-hp", "draw" })
        {
            var s = MouChenGongScene.Start(new("human-recipient-" + branch) { SourceSeat = 1, Hand = [], Peers = [[C(CardKind.Dodge)], [], []], PauseLoss = true, PauseDraw = true });
            s.End(); s.Reach(() => s.Branch(branch) && s.P is { PlayerSeat: 0 }); var root = s.GiftFrame().Id;
            s.Need(s.E<MouChenGongMingceStartedEvent>().Single().Source.OwnerSeat == 1 && s.E<MouChenGongGiftPaidEvent>().Single() is { RecipientSeat: 0, From.OwnerSeat: 1, To.OwnerSeat: 0 },
                "The real allied AI source gives its actual Hand card to the human, who sees both independent recipient branches.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
            s.Reach(() => s.E<MouChenGongMingceCompletedEvent>().Any(e => e.FrameId == root));
            var draw = s.E<MouChenGongDrawReturnedEvent>().Single(e => e.FrameId == root);
            s.Need(branch == "lose-hp" ? s.V().Hp == 2 && draw is { RecipientSeat: 1, RequestedCount: 2, ActualCount: 2 } && s.Ce(1) == 1 :
                s.V().Hp == 3 && draw is { RecipientSeat: 0, RequestedCount: 1, ActualCount: 1 } && s.Ce(1) == 0 && s.E<MouChenGongLossIssuedEvent>().Length == 0,
                "The genuine human choice pays precisely its stated loss/owner reward or recipientDraw1, without substituting branches."); CheckGift(s, root); s.Cold();
        }
        foreach (var rescue in new[] { false, true })
        {
            var s = MouChenGongScene.Start(new("recipient-dying-" + rescue) { SourceSeat = 1, OwnerHp = 1, Hand = [], Peers = [[C(CardKind.Dodge)], [], []], Rescue = rescue });
            s.End(); s.Reach(() => s.Branch("lose-hp") && s.P is { PlayerSeat: 0 }); var root = s.GiftFrame().Id;
            s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "lose-hp");
            if (rescue) { s.Reach(() => s.Hold("rescue-child")); s.Need(s.V().Hp == 0 && s.Ce(1) == 0 && s.E<MouChenGongDrawReturnedEvent>().Length == 0, "Actual Dying must return before the living original owner receives either reward."); s.Private(); s.Cold(); s.Continue(); }
            s.Reach(() => s.E<MouChenGongMingceCompletedEvent>().Any(e => e.FrameId == root));
            s.Need(s.V().IsAlive == rescue && (rescue ? s.V().Hp == 2 : s.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 0)) && s.Ce(1) == 1 &&
                s.E<MouChenGongDrawReturnedEvent>().Single(e => e.FrameId == root) is { RecipientSeat: 1, RequestedCount: 2, ActualCount: 2 } && s.G.State.Winner == Winner.None,
                "A real rescue or nonterminal recipient death completes its native subtree before the accepted living ownerDraw2 and Ce1."); CheckGift(s, root); s.Cold();
        }
        var deadOwner = MouChenGongScene.Start(new("accepted-gift-dead-beneficiary") { SourceSeat = 1, Hand = [], Peers = [[C(CardKind.Dodge)], [], []], GiftChild = "source-death" });
        deadOwner.End(); deadOwner.Reach(() => deadOwner.Hold("gift-child")); var deadRoot = deadOwner.GiftFrame().Id; deadOwner.Private(); deadOwner.Cold(); deadOwner.Continue();
        deadOwner.Reach(() => deadOwner.Branch("lose-hp") && deadOwner.P is { PlayerSeat: 0 });
        deadOwner.Need(!deadOwner.V(1).IsAlive && deadOwner.G.State.Winner == Winner.None, "The original owner really dies after the gift while a teammate keeps the accepted resolution nonterminal.");
        deadOwner.Private(); deadOwner.Cold(); deadOwner.Answer(c => c.Parameters.GetValueOrDefault("branch") == "lose-hp"); deadOwner.Reach(() => deadOwner.E<MouChenGongMingceCompletedEvent>().Any(e => e.FrameId == deadRoot));
        deadOwner.Need(deadOwner.V().Hp == 2 && deadOwner.E<MouChenGongLossReturnedEvent>().Single(e => e.FrameId == deadRoot).ActualAmount == 1 && deadOwner.Ce(1) == 0 &&
            !deadOwner.E<MouChenGongDrawReturnedEvent>().Any(e => e.FrameId == deadRoot) && !deadOwner.E<MouChenGongMingceCompletedEvent>().Single(e => e.FrameId == deadRoot).CeGained,
            "An already accepted recipient loss still resolves once, while a dead original beneficiary receives neither fabricated Draw2 nor Ce."); CheckGift(deadOwner, deadRoot); deadOwner.Cold();
        var lost = MouChenGongScene.Start(new("accepted-gift-source-loss") { ConditionalMingce = true, GiftChild = "source-loss" });
        lost.BeginGift(1); var lostRoot = lost.GiftFrame().Id; lost.Gift(lost.V().Hand.Single().Id); lost.Reach(() => lost.Hold("gift-child")); lost.Cold(); lost.Continue(); lost.Play();
        lost.Need(!lost.Has(MouChenGongScene.Mingce) && lost.V().Hp == 2 && lost.Ce() == 1 && lost.E<MouChenGongDrawReturnedEvent>().Single(e => e.FrameId == lostRoot).ActualCount == 2,
            "Real source loss after the paid gift does not cancel the accepted recipient loss or living owner reward.");
        lost.Use("recover-self"); lost.Play(); lost.Need(lost.Has(MouChenGongScene.Mingce), "Real recovery regrants the conditional source."); lost.PublicBudget(1);
        lost.Reject(new UseProgramSkillCommand(0, MouChenGongScene.Mingce, "gift-and-plan", [], [1], lost.G.Revision, lost.P!.PromptId)); CheckGift(lost, lostRoot); lost.Cold();
        foreach (var empty in new[] { false, true })
        {
            var s = MouChenGongScene.Start(new("draw-shortage-" + empty) { DeckCount = empty ? 4 : 1 });
            if (empty) { s.Use("draw-stock"); s.Play(); s.Need(!s.G.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.DrawPile), "A genuine native draw exhausts the bounded stock before the gift."); }
            var root = PayGift(s); var d = s.E<MouChenGongDrawReturnedEvent>().Single(e => e.FrameId == root);
            s.Need(d.RequestedCount == 2 && d.ActualCount == (empty ? 0 : 1) && d.ReplacementCount == 0 && s.Ce() == 1 &&
                s.E<MouChenGongMingceCompletedEvent>().Single(e => e.FrameId == root).CeGained,
                "A living owner earns Ce after the real Draw2 attempt even when actual stock supplies zero or one card; requested count is never forged as actual."); CheckGift(s, root); s.Cold();
        }
    }

    public static void ActualPlayStartCeAccumulationFrozenDamageThenClearAndSourceLifetimeCold()
    {
        var s = MouChenGongScene.Start(new("actual-extra-Play-accumulation") { InsertPlay = true, Hand = [C(CardKind.Dodge), C(CardKind.Dodge)], PauseDamage = true });
        var first = PayGift(s); var phase = s.E<MouChenGongMingceStartedEvent>().Single(e => e.FrameId == first).Phase; var turn = s.G.State.TurnNumber;
        s.End(); s.Reach(s.ReleaseOffer); s.Need(s.G.State.TurnNumber == turn && s.Ce() == 1, "The real inserted Play earns Ce before the same actual turn's normal Play-start release.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play(); s.PublicBudget(0); s.PublicCe(1);
        var second = PayGift(s); s.Need(s.Ce() == 2 && s.E<MouChenGongMingceStartedEvent>().Single(e => e.FrameId == second).Phase != phase,
            "Declining preserves Ce and a distinct actual normal Play has its own gift quota, accumulating two real earned marks.");
        s.End(); s.Reach(s.ReleaseOffer); s.PublicCe(2); s.Private(); s.RejectPrompt(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.Action("mou-chen-gong-release")); var root = s.ReleaseFrame().Id; s.Private(); s.Cold(); s.Answer(c => c.Targets.SequenceEqual([2]));
        s.Reach(() => s.Hold("damage-child"));
        s.Need(s.ReleaseFrame().MouChenGongRelease is { FrozenX: 2, TargetSeat: 2, Cleared: false } && s.Ce() == 2 && s.V(2).Hp == 10 &&
            s.E<MouChenGongReleaseDamageIssuedEvent>().Single(e => e.FrameId == root) is { SourceSeat: 0, TargetSeat: 2, Amount: 2 } && s.E<MouChenGongCeClearedEvent>().Length == 0,
            "Accepted X is frozen at two, genuine Damage2 has occurred, and all Ce remain held while its actual damage child is pending.");
        s.Private(); s.Cold(); s.Continue(); s.Play(); CheckRelease(s, root, 2); s.PublicCe(0); s.PublicBudget(0); PayGift(s); s.PublicCe(1); s.Cold();

        foreach (var kind in new[] { "armor", "prevention", "source-loss", "source-death" })
        {
            var t = MouChenGongScene.Start(new("release-" + kind) { ConditionalMingce = kind == "source-loss", DamageChild = kind.StartsWith("source-", StringComparison.Ordinal) ? kind : "none",
                PauseDamage = kind != "prevention", PreventRelease = kind == "prevention" });
            PayGift(t); if (kind == "armor") { t.Use("armor-target", [2]); t.Play(); }
            t.End(); t.Reach(t.ReleaseOffer); t.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); t.Reach(() => t.Action("mou-chen-gong-release")); var id = t.ReleaseFrame().Id;
            t.Answer(c => c.Targets.SequenceEqual([2]));
            if (kind != "prevention") { t.Reach(() => t.Hold("damage-child")); t.Need(t.Ce() == 1 && !t.E<MouChenGongCeClearedEvent>().Any(e => e.FrameId == id), "The accepted Ce is not an up-front damage cost."); t.Private(); t.Cold(); t.Continue(); }
            t.Reach(() => t.E<MouChenGongReleaseCompletedEvent>().Any(e => e.FrameId == id));
            t.Need(kind switch {
                "armor" => t.V(2).Hp == 12 && t.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2 && e.ArmorPayment is { ArmorAbsorbed: 1, HpLossAmount: 0 }),
                "prevention" => t.V(2).Hp == 12 && !t.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2) && t.E<ProgramDamagePreventedEvent>().Any(e => e.TargetSeat == 2),
                "source-loss" => t.V().IsAlive && !t.Has(MouChenGongScene.Mingce) && t.V(2).Hp == 11,
                _ => !t.V().IsAlive && t.V(2).Hp == 11 && t.G.State.Winner == Winner.None },
                "Armor, native prevention and actual source loss/nonterminal owner death retain the accepted original sourced damage consequence.");
            if (kind == "source-death") t.Need(t.E<PlayerMarkerChangedEvent>().Any(e => e.PlayerSeat == 0 && e.Marker == PlayerMarkerKind.MouChenGongCe && e.Delta == -1 && e.Count == 0),
                "The real native death cleanup has already removed the owner's attributed Ce before the release's final clearing step.");
            CheckRelease(t, id, 1, kind == "source-death" ? 0 : 1); t.PublicCe(0); t.Cold();
        }
        var dying = MouChenGongScene.Start(new("release-real-dying") { Hand = [C(CardKind.Dodge), C(CardKind.Peach)], PeerHp = [12, 1, 12] });
        PayGift(dying); dying.End(); dying.Reach(dying.ReleaseOffer); dying.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); dying.Reach(() => dying.Action("mou-chen-gong-release"));
        var dyingRoot = dying.ReleaseFrame().Id; dying.Answer(c => c.Targets.SequenceEqual([2])); dying.Reach(() => dying.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        dying.Need(dying.Ce() == 1 && dying.V(2).Hp == 0 && !dying.E<MouChenGongCeClearedEvent>().Any(), "Real target Dying suspends the accepted release before Ce clearing.");
        dying.Private(); dying.Cold(); dying.Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die"); dying.Reach(() => dying.E<MouChenGongReleaseCompletedEvent>().Any(e => e.FrameId == dyingRoot));
        dying.Need(!dying.V(2).IsAlive && dying.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 2) && dying.G.State.Winner == Winner.None, "The real nonterminal victim death returns through the original release."); CheckRelease(dying, dyingRoot, 1); dying.Cold();
    }
    private static void CheckRelease(MouChenGongScene s, long root, int x, int? expectedCleared = null)
    {
        s.Need(s.E<MouChenGongReleaseAcceptedEvent>().Count(e => e.FrameId == root && e.FrozenX == x) == 1 &&
            s.E<MouChenGongReleaseDamageIssuedEvent>().Count(e => e.FrameId == root && e.Amount == x) == 1 &&
            s.E<MouChenGongCeClearedEvent>().Single(e => e.FrameId == root) is { FrozenX: var frozen, ClearedCount: var cleared } && frozen == x && cleared == (expectedCleared ?? x) &&
            s.E<MouChenGongReleaseCompletedEvent>().Count(e => e.FrameId == root && e.Cleared) == 1 && !s.G.ResolutionStack.Any(f => f.Id == root) && s.Ce() == 0,
            "One accepted original X owns one damage, then clears every held Ce and completes once after all native descendants.");
    }
}

internal sealed record MouChenGongSpec(string Name)
{
    public int SourceSeat { get; init; }
    public bool Mingce { get; init; } = true;
    public bool Zhichi { get; init; }
    public bool ConditionalMingce { get; init; }
    public bool ConditionalZhichi { get; init; }
    public bool InsertPlay { get; init; }
    public bool ForeignPulse { get; init; }
    public bool PauseLoss { get; init; }
    public bool PauseDraw { get; init; }
    public bool PauseDamage { get; init; }
    public bool PauseBefore { get; init; }
    public bool PreventFirst { get; init; }
    public bool PreventRelease { get; init; }
    public bool Rescue { get; init; }
    public int DeckCount { get; init; } = 48;
    public string GiftChild { get; init; } = "none";
    public string DamageChild { get; init; } = "none";
    public int OwnerHp { get; init; } = 3;
    public int[] PeerHp { get; init; } = [12, 12, 12];
    public ShanheStartingCard[] Hand { get; init; } = [MouChenGongScene.C(CardKind.Dodge)];
    public ShanheStartingCard[][] Peers { get; init; } = [[], [], []];
}

internal sealed class MouChenGongScene(MouChenGongSpec spec, GameEngine game, ContentRegistry registry)
{
    public const string Mingce = "mou-mobile:mingce", Zhichi = "mou-mobile:zhichi", Driver = "fixture:mcg-driver", Watch = "fixture:mcg-watch",
        Quiet = "fixture:mcg-quiet", Conditional = "fixture:mcg-conditional", Prevent = "fixture:mcg-prevent", Pulse = "fixture:mcg-foreign",
        Owner = "fixture:mcg-human", Ally = "fixture:mcg-ally", EnemyA = "fixture:mcg-enemy-a", EnemyB = "fixture:mcg-enemy-b", Deck = "fixture:mcg-deck",
        GiftReason = "skill-program.mou-mobile:mingce.give", DrawReason = "skill-program.mou-mobile:mingce.Draw";
    public static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);
    public GameEngine G { get; private set; } = game;
    public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
    public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
    public bool Has(string id, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == id) == true;
    public int Ce(int seat = 0) => V(seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouChenGongCe)?.Count ?? 0;
    public bool Action(string action) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    public bool Hold(string option) => P?.SkillPrompt?.SkillId is Watch or Pulse or "fixture:mcg-rescue" && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    public bool Branch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-chen-gong-mingce" && c.Parameters.GetValueOrDefault("branch") == branch) == true;
    public bool ReleaseOffer() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Mingce && Action("activate");
    public ProgramSkillFrame GiftFrame() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouChenGongGift is not null);
    public ProgramSkillFrame ReleaseFrame() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouChenGongRelease is not null);
    public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
    public void BeginGift(int target)
    {
        PublicBudget(0); Accept(new UseProgramSkillCommand(0, Mingce, "gift-and-plan", [], [target], G.Revision, P!.PromptId));
        Reach(() => Action("mou-chen-gong-gift")); PublicBudget(1);
    }
    public void Gift(int card, CardZoneKind zone = CardZoneKind.Hand) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-chen-gong-gift" &&
        c.Cards.SequenceEqual([card]) && c.Parameters.GetValueOrDefault("source-zone") == zone.ToString());
    public void PublicBudget(int used)
    {
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var states = G.CreateSnapshot(viewer).Players[0].SkillRuntimeStates?.Single(s => s.SkillId == Mingce).PublicRuleStates ?? [];
            Need(states.Count(s => s.StateId == "gift-and-plan" && s.Kind == ProgramPublicRuleStateKind.ActivationLimit && s.Scope == SkillUsageScope.Phase && s.Value == 1 && s.Used == used) == 1,
                "Every viewer reads the same accepted actual-Play gift budget Used " + used + " from its real source facts."); Frozen(states);
        }
    }
    public void PublicCe(int count, int owner = 0)
    {
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var markers = G.CreateSnapshot(viewer).Players[owner].Markers ?? [];
            Need((markers.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouChenGongCe)?.Count ?? 0) == count, "Actual earned Ce is public and identical for every viewer."); Frozen(markers);
        }
    }
    public void Native(CardKind kind, int[] targets)
    {
        var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
        Need(a is not null, "A genuine physical " + kind + " with exact native targets is required.");
        Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
    }
    public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
    public void Continue()
    {
        if (P?.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
        else Answer(c => c.Parameters.ContainsKey("option-id"));
    }
    public void Answer(Func<PromptChoice, bool> choose)
    {
        var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(p.PlayerSeat == 0 && c is not null, "Submit only a currently published actual human choice.");
        Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
    }
    public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var n = 0; n < 280; n++)
        {
            if (done()) return; Need(G.State.Winner == Winner.None, "Native terminal cutoff before boundary " + line);
            var p = P;
            if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End();
            else if (p.SkillPrompt?.SkillId is Watch or Pulse or "fixture:mcg-rescue" && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue();
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (Action("skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException("Unexpected actual boundary " + line + " " + Diagnostic());
        }
        throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
    }
    public void Private()
    {
        var p = P!; Need(p.IsPrivate && Enumerable.Range(0, 4).Where(v => v != p.PlayerSeat).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(x => x.Seat != v).All(x => x.Hand.Count == 0)), "Only the real chooser sees private choices and foreign Hands stay opaque.");
        Frozen(p.Choices); foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); }
    }
    public void Frozen<T>(IReadOnlyList<T> items)
    {
        if (items is IList list && list.Count > 0) { var before = JsonSerializer.Serialize(items); var rejected = false; try { list[0] = list[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(items), "Prepared nested collections reject mutation."); }
    }
    public void RejectPrompt()
    {
        var p = P!; Reject(new AnswerPromptCommand(p.PlayerSeat == 0 ? 1 : 0, p.PromptId, p.Choices[0].Id, G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mou-chen-gong-choice"), G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1));
    }
    public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
        Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
    public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains all viewer views, original paid children, actual phase/turn identities, facts, movement and accepted commands."); }
    public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Invalid actor, unpublished or stale input cannot mutate any prepared view, payment, RNG or history."); }
    public void Accept(GameCommand command)
    {
        try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); }
        catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); }
    }
    public void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(message + " " + Diagnostic()); }
    private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
        Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).IsAlive, V(i).Armor, V(i).HandCount, Ce = Ce(i), V(i).Equipment }),
        Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.Contains("MouChenGong", StringComparison.Ordinal)).TakeLast(18).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(12) });

    public static MouChenGongScene Start(MouChenGongSpec spec)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec));
        var setup = new ShanheBattleSetup(7, 1, "mcg-fixed-native", Owner, spec.OwnerHp, 0, 0, 0, 0, [], spec.Hand, [],
            [new(EnemyA, Hp: spec.PeerHp[1]) { StartingHand = spec.Peers[1] }, new(EnemyB, Hp: spec.PeerHp[2]) { StartingHand = spec.Peers[2] }],
            [new(Ally, Hp: spec.PeerHp[0]) { StartingHand = spec.Peers[0] }], new string('e', 64));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        var scene = new MouChenGongScene(spec, g, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
    }

    private sealed class Fixture(MouChenGongSpec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-chen-gong-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            const string bundle = "mou-chen-gong-mobile";
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Mingce, Zhichi }) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = real.Programs[id], ProgramPresentation = real.Presentations[id], Tags = id == Zhichi ? SkillTag.Locked : SkillTag.None });
            var kinds = spec.Hand.Concat(spec.Peers.SelectMany(x => x)).Select(c => c.Kind).Concat(new[] { CardKind.Dodge, CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.IronChain, CardKind.Peach, CardKind.SilverLion, CardKind.ThunderChariot }).Distinct();
            foreach (var kind in kinds) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mcg-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"hold\",\"options\":[{\"id\":\"" + id + "\"}]}";
            static JsonNode A(string id, string effects, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            var acts = new JsonArray(A("lose-self", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-self", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("armor", "{\"op\":\"grantArmor\",\"target\":\"owner\",\"amount\":3}"), A("armor-target", "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("source-less", "{\"op\":\"receiveOwnerDamage\",\"target\":\"owner\",\"amount\":1}"), A("draw-stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("chain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}", 1),
                A("extra-turn", "{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}"));
            foreach (var nature in new[] { "normal", "fire", "thunder" })
            {
                acts.Add(A(nature, "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1,\"nature\":\"" + nature + "\"}"));
                acts.Add(A("foreign-" + nature, "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1,\"nature\":\"" + nature + "\",\"sourceRef\":{\"kind\":\"selectedTarget\"}}", 1));
            }
            acts.Add(A("normal-two", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2}"));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts,
                ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":20,\"priority\":0}]") },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var watches = new JsonArray();
            if (spec.GiftChild != "none")
                foreach (var zone in new[] { "hand", "equipment" }) watches.Add(N("{\"id\":\"gift-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone + "\"],\"movementReasons\":[\"" + GiftReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"optional\":false,\"effects\":[" + Pause("gift-child") +
                    (spec.GiftChild == "source-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : spec.GiftChild == "source-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "") + "]}"));
            if (spec.PauseLoss) watches.Add(N("{\"id\":\"loss\",\"window\":\"afterHpLost\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perPoint\",\"priority\":1000,\"optional\":false,\"effects\":[" + Pause("loss-child") + "]}"));
            if (spec.PauseDraw) watches.Add(N("{\"id\":\"draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + DrawReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"optional\":false,\"effects\":[" + Pause("draw-child") + "]}"));
            if (spec.PauseBefore) watches.Add(N("{\"id\":\"before-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"priority\":1000,\"optional\":false,\"condition\":{\"kind\":\"all\",\"children\":[{\"kind\":\"damageNatureIs\",\"damageNature\":\"fire\"}]},\"effects\":[" + Pause("attempt-child") + "]}"));
            if (spec.PauseDamage || spec.DamageChild != "none") watches.Add(N("{\"id\":\"damage\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamage\",\"priority\":-1000,\"optional\":false,\"effects\":[" + Pause("damage-child") +
                (spec.DamageChild == "source-loss" ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}" : spec.DamageChild == "source-death" ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}" : "") + "]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            var grants = new JsonArray();
            if (spec.ConditionalMingce) grants.Add(N("{\"id\":\"healthy-plan\",\"skillId\":\"" + Mingce + "\",\"minimumHp\":3}"));
            if (spec.ConditionalZhichi) grants.Add(N("{\"id\":\"wounded-protection\",\"skillId\":\"" + Zhichi + "\",\"maximumHp\":2}"));
            if (grants.Count > 0) defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1, ["hpSkillGrants"] = grants });
            if (spec.PreventFirst || spec.PreventRelease) defs.Add(N("{\"id\":\"" + Prevent + "\",\"revision\":1,\"triggers\":[{\"id\":\"one-native-prevention\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}"));
            if (spec.Rescue) defs.Add(N("{\"id\":\"fixture:mcg-rescue\",\"revision\":1,\"triggers\":[{\"id\":\"actual-rescue\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("rescue-child") + ",{\"op\":\"recoverTo\",\"target\":\"owner\",\"numberExpression\":\"integerConstant\",\"minimumValue\":2,\"clampToMaxHp\":true}]}]}"));
            if (spec.InsertPlay) defs.Add(N("{\"id\":\"fixture:mcg-extra-play\",\"revision\":1,\"triggers\":[{\"id\":\"insert\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}"));
            if (spec.ForeignPulse) defs.Add(N("{\"id\":\"" + Pulse + "\",\"revision\":1,\"triggers\":[{\"id\":\"foreign-native-damage\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1,\"sourceRef\":{\"kind\":\"selectedTarget\"}}," + Pause("foreign-arm") + ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2,\"nature\":\"fire\",\"sourceRef\":{\"kind\":\"selectedTarget\"}},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2,\"nature\":\"thunder\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋陈宫真实原生检查",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(x => x!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(x => x!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(x => x, x => x) });
            var local = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in local.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = p, ProgramPresentation = local.Presentations[id] });
            List<string> SourceSkills(int seat)
            {
                var s = new List<string>(); if (watches.Count > 0) s.Add(Watch);
                if (spec.Mingce && spec.SourceSeat == seat) { if (spec.ConditionalMingce) s.Add(Conditional); else s.Add(Mingce); }
                if (seat == 0) { s.Add(Driver); if (spec.Zhichi) { if (spec.ConditionalZhichi) { if (!s.Contains(Conditional)) s.Add(Conditional); } else s.Add(Zhichi); } if (spec.InsertPlay) s.Add("fixture:mcg-extra-play"); if (spec.PreventFirst) s.Add(Prevent); if (spec.Rescue) s.Add("fixture:mcg-rescue"); if (spec.ForeignPulse) s.Add(Pulse); }
                else if (!(seat == 1 && (spec.SourceSeat == 1 && spec.Mingce || spec.ForeignPulse))) s.Add(Quiet);
                if (seat == 2 && spec.PreventRelease) s.Add(Prevent);
                return s;
            }
            b.AddGeneral(new(Owner, "真实陈宫检查人", "", "standard:none", "qun", spec.Zhichi ? 12 : 3, SourceSkills(0)));
            b.AddGeneral(new(Ally, "真实同行", "", "standard:none", "wei", 12, SourceSkills(1)));
            b.AddGeneral(new(EnemyA, "真实对手甲", "", "standard:none", "wei", 12, SourceSkills(2)));
            b.AddGeneral(new(EnemyB, "真实对手乙", "", "standard:none", "wei", 12, SourceSkills(3)));
            b.AddDeck(new(Deck, "固定中性实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, spec.DeckCount).Select(_ => new ContentDeckPhysicalCard("fixture:mcg-card-" + CardKind.Dodge, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋陈宫真实小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
