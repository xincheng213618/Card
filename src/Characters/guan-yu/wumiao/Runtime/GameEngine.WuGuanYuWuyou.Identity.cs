namespace CardGame.Core;

public sealed partial class GameEngine
{
    private WuyouHandLease? ActiveWuyouLease(int holder, int cardId)
    {
        if (!HasWuyouCapability || _cardZones.GetLocation(cardId) != CardLocation.Hand(holder)) return null;
        var lease = ProgramEventHistory<WuyouLeaseIssuedEvent>().Where(e => e.Lease.HolderSeat == holder && e.Lease.CardId == cardId)
            .Select(e => e.Lease).OrderByDescending(e => e.MovementCutoff).ThenByDescending(e => e.FrameId).FirstOrDefault();
        return lease is not null && ValidWuyouLease(lease) && !_cardMovements.Any(m => m.CardId == cardId &&
            m.Sequence > lease.MovementCutoff && m.From == CardLocation.Hand(holder) && m.To != m.From) ? lease : null;
    }
    private ProgramCardIdentityMatch? WuyouIdentity(CharacterState holder, Card card) =>
        ActiveWuyouLease(holder.Seat, card.Id) is { } lease ? new(_contentRegistry.GetSkill(lease.Source.SkillId).Program!,
            new SkillProgramCardIdentity(lease.Source.BindingId, [CardZoneKind.Hand], [], [], lease.EffectiveKind,
                new(SkillProgramConditionKind.Always, 0, [])), lease.Source) : null;
    private bool IsWuyouIdentityMaterial(CharacterState holder, Card card, CardKind kind, CardConversionSource? source) =>
        ActiveWuyouLease(holder.Seat, card.Id) is { } lease && source == lease.Source &&
        (lease.EffectiveKind == kind || lease.EffectiveKind == CardKind.Slash && kind == CardKind.FireSlash && HasZhuqueFan(holder));
    private bool IsWuyouIdentityKind(CharacterState holder, Card card, CardKind kind) =>
        ActiveWuyouLease(holder.Seat, card.Id)?.EffectiveKind == kind;
    private IReadOnlyList<CardConversionSource> WuyouIdentitySources(CharacterState holder, Card card, CardKind kind) =>
        ActiveWuyouLease(holder.Seat, card.Id) is { } lease && lease.EffectiveKind == kind ? [lease.Source] : [];

    private bool WuyouNativeProvider(int actor, int provider, CardKind kind) => actor == provider ||
        IsSlashCard(kind) && ActiveFactionCardRequest is { } request && IsFactionSlashUse(request) &&
        request.OwnerSeat == actor && request.CurrentCandidateSeat == provider;

    private WuyouHandLease? WuyouPreUseLease(CharacterState actor, CardKind kind, IReadOnlyList<int>? materialIds)
    {
        if (!HasWuyouCapability || materialIds is not [var id]) return null;
        var from = _cardZones.GetLocation(id);
        if (from is { Zone: CardZoneKind.Hand, OwnerSeat: { } provider } && WuyouNativeProvider(actor.Seat, provider, kind))
            return ActiveWuyouLease(provider, id);
        foreach (var frame in _resolutionStack.AsEnumerable().Reverse())
        {
            var paid = frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration
                ? declaration.Payment : frame.AcceptedDeclarationPayment;
            if (paid is { Claimed: false, Wuyou: { } lease } && paid.ActorSeat == actor.Seat && paid.Cost.CardId == id &&
                WuyouNativeProvider(actor.Seat, paid.ProviderSeat, kind) && ValidWuyouDeclaration(paid, lease)) return lease;
        }
        return null;
    }
    private bool HasWuyouMaterialPrivilege(CharacterState actor, CardKind kind, IReadOnlyList<int>? materialIds,
        long? existingUseFrameId = null) => existingUseFrameId is { } use
            ? HasIssuedWuyouUse(use, actor.Seat) : WuyouPreUseLease(actor, kind, materialIds) is not null;
    private bool HasWuyouPotentialSlash(CharacterState actor) => HasWuyouCapability && GetHand(actor).Any(card =>
        !IsTurnHandCardRestricted(actor, card) && ActiveWuyouLease(actor.Seat, card.Id) is { } lease &&
        (IsSlashCard(lease.EffectiveKind) || SlashKinds.Any(kind => GetProgramViewAsConversions(actor, card, kind, false).Count != 0)));
    // A foreign chooser may know the public invocation and Hand count, never the
    // private return, selected name, or which entity is still in that Hand.
    private bool HasPublicWuyouContributionPotential(CharacterState actor) => HasWuyouCapability &&
        GetHand(actor).Count > 0 && ProgramEventHistory<WuyouStartedEvent>().Any(started => started.DonorSeat == actor.Seat);
    private bool HasPotentialWuyouFactionSlash(CharacterState actor, CardKind kind) => HasWuyouCapability && IsSlashCard(kind) &&
        GetFactionResponsePolicy(actor, CardKind.Slash) is { } policy && GetFactionProviderSeats(actor.Seat, policy.FactionId)
            .Any(seat => HasPublicWuyouContributionPotential(_players[seat]));

