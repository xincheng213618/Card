namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardKind AnniversaryChouxiName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash ? CardKind.Slash : kind;
    private bool IsAnniversaryChouxiSource(CharacterState owner, CardConversionSource? source) => source is { SkillId: AnniversarySunQuanComposition.Chouxi } && source.OwnerSeat == owner.Seat &&
        HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) && _contentRegistry.GetSkill(source.SkillId).Program!.ViewAs.Any(v => v.Id == source.BindingId);
    private bool AnniversaryChouxiAvailable(CharacterState owner, CardKind kind)
    {
        if (owner.Seat != _currentSeat || _phase != TurnPhase.Play || !owner.IsAlive ||
            ProgramEventHistory<AnniversaryChouxiNameUsedEvent>().Any(e => e.OwnerSeat == owner.Seat && e.ActualTurn == _turnNumber && e.Name == AnniversaryChouxiName(kind))) return false;
        return AnniversaryMingPiles().SelectMany(p => _cardZones.CardsAt(p.Location)).Any(c => c.Kind == kind);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 40500)]
    private bool BanUnavailableAnniversaryChouxi(ProgramConfiguredViewAsQuery q) => q.Instance.SkillId == AnniversarySunQuanComposition.Chouxi && !AnniversaryChouxiAvailable(q.Owner, q.Rule.OutputKind);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 40500)]
    private bool BanUnavailableAnniversaryChouxiSource(ProgramViewAsLegalityQuery q) => q.Source.SkillId == AnniversarySunQuanComposition.Chouxi &&
        (!IsAnniversaryChouxiSource(q.Owner, q.Source) || q.CardIds.Count != 1 || !AnniversaryChouxiAvailable(q.Owner, q.Kind));
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 40500)]
    private void ValidateSubmittedAnniversaryChouxi(ProgramSubmittedUseQuery q)
    {
        foreach (var source in q.ConversionChain.Where(s => s.SkillId == AnniversarySunQuanComposition.Chouxi))
        {
            if (!IsAnniversaryChouxiSource(_players[q.ActorSeat], source) || q.CardIds.Count != 1 || !AnniversaryChouxiAvailable(_players[q.ActorSeat], q.Kind) ||
                ViewAsRule(source) is not { } rule || rule.OutputKind != q.Kind || _cardZones.GetLocation(q.CardIds[0]) is not { OwnerSeat: { } owner } location || owner != q.ActorSeat || !rule.SourceZones.Contains(location.Zone))
                throw new InvalidOperationException("Chouxi requires its exact owned physical material, an existing private sea name, original source and unused actual-turn card name.");
        }
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 40500)]
    private void CaptureAnniversaryChouxiUse(IGameEvent payload)
    {
        if (payload is not CardActionAcceptedEvent { Action: { } a } || a.ActorSeat != a.ProviderSeat || a.RequesterSeat is not null ||
            a.Type == CardActionType.Response && a.EffectiveKind is not (CardKind.Dodge or CardKind.Nullification) ||
            a.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversarySunQuanComposition.Chouxi) is not { } source ||
            ProgramEventHistory<AnniversaryChouxiNameUsedEvent>().Any(e => e.ActionId == a.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new AnniversaryChouxiNameUsedEvent(a.ActionId, a.ActorSeat, _turnNumber, AnniversaryChouxiName(a.EffectiveKind), source));
    }
    private bool AnniversaryChouxiIssuedUse(long id, int actor) => LifecycleCardUse(id) is { Action: { } a } use && a.ActorSeat == actor && a.ProviderSeat == actor && a.RequesterSeat is null && a.PhysicalCards.Count == 1 &&
        a.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversarySunQuanComposition.Chouxi) is { } source && source.OwnerSeat == actor && !string.IsNullOrWhiteSpace(source.SkillInstanceId) &&
        _contentRegistry.GetSkill(source.SkillId).Program!.ViewAs.Any(v => v.Id == source.BindingId && v.OutputKind == use.CardKind) &&
        (ProgramEventHistory<AnniversaryChouxiNameUsedEvent>().Any(e => e.ActionId == a.ActionId && e.OwnerSeat == actor && e.Name == AnniversaryChouxiName(use.CardKind) && e.Source == source) || IsAnniversaryChouxiSource(_players[actor], source));
    [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption, 40500)]
    private bool AnniversaryChouxiSlashDistance(ProgramSlashConversionDistanceQuery q) => IsAnniversaryChouxiSource(q.Owner, q.Source);
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 40500)]
    private bool AnniversaryChouxiIssuedDistance(ProgramCardDistanceQuery q) => q.CardUseFrameId is { } id && AnniversaryChouxiIssuedUse(id, q.Actor.Seat);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 40500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 40500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 40500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 40500)]
    private bool AnniversaryChouxiCountExemption(long id, int actor) => AnniversaryChouxiIssuedUse(id, actor);
}
