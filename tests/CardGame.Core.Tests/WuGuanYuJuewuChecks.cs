using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class WuGuanYuJuewuChecks
{
    private const string JwSkill = "wumiao:juewu", JwOwner = "fixture:jw-owner", JwDriver = "fixture:jw-driver";
    private const string JwMode = "identity:classic-wumiao-juewu", JwQuiet = "fixture:jw-quiet", JwWatch = "fixture:jw-watch";
    private const string JwConditional = "fixture:jw-conditional", JwNeutral = "fixture:jw-neutral", JwForced = "fixture:jw-forced";
    private const string JwZenrun = "ol:zenrun", JwObtain = "skill-program.sun-hong.zenrun.obtain", JwCompensation = "skill-program.sun-hong.zenrun.compensation";

    public static void JuewuAllDamageNamesUseOriginalEntitiesAndNativeQuotaCold()
    {
        var outputs = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
            CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack, CardKind.WumiaoFlood };
        foreach (var kind in outputs)
        {
            var s = JwScenario.Start("pool-" + kind); s.Use("draw-four"); s.Play();
            var targets = kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage ? new[] { 1, 2, 3 } : kind == CardKind.WumiaoFlood ? new[] { 1, 2 } : new[] { 1 };
            var action = s.Convert(kind, targets); var original = s.Card(action.CardId!.Value);
            s.Need(original.Kind == CardKind.Crossbow && original.Rank == 2 && action.ConversionSource!.BindingId == JwBinding(kind),
                "Every complete damage output uses a published exact rank-two entity and its own binding.");
            s.RejectSource(action); s.Native(action); s.Reach(() => s.Hold("committed")); s.Private(0); s.Cold();
            var issued = s.E<JuewuUseIssuedEvent>().Single(); var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
            s.Need(issued.OutputKind == kind && issued.NativeKind == kind && issued.PrintedKind == CardKind.Crossbow && issued.PrintedRank == 2 &&
                issued.CardId == original.Id && issued.From == CardLocation.Hand(0) && issued.ForeignHandEntrySequence is null &&
                use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveRank: 2 } &&
                use.Action.EffectiveKind == kind && use.Action.EffectiveSuit == original.Suit && use.Action.PhysicalCards.Single().CardId == original.Id &&
                s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == issued.UseId && e.CardId == original.Id) == 1 &&
                !s.E<JuewuUseFinishedEvent>().Any(),
                "The real committed native use retains its effective face, original printed cost, actor/provider and once-paid invoice before effect children.");
            s.Continue(); s.Play();
            s.Need(s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == issued.UseId && e.OutputKind == kind) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == kind) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                s.Conversions().All(a => a.ConversionSource!.BindingId != JwBinding(kind)),
                "Each damage name resolves through its actual native effect and retires its one material once; the same name cannot be used again this actual turn.");
            if (kind == CardKind.WumiaoFlood)
            {
                var effects = s.E<WumiaoFloodEffectStartedEvent>().Where(e => e.UseId == issued.UseId).ToArray();
                var discard = s.E<WumiaoFloodDiscardPaidEvent>().Single(e => e.UseId == issued.UseId);
                var draw = s.E<WumiaoFloodDrawReturnedEvent>().Single(e => e.UseId == issued.UseId);
                s.Need(effects.Select(e => (e.TargetIndex, e.TargetSeat)).SequenceEqual(new[] { (0, 1), (1, 2) }) &&
                    discard.TargetSeat == 1 && draw is { RecipientSeat: 2, ActualCount: 1 } &&
                    s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Thunder && (e.TargetSeat == 1 || e.TargetSeat == 2)) == 2 &&
                    s.G.CardMovements.Count(m => m.Sequence == discard.MovementSequence && m.CardId == discard.CardId && m.To == CardLocation.DiscardPile && m.Reason.Value == "card.effect.wumiao-flood.discard") == 1,
                    "Water follows selected ordinal zero then one: each receives thunder damage, the first really discards and the second performs one real draw within one whole Use.");
            }
            s.Cold();
        }

        var q = JwScenario.Start("quota"); q.Use("draw-four"); q.Play();
        q.Native(q.Convert(CardKind.Slash, [1])); q.Play();
        q.Need(!q.Conversions().Any(a => a.PlayedCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash),
            "Unused elemental names do not bypass the ordinary Slash allowance after a real first Slash.");
        var crossbow = q.V().Hand[0].Id; q.Native(q.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId == crossbow && a.ConversionSource is null)); q.Play();
        q.Native(q.Convert(CardKind.FireSlash, [1])); q.Play(); q.Native(q.Convert(CardKind.ThunderSlash, [1])); q.Play();
        q.Need(q.E<JuewuUseIssuedEvent>().Select(e => e.OutputKind).ToHashSet().SetEquals(new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }) &&
            q.Conversions().All(a => a.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)),
            "A real equipped Crossbow permits further native Slash uses while each of the three independent Juewu output names is consumed once.");
        var actualTurn = q.G.State.TurnNumber; q.Use("lose-juewu"); q.Play(); q.Use("grant-juewu"); q.Play();
        q.Need(q.Conversions().All(a => a.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)),
            "A genuinely lost and newly granted source cannot refresh the same skill/name actual-turn ledger.");
        q.End(); q.Reach(() => q.G.State.TurnNumber != actualTurn && q.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        q.Need(q.Conversions().Any(a => a.PlayedCardKind == CardKind.Slash), "A new real actual turn restores the independent name allowance."); q.Cold();
    }

    public static void JuewuForeignHandRankResidencyAppearanceAndPindianCold()
    {
        var s = JwScenario.Start("foreign");
        s.Need(s.V().Hand.All(c => c.Rank == 7) && s.Conversions().Length == 0, "Printed-rank-seven opening cards have no foreign Hand entitlement.");
        var foreign = s.V(1).Hand[0]; s.Take(1, foreign.Id); s.Play();
        s.Need(s.V().Hand.Single(c => c.Id == foreign.Id).Rank == 2 && s.Conversions().Any(a => a.CardId == foreign.Id) &&
            Enumerable.Range(1, 3).All(v => s.G.CreateSnapshot(v).Players[0].Hand.Count == 0),
            "A genuine opaque foreign Hand acquisition changes only its current owner's effective rank; other viewers receive no private faces."); s.Cold();
        s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == foreign.Id && a.Kind == LegalActionKind.Equip && a.ConversionSource is null)); s.Play();
        s.Need(s.V().Equipment.Single(c => c.Id == foreign.Id).Rank == 7 && s.Conversions().All(a => a.CardId != foreign.Id),
            "Leaving Hand immediately ends the foreign-entry rank identity; the actual printed-rank-seven equipped entity is not a rank-two material.");
        s.Use("equipment-back"); s.Reach(() => s.P?.Choices.Any(c => c.Cards.Contains(foreign.Id)) == true); s.Answer(c => c.Cards.Contains(foreign.Id)); s.Play();
        s.Need(s.V().Hand.Single(c => c.Id == foreign.Id).Rank == 7 && s.Conversions().All(a => a.CardId != foreign.Id),
            "A real own Equipment to Hand entry cannot resurrect an older foreign Hand residency.");
        s.Use("give", [foreign.Id], [1]); s.Play(); s.Take(1, foreign.Id); s.Play(); s.Cold();
        s.Need(s.V().Hand.Single(c => c.Id == foreign.Id).Rank == 2, "A new genuine foreign entry grants rank two again to the same printed entity.");
        s.Use("contest", [foreign.Id], [1]); s.Reach(() => s.G.ResolutionStack.OfType<PindianFrame>().Any());
        var contest = s.G.ResolutionStack.OfType<PindianFrame>().Single(); s.Need(s.G.CreateSnapshot(1).PendingDecision is { IsPrivate: true } && s.G.CreateSnapshot(2).PendingDecision is null,
            "The native opponent commits its original private Pindian card without exposing either Hand to unrelated viewers."); s.Cold();
        s.Play(); var result = s.E<PindianResultDeterminedEvent>().Single(e => e.FrameId == contest.Id).Result;
        s.Need(result.SourceCardId == foreign.Id && result.SourceRank == 2 && result.OpponentRank == 7 && !result.SourceWon && s.E<JuewuUseIssuedEvent>().Length == 0 &&
            s.G.CardMovements.Count(m => m.CardId == foreign.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1,
            "Real Pindian captures the passive effective rank before the foreign card leaves Hand; it does not spend a conversion-name allowance."); s.Cold();

        s = JwScenario.Start("foreign"); foreign = s.V(1).Hand[0]; s.Take(1, foreign.Id); s.Play();
        s.Use("lose-juewu"); s.Play(); s.Need(s.V().Hand.Single(c => c.Id == foreign.Id).Rank == 7 && s.Conversions().Length == 0,
            "The passive rank requires a current real source, even while the exact foreign residency remains.");
        s.Use("grant-juewu"); s.Play(); var selectedUse = s.Conversions().First(a => a.CardId == foreign.Id && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1]));
        s.Native(selectedUse); s.Reach(() => s.Hold("committed")); var invoice = s.E<JuewuUseIssuedEvent>().Single();
        s.Need(invoice.PrintedRank == 7 && invoice.ForeignHandEntrySequence is not null && invoice.CardId == foreign.Id &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == invoice.UseId).Action!.EffectiveRank == 2,
            "A regranted source reads the latest real Hand entry and freezes printed seven versus effective two on the accepted native action."); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<JuewuUseFinishedEvent>().Length == 1, "The original foreign entity finishes its native converted Use once.");

        foreach (var group in new[] { false, true })
        {
            s = JwScenario.Start(group ? "foreign-policy-group" : "foreign-policy-pair");
            var ownOpening = s.V().Hand.ToArray(); var sourceCard = s.V(1).Hand[0];
            var opponents = group ? new[] { 1, 2 } : new[] { 1 };
            foreach (var peer in opponents)
            {
                foreach (var held in s.V(peer).Hand.Select(c => c.Id).ToArray()) { s.Take(peer, held); s.Play(); }
                s.Use("give", [ownOpening[peer - 1].Id], [peer]); s.Play();
            }
            s.Need(sourceCard is { Rank: 7, Suit: Suit.Heart } && s.V().Hand.Single(c => c.Id == sourceCard.Id).Rank == 2 &&
                opponents.All(peer => s.V(peer).Hand is [{ Rank: 2, Suit: Suit.Heart }]),
                "Real foreign entries make both the original printed-seven source and each opponent's sole Hand entity rank two before the unchanged Tianbian policy applies."); s.Cold();
            if (group)
                s.Accept(new UseProgramSkillCommand(0, "ol:gushe", "simultaneous-pindian", [sourceCard.Id], opponents, s.G.Revision, s.P!.PromptId));
            else s.Use("contest", [sourceCard.Id], opponents);
            s.Reach(() => s.P is { PlayerSeat: 1 } && (group
                ? s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SimultaneousPindian is { Stage: SimultaneousPindianStage.Selecting })
                : s.G.ResolutionStack.OfType<PindianFrame>().Any()));
            s.Private(1); s.Cold();
            s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault(group ? "branch" : "action") == (group ? "claim" : "pindian-claim")));
            var comparisonId = group ? s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SimultaneousPindian is not null).Id : s.G.ResolutionStack.OfType<PindianFrame>().Single().Id;
            var first = s.E<PindianResultDeterminedEvent>().First(e => e.FrameId == comparisonId).Result;
            s.Need(first.SourceCardId == sourceCard.Id && first.SourceRank == 13 && first.OpponentRank == 13 && first.OpponentCardId == ownOpening[0].Id &&
                s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == sourceCard.Id).Location == CardLocation.Processing,
                "The real paid contest freezes the complete existing Heart-is-K policy for both foreign-Hand participants rather than bypassing it with naked rank two.");
            if (!group) s.Need(s.G.CreateSnapshot(3).PublicRevealedCards.Where(c => c.Id == sourceCard.Id || c.Id == ownOpening[0].Id).Count(c => c.Rank == 7 && c.Kind == CardKind.Crossbow) == 2,
                "Publicly revealed original physical faces remain printed-seven while the ordinary pair's logical comparison is thirteen versus thirteen.");
            s.Private(0); s.Cold();
            s.Answer(c => group ? c.Parameters.GetValueOrDefault("branch") == "claim" : c.Parameters.GetValueOrDefault("action") == "pindian-claim" && c.Parameters.GetValueOrDefault("take") == "true"); s.Play();
            var settled = s.E<PindianResultDeterminedEvent>().Where(e => e.FrameId == comparisonId).ToArray();
            s.Need(settled.Length == opponents.Length && settled.All(e => e.Result.SourceCardId == sourceCard.Id && e.Result.SourceRank == 13 && e.Result.OpponentRank == 13 && !e.Result.SourceWon) &&
                opponents.All(peer => settled.Single(e => e.Result.OpponentSeat == peer).Result.OpponentCardId == ownOpening[peer - 1].Id) &&
                s.V().Hand.Single(c => c.Id == sourceCard.Id).Rank == 7 &&
                s.G.CardMovements.Count(m => m.CardId == sourceCard.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == sourceCard.Id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1 &&
                s.E<JuewuUseIssuedEvent>().Length == 0,
                "Regular and true whole-group Pindian use the same full policy rank; cold claims return the same printed-seven entity once, end its previous foreign residency, and never consume a Juewu Use allowance."); s.Cold();
        }

        s = JwScenario.Start("rank-zhangba"); s.Use("draw-four"); s.Play();
        var raw = s.V().Hand.ToArray(); s.Need(raw.Length == 4 && raw.All(c => c is { Kind: CardKind.Duel, Rank: 7 }) &&
            s.V().Equipment is [{ Kind: CardKind.ZhangbaSerpentSpear }], "A real GameStarting native equipment producer installs the deck's sole Spear; the original four material faces are printed-seven Duels.");
        foreach (var card in raw) { s.Use("give", [card.Id], [1]); s.Play(); }
        foreach (var card in raw) { s.Take(1, card.Id); s.Play(); }
        var pair = raw.Take(2).Select(c => c.Id).ToArray();
        var spear = s.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.ZhangbaSerpentSpear);
        s.Need(s.V().Hand.Count == 4 && s.V().Hand.All(c => c.Rank == 2) && s.G.GetAttackRange(0) == 3 &&
            s.G.GetCombatDistance(0, 1) == 4 && s.G.GetCombatDistance(0, 2) == 5 && spear.SelectableTargetSeats.Contains(1) && !spear.SelectableTargetSeats.Contains(2),
            "All real Hand inputs now have the new rank-two residency: the native Spear sum is four, extending printed range three to distance four but never admitting distance five as a raw seven-plus-seven sum would.");
        var beforeReject = s.CheckpointState();
        s.Need(!s.G.Submit(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [2], s.G.Revision, s.P!.PromptId)).Accepted && s.CheckpointState() == beforeReject,
            "A forged distance-five Spear target cannot pay either actual foreign entity or consume a native Slash allowance.");
        s.Cold(); s.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [1], s.G.Revision, s.P!.PromptId)); s.Reach(() => s.Hold("zhangba-paid"));
        var nativeSpear = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Action is { EffectiveKind: CardKind.Slash });
        s.Need(nativeSpear.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveRank: 4 } &&
            nativeSpear.Action.PhysicalCards.Select(c => c.CardId).ToHashSet().SetEquals(pair) && nativeSpear.Action.PhysicalCards.All(c => c.CardKind == CardKind.Duel && c.From == CardLocation.Hand(0)) &&
            s.E<JuewuUseIssuedEvent>().Length == 0 && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == nativeSpear.Id),
            "The actual two-entity equipment Use freezes effective rank four and both printed Duel costs before its payment child; the passive identity does not fabricate a Juewu conversion or spend an output allowance.");
        s.Private(0); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == nativeSpear.Id && e.CardKind == CardKind.Slash) == 1 && pair.All(id =>
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
            "The distance-four real native Spear Slash pays its original two entities once and completes after the cold child return."); s.Cold();

        foreach (var scene in new[] { "rank-provider-near", "rank-provider-far" })
        {
            s = JwScenario.Start(scene); s.Use("draw-four"); s.Play();
            var provider = Enumerable.Range(1, 3).Single(seat => s.V(seat).Role == Role.Loyalist);
            var target = provider == 1 ? 3 : 1; var supplied = s.V().Hand[0]; s.Use("give", [supplied.Id], [provider]); s.Play();
            var distance = scene == "rank-provider-near" ? 2 : 3;
            s.Need(supplied is { Kind: CardKind.Slash, Rank: 7 } && s.V(provider).Hand is [{ Rank: 2 }] && s.G.GetAttackRange(0) == 1 && s.G.GetCombatDistance(0, target) == distance,
                "A genuine allied Shu provider has the sole original printed-seven Slash, currently rank two in that provider's Hand; the real lord has no weapon range bonus.");
            s.Use("request", targets: [target]);
            if (scene == "rank-provider-far")
            {
                s.Play(); s.Need(!s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash) &&
                    s.V(provider).Hand.Single().Id == supplied.Id && !s.G.CardMovements.Any(m => m.CardId == supplied.Id && m.From == CardLocation.Hand(provider) && m.To == CardLocation.Processing),
                    "A real faction request cannot use its provider's old printed seven to admit distance three: no supplied use or physical payment occurs."); s.Cold(); continue;
            }
            s.Reach(() => s.P is { } p && p.PlayerSeat == provider && p.Choices.Any(c => c.Cards.Contains(supplied.Id)));
            s.Private(provider); s.Cold(); s.Advance(); s.Reach(() => s.Hold("committed"));
            var providedUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Action is { EffectiveKind: CardKind.Slash });
            s.Need(providedUse.Action is { Type: CardActionType.Use, ActorSeat: 0, EffectiveRank: 2 } && providedUse.Action.ProviderSeat == provider &&
                providedUse.Action.PhysicalCards is [var suppliedCost] && suppliedCost.CardId == supplied.Id && suppliedCost.From == CardLocation.Hand(provider) &&
                s.E<JuewuUseIssuedEvent>().Length == 0,
                "A real AI provider supplies its original entity to the lord's native Use with effective rank two frozen from the provider, without pretending that the requester owns the passive source.");
            s.Private(0); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == providedUse.Id && e.CardKind == CardKind.Slash) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == supplied.Id && m.From == CardLocation.Hand(provider) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
                "The legal distance-two supplied native Slash finishes after one real provider payment and cold parent return."); s.Cold();
        }
    }

    public static void JuewuTrueForcedUsesRejectPlayedAndPaidChildrenRightsCold()
    {
        var s = JwScenario.Start("forced"); s.Use("draw-four"); s.Play(); s.Use("give", [s.V().Hand[0].Id], [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "assisted-physical-slash" && c.Parameters.GetValueOrDefault("request-option") == "target") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([1]));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == JwSkill) == true);
        var request = s.P!; var selected = request.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == JwSkill && c.Parameters.GetValueOrDefault("conversion-binding-id") == JwBinding(CardKind.FireSlash));
        var id = selected.Cards.Single(); s.Private(0); s.Cold(); s.Answer(c => c.Id == selected.Id); s.Reach(() => s.Hold("committed"));
        var issued = s.E<JuewuUseIssuedEvent>().Single();
        s.Need(issued.OutputKind == CardKind.FireSlash && issued.CardId == id && issued.Source.OwnerSeat == 0 && issued.TurnOwner == 0 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnerSeat == 1) && s.E<JuewuMaterialPaidEvent>().Length == 1,
            "A foreign real request lets the actual responding user pay one original entity for an elemental true Slash Use; it retains its foreign program parent.");
        s.Cold(); s.Continue(); s.Play(); s.RejectOld(request);
        s.Need(s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == issued.UseId) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == CardKind.FireSlash) == 1 &&
            s.Conversions().All(a => a.PlayedCardKind != CardKind.FireSlash), "Native child completion returns once to the original request and consumes only the chosen output name.");

        s = JwScenario.Start("played"); s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != JwSkill) &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == JwDriver && c.Cards.Count == 1),
            "A real pure Duel Played request exposes its ordinary fallback but no use-only rank-two conversion.");
        s.Private(0); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == JwDriver && c.Cards.Count == 1); s.Play();
        s.Need(s.E<JuewuUseIssuedEvent>().Length == 0 && s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash) && s.Conversions().Length > 0,
            "Actual Played payment completes the Duel without consuming any true-use output allowance.");

        s = JwScenario.Start("lion-loss"); s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.ConversionSource is null)); s.Play();
        s.Use("wound"); s.Play(); var lion = s.V().Equipment.Single();
        s.Native(s.Conversions().First(a => a.CardId == lion.Id && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1])));
        s.Reach(() => s.Hold("cost")); var paid = s.E<JuewuUseIssuedEvent>().Single();
        s.Need(paid.From == CardLocation.Equipment(0) && paid.PrintedKind == CardKind.SilverLion && s.E<JuewuMaterialPaidEvent>().Length == 1 &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == paid.UseId), "An equipped printed-two Silver Lion is a genuine native conversion material whose actual leave-equipment children precede the trick.");
        s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("after-loss"));
        s.Need(s.V().Skills!.All(k => k.ContentId != JwSkill) && s.E<JuewuUseFinishedEvent>().Length == 0,
            "A real HP child revokes the conditional source after payment while its original native Use remains unfinished."); s.Cold();
        s.Continue(); s.Play();
        s.Need(s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == paid.UseId) == 1 && s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == paid.UseId) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == paid.UseId && e.CardKind == CardKind.Duel) == 1,
            "Cold paid-source-loss return drains the real Lion recovery and original trick exactly once without requiring a live skill or repaying the equipment."); s.Cold();

        s = JwScenario.Start("ox-payment"); var oxMaterial = s.V().Hand[0].Id;
        s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.ConversionSource is null && a.CardId == oxMaterial)); s.Play();
        var spill = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [spill], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Native(s.Conversions().First(a => a.CardId == oxMaterial && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1])));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == JwWatch);
        var oxInvoice = s.E<JuewuUseIssuedEvent>().Single(); var paidBatches = new HashSet<long>();
        for (var i = 0; i < 3 && s.P?.SkillPrompt?.SkillId == JwWatch; i++)
        {
            var original = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == oxInvoice.UseId);
            var movement = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            s.Need(original.RecoveryPaidContinuation is { Kind: RecoveryPaidCardUseKind.Trick } && original.Juewu!.Grains is [var grainCost] && grainCost.CardId == spill &&
                original.Juewu.PaymentBatches.Count == 2 && original.Juewu.PaymentBatches is System.Collections.IList { IsReadOnly: true } &&
                original.Juewu.PaymentBatches.All(p => p.Batch.Movements is System.Collections.IList { IsReadOnly: true } && p.Batch.SourceCounts is System.Collections.IList { IsReadOnly: true }) &&
                movement.ResumePaidCardUseFrameId == original.Id && paidBatches.Add(movement.Batch.Id) &&
                original.Juewu.PaymentBatches.Any(p => p.Batch.Id == movement.Batch.Id) &&
                s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == oxMaterial).Location == CardLocation.Processing &&
                s.E<JuewuUseFinishedEvent>().Length == 0 && !s.E<DamageAppliedEvent>().Any(),
                "An actual Ox conversion freezes primary and independent spill invoices on the original native use; each exact payment child pauses before any Duel effect or completion.");
            s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.P?.SkillPrompt?.SkillId == JwWatch || s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        }
        s.Play();
        s.Need(paidBatches.Count == 2 && s.E<JuewuPaymentBatchIssuedEvent>().Count(e => e.UseId == oxInvoice.UseId) == 2 &&
            s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == oxInvoice.UseId) == 1 && s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == oxInvoice.UseId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == spill && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1,
            "Both genuine Ox payment batches return once before the original Duel finishes, without charging its stored grain as another conversion material."); s.Cold();

        s = JwScenario.Start("grain"); var ox = s.V().Hand[0].Id;
        s.Native(s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.ConversionSource is null && a.CardId == ox)); s.Play();
        var grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.V().WoodenOxGrain!.Any(c => c.Id == grain) && Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).Players[0].WoodenOxGrain?.Count ?? 0) == 0) &&
            s.Conversions().Any(a => a.CardId == grain && a.PlayedCardKind == CardKind.Duel), "A real private Wooden Ox grain of printed rank two is an original conversion material without exposing its face to other viewers.");
        s.Cold(); s.Native(s.Conversions().First(a => a.CardId == grain && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1]))); s.Play();
        var oxPaid = s.E<JuewuUseIssuedEvent>().Single();
        s.Need(oxPaid.CardId == grain && oxPaid.From == CardLocation.WoodenOxGrain(0) && s.E<JuewuMaterialPaidEvent>().Single().From == CardLocation.WoodenOxGrain(0) &&
            s.E<JuewuUseFinishedEvent>().Count(e => e.UseId == oxPaid.UseId) == 1 && !s.V().WoodenOxGrain!.Any(c => c.Id == grain),
            "The native ordinary-trick pipeline pays the original grain entity once, returns its real parent, and removes exactly that private stored entity."); s.Cold();
    }

    public static void WaterOriginalOrderNullificationAndNativeChildrenCold()
    {
        var s = JwScenario.Start("water-reverse");
        s.Native(s.Convert(CardKind.WumiaoFlood, [2, 1])); s.Reach(() => s.Hold("committed")); s.Cold(); s.Continue(); s.Play();
        var useId = s.E<JuewuUseIssuedEvent>().Single().UseId;
        s.Need(s.E<WumiaoFloodEffectStartedEvent>().Where(e => e.UseId == useId).Select(e => (e.TargetIndex, e.TargetSeat)).SequenceEqual(new[] { (0, 2), (1, 1) }) &&
            s.E<WumiaoFloodDiscardPaidEvent>().Single(e => e.UseId == useId).TargetSeat == 2 &&
            s.E<WumiaoFloodDrawReturnedEvent>().Single(e => e.UseId == useId) is { RecipientSeat: 1, ActualCount: 1 } &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == useId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1,
            "A reversed native target selection really makes seat two discard and seat one draw, without sorting their original ordinals or issuing another Use."); s.Cold();

        s = JwScenario.Start("water-null"); var hp1 = s.V(1).Hp; var hp2 = s.V(2).Hp;
        s.Native(s.Convert(CardKind.WumiaoFlood, [1, 2])); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
        useId = s.E<JuewuUseIssuedEvent>().Single().UseId;
        var firstWindow = s.G.ResolutionStack.OfType<NullificationWindowFrame>().Single(f => f.ParentFrameId == useId);
        s.Need(firstWindow.ParentFrameId == useId && s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).TargetIndex == 0 && !s.E<WumiaoFloodEffectStartedEvent>().Any(),
            "A real nullification request owns ordinal zero before either native Flood effect starts.");
        var counter = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == JwDriver && c.Cards.Count == 1);
        var counterCard = counter.Cards.Single(); s.Private(0); s.Cold(); s.Answer(c => c.Id == counter.Id); s.Play();
        s.Need(s.E<NullificationRespondedEvent>().Count(e => e.ResolutionId == useId && e.NullificationCardId == counterCard && e.EffectNullified) == 1 &&
            s.E<NullificationResolvedEvent>().Count(e => e.ResolutionId == useId && e.EffectNullified) == 1 &&
            s.E<WumiaoFloodEffectStartedEvent>().Where(e => e.UseId == useId).ToArray() is [{ TargetIndex: 1, TargetSeat: 2 }] &&
            !s.E<WumiaoFloodDiscardPaidEvent>().Any(e => e.UseId == useId) &&
            s.E<WumiaoFloodDrawReturnedEvent>().Single(e => e.UseId == useId) is { RecipientSeat: 2, ActualCount: 1 } &&
            s.V(1).Hp == hp1 && s.V(2).Hp == hp2 - 1 && s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == useId) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1,
            "Actual first-target Nullification suppresses only its thunder damage and discard; ordinal one remains the draw effect and the original physical Flood is paid/finished once."); s.Cold();

        foreach (var scene in new[] { "water-lion", "water-ox", "water-weapon" })
        {
            s = JwScenario.Start(scene); int equipped;
            if (scene == "water-weapon")
            {
                s.Use("weapon"); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("advanced-value")) == true);
                s.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "fixture:jw-reserve");
                s.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"); s.Play();
                equipped = s.V().Equipment.Single(c => c.Kind == CardKind.GeneralWeapon).Id;
                s.Need(!s.G.CreateCardZoneDiagnostics().Any(c => c.CardId == equipped && c.Location == CardLocation.DrawPile),
                    "The weapon is really generated by the native sampled-general producer, rather than a printed GeneralWeapon deck shell."); s.Cold();
            }
            else
            {
                equipped = s.V().Hand[0].Id;
                s.Native(s.G.GetHumanLegalActions().First(a => a.CardId == equipped && a.Kind == LegalActionKind.Equip && a.ConversionSource is null)); s.Play();
            }
            int[] grains = [];
            if (scene == "water-ox")
            {
                grains = [s.V().Hand[0].Id]; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, grains, [], s.G.Revision, s.P!.PromptId)); s.Play();
            }
            else if (scene == "water-lion") { s.Use("wound"); s.Play(); }
            var material = s.V().Hand[0].Id; var naturalHp = s.V().Hp;
            s.Native(s.Conversions().First(a => a.CardId == material && a.PlayedCardKind == CardKind.WumiaoFlood && a.TargetSeats.SequenceEqual([0, 1])));
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wumiao-flood-discard") == true);
            useId = s.E<JuewuUseIssuedEvent>().Single().UseId;
            s.Need(s.V().Hp == naturalHp - 1 && s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).WumiaoFlood is { Stage: WumiaoFloodStage.ChoosingDiscard, TargetIndex: 0, TargetSeat: 0 },
                "The first real thunder damage finishes before that same actual target chooses its original equipped entity to discard.");
            s.Private(0); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([equipped]));
            s.Reach(() => s.P?.SkillPrompt?.SkillId == JwWatch);
            var childBatches = new HashSet<long>(); var recovered = false;
            for (var i = 0; i < 5 && s.P?.SkillPrompt?.SkillId == JwWatch; i++)
            {
                var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId); var r = use.WumiaoFlood!;
                s.Need(r is { Stage: WumiaoFloodStage.DiscardChildren, TargetIndex: 0, TargetSeat: 0, DiscardInvoiceSealed: true } &&
                    r.DiscardCardId == equipped && r.DiscardFrom == CardLocation.Equipment(0) && r.DiscardTo == (scene == "water-weapon" ? CardLocation.OutsideGame : CardLocation.DiscardPile) &&
                    r.DiscardNativeDestruction == (scene == "water-weapon") &&
                    r.DiscardMovements.Count == 1 + grains.Length && r.GrainMaterials.Count == grains.Length &&
                    r.GrainMaterials is System.Collections.IList { IsReadOnly: true } && r.DiscardMovements is System.Collections.IList { IsReadOnly: true } && r.DiscardBatches is System.Collections.IList { IsReadOnly: true } &&
                    s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == material).Location == CardLocation.Processing &&
                    !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId) && !s.E<WumiaoFloodDrawReturnedEvent>().Any(e => e.UseId == useId),
                    "The exact sealed native equipment-and-grain invoice remains frozen on the original Flood while its real children run; neither the second effect nor final cleanup runs early.");
                if (s.Hold("flood-recovered"))
                {
                    recovered = true; var hp = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
                    s.Need(hp.Continuation == PostEventContinuation.WumiaoFlood && hp.ResumeFrameId == useId && s.V().Hp == naturalHp,
                        "Silver Lion's actual recovery child returns to the same paid Flood rather than an unrelated program or a second discard.");
                }
                else
                {
                    var moved = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
                    s.Need(moved.ResumeWumiaoFloodFrameId == useId && childBatches.Add(moved.Batch.Id) &&
                        r.DiscardBatches.Any(b => b.BatchId == moved.Batch.Id) && moved.Batch.Movements.All(m =>
                            r.DiscardMovements.Any(p => p.MovementSequence == m.Sequence && p.CardId == m.CardId && p.From == m.From && p.To == m.To && p.Reason == m.Reason)),
                        "Every primary or Ox-spill movement window carries one exact issued batch and its own original material facts.");
                }
                s.Private(0); s.Cold(); s.Continue();
                s.Reach(() => s.P?.SkillPrompt?.SkillId == JwWatch || s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            }
            s.Play();
            var sealedInvoice = s.E<WumiaoFloodDiscardInvoiceSealedEvent>().Single(e => e.UseId == useId);
            s.Need((scene == "water-lion" ? recovered && s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 : childBatches.Count == (scene == "water-ox" ? 2 : 1)) &&
                sealedInvoice.GrainCount == grains.Length && sealedInvoice.MovementCount == 1 + grains.Length &&
                s.E<WumiaoFloodDiscardMaterialPaidEvent>().Count(e => e.UseId == useId) == 1 + grains.Length &&
                s.G.CardMovements.Count(m => m.CardId == equipped && m.From == CardLocation.Equipment(0) && m.To == (scene == "water-weapon" ? CardLocation.OutsideGame : CardLocation.DiscardPile) && m.Reason.Value == "card.effect.wumiao-flood.discard") == 1 &&
                grains.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1) &&
                s.E<WumiaoFloodDrawReturnedEvent>().Single(e => e.UseId == useId) is { RecipientSeat: 1, ActualCount: 1 } &&
                s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == useId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1,
                "Cold recovery and independent Ox-spill batches drain once before ordinal one's real draw and one original native Use completion."); s.Cold();
            if (scene == "water-weapon")
                s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.Reason.Value == "card.effect.wumiao-flood.discard" && e.ExecutorSeat == 0 && e.ActualCount == 1 && e.HandCount == 0) == 1 &&
                    s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == equipped).Location == CardLocation.OutsideGame &&
                    !s.G.CardMovements.Any(m => m.CardId == equipped && m.To == CardLocation.DiscardPile),
                    "A real general weapon pays one native logical discard, destroys the exact equipment to Outside and seals that actual destination without inventing a physical discard-pile card.");
        }

        s = JwScenario.Start("water-replacement");
        s.Native(s.Convert(CardKind.WumiaoFlood, [1, 0])); s.Reach(() => s.P?.SkillPrompt?.SkillId == JwZenrun && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        useId = s.E<JuewuUseIssuedEvent>().Single().UseId;
        var drawAttempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == useId);
        var accepted = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId);
        s.Need(drawAttempt.Context is { RequestedCount: 1, RecipientSeat: 0 } && accepted.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.WumiaoFlood, RequestedCount: 1, AttemptReturned: false } &&
            accepted.WumiaoFlood is { Stage: WumiaoFloodStage.Drawing, TargetIndex: 1, TargetSeat: 0 } && !s.E<WumiaoFloodDrawReturnedEvent>().Any(e => e.UseId == useId),
            "The second target's accepted native Draw1 is owned by its exact Flood effect before replacement and cannot be confused with the first target's discard.");
        s.Private(0); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == JwZenrun && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target"));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([2]));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        var replacementId = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null).Zenrun!.Selected.Single().CardId;
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
        var invoice = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Id == drawAttempt.Id);
        s.Need(invoice.Replacement!.GainedIds.SequenceEqual([replacementId]) && !s.E<WumiaoFloodDrawReturnedEvent>().Any(e => e.UseId == useId) &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).NativeDraw is { AttemptReturned: false } &&
            s.G.CardMovements.Count(m => m.CardId == replacementId && m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0) && m.Reason.Value == JwObtain) == 1,
            "The replacement's actual foreign obtain and its paid gain child remain inside the still-unreturned accepted native Draw1.");
        s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.P?.SkillPrompt?.SkillId == JwZenrun && s.P.PlayerSeat == 2);
        s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") &&
            !s.E<NativeDrawAttemptDecidedEvent>().Any(e => e.AttemptFrameId == drawAttempt.Id) && !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId),
            "The actual victim privately owns compensation before the Flood draw result or whole Use may finish.");
        s.Private(2); s.Cold(); s.Advance(); s.Reach(() => s.Hold("compensated"));
        s.Need(s.P!.PlayerSeat == 2 && !s.E<WumiaoFloodDrawReturnedEvent>().Any(e => e.UseId == useId),
            "The real compensation gain child also precedes the original draw's typed return.");
        s.Private(2); s.Cold(); s.Advance(); s.Play();
        s.Need(s.E<WumiaoFloodDrawReturnedEvent>().Single(e => e.UseId == useId) is { RecipientSeat: 0, ActualCount: 0 } &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == drawAttempt.Id && e.ParentFrameId == useId && e.RecipientSeat == 0 && e.RequestedCount == 1 && e.Replaced) == 1 &&
            !s.G.CardMovements.Any(m => m.Sequence > drawAttempt.Context.SequenceBefore && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) &&
            s.E<ZenrunCompensationReturnedEvent>().Single() is { ActualDrawCount: 1, ReplacementGainedCount: 0 } &&
            s.G.CardMovements.Count(m => m.Sequence > drawAttempt.Context.SequenceBefore && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(2) && m.Reason.Value == JwCompensation) == 1 &&
            s.E<JuewuMaterialPaidEvent>().Count(e => e.UseId == useId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1,
            "After cold gain and compensation returns, Flood reports Actual0 once, retains the separate obtained entity, and never repeats its accepted draw or original physical payment."); s.Cold();
    }

    private static string JwBinding(CardKind kind) { var text = kind.ToString(); return "rank-two-" + char.ToLowerInvariant(text[0]) + text[1..]; }

    private sealed class JwScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardSnapshot Card(int id) => Enumerable.Range(0, 4).SelectMany(i => V(i).Hand.Concat(V(i).Equipment)).Single(c => c.Id == id);
        public LegalAction[] Conversions() => G.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == JwSkill).ToArray();
        public LegalAction Convert(CardKind kind, int[] targets) { var candidates = Conversions().Where(a => a.PlayedCardKind == kind && a.TargetSeats.SequenceEqual(targets)).ToArray(); Need(candidates.Length > 0, "The exact native output and ordered targets must be published: " + kind); return candidates[0]; }
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == JwWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, JwDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Take(int seat, int id) { var slot = V(seat).Hand.Select((card, index) => (card, index)).Single(p => p.card.Id == id).index; Use("take", targets: [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" && c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        public PlayCardCommand Command(LegalAction a, CardConversionSource? source = null) => new(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = source ?? a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources };
        public void Native(LegalAction a) => Accept(Command(a));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The human must select a genuinely published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Private(int seat) => Need(P is { } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Unrelated viewers cannot see the native private prompt or selected materials.");
        public void RejectSource(LegalAction action) { var before = State(); Need(!G.Submit(Command(action, action.ConversionSource! with { SkillInstanceId = "forged-juewu" })).Accepted && before == State() && !G.Submit(Command(action, action.ConversionSource! with { BindingId = "rank-two-lightning" })).Accepted && before == State(), "A forged source or unsupported damage name is rejected without payment or quota consumption."); }
        public void RejectOld(PendingDecision old) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && before == State(), "An old request cannot replay an already paid native conversion."); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Fixture terminated before real boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == JwWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Kind == DecisionKind.FireAttackDiscard) Answer(c => c.Cards.Count == 1);
                else if (p.SkillPrompt?.SkillId == "ol:gushe" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") is "draw" or "claim")) Answer(c => c.Parameters.GetValueOrDefault("branch") is "draw" or "claim");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "target-card-slot")) Answer(c => c.Parameters.GetValueOrDefault("action") == "target-card-slot");
                else throw new InvalidOperationException("Unexpected exact boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": code=" + result.Error?.Code + " message=" + result.Error?.Message + " revision=" + result.Revision); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public string CheckpointState() => State();
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves actual entity residence, effective face, per-name actual-turn quota, child invoices and all four private views."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(8).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) }, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Issued = E<JuewuUseIssuedEvent>(), Paid = E<JuewuMaterialPaidEvent>(), Finished = E<JuewuUseFinishedEvent>() });
        public static JwScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new JwFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = JwMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new JwScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, JwOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class JwFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wumiao-juewu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wumiao-guan-yu." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Juewu bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); b.AddSkill(new(JwSkill, production.Presentations[JwSkill].Name, production.Presentations[JwSkill].Description) { Program = production.Programs[JwSkill], ProgramPresentation = production.Presentations[JwSkill] });
            if (scene.StartsWith("foreign-policy", StringComparison.Ordinal))
            {
                void Existing(string bundle, string id) { string Text(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Pindian policy bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); } var c = SkillProgramCatalog.Load(Text("rules"), Text("presentation")); b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] }); }
                Existing("classic-qin-mi", "classic:tianbian");
                if (scene == "foreign-policy-group") Existing("ol-wang-lang", "ol:gushe");
            }
            if (scene.StartsWith("rank-", StringComparison.Ordinal))
            {
                string Liegong(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-huang-zhong." + suffix + ".json") ?? throw new InvalidOperationException("Missing real boundary Liegong bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var liegong = SkillProgramCatalog.Load(Liegong("rules"), Liegong("presentation")); const string id = "boundary:liegong-current";
                b.AddSkill(new(id, liegong.Presentations[id].Name, liegong.Presentations[id].Description) { Program = liegong.Programs[id], ProgramPresentation = liegong.Presentations[id] });
            }
            if (scene == "water-replacement")
            {
                string Zen(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-sun-hong." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Zenrun bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var zenrun = SkillProgramCatalog.Load(Zen("rules"), Zen("presentation")); b.AddSkill(new(JwZenrun, zenrun.Presentations[JwZenrun].Name, zenrun.Presentations[JwZenrun].Description) { Program = zenrun.Programs[JwZenrun], ProgramPresentation = zenrun.Presentations[JwZenrun] });
            }
            if (scene == "water-weapon")
            {
                // Register a genuine logical-discard consumer to enable its existing optional semantic recorder.
                // No fixture character owns Biguo or receives an additional behavior.
                string Biguo(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-pang-ji." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Biguo bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var biguo = SkillProgramCatalog.Load(Biguo("rules"), Biguo("presentation")); b.AddSkill(new("ol:biguo", biguo.Presentations["ol:biguo"].Name, biguo.Presentations["ol:biguo"].Description) { Program = biguo.Programs["ol:biguo"], ProgramPresentation = biguo.Presentations["ol:biguo"] });
            }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}").AsObject();
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var contest = A("contest", "{\"op\":\"pindian\",\"target\":\"selectedTarget\",\"amount\":1}", 1, 1); contest["targetKind"] = "otherLivingWithHand";
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var acts = new JsonArray(A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("equipment-back", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}"),
                A("lose-juewu", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"wumiao:juewu\"],\"sourceBind\":\"" + JwNeutral + "\"}"),
                A("grant-juewu", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"wumiao:juewu\"]}"), contest, duel);
            if (scene == "water-weapon") acts.Add(A("weapon", "{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":1}"));
            if (scene.StartsWith("rank-provider", StringComparison.Ordinal))
            {
                var request = A("request", "{\"op\":\"requestFactionCard\",\"target\":\"selectedTarget\",\"providerFactionId\":\"shu\",\"requiredKind\":\"slash\"}", targets: 1); request["targetKind"] = "otherLiving"; acts.Add(request);
            }
            var driver = new JsonObject { ["id"] = JwDriver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]") };
            if (scene.StartsWith("foreign-policy", StringComparison.Ordinal)) driver["cardPolicies"] = N("[{\"id\":\"observe-real-claim\",\"kind\":\"pindianClaim\"}]");
            if (scene.StartsWith("rank-provider", StringComparison.Ordinal)) driver["cardPolicies"] = N("[{\"id\":\"actual-provider-request\",\"kind\":\"factionResponseRequest\",\"requiredCardKinds\":[\"slash\"],\"factionId\":\"shu\",\"ownerRole\":\"lord\"}]");
            if (scene == "played") driver["viewAs"] = N("[{\"id\":\"ordinary-played-slash\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]");
            if (scene == "water-null") driver["viewAs"] = N("[{\"id\":\"ordinary-nullification\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]");
            var defs = new JsonArray(driver,
                N("{\"id\":\"" + JwQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"),
                N("{\"id\":\"" + JwNeutral + "\",\"revision\":1,\"modifiers\":[{\"id\":\"neutral\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":1,\"priority\":0}]}"));
            var own = new List<string> { JwDriver, scene == "lion-loss" ? JwConditional : JwSkill, JwWatch };
            if (scene.StartsWith("foreign-policy", StringComparison.Ordinal)) own.Add("classic:tianbian");
            if (scene == "foreign-policy-group") own.Add("ol:gushe");
            if (scene.StartsWith("rank-", StringComparison.Ordinal)) own.Add("boundary:liegong-current");
            if (scene == "rank-zhangba")
            {
                const string setup = "fixture:jw-actual-spear-setup"; own.Add(setup);
                defs.Add(N("{\"id\":\"" + setup + "\",\"revision\":1,\"triggers\":[{\"id\":\"native-real-weapon\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"owner\"},\"resultBind\":\"actual-spear\"}]}]}"));
            }
            if (scene.StartsWith("rank-", StringComparison.Ordinal)) defs.Add(N("{\"id\":\"fixture:jw-rank-distance\",\"revision\":1,\"modifiers\":[{\"id\":\"actual-distance-boundary\",\"query\":\"incomingDistance\",\"operation\":\"add\",\"value\":" + (scene == "rank-zhangba" ? 3 : scene == "rank-provider-near" ? 1 : 2) + ",\"priority\":0}]}"));
            if (scene == "water-replacement") own.Add(JwZenrun);
            if (scene == "lion-loss") defs.Add(N("{\"id\":\"" + JwConditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-juewu\",\"skillId\":\"wumiao:juewu\",\"minimumHp\":12}]}"));
            if (scene == "forced") defs.Add(N("{\"id\":\"" + JwForced + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-foreign-request\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"chooserRef\":{\"kind\":\"selectedTarget\"},\"resultBind\":\"forced-use\"}]}]}"));
            var watch = new JsonArray(N("{\"id\":\"actual-commit\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + Choice("committed") + "]}"));
            if (scene == "rank-zhangba") watch[0]!["cardKinds"] = N("[\"slash\"]");
            if (scene == "lion-loss") watch.Add(N("{\"id\":\"equipment-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementReasons\":[\"" + CardMoveReasons.Use.Value + "\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[" + Choice("cost") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}," + Choice("after-loss") + "]}"));
            if (scene == "ox-payment")
            {
                watch.Add(N("{\"id\":\"actual-ox-equipment-payment\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementReasons\":[\"" + CardMoveReasons.Use.Value + "\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[" + Choice("ox-equipment") + "]}"));
                watch.Add(N("{\"id\":\"actual-ox-payment-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"" + CardMoveReasons.WoodenOxGrainDiscard.Value + "\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[" + Choice("ox-grain") + "]}"));
            }
            if (scene == "rank-zhangba") watch.Add(N("{\"id\":\"real-spear-paid-pair\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + CardMoveReasons.Use.Value + "\"],\"optional\":false,\"effects\":[" + Choice("zhangba-paid") + "]}"));
            if (scene is "water-lion" or "water-ox" or "water-weapon")
            {
                watch.Add(N("{\"id\":\"flood-real-equipment-discard\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementReasons\":[\"card.effect.wumiao-flood.discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[" + Choice("flood-equipment") + "]}"));
                if (scene == "water-lion") watch.Add(N("{\"id\":\"flood-real-lion-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("flood-recovered") + "]}"));
                else if (scene == "water-ox") watch.Add(N("{\"id\":\"flood-real-ox-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"" + CardMoveReasons.WoodenOxGrainDiscard.Value + "\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[" + Choice("flood-grain") + "]}"));
            }
            if (scene == "water-replacement")
                foreach (var gain in new[] { (Option: "obtained", Reason: JwObtain), (Option: "compensated", Reason: JwCompensation) })
                    watch.Add(N("{\"id\":\"real-" + gain.Option + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + gain.Reason + "\"],\"optional\":false,\"effects\":[" + Choice(gain.Option) + "]}"));
            defs.Add(new JsonObject { ["id"] = JwWatch, ["revision"] = 1, ["triggers"] = watch });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实绝武原生使用、点数与付款子流程", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实绝武夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:jw-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); b.AddGeneral(new(JwOwner, "真实绝武持有者", "supporter", pick, "shu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:jw-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var pp = "fixture:jw-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); var skills = new List<string> { JwQuiet }; if (i == 1 && scene == "forced") skills.Add(JwForced); if (scene == "water-replacement") skills.Add(JwWatch); if (i <= 2 && scene.StartsWith("foreign-policy", StringComparison.Ordinal)) { skills.Add(JwSkill); skills.Add("classic:tianbian"); } if (scene.StartsWith("rank-", StringComparison.Ordinal)) skills.Add("fixture:jw-rank-distance"); if (scene.StartsWith("rank-provider", StringComparison.Ordinal)) skills.Add(JwSkill); b.AddGeneral(new(peers[i - 1], "真实对局角色", "supporter", pp, "shu", 12, skills)); }
            if (scene == "water-weapon") { const string reservePick = "fixture:jw-reserve-pick"; b.AddSkill(new(reservePick, "未选备用", "真实魂牌来源") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -200000000d) }); b.AddGeneral(new("fixture:jw-reserve", "真实备用武将", "supporter", reservePick, "shu", 4, [])); }
            var physicalKind = scene is "lion-loss" or "water-lion" ? CardKind.SilverLion : scene is "grain" or "water-ox" or "ox-payment" ? CardKind.WoodenOx : scene == "played" || scene.StartsWith("rank-provider", StringComparison.Ordinal) ? CardKind.Slash : scene == "rank-zhangba" ? CardKind.Duel : CardKind.Crossbow;
            const string physicalId = "fixture:jw-physical"; var definition = CardCatalog.Get(physicalKind); b.AddCard(new(physicalId, definition.DisplayName, definition.CategoryName, "真实原实体", physicalKind));
            if (scene == "rank-zhangba") b.AddCard(new("fixture:jw-actual-spear", CardCatalog.Get(CardKind.ZhangbaSerpentSpear).DisplayName, "装备牌", "实际唯一丈八", CardKind.ZhangbaSerpentSpear));
            b.AddDeck(new("fixture:jw-deck", "原生点数真实实体", scene == "forced" || scene.StartsWith("rank-", StringComparison.Ordinal) ? 0 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(scene == "rank-zhangba" && i == 0 ? "fixture:jw-actual-spear" : physicalId, Suit.Heart, scene.StartsWith("foreign", StringComparison.Ordinal) || scene.StartsWith("rank-", StringComparison.Ordinal) ? 7 : 2)).ToArray() });
            var roles = scene.StartsWith("rank-provider", StringComparison.Ordinal) ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(JwMode, "真实绝武付款和原生流程", 4, 4, roles, "fixture:jw-deck", GeneralCandidateCount: scene == "water-weapon" ? 5 : 4, GeneralPoolIds: scene == "water-weapon" ? [JwOwner, .. peers, "fixture:jw-reserve"] : [JwOwner, .. peers]));
        }
    }
}
