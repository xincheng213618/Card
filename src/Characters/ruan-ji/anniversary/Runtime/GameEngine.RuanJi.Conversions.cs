namespace CardGame.Core;

public sealed partial class GameEngine
{
    private RuanJiShownEntityAuthorizedEvent? RuanJiAuthorization(int owner, string instance, int cardId, bool red) =>
        ProgramEventHistory<RuanJiShownEntityAuthorizedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner &&
            e.Source.SkillId == AnniversaryRuanJiComposition.Zhaowen && e.Source.SkillInstanceId == instance &&
            e.ActualTurn == _turnNumber && e.CardId == cardId && e.IsRed == red &&
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash);
    private bool RuanJiNameAvailable(int owner, CardKind kind) => !ProgramEventHistory<RuanJiNameUsedEvent>().Any(e =>
        e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.Name == kind);
    private bool RuanJiConversionAvailable(CharacterState owner, Card card, CardConversionSource source, CardKind kind) =>
        source.OwnerSeat == owner.Seat && owner.IsAlive && card.Id > 0 && _cardZones.Contains(CardLocation.Hand(owner.Seat), card.Id) &&
        HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) &&
        RuanJiAuthorization(owner.Seat, source.SkillInstanceId!, card.Id, false) is not null && RuanJiNameAvailable(owner.Seat, kind);

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 42800)]
    private bool BanUnavailableRuanJiConversion(ProgramConfiguredViewAsQuery q) => q.Instance.SkillId == AnniversaryRuanJiComposition.Zhaowen &&
        !RuanJiConversionAvailable(q.Owner, q.Card, new(q.Instance.SkillId, q.Rule.Id, q.Owner.Seat, q.Instance.SkillInstanceId), q.Rule.OutputKind);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 42800)]
    private bool BanUnavailableRuanJiSubmittedSource(ProgramViewAsLegalityQuery q) => q.Source.SkillId == AnniversaryRuanJiComposition.Zhaowen &&
        (q.CardIds is not [var id] || id <= 0 || !_cardZones.Contains(CardLocation.Hand(q.Owner.Seat), id) ||
         !RuanJiConversionAvailable(q.Owner, GetAdvancedCard(id), q.Source, q.Kind));
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 42800)]
    private void ValidateRuanJiSubmittedConversion(ProgramSubmittedUseQuery q)
    {
        foreach (var source in q.ConversionChain.Where(s => s.SkillId == AnniversaryRuanJiComposition.Zhaowen))
            if (q.CardIds is not [var id] || id <= 0 || !_cardZones.Contains(CardLocation.Hand(q.ActorSeat), id) ||
                !RuanJiConversionAvailable(_players[q.ActorSeat], GetAdvancedCard(id), source, q.Kind) ||
                ViewAsRule(source)?.OutputKind != q.Kind)
                throw new InvalidOperationException("Zhaowen requires the original shown black Hand entity, its exact source instance and an unused conversion name in this actual turn.");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42800)]
    private void RecordRuanJiConvertedName(IGameEvent payload)
    {
        if (payload is not CardActionAcceptedEvent { Action: { } a } ||
            a.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversaryRuanJiComposition.Zhaowen) is not { } source) return;
        if (ProgramEventHistory<RuanJiNameUsedEvent>().Any(e => e.ActionId == a.ActionId && e.Source == source)) return;
        if (!IsRuanJiActualUse(a) || a.ActorSeat != a.ProviderSeat || a.RequesterSeat is not null || source.OwnerSeat != a.ActorSeat ||
            a.PhysicalCards is not [var material] || material.From != CardLocation.Hand(a.ActorSeat) ||
            RuanJiAuthorization(a.ActorSeat, source.SkillInstanceId!, material.CardId, false) is null || !RuanJiNameAvailable(a.ActorSeat, a.EffectiveKind) ||
            ViewAsRule(source)?.OutputKind != a.EffectiveKind)
            throw new InvalidOperationException("Zhaowen's accepted use lost its genuine shown physical material or per-name actual-turn quota.");
        AdvanceEventRulesAndQueueFact(new RuanJiNameUsedEvent(a.ActionId, source, _turnNumber, a.EffectiveKind, material.CardId));
    }
    private static bool IsRuanJiActualUse(CardActionContext a) => a.Type == CardActionType.Use ||
        a.Type == CardActionType.Response && a.ActorSeat == a.ProviderSeat && a.RequesterSeat is null && a.ResponderSeat == a.ActorSeat &&
        a.EffectiveKind is CardKind.Dodge or CardKind.Nullification;
    private bool HasRuanJiShownRedUse(int owner, string instance, CardActionContext action) => IsRuanJiActualUse(action) &&
        action.ActorSeat == owner && action.ProviderSeat == owner && action.RequesterSeat is null &&
        action.PhysicalCards.Any(c => c.From == CardLocation.Hand(owner) && RuanJiAuthorization(owner, instance, c.CardId, true) is not null);
}
