using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeCaiWenJiChecks
{
    private const string CjMark = "le:shuangjia", CjBenefit = "le:beifen", CjOwner = "fixture:lcj-owner", CjMode = "identity:classic-le-cai-wen-ji";
    private const string CjDriver = "fixture:lcj-driver", CjQuiet = "fixture:lcj-quiet", CjWatch = "fixture:lcj-watch", CjConditional = "fixture:lcj-conditional";
    private const string CjObtain = "skill-program.le-cai-wen-ji.beifen.obtain", CjDiscard = "skill-program.fixture:lcj-driver.DiscardSelected";

    public static void ShuangjiaOpeningGlobalEntitiesHandLimitAndIncomingDistanceCold()
    {
        var s = CjScenario.Start("opening"); var old = s.V().Hand.Select(c => c.Id).ToHashSet();
        s.Need(s.P is { Kind: DecisionKind.OpeningHand } && !s.E<ShuangjiaInitializedEvent>().Any(), "Real opening customization precedes all Shuangjia initialization.");
        var replaced = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "replace"); s.Private(0); s.Cold(); s.Answer(c => c.Id == replaced.Id);
        var opening = s.V().Hand.Select(c => c.Id).ToArray(); var incoming = opening.Single(id => !old.Contains(id)); var entry = s.G.CardMovements.Last(m => m.CardId == incoming && m.To == CardLocation.Hand(0));
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "confirm"); s.Reach(() => s.P?.SkillPrompt?.SkillId == CjDriver && s.P.Choices.Any(c => c.Cards.Count == 1));
        var frozen = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.ShuangjiaInitialEntities is not null).ShuangjiaInitialEntities!;
        s.Need(frozen is IList { IsReadOnly: true } && frozen.Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(opening) && !s.E<ShuangjiaInitializedEvent>().Any(), "The mother freezes the actual replaced Hand before an earlier GameStarting child can move one of its entities.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([incoming])); s.Play();
        s.Need(s.E<ShuangjiaInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(opening) &&
            s.E<ShuangjiaInitialEntityMarkedEvent>().Single(e => e.CardId == incoming).EntrySequence == entry.Sequence && !s.E<ShuangjiaInitialEntityMarkedEvent>().Any(e => replaced.Cards.Contains(e.CardId)) &&
            s.G.CardMovements.Any(m => m.CardId == incoming && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile), "Initial membership retains the actual replacement invoice even when an earlier real child discards that frozen entity."); s.Cold();

        s = CjScenario.Start("marks"); s.Need(s.V().Hand.Count == 4 && s.V().Hand.All(CjMarked) && s.G.GetCombatDistance(1, 0) == 5 && s.G.GetCombatDistance(0, 1) == 1,
            "Four marked owned entities add four to incoming distance only, without changing the holder's outgoing distance.");
        var mark = s.V().Hand[0].Id; s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == mark && a.Kind == LegalActionKind.Equip)); s.Play();
        s.Need(s.V().Equipment.Single().Id == mark && s.V().Equipment.All(CjMarked) && s.G.GetCombatDistance(1, 0) == 5, "A real marked equipment entity still contributes to owned HE distance even though it has left the Hand.");
        s.Use("equip-to-hand"); s.Reach(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([mark])) == true); s.Private(0); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([mark])); s.Play(); s.Use("draw-four"); s.Play(); var ordinaryLimit = s.V().HandLimit;
        s.Use("give", [mark], [1]); s.Play(); s.Need(s.G.GetCombatDistance(1, 0) == 4 && s.V(1).Hand.Single(c => c.Id == mark) is var given && CjMarked(given), "A transferred global mark retains its visible membership while the original holder's owned count immediately decreases.");
        s.Take(1, mark); s.Need(s.G.GetCombatDistance(1, 0) == 5 && s.V().HandLimit == ordinaryLimit && s.V().Hand.Count(CjMarked) == 4 && s.E<ShuangjiaInitialEntityMarkedEvent>().Count(e => e.CardId == mark) == 1,
            "Reobtaining the exact entity restores the count without creating another initial mark or changing the public ordinary limit.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play(); s.Need(s.V().HandCount == 8 && !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile), "The real discard phase exempts the four marks while the other four Hand cards fit the ordinary limit."); s.Cold();
        foreach (var scene in new[] { "recipient", "inherited" })
        {
            s = CjScenario.Start(scene); var id = s.V().Hand[0].Id; s.Use("give", [id], [1]); s.Play(); s.End(); s.Play(); var peerTurn = s.E<TurnStartedEvent>().First(e => e.ActorSeat == 1).TurnNumber;
            var discarded = s.G.CardMovements.Count(m => m.TurnNumber == peerTurn && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile);
            s.Need(scene == "inherited" ? discarded == 0 && s.V(1).HandCount == 5 : discarded == 2 && s.V(1).HandCount == 3,
                "A current Shuangjia holder inherits exemption for a global marked entity; an unskilled holder retains the label but must really discard to its ordinary limit."); s.Cold();
        }
        s = CjScenario.Start("cap"); foreach (var id in s.V(1).Hand.Take(2).Select(c => c.Id).ToArray()) s.Take(1, id);
        s.Need(s.V().Hand.Count(CjMarked) == 6 && s.G.GetCombatDistance(1, 0) == 6 && s.G.GetCombatDistance(0, 1) == 3, "Six marked owned entities cap the incoming contribution at five; the other holder's remaining two marks independently affect the reverse direction."); s.Cold();
        s = CjScenario.Start("ox"); var ox = s.V().Hand[0].Id; s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == ox && a.Kind == LegalActionKind.Equip)); s.Play(); var grain = s.V().Hand[0].Id;
        s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.V().WoodenOxGrain!.Single().Id == grain && s.G.GetCombatDistance(1, 0) == 4 && Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).Players[0].WoodenOxGrain?.Count ?? 0) == 0), "A real marked Ox grain is outside HE and does not increase distance or reveal its private face."); s.Cold();
    }

    public static void BeifenWholeMarkedLossMissingSuitObtainAndNativeChildrenCold()
    {
        var s = CjScenario.Start("batch"); var lost = s.V().Hand.Select(c => c.Id).ToArray(); s.Use("discard-four", lost); s.Reach(() => s.Hold("obtained"));
        var started = s.E<BeifenStartedEvent>().Single(); var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId);
        s.Need(started is { MarkedHandCount: 0, MissingSuitCount: 4 } && frame.Beifen is { Stage: BeifenStage.ObtainChildren, Search: IList { IsReadOnly: true } } && !s.E<BeifenCompletedEvent>().Any() &&
            s.E<BeifenLossQualifiedEvent>().Where(e => e.Source == started.Source && e.BatchId == started.LossBatchId).Select(e => e.CardId).ToHashSet().SetEquals(lost), "An original atomic loss of four marks qualifies one source/batch and freezes one readonly four-suit search before its actual obtain children return.");
        s.Private(0); s.RejectPrompt(); var old = s.P!; s.Cold(); s.Continue(); s.RejectOld(old); s.Play();
        var gained = s.E<BeifenObtainedEvent>().Where(e => e.FrameId == started.FrameId).ToArray();
        s.Need(gained.Length == 4 && gained.Select(e => e.Suit).ToHashSet().SetEquals(new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }) && gained.All(e => s.G.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == CjObtain) == 1) &&
            !gained.Any(e => s.V().Hand.Single(c => c.Id == e.CardId) is var c && CjMarked(c)) && s.E<BeifenCompletedEvent>().Single() is { ObtainedCount: 4 } && !s.E<NativeDrawInvocationRecordedEvent>().Any(e => e.OwnerSeat == 0),
            "The missing suits produce four actual deck obtains, one entity per suit, without drawing, fabricating new marks, or splitting the original loss into four activations."); s.Cold();
        s = CjScenario.Start("empty"); s.Use("discard-four", s.V().Hand.Select(c => c.Id).ToArray()); s.Play();
        s.Need(s.E<BeifenStartedEvent>().Single() is { MissingSuitCount: 4 } && s.E<BeifenSuitSearchedEvent>().Length == 4 && s.E<BeifenSuitSearchedEvent>().All(e => e.CardId is null) && !s.E<BeifenObtainedEvent>().Any() && s.E<BeifenCompletedEvent>().Single().ObtainedCount == 0 &&
            s.G.CreateCardZoneDiagnostics().Count(c => c.Location == CardLocation.DiscardPile) == 4 && !s.G.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.DrawPile), "An exhausted draw pile skips unavailable suits and completes with zero obtains, preserving the discard pile without a reshuffle."); s.Cold();
        foreach (var scene in new[] { "use", "dodge" })
        {
            s = CjScenario.Start(scene); var id = s.V().Hand[0].Id;
            if (scene == "use") s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])));
            else { s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Private(0); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([id]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == CjDriver); }
            s.Play(); var losses = s.E<BeifenLossQualifiedEvent>().Where(e => e.CardId == id).ToArray();
            s.Need(losses is [var loss] && loss.From == CardLocation.Hand(0) && loss.To == CardLocation.Processing && s.E<BeifenStartedEvent>().Count(e => e.LossBatchId == loss.BatchId) == 1 && s.E<BeifenCompletedEvent>().Length == 1 &&
                s.E<BeifenStartedEvent>().Single() is { MarkedHandCount: 3, MissingSuitCount: 3 } && s.E<BeifenSuitSearchedEvent>().Select(e => e.Suit).ToHashSet().SetEquals(new[] { Suit.Spade, Suit.Club, Suit.Diamond }) && !s.E<BeifenObtainedEvent>().Any() &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                (scene == "use" ? s.E<CardUseFinishedEvent>().Count(e => e.CardId == id && e.CardKind == CardKind.Slash) == 1 : s.E<CardRespondedEvent>().Count(e => e.CardId == id && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge) == 1),
                "A real Slash or true Dodge Hand-to-Processing payment loses the marked entity once; its later native Processing retirement does not qualify a second HE loss."); s.Cold();
        }
        s = CjScenario.Start("owned-move"); var owned = s.V().Hand[0].Id; s.Use("place-owned", [owned], [0]); s.Play();
        s.Need(s.V().Equipment.Single().Id == owned && !s.E<BeifenLossQualifiedEvent>().Any(), "A real direct Hand-to-own-Equipment placement is still owned HE and creates no marked loss.");
        s.Use("equip-to-hand"); s.Reach(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([owned])) == true); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([owned])); s.Play();
        s.Need(CjMarked(s.V().Hand.Single(c => c.Id == owned)) && !s.E<BeifenStartedEvent>().Any() && s.G.CardMovements.Count(m => m.CardId == owned && m.From.OwnerSeat == 0 && m.To.OwnerSeat == 0 && m.From.Zone != m.To.Zone) == 2, "The reverse true Equipment-to-own-Hand return also preserves ownership and cannot create a second benefit."); s.Cold();
    }

    public static void BeifenAllCardUseDistanceAndNaturalLimitsKeepOtherBansCold()
    {
        var s = CjScenario.Start("ratio"); s.Use("draw-four"); s.Play();
        s.Need(s.V().Hand.Count(CjMarked) == 4 && s.V().Hand.Count == 8 && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "Equal marked and other Hand counts do not receive the strict less-than benefit.");
        s.Use("draw-one"); s.Play(); var far = s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) && !CjMarked(s.Card(a.CardId!.Value))); s.RejectNativeSource(far); s.Cold(); s.Native(far); s.Play();
        var accepted = s.E<BeifenUseAcceptedEvent>().Single();
        s.Need(accepted is { MarkedHandCount: 4, OtherHandCount: 5, OriginalKind: CardKind.Slash } && s.V().Hand.Count(CjMarked) == 4 && s.V().Hand.Count == 8 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == accepted.UseId && e.CardKind == CardKind.Slash) == 1 &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The exact accepted distant Use retains its 4-to-5 admission after the unmarked payment changes the live ratio to equality; a new Use cannot borrow that old right."); s.Cold();

        s = CjScenario.Start("declaration"); s.Use("draw-four"); s.Play(); s.Use("draw-one"); s.Play();
        var declared = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) &&
            a.ConversionSource is { SkillId: "classic:guhuo", BindingId: "declare-slash" } && !CjMarked(s.Card(a.CardId!.Value)));
        s.Native(declared); s.Reach(() => s.Hold("declaration")); var declaration = s.G.ResolutionStack.OfType<CardDeclarationFrame>().Single();
        var qualification = s.E<BeifenDeclarationQualifiedEvent>().Single();
        s.Need(declaration.Payment.Beifen is { MarkedHandCount: 4, OtherHandCount: 5 } admission && admission == qualification.Qualification &&
            qualification.DeclarationId == declaration.Id && qualification.OwnerFrameId == declaration.Id && qualification.CardId == declared.CardId && qualification.From == CardLocation.Hand(0) &&
            declaration.Payment.Cost.CardId == declared.CardId && declaration.Payment.Cost.From == CardLocation.Hand(0) && declaration.TargetSeats.SequenceEqual([2]) && !declaration.IsRevealed &&
            s.V().Hand.Count(CjMarked) == 4 && s.V().HandCount == 8 && !s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0) &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1,
            "A genuine published Guhuo declaration prepays one unmarked Hand entity while freezing the original 4-to-5 benefit and distant target before any native Use exists.");
        s.Private(0); s.RejectPrompt(); var originalPrompt = s.P!; s.Cold(); s.Continue(); s.RejectOld(originalPrompt); s.Play();
        accepted = s.E<BeifenUseAcceptedEvent>().Single();
        s.Need(!s.V().Skills!.Any(k => k.ContentId == CjBenefit) && accepted.DeclarationId == declaration.Id && accepted.Source == qualification.Qualification.Source &&
            accepted is { MarkedHandCount: 4, OtherHandCount: 5, OriginalKind: CardKind.Slash } && s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == accepted.UseId && e.SourceSeat == 0 && e.CardKind == CardKind.Slash) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == accepted.UseId && e.CardKind == CardKind.Slash) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == declared.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "The real declaration challenge and native dispatch retain their original prepaid distance after ratio equality and true skill loss, finish once without charging the entity twice, and issue no new distance right."); s.Cold();

        s = CjScenario.Start("late"); s.Use("stock"); s.Play(); s.Use("stock"); s.Play(); s.Use("draw-one"); s.Play();
        s.Need(!s.E<ShuangjiaInitializedEvent>().Any() && !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "A real late acquisition scene has no initialization or marks and receives no distance benefit before holding Beifen.");
        s.Use("grant-beifen"); s.Play(); s.Need(s.V().Hand.Count(CjMarked) == 0 && s.V().HandCount > 0 && s.G.GetCombatDistance(0, 2) == 2, "The hero's numerical zero-mark condition applies after late acquisition without inventing an ever-initialized prerequisite.");
        s.Use("give", [s.V().Hand.First(c => c.Kind == CardKind.SupplyShortage).Id], [2]); s.Play();
        for (var i = 0; i < 2; i++) { s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]))); s.Play(); }
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash) == 2 && s.V().Equipment.Count == 0, "Two genuine distant natural Slashes execute in one Play without a Crossbow or a disguised conversion.");
        for (var i = 0; i < 2; i++) { s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Alcohol)); s.Play(); s.Native(s.Actions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]))); s.Play(); }
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.Alcohol) == 2 && !s.V().HasAlcoholEffect, "Both natural Alcohol uses complete, with real Slashes consuming each effect rather than bypassing Alcohol's intrinsic active-effect restriction.");
        var snatch = s.Actions().First(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([2])); s.Native(snatch); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); s.Private(0); s.Cold(); s.Answer(c => c.Parameters.ContainsKey("slot-index")); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.CardId == snatch.CardId && e.CardKind == CardKind.Snatch) == 1 && s.E<TargetCardTakenEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.FromZone == CardZoneKind.Hand && e.PublicCardId is null) == 1,
            "A natural distant Snatch completes its true anonymous target-card obtain rather than merely appearing in the menu.");
        s.Need(s.Actions().Any(a => a.Kind == LegalActionKind.SupplyShortage && a.TargetSeats.SequenceEqual([2])), "The all-card distance benefit also reaches the native delayed-card family.");
        s.Use("lose-beifen"); s.Play(); s.Need(s.V().Hand.Any(c => c.Kind == CardKind.Slash) && s.V().Hand.Any(c => c.Kind == CardKind.Alcohol) && !s.Actions().Any(a => a.Kind is LegalActionKind.Slash or LegalActionKind.Alcohol), "Native natural counters are still recorded: removing Beifen restores the already-spent Slash and Alcohol limits despite remaining real materials.");
        s.Use("grant-beifen"); s.Play(); s.Use("ban-slash"); s.Play();
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash) && s.Actions().Any(a => a.Kind is LegalActionKind.Snatch or LegalActionKind.SupplyShortage), "Unlimited range and normal counts preserve an exact turn Use prohibition while unrelated legal cards remain available."); s.Cold();
    }

    public static void BeifenIssuedLossSourceRightsPrivacyAndReplayCold()
    {
        foreach (var scene in new[] { "issued-loss", "issued-death", "paid-death" })
        {
            var s = CjScenario.Start(scene); var lost = s.V().Hand.Select(c => c.Id).ToArray(); s.Use("discard-four", lost);
            if (scene != "paid-death")
            {
                s.Reach(() => s.Hold("before-loss")); var q = s.E<BeifenLossQualifiedEvent>().ToArray();
                s.Need(q.Length == 4 && q.Select(e => e.CardId).ToHashSet().SetEquals(lost) && q.Select(e => e.BatchId).Distinct().Count() == 1 && !s.E<BeifenStartedEvent>().Any(), "The original marked loss qualifies its exact source and four invoices before a higher-priority same-batch child runs.");
                s.Private(0); s.RejectPrompt(); s.Cold(); s.Continue();
                if (scene == "issued-death")
                {
                    s.Reach(() => !s.V().IsAlive && s.G.ResolutionStack.Count == 0); s.Need(!s.E<BeifenStartedEvent>().Any() && !s.E<BeifenObtainedEvent>().Any() && !s.E<BeifenCompletedEvent>().Any(), "Death before the locked benefit starts preserves history but creates no new obtains or fictitious successful activation."); s.Cold(); continue;
                }
                s.Reach(() => s.Hold("after-loss")); s.Need(!s.V().Skills!.Any(k => k.ContentId == CjBenefit) && !s.E<BeifenStartedEvent>().Any() && s.E<BeifenLossQualifiedEvent>().SequenceEqual(q), "The real HP conditional source is lost before activation while its original issued qualification remains unchanged."); s.Private(0); s.Cold(); s.Continue();
            }
            s.Reach(() => s.Hold("obtained")); var started = s.E<BeifenStartedEvent>().Single();
            var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId);
            s.Need(parent.Beifen is { Stage: BeifenStage.ObtainChildren, Search: IList { IsReadOnly: true } } && s.E<BeifenObtainedEvent>().Count(e => e.FrameId == started.FrameId) == 4 && !s.E<BeifenCompletedEvent>().Any(), "The original owning frame retains its detached search and four genuine obtained entities while the paid movement child is suspended.");
            var old = s.P!; s.Private(0); s.RejectPrompt(); s.Cold(); s.Continue(); s.RejectOld(old);
            s.Reach(() => s.E<BeifenCompletedEvent>().Any()); s.PlayOrDead();
            s.Need(s.E<BeifenStartedEvent>().Length == 1 && s.E<BeifenCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.LossBatchId == started.LossBatchId && e.ObtainedCount == 4) == 1 &&
                s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == started.FrameId && e.SkillId == CjBenefit && e.BindingId == started.Source.BindingId && e.SkillInstanceId == started.Source.SkillInstanceId && e.Activated && e.Completed) == 1 &&
                (scene == "paid-death" ? !s.V().IsAlive : s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == CjBenefit)), "An issued source-lost benefit or an already-paid benefit whose owner dies drains its original children and completes once without reacquiring the source or repeating obtains."); s.Cold();
        }
    }

    private static bool CjMarked(CardSnapshot c) => c.SkillMarkLabel?.Contains("胡笳", StringComparison.Ordinal) == true;
    private sealed class CjScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardSnapshot Card(int id) => Enumerable.Range(0, 4).SelectMany(i => V(i).Hand.Concat(V(i).Equipment)).First(c => c.Id == id);
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.ConversionSource is null).ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == CjWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, CjDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public PlayCardCommand Command(LegalAction a) => new(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources };
        public void Native(LegalAction a) => Accept(Command(a));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The actual human selects only a published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Take(int seat, int id) { var index = V(seat).Hand.ToList().FindIndex(c => c.Id == id); Use("take", targets: [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Need(P!.Choices.Where(c => c.Parameters.ContainsKey("slot-index")).All(c => c.Cards.Count == 0), "A real foreign Hand selection publishes opaque slots only."); Private(0); Cold(); Answer(c => c.Parameters.GetValueOrDefault("slot-index") == index.ToString(System.Globalization.CultureInfo.InvariantCulture)); Play(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayOrDead() => Reach(() => !V().IsAlive || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Private(int seat) => Need(P?.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the real chooser sees the suspended private choices.");
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++) { if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal before native boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == CjWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic()); }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand command) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + " " + JsonSerializer.Serialize(r.Error)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " " + e.Message + " " + Diagnostic(), e); } }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves global entities, exact loss invoices, accepted Use admission and every native child cursor."); }
        public void RejectPrompt() { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-beifen-child"), G.Revision)).Accepted && State() == before, "Foreign or unpublished child input cannot mutate issued source rights or payment."); }
        public void RejectOld(PendingDecision p) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before, "A retired private prompt cannot repeat its loss or gain."); }
        public void RejectNativeSource(LegalAction action) { var before = State(); var forged = Command(action) with { ConversionSource = new(CjBenefit, "not-a-view-as", 0, "forged-beifen-source") }; Need(!G.Submit(forged).Accepted && before == State(), "The passive benefit never becomes a forged view-as binding or substitutes another source for the actual native material."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(4) }, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount, Marks = V(i).Hand.Count(CjMarked) }), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Losses = E<BeifenLossQualifiedEvent>().TakeLast(8), Started = E<BeifenStartedEvent>().TakeLast(4), Gains = E<BeifenObtainedEvent>().TakeLast(8), Completed = E<BeifenCompletedEvent>().TakeLast(4), Accepted = E<BeifenUseAcceptedEvent>().TakeLast(6) });
        public static CjScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new CjFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene is "issued-death" or "paid-death" ? Role.Renegade : Role.Lord, ModeId = CjMode, UseInteractiveSetup = true, ConfirmOpeningHand = scene == "opening", UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new CjScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, CjOwner, g.Revision, s.P!.PromptId)); if (scene == "opening") s.Reach(() => s.P?.Kind == DecisionKind.OpeningHand); else s.Play(); return s;
        }
    }

    private sealed class CjFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-cai-wen-ji", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix, string bundle = "le-cai-wen-ji") { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual native content " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in new[] { CjMark, CjBenefit }) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            if (scene == "declaration") { var guhuo = SkillProgramCatalog.Load(Read("rules", "classic-yu-ji"), Read("presentation", "classic-yu-ji")); foreach (var (id, p) in guhuo.Programs) b.AddSkill(new(id, guhuo.Presentations[id].Name, guhuo.Presentations[id].Description) { Program = p, ProgramPresentation = guhuo.Presentations[id] }); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\",\"equipment\"],") + "\"effects\":[" + effects + "]}");
            static string Choice(string id, string bind = "seen") => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + bind + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("draw-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"), A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1), A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("equip-to-hand", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}"),
                A("discard-four", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":4}", 4), A("discard-one", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", 1),
                A("grant-beifen", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"le:beifen\"]}"), A("lose-beifen", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"le:beifen\"],\"sourceBind\":\"standard:none\"}"),
                A("ban-slash", "{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"actionTypes\":[\"use\"]}"), A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1));
            var driver = new JsonObject { ["id"] = CjDriver, ["revision"] = 1, ["activations"] = acts };
            var place = A("place-owned", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}", 1, 1); place["sourceZones"] = N("[\"hand\"]"); place["cardCategories"] = N("[\"equipment\"]"); acts.Add(place);
            if (scene == "opening") driver["triggers"] = N("[{\"id\":\"earlier-opening-move\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"priority\":100,\"optional\":false,\"effects\":[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}]");
            if (scene == "dodge") driver["viewAs"] = N("[{\"id\":\"native-dodge\",\"inputKinds\":[\"slash\"],\"inputSuits\":[],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true}]");
            var defs = new JsonArray(driver, N($$"""{"id":"{{CjQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            if (scene == "dodge") defs.Add(N("{\"id\":\"fixture:lcj-reach\",\"revision\":1,\"modifiers\":[{\"id\":\"real-incoming-weapon-range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":8,\"priority\":0}]}"));
            var marksOnly = scene is "opening" or "marks" or "recipient" or "inherited" or "cap" or "ox";
            var own = new List<string> { CjDriver }; if (scene != "late") own.Add(CjMark); if (!marksOnly && scene != "late") own.Add(scene is "issued-loss" or "issued-death" or "declaration" ? CjConditional : CjBenefit);
            if (scene == "declaration") own.Add("classic:guhuo");
            if (scene is "issued-loss" or "issued-death" or "declaration") defs.Add(N($$"""{"id":"{{CjConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-beifen","skillId":"{{CjBenefit}}","minimumHp":{{(scene == "issued-death" ? 12 : 13)}}}]}"""));
            var watch = new JsonArray();
            if (scene == "declaration") watch.Add(N("{\"id\":\"true-prepaid-declaration\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"conversion.declaration.pay\"],\"optional\":false,\"effects\":[" + Choice("declaration") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (scene is "batch" or "issued-loss" or "paid-death") watch.Add(N("{\"id\":\"actual-obtain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + CjObtain + "\"],\"optional\":false,\"effects\":[" + Choice("obtained") + (scene == "paid-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "") + "]}"));
            if (scene is "issued-loss" or "issued-death") watch.Add(N("{\"id\":\"actual-original-loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + CjDiscard + "\"],\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choice("before-loss") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "issued-death" ? 20 : 1) + "}" + (scene == "issued-loss" ? "," + Choice("after-loss", "after") : "") + "]}"));
            if (watch.Count > 0) { own.Add(CjWatch); defs.Add(new JsonObject { ["id"] = CjWatch, ["revision"] = 1, ["triggers"] = watch }); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实乐蔡文姬原生夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in fixture.Programs) b.AddSkill(new(id, id, "真实乐蔡文姬边界") { Program = p, ProgramPresentation = fixture.Presentations[id] });
            const string pick = "fixture:lcj-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); var hp = marksOnly ? 3 : 12; b.AddGeneral(new(CjOwner, "真实乐蔡文姬本人", "supporter", pick, "qun", hp, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lcj-peer-" + i).ToArray(); for (var i = 1; i <= 3; i++) { var pp = "fixture:lcj-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pp, "shu", hp, i == 1 && (scene is "inherited" or "cap") ? [CjQuiet, CjMark] : i == 1 && scene == "dodge" ? [CjQuiet, "fixture:lcj-reach"] : [CjQuiet])); }
            const string physical = "fixture:lcj-physical"; var kind = scene == "ox" ? CardKind.WoodenOx : scene is "ratio" or "use" or "dodge" or "declaration" ? CardKind.Slash : CardKind.Crossbow; var card = CardCatalog.Get(kind); b.AddCard(new(physical, card.DisplayName, card.CategoryName, "真实小牌堆原生实体", kind));
            string[] mixed = ["standard:slash", "standard:alcohol", "standard:snatch", "standard:supply_shortage"]; string[] openingKinds = ["standard:slash", "standard:dodge", "standard:peach", "standard:duel", "standard:crossbow"]; var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond };
            var fourSuits = scene is "batch" or "issued-loss" or "issued-death" or "paid-death";
            var cards = Enumerable.Range(0, scene == "empty" ? 16 : scene == "late" ? 48 : fourSuits ? 80 : 64).Select(i => new ContentDeckPhysicalCard(scene == "opening" ? openingKinds[i % openingKinds.Length] : scene == "late" ? mixed[i % mixed.Length] : physical, fourSuits ? suits[i % 4] : Suit.Heart, 7)).ToArray();
            b.AddDeck(new("fixture:lcj-deck", "真实胡笳原实体牌堆", scene == "late" ? 0 : 4, 0, []) { PhysicalCards = cards });
            var roles = scene is "issued-death" or "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(CjMode, "真实乐蔡文姬原生边界", 4, 4, roles, "fixture:lcj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [CjOwner, .. peers]));
        }
    }
}
