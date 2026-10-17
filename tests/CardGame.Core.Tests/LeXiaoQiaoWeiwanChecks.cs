using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeXiaoQiaoWeiwanChecks
{
    private const string WwSkill = "le:weiwan", WwQin = "le:qiqin", WwOwner = "fixture:lxw-owner", WwMode = "identity:classic-le-xiao-qiao-weiwan";
    private const string WwDriver = "fixture:lxw-driver", WwWatch = "fixture:lxw-watch", WwQuiet = "fixture:lxw-quiet", WwGrant = "fixture:lxw-grant";
    private const string WwDiscard = "skill-program.le-xiao-qiao.weiwan.discard", WwObtain = "skill-program.le-xiao-qiao.weiwan.obtain";
    private static readonly Suit[] WwSuits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];

    public static void WeiwanRandomRegionSuitCohortAndActualGainBranchesCold()
    {
        for (var count = 0; count <= 3; count++)
        {
            var s = WwScenario.Start("regions-" + count); s.Gather(); var cost = s.InitialQin().First();
            var cohort = s.Cohort(2, cost, count, regions: true); var hp = s.V(2).Hp;
            var command = s.Command(cost, 2); s.Accept(command); s.Reach(() => s.Hold("cost"));
            var started = s.E<WeiwanStartedEvent>().Single(); var parent = s.Frame(started.FrameId);
            s.Need(parent.Weiwan is { Stage: WeiwanStage.CostChildren } && started.CostFrom == CardLocation.Hand(0) &&
                s.E<WeiwanCostPaidEvent>().Single().CardId == cost && !s.E<WeiwanSuitSampledEvent>().Any(),
                "A single original Qin Hand payment is committed before the fresh region sampling, without choosing an opponent's concealed card.");
            s.Private(); s.Reject(command); s.RejectPrompt(); s.Cold(); var old = s.P!; s.Continue(); s.RejectOld(old);
            if (count > 0)
            {
                s.Reach(() => s.Hold("obtained")); parent = s.Frame(started.FrameId); var receipt = parent.Weiwan!;
                var sampled = s.E<WeiwanSuitSampledEvent>(); var gained = s.E<WeiwanEntityObtainedEvent>();
                s.Need(receipt.Stage == WeiwanStage.ObtainChildren && receipt.Samples is IList { IsReadOnly: true } &&
                    sampled.Length == 3 && sampled.Select(e => e.Suit).ToHashSet().SetEquals(WwSuits.Where(x => x != started.CostSuit)) &&
                    sampled.Count(e => e.CardId is not null) == count && gained.Length == count &&
                    sampled.Where(e => e.CardId is not null).All(e => cohort.TryGetValue(e.Suit, out var ids) && ids.Contains(e.CardId!.Value) && e.CandidateCount == ids.Count) &&
                    gained.All(e => s.G.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == e.From && m.To == CardLocation.Hand(0) && m.CardKind == e.PrintedKind && m.Reason.Value == WwObtain) == 1) &&
                    s.E<WeiwanObtainPaidEvent>().Single() is { PrimaryCount: var selected, ActualCount: var actual } && selected == count && actual == count &&
                    s.V(2).Hp == hp && !s.E<WeiwanCompletedEvent>().Any(),
                    "Every eligible suit contributes one genuinely random original HEJ entity; the actual atomic obtain is paid before its children and before any count-dependent benefit.");
                if (count == 3) s.Need(gained.Select(e => e.From.Zone).ToHashSet().SetEquals(new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment }),
                    "The three-card representative obtains original Hand, Equipment and Judgment entities rather than a Hand-only shortcut.");
                s.Private(); s.Cold(); s.Continue();
            }
            s.Play(); var completed = s.E<WeiwanCompletedEvent>().Single();
            var expected = count switch { 1 => WeiwanBranch.LoseHp, 2 => WeiwanBranch.UnlimitedUse, 3 => WeiwanBranch.ForbidUse, _ => WeiwanBranch.None };
            s.Need(completed.FrameId == started.FrameId && completed.ActualCount == count && completed.Branch == expected &&
                s.V(2).Hp == hp - (count == 1 ? 1 : 0) && s.E<WeiwanHealthAppliedEvent>().Length == (count == 1 ? 1 : 0) &&
                s.E<WeiwanTurnLeaseIssuedEvent>().Length == (count >= 2 ? 1 : 0) &&
                s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == started.FrameId && e.SkillId == WwSkill && e.Completed) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.Reason.Value == WwDiscard) == 1 &&
                !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == WwSkill),
                "Zero, one, two and three actual gains select mutually exclusive outcomes, with no fabricated cumulative damage or draw and one real actual-Play allowance."); s.Cold();
        }
    }

    public static void WeiwanEquipmentQinCostsLionOxAndEffectiveSuitCold()
    {
        var s = WwScenario.Start("lion"); s.Gather(); var cost = s.InitialQin().First(); s.Place(cost, 0); s.Use("hurt", targets: [0]); s.Play();
        s.Cohort(2, cost, 1); var hp = s.V().Hp; s.Activate(cost, 2); s.Reach(() => s.Hold("recovered"));
        s.Need(s.Frame(s.E<WeiwanStartedEvent>().Single().FrameId).Weiwan is { Stage: WeiwanStage.CostChildren } &&
            s.E<WeiwanCostPaidEvent>().Single().From == CardLocation.Equipment(0) && s.E<SilverLionRemovedRecoveryEvent>().Count(e => e.Reason.Value == WwDiscard) == 1 &&
            s.V().Hp == hp + 1 && !s.E<WeiwanSuitSampledEvent>().Any(),
            "A marked equipped Silver Lion is a real HE cost, and its native recovery finishes before the opponent's fresh suit cohort is sampled.");
        s.Private(); s.Cold(); s.Continue(); s.FinishWeiwan(); s.Need(s.E<WeiwanCompletedEvent>().Single() is { ActualCount: 1, Branch: WeiwanBranch.LoseHp }, "The Lion child returns to the same paid obtain once."); s.Cold();

        s = WwScenario.Start("ox"); s.Gather(); var marked = s.InitialQin(); var ox = marked[0]; var grain = marked[1]; s.Place(ox, 0);
        s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play(); s.Cohort(2, ox, 1);
        var legal = s.G.GetHumanLegalActions().Single(a => a.ProgramSkillId == WwSkill);
        s.Need(legal.SelectableCardIds.Contains(ox) && !legal.SelectableCardIds.Contains(grain) &&
            Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).Players[0].WoodenOxGrain?.Count ?? 0) == 0),
            "The actual marked equipped Ox is a legal cost; its globally marked private grain remains outside HE and is neither published nor selectable.");
        s.Reject(s.Command(grain, 2)); s.Activate(ox, 2); s.Reach(() => s.Hold("cost")); s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("obtained"));
        s.Need(s.E<WeiwanObtainPaidEvent>().Single().ActualCount == 1 && s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
            s.E<WeiwanEntityObtainedEvent>().All(e => e.CardId != grain) && (s.V().WoodenOxGrain?.Count ?? 0) == 0,
            "The real Ox spill drains as its own original movement, without becoming a second paid material or a counted opponent obtain."); s.Private(); s.Cold(); s.Continue(); s.Play();

        foreach (var scene in new[] { "cost-suit", "target-suit" })
        {
            s = WwScenario.Start(scene); s.Gather(); cost = s.InitialQin().First(); var foreign = s.Hand().First(c => c.Id != cost).Id; s.Give(foreign, 2);
            s.Activate(cost, 2); s.FinishWeiwan(); var start = s.E<WeiwanStartedEvent>().Single(); var end = s.E<WeiwanCompletedEvent>().Single();
            s.Need(start.CostSuit == Suit.Heart && s.E<WeiwanCostPaidEvent>().Single().PrintedKind == CardKind.Crossbow &&
                end.ActualCount == (scene == "cost-suit" ? 1 : 0) &&
                s.E<WeiwanSuitSampledEvent>().Single(e => e.Suit == Suit.Spade).CandidateCount == (scene == "cost-suit" ? 1 : 0),
                "The original all-Spade deck retains its printed entities, while cost color freezes the payer's live Hongyan and target grouping uses that target's own effective suit after cost children."); s.Cold();
        }
    }

    public static void WeiwanDirectedAllUseDistanceCountBanAndTurnExpiryCold()
    {
        var s = WwScenario.Start("permission"); s.Gather(); var cost = s.InitialQin().First(); s.Cohort(2, cost, 2);
        s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]))); s.Play();
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash), "A genuine natural Slash has already spent the normal allowance before the directed permission exists.");
        s.Activate(cost, 2); s.FinishWeiwan(); var lease = s.E<WeiwanTurnLeaseIssuedEvent>().Single().Lease;
        s.Need(s.G.GetCombatDistance(0, 2) == 2 && s.V().Equipment.Count == 0 &&
            s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])) && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])),
            "The two-card outcome grants real range and count only toward its original far target, without Crossbow or global unlimited Slash.");
        s.Use("lose-weiwan"); s.Play(); s.Need(s.V().Skills?.Any(skill => skill.ContentId == WwSkill) != true, "The original source is truly lost before using its already issued actual-turn permission.");
        s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]))); s.Play();
        s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Alcohol)); s.Play(); s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]))); s.Play();
        s.Need(!s.V().HasAlcoholEffect && !s.Actions().Any(a => a.Kind == LegalActionKind.Alcohol) && s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.Alcohol) == 1,
            "Directed permission does not waive self Alcohol's count, even after a true Slash consumes its real Alcohol effect.");
        var snatch = s.Actions().First(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([2])); s.RejectSource(snatch); s.Native(snatch);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); s.Private(); s.Cold(); s.Answer(c => c.Parameters.ContainsKey("slot-index")); s.Play();
        s.Native(s.Actions().First(a => a.Kind == LegalActionKind.SupplyShortage && a.TargetSeats.SequenceEqual([2]))); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Any(e => e.CardId == snatch.CardId && e.CardKind == CardKind.Snatch) && s.V(2).Judgment.Any(c => c.Kind == CardKind.SupplyShortage) &&
            s.E<WeiwanUseLeaseCapturedEvent>().Count(e => e.LeaseFrameId == lease.FrameId) >= 4,
            "The same issued policy reaches actual ordinary-trick obtain and delayed Use, and accepted native uses bind the original directed lease rather than a view-as source.");
        s.Use("ban-slash"); s.Play(); s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash) && s.Actions().Any(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([2])), "A real turn Use prohibition takes priority over the distance/count privilege."); s.Cold();

        s = WwScenario.Start("overlap"); s.Gather(); cost = s.InitialQin().First(); s.Cohort(2, cost, 2); s.Activate(cost, 2); s.FinishWeiwan(); var first = s.E<WeiwanStartedEvent>().Single();
        s.End(); s.Play(); s.Need(s.G.State.TurnNumber == first.ActualTurn && !s.E<TurnEndedEvent>().Any(e => e.TurnNumber == first.ActualTurn && e.ActorSeat == 0), "A real pre-end inserted Play creates a new phase without ending the actual-turn policy.");
        s.TakeAll(2); var secondCost = s.InitialQin().First(id => s.Hand().Any(c => c.Id == id)); s.Cohort(2, secondCost, 3); s.Activate(secondCost, 2); s.FinishWeiwan();
        s.Need(s.E<WeiwanTurnLeaseIssuedEvent>().Select(e => e.Lease.ActualCount).SequenceEqual([2, 3]) && s.E<WeiwanStartedEvent>().Last().Phase.Token != first.Phase.Token &&
            s.Actions().All(a => !a.TargetSeats.Contains(2)) && s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])),
            "Two complete real activations in distinct same-turn phases coexist; the later three-card all-Use prohibition dominates the earlier two-card permission and does not ban another target.");
        s.Use("duel", targets: [2]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); s.Private(); s.Cold(); s.Answer(c => c.Cards.Count == 1); s.Play();
        s.Need(s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash), "A real Duel Slash is pure Played and remains a legal response toward the otherwise prohibited card user."); s.Cold();

        s = WwScenario.Start("dianjun"); s.Gather(); cost = s.InitialQin().First(); s.Cohort(2, cost, 2); s.Activate(cost, 2); s.FinishWeiwan(); var ordinary = s.E<WeiwanStartedEvent>().Single(); s.End(); s.Play();
        s.Need(s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == ordinary.ActualTurn) && s.G.State.TurnNumber == ordinary.ActualTurn &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "Actual TurnEnded expires the ordinary-phase lease before real Dianjun's end-owned extra Play; that old lease cannot revive.");
        s.TakeAll(2); cost = s.InitialQin().First(id => s.Hand().Any(c => c.Id == id)); s.Cohort(2, cost, 2); s.Activate(cost, 2); s.FinishWeiwan();
        s.Need(s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])) && s.E<WeiwanTurnLeaseIssuedEvent>().Length == 2, "A new invoice actually issued in the real end-owned Play is valid despite the earlier turn-end fact.");
        s.End(); s.Play(); s.Need(s.G.State.TurnNumber != ordinary.ActualTurn && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The next true TurnStarted expires the tail-issued lease and ordinary range is restored."); s.Cold();

        s = WwScenario.Start("declaration"); s.Gather(); cost = s.InitialQin().First(); s.Cohort(2, cost, 2); s.Activate(cost, 2); s.FinishWeiwan();
        var declarationLease = s.E<WeiwanTurnLeaseIssuedEvent>().Single().Lease;
        var declared = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) &&
            a.ConversionSource is { SkillId: "classic:guhuo", BindingId: "declare-slash" } &&
            s.Hand().Any(c => c.Id == a.CardId && c.Kind == CardKind.Slash && !s.InitialQin().Contains(c.Id)));
        s.Native(declared); s.Reach(() => s.Hold("declaration")); var declaration = s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single();
        var captured = s.E<WeiwanDeclarationCapturedEvent>().Single(e => e.DeclarationId == declaration.Id);
        s.Need(declaration.Payment.Weiwan is { Leases: IList { IsReadOnly: true } } admission && admission.Leases.SequenceEqual([declarationLease]) &&
            captured is { LeaseCount: 1, ActorSeat: 0, ProviderSeat: 0, DeclaredKind: CardKind.Slash } && captured.OwnerFrameId == declaration.Id && captured.CardId == declared.CardId &&
            captured.From == CardLocation.Hand(0) && captured.CaptureSequence == admission.CaptureSequence &&
            s.E<WeiwanDeclarationLeaseCapturedEvent>().Count(e => e.DeclarationId == declaration.Id && e.Index == 0 && e.LeaseFrameId == declarationLease.FrameId) == 1 &&
            declaration.Payment.Cost.CardId == declared.CardId && declaration.Payment.Cost.From == CardLocation.Hand(0) && declaration.TargetSeats.SequenceEqual([2]) && !declaration.IsRevealed &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1 &&
            !s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0),
            "A genuine published far Guhuo prepays its original Hand entity and freezes the nonempty directed Weiwan lease on the actual declaration before any native Use exists.");
        s.Private(); s.RejectPrompt(); var originalPrompt = s.P!; s.Cold(); s.Continue(); s.RejectOld(originalPrompt); s.Play();
        var inherited = s.E<WeiwanUseAcceptedEvent>().Single(e => e.DeclarationId == declaration.Id);
        s.Need(s.V().Skills?.Any(skill => skill.ContentId == WwSkill) != true && inherited is { ActorSeat: 0, OriginalKind: CardKind.Slash, LeaseCount: 1 } &&
            inherited.CaptureSequence == captured.CaptureSequence && inherited.ActualTurn == captured.ActualTurn && inherited.TurnOwner == captured.TurnOwner &&
            s.E<WeiwanUseLeaseCapturedEvent>().Count(e => e.UseId == inherited.UseId && e.Index == 0 && e.LeaseFrameId == declarationLease.FrameId) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == inherited.UseId && e.CardKind == CardKind.Slash && e.SourceSeat == 0) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == inherited.UseId && e.CardKind == CardKind.Slash) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            s.E<WeiwanTurnLeaseIssuedEvent>().Single().Lease == declarationLease &&
            s.E<WeiwanDeclarationCapturedEvent>().Count(e => e.DeclarationId == declaration.Id) == 1,
            "After true skill loss in the prepaid child, the same declaration transfers its immutable original lease into one real distant Slash, with no live-source fallback, duplicate payment or fabricated new permission."); s.Cold();
    }

    public static void WeiwanPaidSourceLossDeathAndInputPrivacyCold()
    {
        foreach (var scene in new[] { "source-loss", "cost-death", "obtain-death" })
        {
            var s = WwScenario.Start(scene); s.Gather(); var cost = s.InitialQin().First(); s.Cohort(2, cost, 1); var hp = s.V(2).Hp;
            var nonQin = s.Hand().First(c => !s.InitialQin().Contains(c.Id)).Id; s.Reject(s.Command(nonQin, 2)); s.Reject(s.Command(cost, 0));
            var command = s.Command(cost, 2); s.Accept(command); s.Reach(() => s.Hold("cost")); var start = s.E<WeiwanStartedEvent>().Single();
            s.Need(s.E<WeiwanCostPaidEvent>().Length == 1 && !s.E<WeiwanSuitSampledEvent>().Any(), "The exact source and real original cost are paid before the actual source-loss/death child.");
            s.Private(); s.RejectPrompt(); s.Cold(); var prompt = s.P!; s.Continue(); s.RejectOld(prompt);
            if (scene == "cost-death")
            {
                s.Reach(() => s.E<WeiwanCompletedEvent>().Any()); s.PlayOrDead();
                s.Need(!s.V().IsAlive && !s.E<WeiwanEntityObtainedEvent>().Any() && s.E<WeiwanCompletedEvent>().Single() is { ActualCount: 0, Branch: WeiwanBranch.None } && s.V(2).Hp == hp,
                    "A dead actor drains its paid cost child but starts no new opponent obtain or fresh benefit."); s.Cold(); continue;
            }
            s.Reach(() => s.Hold("obtained")); var receipt = s.Frame(start.FrameId).Weiwan!;
            s.Need(receipt is { Stage: WeiwanStage.ObtainChildren, ActualCount: 1, Samples: IList { IsReadOnly: true } } &&
                s.E<WeiwanEntityObtainedEvent>().Length == 1 && !s.E<WeiwanCompletedEvent>().Any() && (scene != "source-loss" || s.V().Skills?.Any(skill => skill.ContentId == WwSkill) != true),
                "The issued original source survives true skill loss and retains the exact already-obtained entity while its native gain child is suspended.");
            s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.E<WeiwanCompletedEvent>().Any()); s.PlayOrDead();
            s.Need(s.E<WeiwanStartedEvent>().Length == 1 && s.E<WeiwanCostPaidEvent>().Length == 1 && s.E<WeiwanEntityObtainedEvent>().Length == 1 &&
                s.E<WeiwanCompletedEvent>().Single() is { ActualCount: 1, Branch: WeiwanBranch.LoseHp } && s.V(2).Hp == hp - 1 &&
                s.E<WeiwanHealthAppliedEvent>().Count(e => e.TargetSeat == 2 && e.Amount == 1) == 1 &&
                s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == start.FrameId && e.SkillId == WwSkill && e.Completed) == 1 &&
                (scene == "obtain-death" ? !s.V().IsAlive : s.V().IsAlive && s.V().Skills?.Any(skill => skill.ContentId == WwSkill) != true),
                "Source loss or death after the actual obtain cannot repeat the original payment, lose its already committed branch, or invent a replacement skill instance."); s.Cold();
        }
    }

    private sealed class WwScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public IReadOnlyList<CardSnapshot> Hand() => V().Hand;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame Frame(long id) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
        public int[] InitialQin() => E<QiqinInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).Select(e => e.CardId).ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.CardId is not null && a.ConversionSource is null && a.ProgramSkillId is null).ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == WwWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public UseProgramSkillCommand Command(int card, int target) => new(0, WwSkill, "discard-qin-obtain-suits", [card], [target], G.Revision, P!.PromptId);
        public void Activate(int card, int target) => Accept(Command(card, target));
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, WwDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Give(int card, int seat) { Use("give", [card], [seat]); Play(); }
        public void TakeAll(int seat) { if (V(seat).HandCount == 0) return; Use("take-all", targets: [seat]); Play(); }
        public void Gather() { Use("stock"); Play(); for (var i = 1; i <= 3; i++) TakeAll(i); Need(Hand().Count == WwFixture.DeckCount(scene) && InitialQin().Length == 4, "The whole small known inventory is acquired only through actual draw and original foreign-Hand movement, without seed searching or changing RNG."); }
        public void Place(int card, int seat) { Use("place", [card], [seat]); Play(); }
        public Dictionary<Suit, HashSet<int>> Cohort(int seat, int cost, int count, bool regions = false)
        {
            var card = Hand().Concat(V().Equipment).Single(c => c.Id == cost); var costSuit = card.Suit;
            var suits = WwSuits.Where(x => x != costSuit).ToArray(); var result = new Dictionary<Suit, HashSet<int>>();
            var reserved = InitialQin().ToHashSet();
            int Pick(Suit suit, CardKind? kind = null) => Hand().First(c => c.Suit == suit && c.Id != cost &&
                (kind is null ? !reserved.Contains(c.Id) && c.Kind is not (CardKind.Slash or CardKind.Alcohol or CardKind.Snatch or CardKind.SupplyShortage) : c.Kind == kind)).Id;
            for (var i = 0; i < count; i++)
            {
                var suit = suits[i]; var id = Pick(suit, regions && i == 1 ? CardKind.Crossbow : regions && i == 2 ? CardKind.Indulgence : null);
                result[suit] = [id];
                if (regions && i == 1) Place(id, seat);
                else if (regions && i == 2) { Native(Actions().Single(a => a.CardId == id && a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([seat]))); Play(); }
                else Give(id, seat);
            }
            // A same-suit Hand entity remains after all three eligible suits are taken.
            var excluded = Pick(costSuit); Give(excluded, seat);
            if (count > 0) { var duplicate = Pick(suits[0]); Give(duplicate, seat); result[suits[0]].Add(duplicate); }
            return result;
        }
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> choose) { var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(p.PlayerSeat == 0 && c is not null, "Only the real human chooser answers a published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void FinishWeiwan() { var n = E<WeiwanStartedEvent>().Length; Reach(() => E<WeiwanCompletedEvent>().Length == n); PlayOrDead(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayOrDead() => Reach(() => !V().IsAlive || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal before boundary " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.SkillPrompt?.SkillId == WwWatch) Continue();
                else if (p.SkillPrompt?.SkillId == "fixture:lxw-insert" && p.Choices is [var target] &&
                    target.Targets.SequenceEqual([0]) && target.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                    target.Parameters.GetValueOrDefault("target-kind") == "CurrentTurnPlayer")
                { Private(); Cold(); Answer(c => c.Id == target.Id); }
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null), "Private input and original hidden Hand identities remain absent from every other player view.");
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, "Rejected " + c.GetType().Name + " " + JsonSerializer.Serialize(result.Error)); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public string Diagnostic() => JsonSerializer.Serialize(new { scene, P, Hp = V().Hp, TargetHp = V(2).Hp, Frames = G.ResolutionStack, Starts = E<WeiwanStartedEvent>(), Costs = E<WeiwanCostPaidEvent>(), Samples = E<WeiwanSuitSampledEvent>(), Obtains = E<WeiwanEntityObtainedEvent>(), Completed = E<WeiwanCompletedEvent>() });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves original random-state samples, paid HE invoices, immutable cohorts, private children and actual-turn directed authorization."); }
        public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()).Accepted && before == State(), "Unpublished materials, wrong targets and replayed commands reject without paying or drawing another random sample."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-weiwan-child"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void RejectSource(LegalAction a) => Reject(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = new(WwSkill, "not-a-conversion", 0, "forged-weiwan-source") });
        public static WwScenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new WwFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene is "cost-death" or "obtain-death" ? Role.Renegade : Role.Lord, ModeId = WwMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 7 }, r);
            var s = new WwScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, WwOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class WwFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-xiao-qiao-weiwan", new(1, 0, 0), []);
        private static readonly CardKind[] Mixed = [CardKind.Crossbow, CardKind.SilverLion, CardKind.WoodenOx, CardKind.Indulgence, CardKind.Slash, CardKind.Snatch, CardKind.SupplyShortage, CardKind.Alcohol, CardKind.Peach, CardKind.Duel, CardKind.Dodge, CardKind.DrawTwo];
        public static int DeckCount(string scene) => scene is "lion" or "ox" or "cost-suit" or "target-suit" or "source-loss" or "cost-death" or "obtain-death" ? 32 : 48;
        public void Register(IContentRegistryBuilder b)
        {
            void Real(string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids.Length == 0 ? c.Programs.Keys.ToArray() : ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            }
            Real("le-xiao-qiao", WwQin, WwSkill); if (scene is "cost-suit" or "target-suit") Real("boundary-xiao-qiao", "boundary:hongyan"); if (scene == "dianjun") Real("ol-zhang-yi", "ol:dianjun");
            if (scene == "declaration") Real("classic-yu-ji");
            static JsonNode N(string s) => JsonNode.Parse(s)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards > 0 ? "\"sourceZones\":[\"hand\"]," : "") + "\"effects\":[" + effects + "]}");
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take-all", "{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"taken\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"taken\",\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("place", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}", 1, 1),
                A("hurt", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                A("lose-weiwan", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"le:weiwan\"],\"sourceBind\":\"standard:none\"}"),
                A("ban-slash", "{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"actionTypes\":[\"use\"]}"));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2; acts.Add(duel);
            var place = acts.Single(n => n!["id"]!.GetValue<string>() == "place")!; place["cardCategories"] = N("[\"equipment\"]");
            var defs = new JsonArray(new JsonObject { ["id"] = WwDriver, ["revision"] = 1, ["modifiers"] = N("[{\"id\":\"retain-fixture-inventory\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":64,\"priority\":0}]"), ["activations"] = acts },
                N("{\"id\":\"" + WwQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var own = new List<string> { WwDriver, WwWatch, WwQin, scene is "source-loss" or "cost-death" or "declaration" ? WwGrant : WwSkill };
            if (scene is "source-loss" or "cost-death" or "declaration") defs.Add(N("{\"id\":\"" + WwGrant + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-weiwan\",\"skillId\":\"le:weiwan\",\"minimumHp\":" + (scene == "cost-death" ? 12 : 13) + "}]}"));
            if (scene == "declaration") own.Add("classic:guhuo");
            if (scene is "cost-suit" or "target-suit") own.Add("boundary:hongyan"); if (scene == "dianjun") own.Add("ol:dianjun");
            if (scene == "overlap") { const string insert = "fixture:lxw-insert"; own.Add(insert); defs.Add(N("{\"id\":\"" + insert + "\",\"revision\":1,\"triggers\":[{\"id\":\"fresh-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}")); }
            var watches = new JsonArray();
            if (scene == "declaration") watches.Add(N("{\"id\":\"native-declaration-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"conversion.declaration.pay\"],\"optional\":false,\"effects\":[" + Choice("declaration") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            foreach (var zone in new[] { "hand", "equipment" }) watches.Add(N("{\"id\":\"cost-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone + "\"],\"movementOccurrence\":\"perOwnerBatch\",\"movementReasons\":[\"" + WwDiscard + "\"],\"optional\":false,\"effects\":[" + Choice("cost") + (scene is "source-loss" or "cost-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "cost-death" ? 20 : 1) + "}" : "") + "]}"));
            watches.Add(N("{\"id\":\"actual-obtain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + WwObtain + "\"],\"optional\":false,\"effects\":[" + Choice("obtained") + (scene == "obtain-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "") + "]}"));
            if (scene == "lion") watches.Add(N("{\"id\":\"native-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("recovered") + "]}"));
            defs.Add(new JsonObject { ["id"] = WwWatch, ["revision"] = 1, ["triggers"] = watches });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Real native Weiwan fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var fixtures = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in fixtures.Programs) b.AddSkill(new(id, id, "Native Weiwan children and original entities") { Program = p, ProgramPresentation = fixtures.Presentations[id] });
            const string pick = "fixture:lxw-pick"; b.AddSkill(new(pick, "Owner", "Fixed human candidate") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); b.AddGeneral(new(WwOwner, "Real Weiwan owner", "supporter", pick, "wu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lxw-peer-" + i).ToArray(); for (var i = 1; i <= 3; i++) { var key = "fixture:lxw-peer-pick-" + i; b.AddSkill(new(key, "Other", "Fixed public candidate") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "Real region owner", "supporter", key, "shu", 12, scene == "target-suit" && i == 2 ? [WwQuiet, "boundary:hongyan"] : [WwQuiet])); }
            foreach (var kind in Mixed) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:lxw-" + kind, c.DisplayName, c.CategoryName, "Unmodified printed entity", kind)); }
            var fixedKind = scene == "lion" ? CardKind.SilverLion : scene == "ox" ? CardKind.WoodenOx : CardKind.Crossbow;
            var deck = Enumerable.Range(0, DeckCount(scene)).Select(i => new ContentDeckPhysicalCard("fixture:lxw-" + (DeckCount(scene) == 48 ? Mixed[i / 4] : fixedKind), scene is "cost-suit" or "target-suit" ? Suit.Spade : WwSuits[i % 4], 7)).ToArray();
            b.AddDeck(new("fixture:lxw-deck", "Small complete original inventory", 4, 0, []) { PhysicalCards = deck });
            var roles = scene is "cost-death" or "obtain-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(WwMode, "Real Weiwan atomic region obtains", 4, 4, roles, "fixture:lxw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [WwOwner, .. peers]));
        }
    }
}
