namespace CardGame.Core;

public sealed partial class GameEngine
{
    private NativePublicPileCardRequest NativePublicPileUseRequest(int actor, int provider,
        CardKind? kind = null, long? parent = null) =>
        new(parent, _pendingDecision?.PromptId, actor, provider, actor == provider ? null : actor,
            null, kind, null, NativePublicPileDirection.Use);

    private NativePublicPileCardRequest NativePublicPileResponseRequest(CharacterState provider, CardKind kind)
    {
        long? ownerId = null;
        var actor = provider.Seat;
        int? requester = null;
        ProgramCardContinuation? continuation = null;
        int? cursor = null, epoch = null;
        var direction = NativePublicPileDirection.Played;
        if (ActiveDying is { } dying && dying.ResponderSeat == provider.Seat && kind is CardKind.Peach or CardKind.Alcohol)
        { ownerId = dying.FrameId; cursor = dying.ResponderIndex; direction = NativePublicPileDirection.Use; }
        else if (kind == CardKind.Nullification && ActiveNullificationWindow is { } counterspell)
        { ownerId = counterspell.Id; cursor = counterspell.CandidateIndex; epoch = counterspell.ChainDepth; direction = NativePublicPileDirection.Use; continuation = ProgramCardContinuation.NullificationResponse; }
        else if (kind == CardKind.Dodge && ActiveFactionDefense is { } defense && defense.CurrentCandidateSeat == provider.Seat)
        { ownerId = defense.OwnerFrameId; cursor = defense.CandidateIndex; actor = defense.OwnerSeat; requester = actor; continuation = ProgramCardContinuation.FactionDefenseDodge; }
        else if (kind == CardKind.Slash && ActiveFactionCardRequest is { AwaitingProviders: true } supply && supply.CurrentCandidateSeat == provider.Seat)
        { ownerId = supply.OwnerFrameId; cursor = supply.CandidateIndex; actor = supply.OwnerSeat; requester = actor; }
        else if (kind == CardKind.Slash && ActiveBorrowedSword is { AwaitingSlashChoice: true } borrowed && borrowed.WeaponOwnerSeat == provider.Seat)
        { ownerId = borrowed.ResolutionId; direction = NativePublicPileDirection.Use; }
        else if (kind == CardKind.Slash && ActiveQinglongCrescentBlade is { } followup && followup.Attack.SourceSeat == provider.Seat)
        { ownerId = followup.OwnerFrameId; direction = NativePublicPileDirection.Use; }
        else if (kind == CardKind.Slash && ActiveDuel is { } duel && duel.ResponderSeat == provider.Seat)
        { ownerId = duel.ResolutionId; cursor = duel.SuccessfulSlashResponses; continuation = ProgramCardContinuation.DuelSlash; }
        else if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack, CurrentAttack: { } groupAttack } group && groupAttack.TargetSeat == provider.Seat)
        { ownerId = group.ResolutionId; cursor = group.TargetIndex; epoch = groupAttack.SuccessfulDodgeResponses; continuation = ProgramCardContinuation.GroupResponse; }
        else if (kind == CardKind.Dodge && ActiveCardAttack is { } attack && attack.TargetSeat == provider.Seat)
        { ownerId = attack.ResolutionId; cursor = attack.SuccessfulDodgeResponses; direction = NativePublicPileDirection.Use; continuation = ProgramCardContinuation.Dodge; }
        return new(ownerId, _pendingDecision?.PromptId, actor, provider.Seat, requester,
            actor, kind, continuation, direction) { RequestCursor = cursor, RequestEpoch = epoch };
    }

    private IReadOnlyList<NativePublicPileCardSelection> GetNativePublicPileOriginalCards(NativePublicPileCardRequest request)
    {
        if ((!HasLiFengCapability && !HasMouGszMobile) || !IsValidPlayerSeat(request.ActorSeat) || !IsValidPlayerSeat(request.ProviderSeat) ||
            !_players[request.ActorSeat].IsAlive || !_players[request.ProviderSeat].IsAlive || _winner != Winner.None)
            return [];
        if (_resolutionStack.Any(f => f.NativePublicPilePayment is { SequenceAfter: 0 } pending &&
                pending.Request.ProviderSeat == request.ProviderSeat && pending.Request != request)) return [];
        return ShuliangOriginalGrainChoices(request).Concat(MouGszHuOriginalChoices(request)).Where(selection => selection.Request == request &&
            selection.AccessSource.OwnerSeat == request.ProviderSeat && selection.Pile.OwnerSeat == request.ProviderSeat &&
            Enum.IsDefined(selection.Card.Kind) && _cardZones.GetLocation(selection.Card.Id) == selection.Pile.Location &&
            PublicPileCards(selection.Pile).Any(c => c == selection.Card) &&
            (request.RequiredKind is null || MatchesRequiredCard(selection.Card.Kind, request.RequiredKind.Value)) &&
            (IsShuliangOriginalGrainChoice(request, selection) || IsMouGszHuOriginalChoice(request, selection))).DistinctBy(selection => selection.Card.Id).ToArray();
    }

    private NativePublicPileCardSelection RequireNativePublicPileOriginalCard(NativePublicPileCardRequest request,
        NativePublicPileCardSelection selection) => GetNativePublicPileOriginalCards(request).SingleOrDefault(s => s == selection)
        ?? throw new InvalidOperationException("An original public-pile card lost its exact live source, request or shared quota.");

    private IReadOnlyList<Card> NativePublicPileResponseCards(CharacterState provider, CardKind kind)
    {
        var request = NativePublicPileResponseRequest(provider, kind);
        return request.ParentFrameId is null ? [] : GetNativePublicPileOriginalCards(request).Select(s => s.Card).ToArray();
    }

    private IReadOnlyList<Card> NativePublicPileSlashUseCards(CharacterState provider)
    {
        var response = NativePublicPileResponseRequest(provider, CardKind.Slash);
        var request = response.ParentFrameId is null
            ? NativePublicPileUseRequest(provider.Seat, provider.Seat, CardKind.Slash, _resolutionStack.LastOrDefault()?.Id)
            : response;
        return GetNativePublicPileOriginalCards(request).Select(s => s.Card).ToArray();
    }

    private IReadOnlyList<Card> NativePublicPileOriginalUseCards(CharacterState provider) =>
        GetNativePublicPileOriginalCards(NativePublicPileUseRequest(provider.Seat, provider.Seat,
            parent: _resolutionStack.LastOrDefault()?.Id)).Select(s => s.Card).ToArray();

    private void PrepareNativePublicPileProvidedSlash(FactionCardRequestHandle request,
        CharacterState provider, IReadOnlyList<Card> cards, CardConversionSource? conversion)
    {
        if (conversion is not null || cards is not [var card] || _cardZones.GetLocation(card.Id).Zone != CardZoneKind.PublicPersistentPile) return;
        var frozen = new NativePublicPileCardRequest(request.OwnerFrameId, _pendingDecision?.PromptId,
            request.OwnerSeat, provider.Seat, request.OwnerSeat, request.OwnerSeat,
            CardKind.Slash, null, NativePublicPileDirection.Played) { RequestCursor = request.CandidateIndex };
        var choice = GetNativePublicPileOriginalCards(frozen).SingleOrDefault(s => s.Card == card)
            ?? throw new InvalidOperationException("A supplied original grain lost its exact provider and native principal.");
        PrepareNativePublicPilePayment(_resolutionStack.Single(f => f.Id == request.OwnerFrameId), choice);
    }

    private Card? FindNativePublicPileOriginalCard(CharacterState provider, int id)
    {
        var prepared = _resolutionStack.LastOrDefault(f => f.NativePublicPilePayment is { SequenceAfter: 0 } receipt &&
            receipt.Request.ProviderSeat == provider.Seat && receipt.Card.Id == id)?.NativePublicPilePayment;
        if (prepared is not null && _cardZones.GetLocation(id) == prepared.Pile.Location) return prepared.Card;
        if (_phase != TurnPhase.Play || _currentSeat != provider.Seat) return null;
        return GetNativePublicPileOriginalCards(NativePublicPileUseRequest(provider.Seat, provider.Seat))
            .SingleOrDefault(s => s.Card.Id == id)?.Card;
    }

    private IReadOnlyList<LegalAction> NativePublicPileOriginalPlayActions(CharacterState actor, int? selectedId)
    {
        var selections = GetNativePublicPileOriginalCards(NativePublicPileUseRequest(actor.Seat, actor.Seat));
        return selections.Where(s => selectedId is null || s.Card.Id == selectedId).SelectMany(s =>
            BuildLegalActionsCore(actor, includeProgramActions: false, selectedPhysicalCardId: s.Card.Id,
                    nativeOriginalCard: s.Card)
                .Where(a => a.CardId == s.Card.Id && a.ConversionSource is null &&
                    (a.AdditionalConversionSources is null || a.AdditionalConversionSources.Count == 0) &&
                    a.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or
                        LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral) &&
                    a.MaxCardCount <= 1 && (a.PlayedCardKind ?? s.Card.Kind) is var kind &&
                    (kind == s.Card.Kind || s.Card.Kind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(actor)))
                .Select(a => a with { NativePublicPileSource = s.AccessSource,
                    Description = $"【{_contentRegistry.GetSkill(s.AccessSource.SkillId).Name}】{a.Description}" })).ToArray();
    }

    private bool IsNativePublicPileOriginalChoice(CharacterState owner, Card card, CardKind kind, bool response)
    {
        if ((!HasLiFengCapability && !HasMouGszMobile) || card.Id <= 0 ||
            _cardZones.GetLocation(card.Id) is not { Zone: CardZoneKind.PublicPersistentPile } location ||
            location.OwnerSeat != owner.Seat) return false;
        var prepared = _resolutionStack.LastOrDefault(f => f.NativePublicPilePayment is { SequenceAfter: 0 } p &&
            p.Request.ProviderSeat == owner.Seat && p.Card.Id == card.Id)?.NativePublicPilePayment;
        if (prepared is not null) return prepared.Card == card && _cardZones.GetLocation(card.Id) == prepared.Pile.Location &&
            (card.Kind == kind || IsSlashCard(card.Kind) && kind == CardKind.Slash ||
                card.Kind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(_players[prepared.Request.ActorSeat]));
        var request = response ? NativePublicPileResponseRequest(owner, kind) : NativePublicPileUseRequest(owner.Seat, owner.Seat);
        return (MatchesRequiredCard(card.Kind, kind) || card.Kind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(owner)) &&
            GetNativePublicPileOriginalCards(request).Any(s => s.Card == card);
    }

    private void PrepareNativePublicPilePayment(ResolutionFrame parent, NativePublicPileCardSelection selection)
    {
        selection = RequireNativePublicPileOriginalCard(selection.Request, selection);
        if (parent.NativePublicPilePayment is { } previous &&
            (previous.SequenceAfter == 0 || previous.ActionId is null))
            throw new InvalidOperationException("A native public-pile request still owns its previous payment.");
        ReplaceRuntimeFrame(parent.Id, parent with { NativePublicPilePayment = new(parent.Id, selection,
            _turnNumber, _turnProgression.OwnerSeat, _movementSequence) });
    }

    private void PrepareNativePublicPileResponsePayment(CharacterState provider, Card card,
        CardKind kind, CardConversionSource? conversion)
    {
        if (PrepareMouGszHuResponse(provider, [card], kind, conversion)) return;
        if (conversion is not null || card.Id <= 0 || _cardZones.GetLocation(card.Id).Zone != CardZoneKind.PublicPersistentPile ||
            _resolutionStack.Any(f => f.NativePublicPilePayment is { SequenceAfter: 0 } p &&
                p.Request.ProviderSeat == provider.Seat && p.Card.Id == card.Id)) return;
        var request = NativePublicPileResponseRequest(provider, kind);
        var selection = GetNativePublicPileOriginalCards(request).SingleOrDefault(s => s.Card == card)
            ?? throw new InvalidOperationException("An original public-pile response is no longer authorized.");
        var parent = _resolutionStack.SingleOrDefault(f => f.Id == request.ParentFrameId)
            ?? throw new InvalidOperationException("An original public-pile response lost its native mother.");
        PrepareNativePublicPilePayment(parent, selection);
    }

    private void PrepareNativePublicPileUse(CardUseFrame use)
    {
        PrepareMouGszHuUse(use);
        if (use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost], ConversionChain.Count: 0 } action ||
            cost.From.Zone != CardZoneKind.PublicPersistentPile) return;
        if (IsFriendYangmingOriginalUseParent(use)) return;
        var previous = _resolutionStack.Count > 1 ? _resolutionStack[^2] : null;
        if (previous?.NativePublicPilePayment is { SequenceAfter: 0 } selected &&
            selected.Card.Id == cost.CardId && selected.Card.Kind == cost.CardKind && selected.Pile.Location == cost.From &&
            selected.Request.ActorSeat == action.ActorSeat && selected.Request.ProviderSeat == action.ProviderSeat)
        {
            ReplaceRuntimeFrame(previous.Id, previous with { NativePublicPilePayment = null });
            ReplaceRuntimeFrame(use.Id, use with { NativePublicPilePayment = selected with { OwnerFrameId = use.Id, ActionId = action.ActionId } });
            return;
        }
        var request = use.CardKind is CardKind.Peach or CardKind.Alcohol && ActiveDying is { } dying &&
            dying.ResponderSeat == action.ActorSeat && action.ActorSeat == action.ProviderSeat
            ? NativePublicPileResponseRequest(_players[action.ProviderSeat], use.CardKind)
            : NativePublicPileUseRequest(action.ActorSeat, action.ProviderSeat, parent: use.Id);
        var choice = GetNativePublicPileOriginalCards(request).SingleOrDefault(s => s.Card.Id == cost.CardId &&
            s.Card.Kind == cost.CardKind && s.Pile.Location == cost.From)
            ?? throw new InvalidOperationException("An original public-pile use has no exact native source authorization.");
        PrepareNativePublicPilePayment(use, choice);
        var current = _resolutionStack.Single(f => f.Id == use.Id);
        ReplaceRuntimeFrame(use.Id, current with { NativePublicPilePayment = current.NativePublicPilePayment! with { ActionId = action.ActionId } });
    }

    private bool TryNativePublicPilePaymentLocation(CharacterState provider, Card card, out CardLocation from)
    {
        var payment = _resolutionStack.LastOrDefault(f => f.NativePublicPilePayment is { SequenceAfter: 0 } p &&
            p.Request.ProviderSeat == provider.Seat && p.Card.Id == card.Id)?.NativePublicPilePayment;
        from = payment?.Pile.Location ?? default;
        return payment is not null && payment.Card == card && payment.OwnerFrameId > 0 &&
            payment.ActualTurn == _turnNumber && payment.TurnOwnerSeat == _turnProgression.OwnerSeat &&
            _cardZones.GetLocation(card.Id) == from;
    }

    private void CaptureNativePublicPilePaymentBatch(CardMovementBatchContext batch)
    {
        if (batch.ParentFrameId is not { } id || _resolutionStack.SingleOrDefault(f => f.Id == id) is not
            { NativePublicPilePayment: { SequenceAfter: 0 } receipt } parent) return;
        var entries = batch.Movements.Where(m => m.CardId == receipt.Card.Id && m.CardKind == receipt.Card.Kind &&
            m.From == receipt.Pile.Location && m.To == CardLocation.Processing &&
            m.Sequence > receipt.SequenceBefore && (m.Reason == CardMoveReasons.Use ||
                m.Reason == CardMoveReasons.EquipmentUse || m.Reason == CardMoveReasons.IronChainUse ||
                m.Reason == CardMoveReasons.Respond || m.Reason == CardMoveReasons.Nullification)).ToArray();
        if (entries.Length == 0) return;
        if (entries is not [var entered] || entered.Sequence <= 0 || entered.Sequence > _cardMovements.Count ||
            _cardMovements[(int)entered.Sequence - 1] != entered || receipt.ActualTurn != _turnNumber ||
            receipt.TurnOwnerSeat != _turnProgression.OwnerSeat)
            throw new InvalidOperationException("An original public-pile payment lost its one real native entry.");
        var paid = receipt with { SequenceAfter = entered.Sequence };
        ReplaceRuntimeFrame(id, parent with { NativePublicPilePayment = paid });
        AdvanceEventRulesAndQueueFact(new NativePublicPileOriginalPaidEvent(id, entered.CardId, entered.CardKind,
            entered.From, entered.Sequence, entered.Reason, paid.Source, paid.Request.ActorSeat, paid.Request.ProviderSeat,
            paid.Request.RequesterSeat, paid.Request.ResponderSeat, paid.Request.Direction, paid.ActualTurn, paid.TurnOwnerSeat,
            paid.Request.RequestCursor, paid.Request.RequestEpoch));
        if (paid.Source.SkillId == "ol:shuliang") RecordShuliangGrainPayment(paid);
        else if (!IsMouGszHuOriginalPayment(paid)) throw new InvalidOperationException("An original public-pile payment lost its independently authorized source.");
        if (paid.ActionId is { } actionId)
            AdvanceEventRulesAndQueueFact(new NativePublicPileOriginalBoundEvent(id, actionId, entered.CardId, entered.Sequence));
    }

    private void BindNativePublicPilePayment(ResolutionFrame parent, CardActionContext action)
    {
        if (parent.NativePublicPilePayment is not { SequenceAfter: > 0, ActionId: null } receipt ||
            action.PhysicalCards is not [var cost] || cost.CardId != receipt.Card.Id || cost.CardKind != receipt.Card.Kind ||
            cost.From != receipt.Pile.Location || action.ProviderSeat != receipt.Request.ProviderSeat ||
            action.ActorSeat != receipt.Request.ActorSeat || action.RequesterSeat != receipt.Request.RequesterSeat ||
            action.ConversionChain.Count != 0) return;
        ReplaceRuntimeFrame(parent.Id, parent with { NativePublicPilePayment = receipt with { ActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new NativePublicPileOriginalBoundEvent(parent.Id, action.ActionId, cost.CardId, receipt.SequenceAfter));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27590)]
    private void BindAcceptedNativePublicPileOriginal(IGameEvent payload)
    {
        if (payload is not CardActionAcceptedEvent { Action: { } action }) return;
        foreach (var parent in _resolutionStack.ToArray()) BindNativePublicPilePayment(parent, action);
    }

    private bool IsNativePublicPileOriginalActionCost(CardActionContext action, CardActionCost cost) =>
        action.PhysicalCards is [var only] && only == cost && action.ConversionChain.Count == 0 &&
        cost.From.Zone == CardZoneKind.PublicPersistentPile && cost.From.OwnerSeat == action.ProviderSeat &&
        ProgramEventHistory<NativePublicPileOriginalBoundEvent>().Where(e => e.ActionId == action.ActionId && e.CardId == cost.CardId)
            .Any(bound => ProgramEventHistory<NativePublicPileOriginalPaidEvent>().Any(paid =>
                paid.ParentFrameId == bound.ParentFrameId && paid.CardId == bound.CardId && paid.EntrySequence == bound.EntrySequence &&
                paid.From == cost.From && paid.CardKind == cost.CardKind && paid.ActorSeat == action.ActorSeat &&
                paid.ProviderSeat == action.ProviderSeat && paid.RequesterSeat == action.RequesterSeat));

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 27590)]
    private void AssertNativePublicPileOriginalPayments()
    {
        foreach (var parent in _resolutionStack)
        {
            if (parent.NativePublicPilePayment is not { } paid) continue;
            if (paid.OwnerFrameId != parent.Id || paid.Source.OwnerSeat != paid.Request.ProviderSeat ||
                paid.Pile.OwnerSeat != paid.Request.ProviderSeat || paid.Selection.Request != paid.Request ||
                !Enum.IsDefined(paid.Request.Direction) || paid.SequenceBefore < 0 || paid.SequenceBefore > _movementSequence ||
                paid.SequenceAfter < 0 || paid.SequenceAfter > _movementSequence ||
                paid.SequenceAfter > 0 && paid.SequenceAfter <= paid.SequenceBefore ||
                paid.Request.RequestCursor is < 0 || paid.Request.RequestEpoch is < 0)
                throw new InvalidOperationException("An original public-pile payment lost its owning frame and immutable native request.");
            if (paid.SequenceAfter == 0) continue;
            var records = ProgramEventHistory<NativePublicPileOriginalPaidEvent>().Where(e =>
                e.ParentFrameId == parent.Id && e.EntrySequence == paid.SequenceAfter).ToArray();
            if (records is not [var fact] || fact.Source != paid.Source || fact.CardId != paid.Card.Id ||
                fact.CardKind != paid.Card.Kind || fact.From != paid.Pile.Location ||
                fact.ActorSeat != paid.Request.ActorSeat || fact.ProviderSeat != paid.Request.ProviderSeat ||
                fact.RequesterSeat != paid.Request.RequesterSeat || fact.ResponderSeat != paid.Request.ResponderSeat ||
                fact.Direction != paid.Request.Direction || fact.ActualTurn != paid.ActualTurn ||
                fact.TurnOwnerSeat != paid.TurnOwnerSeat || fact.RequestCursor != paid.Request.RequestCursor ||
                fact.RequestEpoch != paid.Request.RequestEpoch || !_cardMovements.Any(m =>
                    m.Sequence == fact.EntrySequence && m.CardId == fact.CardId && m.CardKind == fact.CardKind &&
                    m.From == fact.From && m.To == CardLocation.Processing && m.Reason == fact.Reason))
                throw new InvalidOperationException("An original public-pile payment lost its one actual native entry.");
            if (paid.ActionId is { } actionId && ProgramEventHistory<NativePublicPileOriginalBoundEvent>().Count(e =>
                    e.ParentFrameId == parent.Id && e.ActionId == actionId && e.CardId == paid.Card.Id &&
                    e.EntrySequence == paid.SequenceAfter) != 1)
                throw new InvalidOperationException("An original public-pile payment lost its exact accepted native action.");
        }
    }
}
