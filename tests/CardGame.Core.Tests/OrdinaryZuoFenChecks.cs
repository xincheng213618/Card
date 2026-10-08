using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryZuoFenChecks
{
    private const string Zhao = "ol:zhaosong", Lei = "ol:zhaosong-lei", Fu = "ol:zhaosong-fu", Song = "ol:zhaosong-song", Lisi = "ol:lisi";
    private const string Driver = "fixture:zuo-fen-driver", Gain = "fixture:zuo-fen-gain", Health = "fixture:zuo-fen-health";
    private const string Discard = "fixture:zuo-fen-discard", Foreign = "fixture:zuo-fen-foreign", Suppress = "fixture:zuo-fen-suppress";
    private const string Changed = "fixture:zuo-fen-changed", Dummy = "fixture:zuo-fen-native-grant";
    private const string Mode = "identity:classic-zuo-fen-fixture";
    private enum Scenario { Gift, Lei, Fu, Song, LisiSingle, LisiPair, LisiNullification, Pure }

    public static void FaceUpGiftIssuesOnePublicTokenAndRetainsItAfterSourceLoss()
    {
        foreach (var (kind, mark, derived) in new[] { (CardKind.Nullification, PlayerMarkerKind.CategoryLei, Lei),
            (CardKind.Crossbow, PlayerMarkerKind.CategoryFu, Fu), (CardKind.Slash, PlayerMarkerKind.CategorySong, Song) })
        {
            var (g, registry) = Create(Scenario.Gift, kind);
            Answer(g, Activate); Reach(g, p => MarkChoice(p, "gift"));
            var original = MarkFrame(g); var r = original.RecipientCategoryMark!;
            var shown = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("mark-action") == "gift").Cards.Single();
            Require(r is { Stage: RecipientCategoryMarkStage.ChoosingGift, HolderSeat: 0, Token: null } &&
                r.Source.OwnerSeat == 1 && r.Source.SkillId == Zhao && r.Materials.Count == 6 &&
                r.Materials.All(m => m.From == CardLocation.Hand(0)) && V(g, 0).Hand.Any(c => c.Id == shown),
                "The real draw-ending opportunity asks the other living holder for exactly one of its own frozen Hand entities.");
            Frozen(r.Materials); Private(g); g = Cold(g, registry);
            Answer(g, c => c.Cards.SequenceEqual([shown]));
            Reach(g, p => p is { PlayerSeat: 1 } && p.SkillPrompt?.SkillId == Gain && IsContinue(p));
            var paidFrame = MarkFrame(g); var paid = paidFrame.RecipientCategoryMark!;
            var granted = E<RecipientCategoryMarkGrantedEvent>(g).Single(e => e.FrameId == original.Id);
            var reveal = E<ProgramCardsRevealedEvent>(g).Single(e => e.FrameId == original.Id && e.Bind == "category-mark-gift");
            Require(paid is { Stage: RecipientCategoryMarkStage.GiftChildren, HolderSeat: 0, BatchId: > 0 } &&
                paid.SelectedCardIds.SequenceEqual([shown]) && paid.Token == granted.Token &&
                granted.Token is { HolderSeat: 0, SourceSkillId: Zhao, StateId: "zhaosong" } && granted.Token.DerivedSkillId == derived &&
                granted.Token.Origin == r.Source && reveal.OwnerSeat == 0 && reveal.Cards is [{ Id: var revealedId, Kind: var revealedKind, Suit: Suit.Spade }] &&
                revealedId == shown && revealedKind == kind && V(g, 1).Hand.Any(c => c.Id == shown) &&
                paidFrame.PendingMovementContinuation?.SubjectSeat == 0 &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.ResumeProgramFrameId is null && f.Batch.ParentFrameId == original.Id && f.Batch.AwaitingProgramFrameId == original.Id && f.Batch.Id == paid.BatchId) &&
                Enumerable.Range(0, 4).All(viewer => Marker(g, viewer, mark) == 1) && V(g, 0).Skills!.Any(s => s.Id == derived),
                "A face-up real gift issues its public category token and independent holder grant before the native recipient gain child.");
            Frozen(reveal.Cards); Frozen(paid.SelectedCardIds); PublicShown(g, reveal.Cards); Private(g); g = Cold(g, registry);
            Continue(g); Play(g);
            Require(E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 1 && e.SourceSkillId == Gain && e.SkillIds.Contains(Suppress)) &&
                V(g, 1).Skills!.All(s => s.Id != Zhao) && V(g, 0).Skills!.Any(s => s.Id == derived) &&
                Enumerable.Range(0, 4).All(viewer => Marker(g, viewer, mark) == 1) &&
                E<RecipientCategoryMarkCompletedEvent>(g).Count(e => e.FrameId == original.Id && !e.Consumed) == 1 &&
                g.CardMovements.Count(m => m.CardId == shown && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
                "Real acquired-source suppression inside the paid gain child preserves the holder's token and returns the original gift exactly once.");
            Use(g, "hurt-target", [1]); Play(g);
            Require(V(g, 1).Hp == 2 && V(g, 1).Skills!.Any(s => s.Id == Zhao), "A real HP change restores the original giver's qualification before the next draw-ending opportunity.");
            var turn = g.CreateSnapshot(0).TurnNumber; Use(g, "extra"); Play(g); End(g);
            Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && g.CreateSnapshot(0).TurnNumber > turn);
            Require(E<RecipientCategoryMarkGrantedEvent>(g).Length == 1 && E<RecipientCategoryMarkStartedEvent>(g).Count(e =>
                e.Operation == SkillProgramEffectOp.GiveHandAndGrantCategoryMark) == 1 && Marker(g, 0, mark) == 1 &&
                V(g, 0).HandCount == 5 && V(g, 1).HandCount == 5,
                "The next actual draw phase cannot issue any second category while this holder still has a token, even after source qualification returns.");
        }
    }

    public static void LeiRecoversNativeDyingToOneAndDrawsOnce()
    {
        var (g, registry) = Create(Scenario.Lei, CardKind.Nullification); TakeGift(g); Play(g);
        var token = E<RecipientCategoryMarkGrantedEvent>(g).Single().Token;
        Require(token.Kind == RecipientCategoryMarkKind.Rescue && Marker(g, 0, PlayerMarkerKind.CategoryLei) == 1 &&
            V(g, 0).Skills!.Any(s => s.Id == Lei) && V(g, 0).Skills!.All(s => s.Id != Zhao),
            "Only the independent holder rescue ability is acquired from the trick gift.");
        Use(g, "die"); Reach(g, p => Offer(p, Lei, 0));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 0);
        Require(V(g, 0) is { Hp: 0, MaxHp: 5, IsAlive: true }, "A legal HP-loss command reaches genuine zero-HP native Dying before the token is invoked.");
        Answer(g, Activate); Reach(g, p => p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Health && IsContinue(p));
        var frame = MarkFrame(g); var r = frame.RecipientCategoryMark!;
        Require(r is { Stage: RecipientCategoryMarkStage.RecoveryChildren, HolderSeat: 0, Consumed: true, RecoveryIssued: true, RecoveryAmount: 1, DrawIssued: false } &&
            r.Token == token && r.DyingFrameId == dying.Id && r.Source.SkillId == Lei && r.Source.OwnerSeat == 0 &&
            g.ResolutionStack.OfType<DyingFrame>().Any(f => f.Id == dying.Id) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Change is { Kind: HpChangeKind.Recovery, TargetSeat: 0, HpBefore: 0, HpAfter: 1 } && f.Change.ParentFrameId == frame.Id) &&
            Enumerable.Range(0, 4).All(viewer => Marker(g, viewer, PlayerMarkerKind.CategoryLei) == 0) &&
            E<RecipientCategoryMarkConsumedEvent>(g).Count(e => e.FrameId == frame.Id && e.TokenId == token.TokenId) == 1 &&
            E<RecipientCategoryMarkDrawIssuedEvent>(g).Length == 0,
            "The holder consumes its original token once, recovers through its owning native HP child, and owes the subsequent Draw until that child returns.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(V(g, 0) is { Hp: 1, MaxHp: 5, HandCount: 6, IsAlive: true } && !g.ResolutionStack.OfType<DyingFrame>().Any() &&
            E<RecipientCategoryMarkRecoveryIssuedEvent>(g).Count(e => e.FrameId == frame.Id && e.HolderSeat == 0 && e.Amount == 1) == 1 &&
            E<RecipientCategoryMarkDrawIssuedEvent>(g).Count(e => e.FrameId == frame.Id && e.HolderSeat == 0 && e.ActualCount == 1) == 1 &&
            E<RecipientCategoryMarkCompletedEvent>(g).Count(e => e.FrameId == frame.Id && e.Consumed) == 1 &&
            E<MaximumHpChangedEvent>(g).Length == 0,
            "Cold HP return completes recover-to-one plus one genuine Draw exactly once, preserves maximum HP and resumes the interrupted native program.");
    }

    public static void FuBlindlyDiscardsUpToTwoFromOneArea()
    {
        foreach (var count in new[] { 0, 1, 2 })
        {
            var (g, registry) = Create(Scenario.Fu, CardKind.Crossbow); TakeGift(g); Reach(g, p => Offer(p, Fu, 0));
            Answer(g, Activate); Reach(g, p => MarkChoice(p, "discard-target"));
            var frame = MarkFrame(g); var token = frame.RecipientCategoryMark!.Token!;
            Require(frame.RecipientCategoryMark is { Consumed: true, Stage: RecipientCategoryMarkStage.ChoosingTarget } &&
                token.Kind == RecipientCategoryMarkKind.Discard && Marker(g, 0, PlayerMarkerKind.CategoryFu) == 0,
                "The real own Play-start invocation spends the independent holder's Fu token before its optional zero-to-two selection.");
            Answer(g, c => c.Targets.SequenceEqual([2])); Reach(g, p => MarkChoice(p, "discard-card"));
            var r = MarkFrame(g).RecipientCategoryMark!;
            var candidates = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("mark-action") == "discard-card").ToArray();
            Require(r.TargetSeat == 2 && r.Materials.Count == 4 && r.Materials.All(m => m.Hidden && m.From == CardLocation.Hand(2)) &&
                candidates.Length == 4 && candidates.All(c => c.Cards.Count == 0 && c.Targets.Count == 0 &&
                    c.Parameters["source-zone"] == nameof(CardZoneKind.Hand) && c.Parameters["card-owner-seat"] == "2") && P(g)!.ValidCardIds.Count == 0,
                "The holder chooses one area owner and sees only frozen opaque foreign Hand slots, without hidden entity identity or appearance.");
            Frozen(r.Materials); Private(g);
            var selected = r.Materials.OrderBy(m => m.SlotIndex).Take(count).Select(m => m.CardId).ToArray();
            for (var i = 0; i < count; i++) { var slot = i.ToString(System.Globalization.CultureInfo.InvariantCulture); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == slot); }
            if (count < 2) Answer(g, c => c.Parameters.GetValueOrDefault("mark-action") == "finish");
            if (count != 0)
            {
                Reach(g, p => p is { PlayerSeat: 2 } && p.SkillPrompt?.SkillId == Discard && IsContinue(p));
                var paid = MarkFrame(g).RecipientCategoryMark!;
                Require(paid is { Stage: RecipientCategoryMarkStage.DiscardChildren, TargetSeat: 2, BatchId: > 0 } &&
                    paid.SelectedCardIds.SequenceEqual(selected) &&
                    g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.ResumeProgramFrameId is null && f.Batch.ParentFrameId == frame.Id && f.Batch.AwaitingProgramFrameId == frame.Id && f.Batch.Id == paid.BatchId) &&
                    selected.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(2) && m.To == CardLocation.DiscardPile &&
                        m.Reason.Value == $"skill-program.{Fu}.recipient-category.discard") == 1) &&
                    E<RecipientCategoryMarkCompletedEvent>(g).All(e => e.FrameId != frame.Id),
                    "Only the original chosen entities pay one real discard batch, whose native child remains owned by this exact consumed receipt.");
                Frozen(paid.SelectedCardIds); Private(g); if (count == 2) g = Cold(g, registry); Continue(g);
            }
            Play(g);
            Require(V(g, 2).HandCount == 4 - count && E<RecipientCategoryMarkConsumedEvent>(g).Count(e => e.FrameId == frame.Id && e.TokenId == token.TokenId) == 1 &&
                E<RecipientCategoryMarkCompletedEvent>(g).Count(e => e.FrameId == frame.Id && e.Consumed) == 1 &&
                E<RecipientCategoryMarkMovementPaidEvent>(g).Count(e => e.FrameId == frame.Id) == (count == 0 ? 0 : 1) &&
                (count == 0 || E<RecipientCategoryMarkMovementPaidEvent>(g).Single(e => e.FrameId == frame.Id).Count == count) &&
                E<RecipientCategoryMarkDrawIssuedEvent>(g).Length == 0,
                "Zero, one and two are all genuine accepted Fu outcomes; they consume once, discard only the selected amount and add no historical one-card Draw penalty or benefit.");
        }
    }

    public static void SongAddsLegalTargetsToOriginalSlash()
    {
        var (g, registry) = Create(Scenario.Song, CardKind.Slash); TakeGift(g); Play(g);
        var physical = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        var card = physical.CardId!.Value; SubmitPlay(g, physical); Reach(g, p => Offer(p, Song, 0));
        Answer(g, Activate); Reach(g, p => MarkChoice(p, "slash-target"));
        var frame = MarkFrame(g); var r = frame.RecipientCategoryMark!;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == r.CardUseFrameId);
        Require(r is { Stage: RecipientCategoryMarkStage.ChoosingSlashTargets, Consumed: true } && r.Token?.Kind == RecipientCategoryMarkKind.ExtraTargets &&
            r.BaseTargetSeats.SequenceEqual([1]) && r.AddedTargetSeats.Count == 0 && use.CardId == card && use.Action?.ActionId == r.ActionId &&
            use.TargetSeats.SequenceEqual([1]) && P(g)!.Choices.Where(c => c.Targets.Count != 0).SelectMany(c => c.Targets).Order().SequenceEqual([2, 3]) &&
            Marker(g, 0, PlayerMarkerKind.CategorySong) == 0,
            "One original native Slash target permits only the two remaining distance-legal living targets and spends its holder token without replacing the use or cost.");
        Frozen(r.BaseTargetSeats); Frozen(r.CandidateSeats); Private(g); g = Cold(g, registry);
        Answer(g, c => c.Targets.SequenceEqual([2]));
        Require(MarkFrame(g).RecipientCategoryMark!.AddedTargetSeats.SequenceEqual([2]) &&
            P(g)!.Choices.Where(c => c.Targets.Count != 0).SelectMany(c => c.Targets).SequenceEqual([3]),
            "The first added target remains frozen and cannot be chosen twice.");
        Answer(g, c => c.Targets.SequenceEqual([3])); Play(g);
        var resolved = E<RecipientCategorySlashTargetsResolvedEvent>(g).Single(e => e.FrameId == frame.Id);
        Require(resolved.CardUseFrameId == use.Id && resolved.ActionId == r.ActionId && resolved.BeforeTargets.SequenceEqual([1]) &&
            resolved.AddedTargets.SequenceEqual([2, 3]) && resolved.ResultTargets.SequenceEqual([1, 2, 3]) &&
            Enumerable.Range(1, 3).All(seat => E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == seat && e.Amount == 1) == 1) &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && V(g, 0).Hp == 5 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            E<RecipientCategoryMarkConsumedEvent>(g).Count(e => e.FrameId == frame.Id) == 1 &&
            E<RecipientCategoryMarkCompletedEvent>(g).Count(e => e.FrameId == frame.Id && e.Consumed) == 1,
            "The exact original Slash gains both targets and resolves all three native effects once, with one original material and no old Song HP penalty.");
        Frozen(resolved.BeforeTargets); Frozen(resolved.AddedTargets); Frozen(resolved.ResultTargets);
    }

    public static void ActualSingleAndPairDodgeUsesGiveEveryNativeMaterialOnce()
    {
        foreach (var count in new[] { 1, 2 })
        {
            var (g, registry) = Create(count == 1 ? Scenario.LisiSingle : Scenario.LisiPair, CardKind.Dodge);
            Play(g); End(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Foreign && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
            Answer(g, c => c.Targets.SequenceEqual([0]));
            Reach(g, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } && p.Choices.Any(c => c.Cards.Count == count));
            Require(g.CreateSnapshot(0).CurrentSeat == 1 && V(g, 0).HandCount == 6,
                "The original attack occurs in another actual turn before the holder pays any real Dodge material.");
            var incoming = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardAttack is not null);
            var dodge = P(g)!.Choices.First(c => c.Cards.Count == count && (count == 1
                ? c.Parameters.GetValueOrDefault("response") == "dodge"
                : c.Parameters.GetValueOrDefault("response") == "extended-view-as"));
            var cards = dodge.Cards.ToArray(); Answer(g, c => c.Id == dodge.Id);
            Reach(g, p => Offer(p, Lisi, 0));
            Require(cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1) &&
                E<OffTurnUsedMaterialDiscardedEvent>(g).Count(e => e.ActorSeat == 0) == count,
                "Only the actual used Dodge materials that really reached DiscardPile authorize the optional gift.");
            Answer(g, Activate); Reach(g, p => Has(p, "off-turn-used-card-gift"));
            var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OffTurnUsedCardGift is not null);
            var receipt = frame.OffTurnUsedCardGift!;
            Require(receipt.Kind == OffTurnUsedCardKind.DodgeUse && receipt.CardUseFrameId == incoming.Id &&
                receipt.Source.SkillId == Lisi && receipt.Source.OwnerSeat == 0 && receipt.ActualTurnOwnerSeat == 1 &&
                receipt.OriginalEntities.Select(e => e.CardId).Order().SequenceEqual(cards.Order()) &&
                receipt.OriginalEntities.All(e => e.OriginalFrom == CardLocation.Hand(0) && e.MovementSequence > 0) &&
                P(g)!.Choices.Any(c => c.Targets.SequenceEqual([2]) && c.Cards.Order().SequenceEqual(cards.Order())) &&
                V(g, 2).HandCount <= V(g, 0).HandCount && (count != 2 || V(g, 2).HandCount == V(g, 0).HandCount),
                "One owning receipt aggregates the entire genuine single or pair Dodge Use and permits the equal-Hand-count recipient.");
            Frozen(receipt.OriginalEntities); Frozen(receipt.CandidateSeats); Private(g); g = Cold(g, registry);
            Answer(g, c => c.Targets.SequenceEqual([2]));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 2 && IsContinue(p));
            var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frame.Id).OffTurnUsedCardGift!;
            Require(paid is { Stage: OffTurnUsedCardGiftStage.GiftChildren, RecipientSeat: 2 } && paid.PaidCardIds is not null &&
                paid.PaidCardIds.Order().SequenceEqual(cards.Order()) && paid.GiftBatchId > 0 &&
                cards.All(id => V(g, 2).Hand.Any(c => c.Id == id)) &&
                !E<OffTurnUsedCardGiftResolvedEvent>(g).Any(e => e.FrameId == frame.Id),
                "The atomic gift moves every original material before its real recipient gain child and retains the exact outstanding return.");
            var gain = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain && f.OwnerSeat == 2);
            Frozen(paid.PaidCardIds!); Private(g); g = Cold(g, registry); Continue(g);
            Reach(g, p => p is { PlayerSeat: 2 } && p.SkillPrompt?.SkillId == Gain && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
            Answer(g, c => c.Targets.SequenceEqual([0]));
            Reach(g, p => p is { PlayerSeat: 2 } && p.SkillPrompt?.SkillId == Changed && IsContinue(p));
            var changed = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Changed);
            var changedWindow = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Id == changed.WindowContext!.ParentFrameId);
            var outstanding = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frame.Id).OffTurnUsedCardGift!;
            Require(V(g, 0).Hp == 4 && V(g, 0).Skills!.All(s => s.Id != Lisi) &&
                E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 2 && e.SourceSkillId == Gain && e.SkillIds.Contains(Dummy)) == 1 &&
                changed.WindowContext is { Window: SkillProgramTriggerWindow.SkillsChanged, OwnerSeat: 2 } &&
                changedWindow is { Window: SkillProgramTriggerWindow.SkillsChanged, OwnerSeat: 2, Continuation: ProgramLifecycleContinuation.ResumeParentProgram } &&
                changedWindow.ResumeProgramFrameId == gain.Id &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == gain.Id).WindowContext?.ParentFrameId ==
                    g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.Id == paid.GiftBatchId).Id &&
                outstanding.Stage == OffTurnUsedCardGiftStage.GiftChildren && outstanding.PaidCardIds!.SequenceEqual(cards) &&
                E<OffTurnUsedCardGiftResolvedEvent>(g).All(e => e.FrameId != frame.Id),
                "After real source suppression, a real recipient grant opens the exact native SkillsChanged child under the original paid gift, without resolving or repaying it early.");
            Private(g); g = Cold(g, registry); Continue(g); Play(g);
            Require(E<OffTurnUsedCardGiftPaidEvent>(g).Count(e => e.FrameId == frame.Id && e.CardCount == count && e.RecipientSeat == 2) == 1 &&
                E<OffTurnUsedCardGiftResolvedEvent>(g).Count(e => e.FrameId == frame.Id && e.CardCount == count && e.RecipientSeat == 2) == 1 &&
                E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Lisi)) &&
                V(g, 0).Hp == 4 && V(g, 0).Skills!.All(s => s.Id != Lisi) &&
                cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(2)) == 1) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == incoming.Id) == 1,
                "Cold native child return survives real acquired-source suppression after payment and completes this whole actual Use exactly once without duplicating material or attack completion.");
        }
        ActualNullificationUseGiftsItsNativeMaterialOnce();
    }

    private static void ActualNullificationUseGiftsItsNativeMaterialOnce()
    {
        var (g, registry) = Create(Scenario.LisiNullification, CardKind.Nullification); Play(g); End(g);
        Reach(g, p => p is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } && p.Choices.Any(c => c.Cards.Count == 1));
        var incoming = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.DrawTwo);
        Require(incoming is { SourceSeat: 1, CardId: 0, PhysicalCardIds.Count: 0 } && g.CreateSnapshot(0).CurrentSeat == 1 &&
            E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == incoming.Id && e.SourceSeat == 1 && e.CardKind == CardKind.DrawTwo) == 1,
            "A real foreign-turn zero-material virtual ordinary trick opens the native single-card Nullification Use boundary.");
        var card = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "nullification").Cards.Single();
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([card])); Reach(g, p => Offer(p, Lisi, 0));
        var accepted = E<CardActionAcceptedEvent>(g).Single(e => e.Action is { ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Nullification } &&
            e.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual([card])).Action;
        var material = E<OffTurnUsedMaterialDiscardedEvent>(g).Single(e => e.Entity.CardId == card);
        Require(accepted.Type == CardActionType.Response && accepted.PhysicalCards.Single().From == CardLocation.Hand(0) &&
            material.Kind == OffTurnUsedCardKind.NullificationUse && material.ActionId == accepted.ActionId && material.CardUseFrameId == incoming.Id &&
            material.Entity.OriginalFrom == CardLocation.Hand(0) && material.ActualTurnOwnerSeat == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.NullificationFinished) == 1,
            "The post-acceptance native capture hook recognizes the actually discarded Nullification material and exact original parent without inventing a second action.");
        Answer(g, Activate); Reach(g, p => Has(p, "off-turn-used-card-gift"));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OffTurnUsedCardGift is not null);
        Require(frame.OffTurnUsedCardGift is { Kind: OffTurnUsedCardKind.NullificationUse } r && r.ActionId == accepted.ActionId &&
            r.CardUseFrameId == incoming.Id && r.OriginalEntities.Select(e => e.CardId).SequenceEqual([card]) &&
            P(g)!.Choices.Any(c => c.Cards.SequenceEqual([card]) && c.Targets.SequenceEqual([2])),
            "The one genuine Nullification Use publishes one whole-material recipient choice.");
        Private(g); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => p is { PlayerSeat: 2 } && p.SkillPrompt?.SkillId == Gain && IsContinue(p));
        Require(V(g, 2).Hand.Any(c => c.Id == card) && E<OffTurnUsedCardGiftResolvedEvent>(g).All(e => e.FrameId != frame.Id),
            "The actual Nullification material is given before its native gain child returns.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(E<OffTurnUsedCardGiftPaidEvent>(g).Count(e => e.FrameId == frame.Id && e.CardCount == 1) == 1 &&
            E<OffTurnUsedCardGiftResolvedEvent>(g).Count(e => e.FrameId == frame.Id && e.CardCount == 1) == 1 &&
            E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == accepted.ActionId) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(2)) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == incoming.Id) == 1,
            "Cold gift child return completes the native Nullification and its original virtual trick once, with one physical gift and one accepted action.");
    }

    public static void OwnTurnAndPureResponsesNeverBecomeOffTurnUsedGifts()
    {
        var (g, registry) = Create(Scenario.Pure, CardKind.Slash); Play(g);
        var own = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]));
        SubmitPlay(g, own); Play(g);
        Require(E<CardUseFinishedEvent>(g).Length == 1 && E<OffTurnUsedMaterialDiscardedEvent>(g).Length == 0 &&
            E<OffTurnUsedCardGiftStartedEvent>(g).Length == 0,
            "A real own-turn physical Slash reaches DiscardPile but cannot become an off-turn gift.");
        Use(g, "hurt-target", [1]); Play(g);
        Require(V(g, 1).Hp == 2, "Native HP loss puts the real foreign Duel responder at HP2 so its AI genuinely prefers its legal Slash.");
        Use(g, "opponent-duel", [1]);
        Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0, IncomingCard: CardKind.Duel });
        var duel = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel);
        Require(duel.CardId == 0 && duel.PhysicalCardIds is { Count: 0 } && duel.SourceSeat == 1 &&
            E<OffTurnUsedCardGiftStartedEvent>(g).Length == 0,
            "A true foreign-turn zero-material Duel has no entity to invent or gift.");
        Answer(g, c => c.Cards.Count == 1);
        Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 1, IncomingCard: CardKind.Duel });
        var choices = P(g)!.Choices.Where(c => c.Cards.Count == 1).Select(c => c.Cards[0]).ToHashSet();
        Require(choices.Count > 0 && V(g, 1).Skills!.Any(s => s.Id == Lisi) && g.CreateSnapshot(0).CurrentSeat == 0,
            "The skill owner really has a published legal pure Slash response during another role's actual turn.");
        g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 1 && choices.Contains(e.CardId) && e.EffectiveCardKind == CardKind.Slash),
            "The NPC really pays a pure Slash response using only its native Advance command.");
        Play(g);
        Require(E<OffTurnUsedMaterialDiscardedEvent>(g).Length == 0 && E<OffTurnUsedCardGiftStartedEvent>(g).Length == 0 &&
            E<OffTurnUsedCardGiftPaidEvent>(g).Length == 0 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == duel.Id) == 1,
            "Pure response completion and a zero-material actual Use neither publish a gift nor fabricate material, even after cold restoration.");
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Offer(PendingDecision p, string skill, int seat) => p.PlayerSeat == seat && p.SkillPrompt?.SkillId == skill && Has(p, "activate");
    private static bool Activate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool MarkChoice(PendingDecision p, string action) => Has(p, "recipient-category-mark") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("mark-action") == action);
    private static ProgramSkillFrame MarkFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RecipientCategoryMark is not null);
    private static int Marker(GameEngine g, int viewer, PlayerMarkerKind kind) => g.CreateSnapshot(viewer).Players[0].Markers?.SingleOrDefault(m => m.Kind == kind)?.Count ?? 0;
    private static void TakeGift(GameEngine g) { Answer(g, Activate); Reach(g, p => MarkChoice(p, "gift")); Answer(g, c => c.Parameters.GetValueOrDefault("mark-action") == "gift"); }
    private static void PublicShown(GameEngine g, IReadOnlyList<CardSnapshot> cards)
    {
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var snapshot = g.CreateSnapshot(viewer); Frozen(snapshot.PublicRevealedCards);
            Require(snapshot.PublicRevealedCards.OrderBy(c => c.Id).SequenceEqual(cards.OrderBy(c => c.Id)),
                "Every prepared view shows exactly the committed face-up gift, with frozen appearance and no unrelated hidden Hand entity.");
        }
    }
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "A genuine Zuo Fen fixture command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 140; i++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            Require(g.CreateSnapshot(0).Status != EngineStatus.Completed, "The fixed native Zuo Fen boundary was not reached before game completion: " + State(g));
            Step(g);
        }
        throw new InvalidOperationException("The fixed native Zuo Fen boundary was not reached: " + State(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not null && Has(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && IsContinue(p)) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Frozen<T>(IReadOnlyList<T> list) => Require(list is System.Collections.IList { IsReadOnly: true }, "Every exposed nested receipt and choice collection is read-only.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The owning native selection is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Other prepared views receive neither private choices nor foreign Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Choice parameters are frozen."); }
        var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot transfer a card, consume a token or change an outstanding native return.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all prepared views, exact typed parents, original physical entities, tokens, facts and accepted commands."); return copy;
    }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario, CardKind kind)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, kind));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:zuo-fen-owner", g.Revision, P(g)!.PromptId));
        Reach(g, p => scenario is Scenario.LisiSingle or Scenario.LisiPair or Scenario.LisiNullification or Scenario.Pure ? p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } : Offer(p, Zhao, 1));
        Require(V(g, 1).GeneralId == "fixture:zuo-fen-peer-1", "Real selection weights place the one original giver at seat1 without a seed search.");
        return (g, registry);
    }
    private sealed class Fixture(Scenario scenario, CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-zuo-fen", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zuo-fen.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-zuo-fen.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, program) in formal.Programs) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description)
                { Program = program, ProgramPresentation = formal.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zuo-fen-driver","revision":1,"modifiers":[
                {"id":"hand-limit","query":"handLimit","operation":"add","value":20,"priority":0,"condition":{"kind":"always"}},
                {"id":"range","query":"attackRange","operation":"add","value":2,"priority":0,"condition":{"kind":"always"}}],
                "activations":[
                  {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                  {"id":"hurt-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                  {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
              {"id":"fixture:zuo-fen-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":6,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zuo-fen-peer-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:zuo-fen-giver","revision":1,"triggers":[{"id":"acquire","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:zhaosong"]}]}]},
              {"id":"fixture:zuo-fen-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:zuo-fen-gain","revision":1,"triggers":[{"id":"native-gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zuo-fen-health","revision":1,"triggers":[{"id":"native-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zuo-fen-discard","revision":1,"triggers":[{"id":"native-discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","sourceZones":["hand","equipment","judgment"],"movementDiscardOnly":true,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:zuo-fen-foreign","revision":1,"triggers":[{"id":"native-attack","window":"afterNormalDraw","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var skills = rules["skills"]!.AsArray();
            if (scenario == Scenario.Gift) skills.Single(n => n!["id"]!.GetValue<string>() == Gain)!["triggers"]![0]!["effects"]!.AsArray()
                .Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:zuo-fen-suppress"]}"""));
            if (scenario is Scenario.LisiSingle or Scenario.LisiPair)
            {
                skills.Single(n => n!["id"]!.GetValue<string>() == Driver)!["triggers"] = JsonNode.Parse("""[{"id":"acquire-lisi","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:lisi"]}]}]""");
                var gainEffects = skills.Single(n => n!["id"]!.GetValue<string>() == Gain)!["triggers"]![0]!["effects"]!.AsArray();
                gainEffects.Add(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}"""));
                gainEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"selectedTarget","amount":1}"""));
                gainEffects.Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:zuo-fen-native-grant"]}"""));
                skills.Add(JsonNode.Parse("""{"id":"fixture:zuo-fen-changed","revision":1,"triggers":[{"id":"paid-native-skills-child","window":"skillsChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"all","children":[{"kind":"compare","left":{"kind":"currentHp"},"operator":"equal","right":{"kind":"integerConstant","value":4}},{"kind":"compare","left":{"kind":"currentHandCount"},"operator":"greaterThan","right":{"kind":"integerConstant","value":4}}]},"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}"""));
            }
            if (scenario == Scenario.LisiNullification) skills.Single(n => n!["id"]!.GetValue<string>() == Foreign)!["triggers"] = JsonNode.Parse("""[{"id":"native-trick","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo"}]}]""");
            if (scenario == Scenario.LisiPair) skills.Single(n => n!["id"]!.GetValue<string>() == Driver)!["viewAs"] = JsonNode.Parse("""[{"id":"pair-dodge","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"sameSuit":true,"outputKind":"dodge","forPlay":false,"forResponse":true,"allowSameKind":true}]""");
            var labels = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var d = new Dictionary<string, object> { ["name"] = id, ["description"] = "固定小实体正式左棻原生行为驱动" };
                if (id is Gain or Health or Discard or Changed) d["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)d;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "固定原生边界") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "真实HP4资格抑制", "测试已发行义务不重验原来源") { SuppressionRule = new(4) });
            b.AddSkill(new(Dummy, "原生技能获取子窗锚点", "仅通过真实GrantSkills获得，令真实SkillsChanged观察者暂停") );
            b.AddSkill(new("fixture:zuo-fen-first", "固定首位其他角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:zuo-fen-peer", "其他角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var owner = new List<string> { "fixture:zuo-fen-initial" };
            if (scenario is Scenario.LisiSingle or Scenario.LisiPair) owner.Add(Suppress);
            if (scenario is Scenario.LisiNullification or Scenario.Pure) owner.Add(Lisi);
            if (scenario == Scenario.Lei) owner.Add(Health);
            b.AddGeneral(new("fixture:zuo-fen-owner", "标记持有者及真实使用者", "supporter", Driver, "jin", 4, owner));
            for (var seat = 1; seat < 4; seat++)
            {
                var peer = new List<string> { "fixture:zuo-fen-peer-initial", "fixture:zuo-fen-quiet" };
                if (seat == 1 && scenario == Scenario.LisiNullification) peer.Remove("fixture:zuo-fen-quiet");
                if (seat == 1 && (scenario is Scenario.Gift or Scenario.Lei or Scenario.Fu or Scenario.Song)) peer.Add("fixture:zuo-fen-giver");
                if (seat == 1 && scenario == Scenario.Gift || seat == 2 && (scenario is Scenario.LisiSingle or Scenario.LisiPair or Scenario.LisiNullification)) peer.Add(Gain);
                if (seat == 2 && (scenario is Scenario.LisiSingle or Scenario.LisiPair)) peer.Add(Changed);
                if (seat == 1 && (scenario is Scenario.LisiSingle or Scenario.LisiPair or Scenario.LisiNullification)) peer.Add(Foreign);
                if (seat == 1 && scenario == Scenario.Pure) peer.Add(Lisi);
                if (seat == 2 && scenario == Scenario.Fu) peer.Add(Discard);
                b.AddGeneral(new($"fixture:zuo-fen-peer-{seat}", "固定真实其他角色", "supporter", seat == 1 ? "fixture:zuo-fen-first" : "fixture:zuo-fen-peer", "qun", 4, peer));
            }
            var card = kind switch { CardKind.Slash => "standard:slash", CardKind.Dodge => "standard:dodge", CardKind.Crossbow => "standard:crossbow", CardKind.Nullification => "standard:nullification", _ => throw new InvalidOperationException("The tiny fixed deck requires one declared real card kind.") };
            b.AddDeck(new("fixture:zuo-fen-deck", "固定小实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(card, Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "完整左棻原生共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zuo-fen-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zuo-fen-owner", "fixture:zuo-fen-peer-1", "fixture:zuo-fen-peer-2", "fixture:zuo-fen-peer-3"]));
        }
    }
}
