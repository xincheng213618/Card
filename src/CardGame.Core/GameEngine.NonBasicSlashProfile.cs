namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueNonBasicSlashPolicy(long useId, CardActionContext? action)
    {
        if (action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards: [var cost] } ||
            action.ConversionChain.SingleOrDefault(s => ViewAsRule(s)?.NonBasicUnrespondableSlash == true) is not { } source) return;
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("A non-basic Slash lost its real native Use.");
        if (source.OwnerSeat != action.ActorSeat || action.ProviderSeat != action.ActorSeat || cost.From.OwnerSeat != source.OwnerSeat ||
            cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || CardUseCategoryCatalog.Get(cost.CardKind) == CardUseCategories.Basic ||
            !HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) ||
            _cardZones.GetLocation(cost.CardId) != cost.From || use.NonBasicSlashPolicy is not null)
            throw new InvalidOperationException("A non-basic Slash policy requires its exact active conversion and one actual owned material.");
        var issued = new IssuedCardNoResponse(action.ActionId, useId, source, _turnNumber, _cardUseDebitPhaseInstanceId);
        var policy = new ConvertedNonBasicSlashPolicy(useId, action.ActionId, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, cost);
        UpdateLifecycleCardUse(useId, u => u with { NonBasicSlashPolicy = policy });
        AdvanceEventRulesAndQueueFact(new ConvertedNonBasicSlashIssuedEvent(useId, action.ActionId, source,
            _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, cost.CardId, cost.CardKind, cost.From));
        if (use.IssuedNoResponse is null) AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(issued));
    }
    private bool HasIssuedNonBasicSlashDistance(long? useId)
    {
        if (useId is not { } id || LifecycleCardUse(id) is not { Action: { } current, NonBasicSlashPolicy: { } policy } use) return false;
        if (current.Type != CardActionType.Use || use.CardKind != current.EffectiveKind) return false;
        var action = current;
        if (use.CurrentSlashFirePolicy is { Converted: true } fire)
        {
            if (!IsCurrentSlashFireChangedUse(use)) return false;
            AssertCurrentSlashFirePolicy(use); action = fire.OriginalAction;
        }
        return policy.CardUseFrameId == id && action.ActionId == policy.ActionId && action.EffectiveKind == CardKind.Slash &&
            action.PhysicalCards is [var cost] && cost == policy.Material && cost.From.OwnerSeat == policy.Source.OwnerSeat &&
            cost.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && CardUseCategoryCatalog.Get(cost.CardKind) != CardUseCategories.Basic &&
            action.ConversionChain.Contains(policy.Source) && ViewAsRule(policy.Source)?.NonBasicUnrespondableSlash == true &&
            _contentRegistry.GetSkill(policy.Source.SkillId).Program?.GameplayHash == policy.GameplayHash &&
            CompleteProgramEventHistory().OfType<ConvertedNonBasicSlashIssuedEvent>().Count(e => e.CardUseFrameId == id && e.ActionId == policy.ActionId &&
                e.Source == policy.Source && e.GameplayHash == policy.GameplayHash && e.MaterialId == cost.CardId && e.MaterialKind == cost.CardKind && e.From == cost.From) == 1;
    }
    private void AssertNonBasicSlashPolicies()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.NonBasicSlashPolicy is not null))
            if (!HasIssuedNonBasicSlashDistance(use.Id)) throw new InvalidOperationException("A non-basic Slash lost its separate exact issued material/source invoice.");
    }
    private bool IgnoresNonBasicSlashDistance(CharacterState owner, CardConversionSource source) =>
        source.OwnerSeat == owner.Seat && HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) &&
        ViewAsRule(source)?.NonBasicUnrespondableSlash == true;

    [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption, 20)]
    private bool IgnoresNonBasicSlashDistanceQuery(ProgramSlashConversionDistanceQuery q) =>
        IgnoresNonBasicSlashDistance(q.Owner, q.Source);


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 80)]
    private bool DistancePhysicalSlash80HasIssuedNonBasicSlashDistance(ProgramSlashDistanceQuery q) =>
        HasIssuedNonBasicSlashDistance(q.ExistingUseFrameId);

}
