using System.Text;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasSiruiCapability => _contentRegistry.ProgramDependencies.UsesEqualNameLengthUse;

    private static int SiruiNameLength(CardKind kind) => CardCatalog.Get(kind).DisplayName.EnumerateRunes().Count();
    private bool IsSiruiSource(CardConversionSource? source) => source is { SkillId: "le:sirui" } &&
        ViewAsRule(source) is { EqualNameLengthUse: true };

    private ActualDiscardRecoveryPhaseKey? SiruiCurrentPhase(int actor)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        return _setupComplete && _winner == Winner.None && _phase == TurnPhase.Play &&
            phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } && phase.ActorSeat == actor &&
            phase.ActualTurnOwnerSeat == actor && _turnProgression.OwnerSeat == actor &&
            _currentSeat == actor && IsRecordedActualDiscardRecoveryPhase(phase) &&
            (IsEndOwnedExtraPlayPhase() || !ProgramEventHistory<TurnEndedEvent>()
                .Any(e => e.TurnNumber == phase.TurnNumber && e.ActorSeat == actor))
            ? phase : null;
    }

    private bool CanUseSiruiPhase(CharacterState owner, CardConversionSource source) =>
        owner.IsAlive && source.OwnerSeat == owner.Seat && IsSiruiSource(source) &&
        HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) &&
        SiruiCurrentPhase(owner.Seat) is { } phase && !ProgramEventHistory<SiruiPhaseUseIssuedEvent>()
            .Any(e => e.Source.OwnerSeat == owner.Seat && e.Phase.Token == phase.Token);

    private bool CanUseSiruiConversion(CharacterState owner, Card card, IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, bool forResponse, bool dyingUse = false, int? requestedUseTarget = null)
    {
        if (!rule.EqualNameLengthUse) return true;
        var source = new CardConversionSource(instance.SkillId, rule.Id, owner.Seat, instance.SkillInstanceId);
        var location = _cardZones.GetLocation(card.Id);
        if (!CanUseSiruiPhase(owner, source) || SiruiPureProviderFrontier(owner.Seat) || location.OwnerSeat != owner.Seat ||
            location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            SiruiNameLength(card.Kind) != SiruiNameLength(rule.OutputKind) ||
            IsTurnHandCardRestricted(owner, card) || HasProgramCardIdentity(owner, card) ||
            !IsActivityOrdinaryTrickNameAvailable(rule.OutputKind)) return false;
        return !forResponse && !dyingUse && requestedUseTarget is null ||
            IsSiruiTrueResponseUse(owner, rule.OutputKind, dyingUse, requestedUseTarget);
    }

    private bool SiruiPureProviderFrontier(int actor)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long owner) => top?.Id == owner || top is ResponseWindowFrame response && response.ParentFrameId == owner;
        return ActiveFactionCardRequest is { AwaitingProviders: true } f && f.CurrentCandidateSeat == actor && At(f.OwnerFrameId) ||
            ActiveFactionDefense is { } d && d.CurrentCandidateSeat == actor && At(d.Attack.ResolutionId);
    }

    private bool IsSiruiTrueResponseUse(CharacterState owner, CardKind kind, bool dyingUse = false,
        int? requestedUseTarget = null)
    {
        if (SiruiCurrentPhase(owner.Seat) is null) return false;
        if (requestedUseTarget is { } target)
            return kind == CardKind.Peach && IsWenyiPeachSupplyQuery(owner, target);
        if (dyingUse || kind is CardKind.Peach or CardKind.Alcohol)
            return ActiveDying is { } dying && dying.ResponderSeat == owner.Seat &&
                (kind == CardKind.Peach || kind == CardKind.Alcohol && dying.VictimSeat == owner.Seat);
        if (kind == CardKind.Nullification)
            return _resolutionStack.LastOrDefault() is NullificationWindowFrame n &&
                n.CandidateIndex < n.CandidateSeats.Count && n.CandidateSeats[n.CandidateIndex] == owner.Seat;
        if (kind == CardKind.Dodge) return IsProgramResponseCardUse(owner, kind);
        if (!IsSlashCard(kind) || ActiveFactionCardRequest is { AwaitingProviders: true }) return false;
        if (ActiveBorrowedSword is { ActiveAttack: null } borrowed && borrowed.WeaponOwnerSeat == owner.Seat) return true;
        if (ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == owner.Seat &&
            SameAttackOwner(qinglong.Attack, ActiveCardAttack)) return true;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || GetPausedPrivateOfferEffect(f) is not { } effect) return false;
        if (IsQigongRequest(f)) return f.LvKuangLvXiang!.Actor == owner.Seat;
        if (IsCaoChunInitialRequest(f) || IsWuJingHejiRequest(f)) return f.OwnerSeat == owner.Seat;
        return RoundOnceCardlessProgramActor(f, effect.Op, owner.Seat);
    }

    private bool SiruiIgnoresSlashDistance(CharacterState actor, CardConversionSource? source) =>
        source is not null && IsSiruiSource(source) && IsSlashCard(ViewAsRule(source)!.OutputKind) &&
        CanUseSiruiPhase(actor, source);
    private bool SiruiIgnoresSlashLimit(CharacterState actor, CardConversionSource? source) => SiruiIgnoresSlashDistance(actor, source);

    private IReadOnlyList<SlashUseVariant> SiruiPhysicalSlashVariants(CharacterState actor, Card card) =>
        SlashKinds.Where(kind => GetProgramViewAsConversions(actor, card, kind, forResponse: false)
                .Any(source => IsSiruiSource(source) && ViewAsRule(source)!.OutputKind == kind))
            .Select(kind => new SlashUseVariant(kind, AdditionalConversionSource: null, UsesZhuqueFan: false)).ToArray();

    private bool SiruiExplicitForcedSlashFrontier(CharacterState actor)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long parent) => top?.Id == parent || top is ResponseWindowFrame response && response.ParentFrameId == parent;
        return ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } borrowed &&
                borrowed.WeaponOwnerSeat == actor.Seat && At(borrowed.ResolutionId) ||
            ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == actor.Seat &&
                SameAttackOwner(qinglong.Attack, ActiveCardAttack) && At(qinglong.Attack.ResolutionId);
    }

    private bool SiruiNativeConversionMatchesOutput(CharacterState actor, CardConversionSource source, CardKind kind) =>
        !IsSiruiSource(source) || !SiruiExplicitForcedSlashFrontier(actor) || ViewAsRule(source)!.OutputKind == kind;

    private bool SiruiAllowsUnspecifiedForcedSlash(CharacterState actor, Card card, CardKind kind) =>
        !IsSlashCard(kind) || !SiruiExplicitForcedSlashFrontier(actor) || IsNativeResponseCard(card, kind) ||
        SiruiPhysicalSlashVariants(actor, card).Count == 0;

    private bool SiruiSlashAliasAllowed(CharacterState actor, bool forResponse) =>
        IsSiruiTrueResponseUse(actor, CardKind.Slash) &&
        (!forResponse || ActiveBorrowedSword is not null || ActiveQinglongCrescentBlade is not null ||
            _resolutionStack.LastOrDefault() is ProgramSkillFrame);

    private bool HasSiruiForcedSlash(CharacterState actor, CharacterState target, QinglongCrescentBladeHandle pending) =>
        UsesFormalQinglongCrescentBlade && pending.Attack.SourceSeat == actor.Seat &&
        pending.Attack.TargetSeat == target.Seat && SameAttackOwner(pending.Attack, ActiveCardAttack) &&
        CanUseQinglongCrescentBladeTarget(actor, target) && SlashKinds.Any(kind => HasSiruiSlashDistance(actor, kind));

    private bool SiruiBorrowedChoiceAllowed(CharacterState actor, CharacterState target, Card card, PromptChoice choice)
    {
        if (SiruiPhysicalSlashVariants(actor, card).Count == 0) return true;
        if (!Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("response-card-kind"), out var kind) ||
            !IsSlashCard(kind)) return false;
        _ = TryReadConversionSource(choice.Parameters, out var source);
        var from = _cardZones.GetLocation(card.Id);
        var excluded = from == CardLocation.Equipment(actor.Seat) &&
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon ? card.Id : (int?)null;
        // The coarse native request may exist because Sirui can reach this
        // target. Its unconverted sibling choice must still prove its own range.
        return CanUseSlashTarget(actor, target, card, source, kind, physicalCardIds: [card.Id],
            excludedEquipmentId: excluded);
    }

    private bool IsSiruiPhysicalSlashKind(CharacterState actor, Card card, CardKind kind) =>
        IsSlashCard(kind) && GetProgramViewAsConversions(actor, card, kind, forResponse: false)
            .Any(source => IsSiruiSource(source) && ViewAsRule(source)!.OutputKind == kind);

    private CardKind SiruiSelectedSlashKind(CharacterState actor, Card card, CardKind fallback,
        CardConversionSource? explicitSource = null)
    {
        var selected = explicitSource ?? _selectedUseConversion ?? _selectedResponseConversion;
        return selected is not null && IsSiruiSource(selected) && ViewAsRule(selected) is { } rule &&
            IsSlashCard(rule.OutputKind) && GetProgramViewAsConversions(actor, card, rule.OutputKind, false).Contains(selected)
            ? rule.OutputKind : fallback;
    }

    private bool HasSiruiSlashDistance(CharacterState actor, CardKind kind, IReadOnlyList<int>? materialIds = null,
        CardConversionSource? conversionSource = null, long? existingUseFrameId = null)
    {
        if (!IsSlashCard(kind)) return false;
        if (existingUseFrameId is { } existing && materialIds is [var issuedId] && _resolutionStack.OfType<CardUseFrame>().Any(use =>
                use.Id == existing &&
                use.Sirui is { NativeKind: SiruiNativeUseKind.CardUse } r && r.CardId == issuedId &&
                r.Source.OwnerSeat == actor.Seat && IsSlashCard(r.OutputKind) &&
                (use.CardKind == kind || IsCurrentSlashFireChangedUse(use) && kind == CardKind.FireSlash) &&
                ValidSiruiInvoice(use, r, requirePayment: false))) return true;
        if (existingUseFrameId is not null || materialIds is { Count: not 1 }) return false;
        if (materialIds is not null && conversionSource is null) return false;
        var cards = GetHand(actor).Concat(GetEquipment(actor));
        if (materialIds is [var id]) cards = cards.Where(c => c.Id == id);
        return cards.Any(card => GetSkillBindingShard(actor).ProgramInstances.Any(instance =>
            instance.Program.ViewAs.Any(rule => rule.EqualNameLengthUse && rule.OutputKind == kind &&
                (conversionSource is null || conversionSource == new CardConversionSource(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId)) &&
                CanUseSiruiConversion(actor, card, instance, rule, forResponse: false))));
    }
    private bool SiruiIgnoresAlcoholLimit(CharacterState actor, CardConversionSource? source) =>
        source is not null && IsSiruiSource(source) &&
        ViewAsRule(source)!.OutputKind is CardKind.Alcohol or CardKind.RealgarWine && CanUseSiruiPhase(actor, source);
    private bool SiruiIgnoresTrickDistance(CharacterState actor, CardConversionSource? source,
        IReadOnlyList<int>? materialIds = null) => source is not null && IsSiruiSource(source) &&
        ViewAsRule(source)!.OutputKind == CardKind.Snatch && CanUseSiruiPhase(actor, source) &&
        materialIds is [var id] && _cardZones.GetLocation(id).OwnerSeat == actor.Seat &&
        _cardZones.GetLocation(id).Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        SiruiNameLength(_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id).Kind) == SiruiNameLength(CardKind.Snatch);

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 20)]
    private bool SiruiConversionUnavailable(ProgramConfiguredViewAsQuery query) =>
        !CanUseSiruiConversion(query.Owner, query.Card, query.Instance, query.Rule, query.ForResponse, query.DyingUse, query.RequestedUseTarget);

    [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption, 10)]
    private bool SiruiIgnoresSlashDistanceQuery(ProgramSlashConversionDistanceQuery q) =>
        SiruiIgnoresSlashDistance(q.Owner, q.Source);


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 40)]
    private bool DistanceBorrowedSlash40HasSiruiSlashDistance(ProgramSlashDistanceQuery q) =>
        HasSiruiSlashDistance(q.Actor,q.Kind,q.PhysicalCardIds,q.Conversion);


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 30)]
    private bool DistanceBorrowedPhysicalSlash30IsSiruiPhysicalSlashKind(ProgramSlashDistanceQuery q) =>
        IsSiruiPhysicalSlashKind(q.Actor,q.PhysicalCard!,q.Kind);


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 20)]
    private bool DistanceBorrowedPaidWeaponSlash20SiruiPhysicalSlashVariants(ProgramSlashDistanceQuery q) =>
        SiruiPhysicalSlashVariants(q.Actor,q.PhysicalCard!).Concat(JuewuPhysicalSlashVariants(q.Actor,q.PhysicalCard!)).Concat(FulePhysicalSlashVariants(q.Actor, q.PhysicalCard!)).Any();

}
