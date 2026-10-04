namespace CardGame.Core;

public sealed record OrdinaryTrickCannotNullifyReceipt(CardConversionSource Source, long CardUseFrameId,
    long ActionId, int ActorSeat, CardKind EffectiveKind);
public sealed record OrdinaryTrickCannotNullifyIssuedEvent(OrdinaryTrickCannotNullifyReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly bool _hasOrdinaryTrickCannotNullifyCapability;
    private void ObserveUnnullifiableOrdinaryTrick(IGameEvent payload)
    {
        if (!_hasOrdinaryTrickCannotNullifyCapability || payload is not CardUseDeclaredEvent declared ||
            LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.EffectiveKind == CardKind.Nullification || !IsOrdinaryTrick(action.EffectiveKind)) return;
        var binding = CardPolicies(_players[action.ActorSeat], SkillProgramCardPolicyKind.CannotNullifyOwnOrdinaryTrick, action.EffectiveKind).FirstOrDefault();
        if (binding.Source is null) return;
        if (use.OrdinaryTrickCannotNullify is not null || use.SourceSeat != action.ActorSeat || use.CardKind != action.EffectiveKind ||
            declared.SourceSeat != action.ActorSeat || declared.CardKind != action.EffectiveKind)
            throw new InvalidOperationException("Cannot-nullify policy lost its real original trick declaration.");
        var receipt = new OrdinaryTrickCannotNullifyReceipt(new(binding.Source.SkillId, binding.Policy.Id, action.ActorSeat,
            binding.Source.SkillInstanceId), use.Id, action.ActionId, action.ActorSeat, action.EffectiveKind);
        ReplaceRuntimeFrame(use.Id, use with { OrdinaryTrickCannotNullify = receipt });
        AdvanceEventRulesAndQueueFact(new OrdinaryTrickCannotNullifyIssuedEvent(receipt));
    }
    private bool IsOriginalOrdinaryTrickCannotNullify(long useId)
    {
        if (LifecycleCardUse(useId)?.OrdinaryTrickCannotNullify is not { } r) return false;
        AssertOrdinaryTrickCannotNullify(LifecycleCardUse(useId)!); return true;
    }
    private void AssertOrdinaryTrickCannotNullify(CardUseFrame use)
    {
        if (use.OrdinaryTrickCannotNullify is not { } r) return;
        if (!_hasOrdinaryTrickCannotNullifyCapability || r.CardUseFrameId != use.Id ||
            use.Action is not { Type: CardActionType.Use } action || r.ActionId != action.ActionId ||
            r.ActorSeat != action.ActorSeat || r.ActorSeat != use.SourceSeat || r.Source.OwnerSeat != r.ActorSeat ||
            r.EffectiveKind != use.CardKind || r.EffectiveKind != action.EffectiveKind ||
            !IsOrdinaryTrick(r.EffectiveKind) || r.EffectiveKind == CardKind.Nullification ||
            string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            _contentRegistry.Skills.GetValueOrDefault(r.Source.SkillId)?.Program?.CardPolicies.SingleOrDefault(p =>
                p.Id == r.Source.BindingId && p.Kind == SkillProgramCardPolicyKind.CannotNullifyOwnOrdinaryTrick) is not { } policy ||
            !policy.CardKinds.Contains(r.EffectiveKind) ||
            CompleteProgramEventHistory().OfType<OrdinaryTrickCannotNullifyIssuedEvent>().Count(e => e.Receipt == r) != 1 ||
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id &&
                e.SourceSeat == r.ActorSeat && e.CardKind == r.EffectiveKind && e.CardId == use.CardId) != 1)
            throw new InvalidOperationException("An issued cannot-nullify ordinary trick lost its exact original use/facts.");
    }
}
