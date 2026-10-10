namespace CardGame.Core;

public sealed partial record PlayerSnapshot
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TurnHandCategoryRestriction>? TurnHandCategoryRestrictions
    {
        get => _handCategoryRestrictions;
        init => _handCategoryRestrictions = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
    private readonly IReadOnlyList<TurnHandCategoryRestriction>? _handCategoryRestrictions;
}
public sealed partial class GameEngine
{
    private void GrantDamageSourceHandCategory(ProgramSkillFrame f, SkillProgramCardCategory category)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } victim } c ||
            victim != f.OwnerSeat || !IsValidPlayerSeat(source) || !_players[source].IsAlive || ActiveDamageTrigger is not { } damage ||
            damage.Id != c.ParentFrameId || damage.ParentFrameId != c.DamageFrameId || GetDamageTriggerAttack(damage).IsSourceLess ||
            _resolutionStack.Count < 2 || _resolutionStack[^2].Id != damage.Id || damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            !MountObserverCandidateMatches(f, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()))
        { CancelProgramBindingAndCleanup(f, "伤害来源已不存在，未发行手牌类别限制。"); return; }
        var policy = _turnCardUseEffects.GrantHandCategoryRestriction(_turnNumber, _currentSeat, f.Id, damage.Id,
            CreateProgramTurnEffectSource(f), source, category);
        AdvanceEventRulesAndQueueFact(new HandCategoryRestrictionGrantedEvent(policy));
    }
    private bool IsTurnHandCategoryRestricted(CharacterState owner, Card card) =>
        _turnCardUseEffects.HasHandCategoryRestriction(_turnNumber, _currentSeat, owner.Seat) &&
        _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat) && _turnCardUseEffects.IsHandCategoryRestricted(
            _turnNumber, _currentSeat, owner.Seat, GetProgramCardCategory(AdvancedEffectiveHandKind(owner, card)));
    private bool IsHandCategoryMaterialRestricted(IReadOnlyList<int> ids) => ids.Any(id =>
        _cardZones.GetLocation(id) is { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } from &&
        IsTurnHandCategoryRestricted(_players[owner], _cardZones.CardsAt(from).Single(c => c.Id == id)));
    private bool IsSelfHandCategoryDiscardForbidden(int actor, Card card, CardLocation from, OwnedCardMoveIntent intent) =>
        intent == OwnedCardMoveIntent.Discard && from == CardLocation.Hand(actor) && IsTurnHandCategoryRestricted(_players[actor], card);
    // Hand-limit exemptions and self-discard prohibitions answer different questions.
    private IReadOnlyList<Card> GetSelfDiscardableHand(CharacterState owner) => GetDiscardEligibleHand(owner)
        .Where(c => !IsSelfHandCategoryDiscardForbidden(owner.Seat, c, CardLocation.Hand(owner.Seat), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<Card> GetSelfDiscardableOwnedHand(CharacterState owner) => GetHand(owner)
        .Where(c => !IsSelfHandCategoryDiscardForbidden(owner.Seat, c, CardLocation.Hand(owner.Seat), OwnedCardMoveIntent.Discard)).ToArray();
    private int RequiredSelfHandLimitDiscard(CharacterState owner) => Math.Min(
        Math.Max(0, GetDiscardEligibleHand(owner).Count - GetHandLimit(owner)), GetSelfDiscardableHand(owner).Count);
    private IReadOnlyList<TurnHandCategoryRestriction>? GetTurnHandCategoryRestrictionSnapshot(int seat)
    {
        var policies = _turnCardUseEffects.HandCategoryRestrictions.Where(p => p.TurnNumber == _turnNumber && p.TurnSeat == _currentSeat && p.AffectedSeat == seat).ToArray();
        return policies.Length == 0 ? null : Array.AsReadOnly(policies);
    }
    private bool ProgramSelectionWillSelfDiscard(ProgramSkillFrame f, string bind)
    {
        var instructions = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions;
        // Do not infer an unchosen future discard branch from the binding name.
        // Other conditional destinations are revalidated with the frozen actual
        // selection actor by MoveProgramBoundCards only when that node executes.
        return instructions.Any(e => e.Op == SkillProgramEffectOp.MoveBoundCards && e.SourceBind == bind &&
            e.Destination == SkillProgramCardDestination.DiscardPile && (e.Condition.Kind == SkillProgramConditionKind.Always ||
            e.Condition.Kind == SkillProgramConditionKind.ChoiceIs && f.ChoiceBindings.Any(c =>
                c.Name == e.Condition.SourceBind && c.OptionId == e.Condition.OptionId)));
    }
    private bool CanSelectSelfDiscardBinding(ProgramSkillFrame f, string bind, int selectionActorSeat, Card card, CardLocation location) =>
        !ProgramSelectionWillSelfDiscard(f, bind) || !IsSelfHandCategoryDiscardForbidden(selectionActorSeat,
            card, location, OwnedCardMoveIntent.Discard);
    private bool CanSelectActivationSelfDiscard(SkillProgramActivation a, Card c, int owner) =>
        !a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardSelected && e.Condition.Kind == SkillProgramConditionKind.Always) ||
        !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard);
}