    private bool IsWuyouFactionPaymentLegal(FactionCardRequestHandle pending, CardKind kind,
        IReadOnlyList<Card> cards, bool isTrueZhangba = false)
    {
        if (!HasWuyouCapability || !IsFactionSlashUse(pending) || pending.TargetSeat is not { } targetSeat ||
            !SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !HasPotentialWuyouFactionSlash(_players[pending.OwnerSeat], kind)) return true;
        var providerSeat = pending.CurrentCandidateSeat;
        var actor = _players[pending.OwnerSeat]; var provider = _players[providerSeat]; var target = _players[targetSeat];
        var ids = cards.Select(card => card.Id).ToArray(); var suit = PhysicalGroupSuit(actor, cards);
        var color = PhysicalGroupColor(actor, cards); var rank = ProvidedSpecificSlashRank(actor, provider, cards, isTrueZhangba, kind);
        if (pending.IsBorrowedSwordUse) return IsLegalBorrowedSwordSlashTarget(actor, target, kind,
            physicalSuit: suit, allowAnyPhysicalSuit: false, effectiveColor: color, effectiveRank: rank, physicalCardIds: ids);
        if (pending.IsQinglongCrescentBladeUse) return CanUseQinglongCrescentBladeTarget(actor, target, kind,
            color, allowAnyColor: false, physicalSuit: suit, effectiveRank: rank, physicalCardIds: ids);
        if (pending.IsAssistedProgramUse) return IsAssistedProvidedSlashTarget(actor.Seat, targetSeat, kind,
            rank, allowPotentialRank: false, physicalCardIds: ids);
        return CanUseProvidedSlashTarget(actor, target, kind, physicalSuit: suit, allowAnyPhysicalSuit: false,
            effectiveColor: color, effectiveRank: rank, physicalCardIds: ids);
    }

