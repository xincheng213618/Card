using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IPrivateOfferProgramHost
    {
        public SkillProgramStepOutcome DepositPrivateCardOffer(ProgramSkillFrame f, string bind, string resolver) => engine.DepositPrivateCardOffer(f, bind, resolver);
        public SkillProgramStepOutcome ResolvePrivateCardOffer(ProgramSkillFrame f) => engine.BeginDeferredPrivateOffer(f);
        public SkillProgramStepOutcome ResolveGameTargetHandHp(ProgramSkillFrame f, string state) => engine.BeginGameTargetHandHp(f, state);
        public bool CanContinueIssuedPrivateOffer(ProgramSkillFrame f) => engine.CanContinueIssuedPrivateOffer(f);
    }
    private long PrivateOfferSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string PrivateOfferReason(ProgramSkillFrame f, string segment) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.ResolveDeferredPrivateCardOffer}.{segment}";
    private static CardConversionSource PrivateOfferSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome DepositPrivateCardOffer(ProgramSkillFrame supplied, string bind, string resolver)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var set = GetProgramCardSet(f, bind);
        var program = _contentRegistry.GetSkill(f.SkillId).Program!;
        if (f.PrivateOffer is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            f.OwnerSeat != _currentSeat || context.OwnerSeat != f.OwnerSeat || f.SelectedTargetSeats is not [var target] ||
            target == f.OwnerSeat || !_players[target].IsAlive || set.CardIds is not [var id] ||
            !set.SourceLocations.SequenceEqual([CardLocation.Hand(f.OwnerSeat)]) || _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat) ||
            program.Triggers.SingleOrDefault(t => t.Id == resolver) is not { Optional: false, Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } trigger ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveDeferredPrivateCardOffer }])
            throw new InvalidOperationException("A deferred private offer requires one exact ending Hand entity, living other and mandatory resolver.");
        var grant = _players[f.OwnerSeat].SkillGrants.Grants.Single(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId);
        var offer = new DeferredPrivateOfferIdentity(f.Id, GetProgramBindingId(f), resolver, f.GameplayHash, target, _turnNumber);
        var identity = new PrivateTurnHoldIdentity(f.Id, f.SkillId, f.SkillInstanceId, grant.SourceId, int.MaxValue) { DeferredOffer = offer };
        var location = new CardLocation(CardZoneKind.PrivateTurnHold, f.OwnerSeat, privateTurnHold: identity);
        _cardZones.EnsurePrivateTurnHold(location);
        var before = PrivateOfferSequence;
        ReplaceRuntimeTop(f = f with { PrivateOffer = new(f.InstructionIndex, location, id, PrivateOfferStage.DepositChildren, before, before),
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetHand(_players[f.OwnerSeat]).Single(c => c.Id == id), CardLocation.Hand(f.OwnerSeat), location,
            new($"skill-program.{f.SkillId}.{SkillProgramEffectOp.DepositBoundPrivateCardOffer}"));
        var after = PrivateOfferSequence;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { PrivateOffer = f.PrivateOffer! with { SequenceAfter = after } });
        AdvanceEventRulesAndQueueFact(new PrivateCardOfferDepositedEvent(f.Id, PrivateOfferSource(f), f.GameplayHash, target, _turnNumber, before, after));
        if (!TryDrainFireTargetMovement(f)) ReturnPrivateOfferMovement(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool TryBeginDeferredPrivateOffers(CharacterState target)
    {
        if (_winner != Winner.None || !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DepositBoundPrivateCardOffer)) return false;
        var locations = _cardZones.PrivateTurnHoldLocations.Where(l => l.PrivateTurnHold?.DeferredOffer is { } o &&
            o.TargetSeat == target.Seat && o.CreatedTurn < _turnNumber && l.OwnerSeat is { } source && _players[source].IsAlive && _cardZones.CardsAt(l).Count == 1)
            .OrderBy(l => l.PrivateTurnHold!.HoldId).ToArray();
        if (locations.Length == 0) return false;
        if (_resolutionStack.Count != 0 || _pendingDecision is not null || target.Seat != _currentSeat)
            throw new InvalidOperationException("A deferred offer must start at its exact clean actual turn boundary.");
        var candidates = locations.Select((location, index) =>
        {
            var h = location.PrivateTurnHold!; var o = h.DeferredOffer!;
            if (!ValidPrivateOfferDeposit(location, out _)) throw new InvalidOperationException("A deferred offer lost its original private deposit ledger.");
            return new ProgramTriggerCandidate(location.OwnerSeat!.Value, h.SkillId, o.ResolverBindingId, h.SkillInstanceId, o.GameplayHash, 0, index);
        }).ToArray();
        var root = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, target.Seat,
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Array.AsReadOnly(candidates),
            ProgramLifecycleContinuation.ResumeAfterDeferredPrivateOffers, CaptureProgramTriggerFacts(target))
        { DeferredPrivateOffers = new(_turnNumber, target.Seat, locations) };
        PushRuntimeFrame(root); AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }
    private bool ValidPrivateOfferDeposit(CardLocation location, out int cardId)
    {
        cardId = 0;
        if (location is not { Zone: CardZoneKind.PrivateTurnHold, OwnerSeat: { } source, PrivateTurnHold: { DeferredOffer: { } o } h } ||
            source == o.TargetSeat || !IsValidPlayerSeat(source) || !IsValidPlayerSeat(o.TargetSeat) || h.ExpiresTurnNumber != int.MaxValue ||
            h.HoldId != o.DepositFrameId || _contentRegistry.Skills.GetValueOrDefault(h.SkillId)?.Program is not { } program || program.GameplayHash != o.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == o.ResolverBindingId)?.Effects is not [{ Op: SkillProgramEffectOp.ResolveDeferredPrivateCardOffer }]) return false;
        var facts = CompleteProgramEventHistory().OfType<PrivateCardOfferDepositedEvent>().Where(e => e.FrameId == o.DepositFrameId).ToArray();
        if (facts is not [var fact] || fact.Source != new CardConversionSource(h.SkillId, o.DepositBindingId, source, h.SkillInstanceId) ||
            fact.GameplayHash != o.GameplayHash || fact.TargetSeat != o.TargetSeat || fact.CreatedTurn != o.CreatedTurn || fact.After <= fact.Before) return false;
        var moves = _cardMovements.Where(m => m.Sequence > fact.Before && m.Sequence <= fact.After).ToArray();
        if (moves is not [var move] || move.From != CardLocation.Hand(source) || move.To != location ||
            move.Reason.Value != $"skill-program.{h.SkillId}.{SkillProgramEffectOp.DepositBoundPrivateCardOffer}") return false;
        cardId = move.CardId; return true;
    }
    private CardLocation? CurrentDeferredPrivateOffer(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not
                { Continuation: ProgramLifecycleContinuation.ResumeAfterDeferredPrivateOffers, DeferredPrivateOffers: { } due } parent ||
            parent.OwnerSeat != _currentSeat || due.TargetSeat != _currentSeat || due.TurnNumber != _turnNumber ||
            due.Locations.Count != parent.Candidates.Count || parent.CandidateIndex < 0 || parent.CandidateIndex >= due.Locations.Count ||
            parent.Candidates[parent.CandidateIndex] != candidate || context.OwnerSeat != candidate.OwnerSeat ||
            context.SourceSeat != due.TargetSeat || context.TargetSeat != due.TargetSeat || context.OccurrenceIndex != candidate.OccurrenceIndex)
            return null;
        var location = due.Locations[parent.CandidateIndex]; var h = location.PrivateTurnHold!; var o = h.DeferredOffer!;
        return location.OwnerSeat == candidate.OwnerSeat && h.SkillId == candidate.SkillId && h.SkillInstanceId == candidate.SkillInstanceId &&
            o.GameplayHash == candidate.GameplayHash && o.ResolverBindingId == candidate.BindingId && o.TargetSeat == due.TargetSeat &&
            o.CreatedTurn < due.TurnNumber && ValidPrivateOfferDeposit(location, out _) ? location : null;
    }
    private bool CanRunDeferredPrivateOffer(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        CurrentDeferredPrivateOffer(candidate, context) is { } location && _players[candidate.OwnerSeat].IsAlive &&
        _players[_currentSeat].IsAlive && _cardZones.CardsAt(location).Count == 1 && _winner == Winner.None;
    private bool IsDeferredPrivateOfferResolver(ProgramTriggerCandidate candidate) =>
        _contentRegistry.Skills.GetValueOrDefault(candidate.SkillId)?.Program?.Triggers.SingleOrDefault(t => t.Id == candidate.BindingId)
            ?.Effects is [{ Op: SkillProgramEffectOp.ResolveDeferredPrivateCardOffer }];
    private CardLocation? DeferredPrivateOfferParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } context) return null;
        return CurrentDeferredPrivateOffer(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex), context);
    }
    private bool CanContinueIssuedPrivateOffer(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        DeferredPrivateOfferParent(f) is not null && (f.PrivateOffer is null || ValidPrivateOfferReceipt(f));
    private SkillProgramStepOutcome BeginDeferredPrivateOffer(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var location = DeferredPrivateOfferParent(f) ?? throw new InvalidOperationException("A private offer lost its exact due parent/source.");
        if (f.PrivateOffer is not null || !ValidPrivateOfferDeposit(location, out var id) || _cardZones.GetLocation(id) != location ||
            f.InstructionIndex != 1) throw new InvalidOperationException("A due private offer cannot be viewed twice or reconstructed from a different card.");
        ReplaceRuntimeTop(f = f with { PrivateOffer = new(1, location, id, PrivateOfferStage.ChoosingExchange, 0, 0) });
        AdvanceEventRulesAndQueueFact(new PrivateCardOfferViewedEvent(f.Id, location.PrivateTurnHold!.HoldId, _currentSeat, _turnNumber));
        PublishPrivateOffer(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> PrivateOfferChoices(ProgramSkillFrame f)
    {
        var r = f.PrivateOffer!; var actor = r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat;
        var card = _cardZones.CardsAt(r.Location).Single(c => c.Id == r.CardId); var category = GetProgramCardCategory(card.Kind);
        var choices = GetHand(_players[actor]).Where(c => GetProgramCardCategory(c.Kind) == category).Select(c =>
            new PromptChoice(new($"private-offer.{f.Id}.give-{c.Id}"), $"交给来源【{c.DisplayName}】，获得暗置的【{card.DisplayName}】", [c.Id], [actor],
                new Dictionary<string, string> { ["program-action"] = "deferred-private-offer", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "exchange" })).ToList();
        choices.Add(new(new($"private-offer.{f.Id}.remove"), $"移去暗置的【{card.DisplayName}】并失去1点体力", [], [actor],
            new Dictionary<string, string> { ["program-action"] = "deferred-private-offer", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "remove" }));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishPrivateOffer(ProgramSkillFrame f)
    {
        var actor = f.PrivateOffer!.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat; var choices = PrivateOfferChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, actor, "观看笔伐暗置牌并选择同类型手牌交换，或移去并失去体力。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [actor], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = actor, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[actor].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveDeferredPrivateOfferChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No owning private offer.");
        if (f.PrivateOffer is not { Stage: PrivateOfferStage.ChoosingExchange } r || !ValidPrivateOfferReceipt(f) ||
            _pendingDecision is not { IsPrivate: true } decision || decision.PlayerSeat != r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat ||
            !AssistedChoicesEqual([selected], PrivateOfferChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The private offer choice changed its exact target or material.");
        ClearPendingDecision(); var target = decision.PlayerSeat; var before = PrivateOfferSequence;
        if (selected.Parameters["branch"] == "exchange")
        {
            var id = selected.Cards.Single(); var from = CardLocation.Hand(target);
            ReplaceRuntimeTop(f = f with { PrivateOffer = r with { Stage = PrivateOfferStage.ExchangeChildren, SequenceBefore = before,
                SequenceAfter = before, PaymentCardId = id, PaymentFrom = from }, PendingMovementContinuation = new(target, 0, null) });
            MoveCard(GetHand(_players[target]).Single(c => c.Id == id), from, CardLocation.Hand(f.OwnerSeat), new(PrivateOfferReason(f, "exchange")));
        }
        else
        {
            ReplaceRuntimeTop(f = f with { PrivateOffer = r with { Stage = PrivateOfferStage.RemovalChildren, SequenceBefore = before,
                SequenceAfter = before }, PendingMovementContinuation = new(target, 0, null) });
            MoveCard(_cardZones.CardsAt(r.Location).Single(c => c.Id == r.CardId), r.Location, CardLocation.DiscardPile, new(PrivateOfferReason(f, "remove")));
        }
        CommitPrivateOfferSegment(f.Id); if (!TryDrainFireTargetMovement(GetActiveProgramFrame(f.Id))) ReturnPrivateOfferMovement(GetActiveProgramFrame(f.Id));
    }
    private void CommitPrivateOfferSegment(long id)
    {
        var f = GetActiveProgramFrame(id); var after = PrivateOfferSequence;
        ReplaceRuntimeTop(f = f with { PrivateOffer = f.PrivateOffer! with { SequenceAfter = after } });
        AdvanceEventRulesAndQueueFact(new PrivateCardOfferSegmentIssuedEvent(f.Id, f.PrivateOffer!.Location.PrivateTurnHold!.HoldId,
            f.PrivateOffer.Stage, 1, f.PrivateOffer.SequenceBefore, after));
    }
    private IReadOnlyList<CardSnapshot> DeferredPrivateOfferViewedCards(int viewer) => Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.PrivateOffer is { Stage: PrivateOfferStage.ChoosingExchange } r && r.Location.PrivateTurnHold!.DeferredOffer!.TargetSeat == viewer &&
            ValidPrivateOfferReceipt(f) && _pendingDecision is { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } p && p.PlayerSeat == viewer &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "deferred-private-offer" &&
                c.Parameters.GetValueOrDefault("frame-id") == f.Id.ToString(CultureInfo.InvariantCulture)))
        .Select(f => ToSnapshot(_cardZones.CardsAt(f.PrivateOffer!.Location).Single(c => c.Id == f.PrivateOffer.CardId))).ToArray());
}
