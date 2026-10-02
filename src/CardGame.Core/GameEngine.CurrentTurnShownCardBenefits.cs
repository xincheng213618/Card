namespace CardGame.Core;

public sealed record CurrentTurnDirectedHeartSlashBonus(int TurnNumber, int TurnOwnerSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, int TargetSeat);
public sealed record CurrentTurnDirectedHeartSlashBonusGrantedEvent(CurrentTurnDirectedHeartSlashBonus Bonus) : IGameEvent;
public sealed partial class GameEngine
{
    private readonly List<CurrentTurnDirectedHeartSlashBonus> _currentTurnHeartSlashBonuses = [];
    private bool HasShownCardTurnCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantCurrentTurnDirectedHeartSlashBonus) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantCurrentTurnDirectedHeartSlashBonus);
    private void RevealOwnedBoundCardAppearance(ProgramSkillFrame frame, string bind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var set = GetProgramCardSet(active, bind);
        if (set.CardIds.Count == 0) return;
        if (set.CardIds is not [var id] || set.SourceLocations is not [var source] ||
            source.OwnerSeat is not { } owner || source != CardLocation.Hand(owner))
            throw new InvalidOperationException("Shown appearance requires one real owned Hand entity.");
        if (!_players[owner].IsAlive || _cardZones.GetLocation(id) != source)
        { SetProgramCardSet(frame.Id, bind, [], SkillProgramCardSetVisibility.Public, []); return; }
        var card = _cardZones.CardsAt(source).Single(c => c.Id == id);
        var suit = EffectiveSuit(_players[owner], card);
        ReplaceRuntimeTop(active with { CardSetBindings = Array.AsReadOnly(active.CardSetBindings.Select(b =>
            b.Name == bind ? b with { Visibility = SkillProgramCardSetVisibility.Public, FrozenRevealedSuit = suit } : b).ToArray()) });
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner, bind, Array.AsReadOnly(new[] { ToSnapshot(card) })));
    }
    private void GrantCurrentTurnDirectedHeartSlashBonus(ProgramSkillFrame frame, int targetSeat)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (!IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("Directed Heart Slash benefit requires a living other target.");
        var fact = new CurrentTurnDirectedHeartSlashBonus(_turnNumber, _currentSeat, frame.Id,
            frame.InstructionIndex - 1, CreateProgramTurnEffectSource(frame), targetSeat);
        var prior = _currentTurnHeartSlashBonuses.SingleOrDefault(b => b.ParentFrameId == fact.ParentFrameId && b.EffectIndex == fact.EffectIndex);
        if (prior is not null) { if (prior != fact) throw new InvalidOperationException("Directed benefit changed identity."); return; }
        _currentTurnHeartSlashBonuses.Add(fact);
        AdvanceEventRulesAndQueueFact(new CurrentTurnDirectedHeartSlashBonusGrantedEvent(fact));
    }
    private IEnumerable<(CardUseEffectSource Source, int Amount)> GetCurrentTurnHeartSlashBonuses(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind)) return [];
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == attack.ResolutionId);
        if (use?.Action is not { Type: CardActionType.Use, EffectiveSuit: Suit.Heart } action) return [];
        return _currentTurnHeartSlashBonuses.Where(b => b.TurnNumber == _turnNumber && b.TurnOwnerSeat == _currentSeat &&
            b.Source.OwnerSeat == action.ActorSeat && b.Source.OwnerSeat == attack.CardUserSeat && b.TargetSeat == attack.TargetSeat)
            .GroupBy(b => (b.Source.OwnerSeat, b.Source.SkillId, b.TargetSeat)).Select(group => (group.First().Source, 1));
    }
    private bool HasSlashUseDistanceBySuit(CharacterState owner, CardKind kind, Suit? suit) =>
        suit is { } value && IsSlashCard(kind) && GetSkillBindingShard(owner).Programs.Any(p => p.CardPolicies.Any(policy =>
            policy.Kind == SkillProgramCardPolicyKind.IgnoreSlashUseDistanceBySuit && policy.InputSuit == value &&
            policy.CardKinds.Contains(kind) && policy.Condition.Evaluate(CreateSkillContext(owner))));
    private sealed partial class ProgramSkillHost : ICurrentTurnShownCardProgramHost
    {
        public void RevealOwnedBoundCardAppearance(ProgramSkillFrame f, string bind) => engine.RevealOwnedBoundCardAppearance(f, bind);
        public void IssueCurrentTurnNonLockedSkillSuppression(ProgramSkillFrame f, int seat) => engine.IssueCurrentTurnNonLockedSkillSuppression(f, seat);
        public void GrantCurrentTurnDirectedHeartSlashBonus(ProgramSkillFrame f, int seat) => engine.GrantCurrentTurnDirectedHeartSlashBonus(f, seat);
    }
}