    private void IssueWuyouUse(CardUseFrame use)
    {
        if (!HasWuyouCapability || use.Action is not { Type: CardActionType.Use, PhysicalCards: [var material] } action ||
            material.From.Zone != CardZoneKind.Hand || material.From.OwnerSeat != action.ProviderSeat) return;
        var payment = use.AcceptedDeclarationPayment ?? UnclaimedDeclarationPayment(action.ProviderSeat, material.CardId);
        var lease = payment is { Wuyou: { } original } && payment.Cost.CardId == material.CardId &&
            payment.ActorSeat == action.ActorSeat && ValidWuyouDeclaration(payment, original)
                ? original : ActiveWuyouLease(action.ProviderSeat, material.CardId);
        if (lease is null) return;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || use.WuyouUse is not null ||
            use.PhysicalCardIds is not [var id] || id != material.CardId || use.CardId != id ||
            use.SourceSeat != action.ActorSeat || !WuyouNativeProvider(action.ActorSeat, action.ProviderSeat, use.CardKind) ||
            lease.CardId != material.CardId || lease.HolderSeat != action.ProviderSeat || lease.PrintedKind != material.CardKind ||
            !(use.CardKind == lease.EffectiveKind || lease.EffectiveKind == CardKind.Slash && use.CardKind == CardKind.FireSlash &&
                HasZhuqueFan(_players[action.ActorSeat]) || action.ConversionChain.Any(source => IsJuewuSource(source) &&
                    ViewAsRule(source) is { RankTwoDamageUse: true } rule && (rule.OutputKind == use.CardKind ||
                        rule.OutputKind == CardKind.Slash && use.CardKind == CardKind.FireSlash && HasZhuqueFan(_players[action.ActorSeat])))))
            throw new InvalidOperationException("Wuyou must bind the original provider Hand entity to its one accepted native use.");
        var receipt = new WuyouNativeUseReceipt(lease, use.Id, action.ActionId, action.ActorSeat,
            action.ProviderSeat, use.CardKind, material, _movementSequence);
        ReplaceRuntimeFrame(use.Id, use with { WuyouUse = receipt });
        AdvanceEventRulesAndQueueFact(new WuyouUseAcceptedEvent(receipt));
    }
    private bool HasIssuedWuyouUse(long useId, int actor)
    {
        if (LifecycleCardUse(useId) is not { WuyouUse: { } r, Action: { Type: CardActionType.Use } action } use ||
            r.UseId != use.Id || r.ActionId != action.ActionId || action.ActorSeat != actor || use.SourceSeat != actor ||
            !WuyouIssuedActorMatches(use, r) || r.ProviderSeat != action.ProviderSeat ||
            action.PhysicalCards is not [var material] || material != r.Material ||
            r.Lease.HolderSeat != r.ProviderSeat || r.Lease.CardId != material.CardId ||
            material.From != CardLocation.Hand(r.ProviderSeat) || !ValidWuyouLease(r.Lease) ||
            !(use.CardKind == r.EffectiveKind || IsSlashCard(r.EffectiveKind) && use.CardKind == CardKind.FireSlash &&
                IsCurrentSlashFireChangedUse(use))) return false;
        return ProgramEventHistory<WuyouUseAcceptedEvent>().Where(e => e.Receipt.UseId == useId).ToArray() is [var accepted] && accepted.Receipt == r;
    }
    private bool WuyouIssuedActorMatches(CardUseFrame use, WuyouNativeUseReceipt receipt)
    {
        var actor = receipt.ActorSeat;
        foreach (var change in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id))
        {
            if (change.PreviousActorSeat != actor || change.ProviderSeat != receipt.ProviderSeat) return false;
            actor = change.ActorSeat;
        }
        return use.SourceSeat == actor && use.Action?.ActorSeat == actor;
    }

    private DeclaredCardPayment CaptureWuyouDeclarationPayment(DeclaredCardPayment payment, CardDeclarationParentReturn parent)
    {
        if (parent.Purpose is not (CardDeclarationPurpose.Use or CardDeclarationPurpose.Dodge or CardDeclarationPurpose.Counterspell or
                CardDeclarationPurpose.Recovery or CardDeclarationPurpose.BorrowedSword or CardDeclarationPurpose.ProvidedSlash or
                CardDeclarationPurpose.BorrowedSwordProvidedSlash or CardDeclarationPurpose.QinglongProvidedSlash) ||
            payment.Claimed || payment.Wuyou is not null ||
            ActiveWuyouLease(payment.ProviderSeat, payment.Cost.CardId) is not { } lease) return payment;
        AdvanceEventRulesAndQueueFact(new WuyouDeclarationBoundEvent(payment.DeclarationId, payment.OwnerFrameId,
            payment.ProviderSeat, parent.ActorSeat, payment.Cost.CardId, lease));
        return payment with { Wuyou = lease };
    }
    private bool ValidWuyouDeclaration(DeclaredCardPayment payment, WuyouHandLease lease) => payment.Wuyou == lease &&
        ValidWuyouLease(lease) && lease.HolderSeat == payment.ProviderSeat && lease.CardId == payment.Cost.CardId &&
        payment.Cost.From == CardLocation.Hand(payment.ProviderSeat) &&
        _resolutionStack.SingleOrDefault(f => f.Id == payment.OwnerFrameId) is { } owner &&
        (owner.AcceptedDeclarationPayment == payment || owner is CardDeclarationFrame d && d.Payment == payment) &&
        ProgramEventHistory<WuyouDeclarationBoundEvent>().Where(e => e.DeclarationId == payment.DeclarationId).ToArray() is [var fact] &&
        fact.OwnerFrameId == payment.DeclarationId && fact.ProviderSeat == payment.ProviderSeat &&
        fact.ActorSeat == payment.ActorSeat && fact.CardId == payment.Cost.CardId && fact.Lease == lease;

    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 28520)]
    private void AddWuyouIdentityActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (!HasWuyouCapability) return;
        foreach (var card in GetHand(actor).Where(c => selectedPhysicalCardId is null || selectedPhysicalCardId == c.Id))
        {
            var lease = ActiveWuyouLease(actor.Seat, card.Id); if (lease is null) continue;
            var kind = lease.EffectiveKind; var source = lease.Source; var suit = EffectiveSuit(actor, card); var color = SuitColor(suit);
            if (kind == CardKind.IronChain)
                actions.Add(new(LegalActionKind.Recast, card.Id, null, "武佑：重铸【铁索连环】", kind) { ConversionSource = source });
            if (IsTurnHandCardRestricted(actor, card) || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) continue;
            if (IsOrdinaryTrick(kind) && kind != CardKind.Nullification)
            {
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind, suit, enforceUsePermission: true,
                    beneficiaryShieldSuit: suit, actualEffectiveColor: color, hasActualColor: true, physicalCardIds: [card.Id], conversionSource: source))
                    actions.Add(new(option.ActionKind, card.Id, option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                        "武佑：" + option.Description, kind, option.TargetCardId, option.TargetSeats) { ConversionSource = source });
            }
            else if (kind == CardKind.Peach && IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, kind) && !IsCardTargetProhibited(actor, kind, suit, color) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, suit))
                actions.Add(new(LegalActionKind.Peach, card.Id, actor.Seat, "武佑：使用【桃】", kind) { ConversionSource = source });
            else if (kind == CardKind.Alcohol && actor.HasHpValue && !actor.HasAlcoholEffect &&
                !IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, kind) && !IsCardTargetProhibited(actor, kind, suit, color) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, suit))
                actions.Add(new(LegalActionKind.Alcohol, card.Id, null, "武佑：使用【酒】", kind) { ConversionSource = source });
            else if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
            {
                var actionKind = kind == CardKind.Indulgence ? LegalActionKind.Indulgence :
                    kind == CardKind.SupplyShortage ? LegalActionKind.SupplyShortage : LegalActionKind.Lightning;
                foreach (var target in _players.Where(p => p.IsAlive && !p.JudgmentAreaAbolished &&
                    (kind == CardKind.Lightning ? p.Seat == actor.Seat : p.Seat != actor.Seat) &&
                    !HasJudgmentEffectiveCard(p, kind) && !IsDirectedCardTargetProhibited(actor.Seat, p.Seat, kind) &&
                    !IsCardTargetProhibited(p, kind, suit, color) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, suit)))
                    actions.Add(new(actionKind, card.Id, target.Seat,
                        $"武佑：对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】", kind) { ConversionSource = source });
            }
        }
    }

    private bool TryExecuteWuyouIdentityUse(CharacterState actor, Card card, LegalAction action)
    {
        if (action.Kind == LegalActionKind.Recast || action.PlayedCardKind is not { } kind || !IsOrdinaryTrick(kind) || kind == CardKind.Nullification ||
            !IsWuyouIdentityMaterial(actor, card, kind, action.ConversionSource)) return false;
        var suit = EffectiveSuit(actor, card);
        var adjusted = IsTargetAdjustmentAction(actor, action) && _selectedNextCardTargetSeats?.SequenceEqual(action.TargetSeats) == true;
        var option = BuildProgramOrdinaryTrickUseOptions(actor, kind, suit, enforceUsePermission: true,
            beneficiaryShieldSuit: suit, actualEffectiveColor: SuitColor(suit), hasActualColor: true,
            physicalCardIds: [card.Id], conversionSource: action.ConversionSource)
            .Where(o => o.ActionKind == action.Kind && o.TargetCardId == action.TargetCardId &&
                (o.TargetSeats.SequenceEqual(action.TargetSeats) || adjusted)).OrderByDescending(o => o.TargetSeats.Count).FirstOrDefault()
            ?? throw new InvalidOperationException("Wuyou's original named entity lost its native trick targets.");
        var id = BeginCardUse(card, actor.Seat, option.TargetSeats, kind, conversionSource: action.ConversionSource,
            designatedTargetSeats: kind == CardKind.BorrowedSword && adjusted && action.TargetSeats.Count >= 4
                ? action.TargetSeats.Where((_, index) => index % 2 == 0).ToArray() : null);
        var targets = LifecycleCardUse(id)!.TargetSeats;
        MoveCard(card, CardLocation.Hand(actor.Seat), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, targets, option.ActionKind,
            option.TargetCardId, option.RequiredCardKind, kind);
        return true;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 28520)]
    private void AssertWuyouNativeUses()
    {
        if (!HasWuyouCapability) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.WuyouUse is not null))
            if (!HasIssuedWuyouUse(use.Id, use.SourceSeat))
                throw new InvalidOperationException("Wuyou's accepted native entity lost its original provider, identity or exact action invoice.");
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 30)]
    private bool DistanceCardUseExemption30HasIssuedWuyouUse(ProgramCardDistanceQuery q) =>
        q.CardUseFrameId is { } wuyouUse && HasIssuedWuyouUse(wuyouUse, q.Actor.Seat);


    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 20)]
    private bool DistanceProvidedSlash20HasPotentialWuyouFactionSlash(ProgramSlashDistanceQuery q) =>
        q.AllowAnyPhysicalSuit && q.PhysicalCardIds is null && HasPotentialWuyouFactionSlash(q.Actor, q.Kind);


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 30)]
    private bool DistanceBorrowedSlash30HasPublicWuyouContributionPotential(ProgramSlashDistanceQuery q) =>
        q.PhysicalCardIds is null && q.AllowAnyPhysicalSuit && HasPublicWuyouContributionPotential(q.Actor);

}
