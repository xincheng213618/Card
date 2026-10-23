namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool AnniversaryYizanModified(int owner) => ProgramEventHistory<AnniversaryLongyuanAwakenedEvent>().Any(e => e.Source.OwnerSeat == owner &&
        e.Source.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Longyuan && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash);
    private bool IsAnniversaryYizanSelection(ProgramMultiCardViewAsSelection s) => s.Source.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan &&
        ViewAsRule(s.Source) is { InputCount: 2 } r && r.OutputKind == s.OutputKind;
    private bool AnniversaryYizanMaterials(CharacterState owner, CardConversionSource source, IReadOnlyList<int> ids)
    {
        if (source.SkillId != AnniversaryZhaoTongZhaoGuangComposition.Yizan || source.OwnerSeat != owner.Seat || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) || ViewAsRule(source) is not { } r ||
            ids.Count != r.InputCount || ids.Distinct().Count() != ids.Count || (r.InputCount == 1) != AnniversaryYizanModified(owner.Seat)) return false;
        var cards = GetHand(owner).Concat(GetEquipment(owner)).Where(c => ids.Contains(c.Id)).ToArray();
        return cards.Length == ids.Count && cards.Any(c => GetProgramCardCategory(EffectiveConversionInputKind(owner, c)) == SkillProgramCardCategory.Basic) &&
            (r.InputCount != 1 || cards.All(c => GetProgramCardCategory(EffectiveConversionInputKind(owner, c)) == SkillProgramCardCategory.Basic));
    }
    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 44600)]
    private bool BanUnavailableAnniversaryYizanSingle(ProgramConfiguredViewAsQuery q) => q.Instance.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan &&
        !AnniversaryYizanMaterials(q.Owner, new(q.Instance.SkillId, q.Rule.Id, q.Owner.Seat, q.Instance.SkillInstanceId), [q.Card.Id]);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 44600)]
    private bool BanUnavailableAnniversaryYizan(ProgramViewAsLegalityQuery q) => q.Source.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan &&
        (!AnniversaryYizanMaterials(q.Owner, q.Source, q.CardIds) || ViewAsRule(q.Source) is not { } r ||
            r.OutputKind != q.Kind && !(q.ForResponse && q.Kind == CardKind.Slash && r.OutputKind == CardKind.FireSlash));
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 44600)]
    private void ValidateAnniversaryYizanUse(ProgramSubmittedUseQuery q)
    {
        foreach (var source in q.ConversionChain.Where(s => s.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan))
            if (!AnniversaryYizanMaterials(_players[source.OwnerSeat], source, q.CardIds) || ViewAsRule(source)?.OutputKind != q.Kind)
                throw new InvalidOperationException("Yizan requires its exact live HE materials, at least one basic card, and the current original or awakened cost.");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44600)]
    private void CountActualAnniversaryYizan(IGameEvent payload)
    {
        CardActionContext a; long? useId = null;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } action } use &&
            use.Id == declared.ResolutionId && use.CardId == declared.CardId && use.CardKind == declared.CardKind && action.ActorSeat == declared.SourceSeat)
        { a = action; useId = use.Id; }
        else if (payload is CardActionAcceptedEvent accepted)
        { a = accepted.Action; useId = a.Type == CardActionType.Use ? _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == a.ActionId)?.Id : null; }
        else return;
        var sources = a.ConversionChain.Where(s => s.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan).ToArray();
        if (sources.Length == 0) return;
        if (sources is not [var source] || !IsValidPlayerSeat(source.OwnerSeat) || source.OwnerSeat != a.ProviderSeat ||
            ViewAsRule(source) is not { } r || a.PhysicalCards.Count != r.InputCount || a.PhysicalCards.Select(c => c.CardId).Distinct().Count() != r.InputCount ||
            a.PhysicalCards.Any(c => c.From.OwnerSeat != source.OwnerSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            r.OutputKind != a.EffectiveKind && !(a.Type == CardActionType.Response && a.EffectiveKind == CardKind.Slash && r.OutputKind == CardKind.FireSlash))
            throw new InvalidOperationException("An accepted Yizan action lost its exact native provider, conversion binding or frozen physical costs.");
        if (ProgramEventHistory<AnniversaryYizanInvokedEvent>().Any(e => e.ActionId == a.ActionId)) return;
        if (a.Type == CardActionType.Use && useId is null)
            throw new InvalidOperationException("An actual Yizan Use must retain its original native CardUse frame.");
        AdvanceEventRulesAndQueueFact(new AnniversaryYizanInvokedEvent(a.ActionId, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, _turnNumber, r.InputCount == 1, a.PhysicalCards.Count, useId));
    }

    private bool TryResolveAnniversaryYizanDyingAlcohol(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        if (selection.OutputKind != CardKind.Alcohol || !IsAnniversaryYizanSelection(selection)) return false;
        if (ActiveDying is not { } dying || dying.VictimSeat != owner.Seat || dying.ResponderSeat != owner.Seat ||
            !AnniversaryYizanMaterials(owner, selection.Source, selection.Cards.Select(c => c.Id).ToArray()) ||
            HasSelfCardTargetProhibition(owner.Seat) || IsCardUseForbidden(owner.Seat, CardKind.Alcohol, CardActionType.Use))
            throw new InvalidOperationException("Yizan Alcohol must be its actual owner's native self-rescue with the current pair cost.");
        ClearPendingDecision();
        ResolveRecoveryCard(owner, owner, selection.Cards[0], "酒", CardKind.Alcohol, conversionSource: selection.Source,
            dyingResponse: new(dying.FrameId, owner.Seat, false, null, true, selection.Cards[0].Id), physicalCards: selection.Cards);
        return true;
    }
    private bool TryFinishAnniversaryYizanAlcoholCost(CardUseFrame use, Card card)
    {
        if (use.CardKind != CardKind.Alcohol || use.Action is not { Type: CardActionType.Use } a ||
            a.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversaryZhaoTongZhaoGuangComposition.Yizan) is not { } source ||
            ViewAsRule(source) is not { InputCount: 2 }) return false;
        if (use.CardId != card.Id || use.PhysicalCardIds is not { Count: 2 } ids || !ids.SequenceEqual(a.PhysicalCards.Select(c => c.CardId)) ||
            ProgramEventHistory<AnniversaryYizanInvokedEvent>().Count(e => e.ActionId == a.ActionId && e.Source == source && e.NativeUseFrameId == use.Id && !e.Modified && e.MaterialCount == 2) != 1)
            throw new InvalidOperationException("Yizan Alcohol lost its once-accepted native two-material invoice.");
        foreach (var id in ids)
            if (_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(c => c.Id == id) is { } material)
                MoveCard(material, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
        return true;
    }
}
