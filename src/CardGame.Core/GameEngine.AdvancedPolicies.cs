namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly HashSet<int> _nextUnlimitedCardOwners = [];
    private readonly HashSet<long> _unlimitedCardUses = [];
    private void ObserveAdvancedLifecycleEvent(IGameEvent payload)
    {
        if (payload is TurnStartedEvent) { _nextUnlimitedCardOwners.Clear(); _unlimitedCardUses.Clear(); }
        (int Seat, string Skill)? activated = payload switch
        {
            ProgramSkillResolvedEvent { Completed: true } item => (item.OwnerSeat, item.SkillId),
            ProgramBindingResolvedEvent { Activated: true } item => (item.OwnerSeat, item.SkillId),
            ProgramViewAsConvertedEvent item => (item.OwnerSeat, item.SkillId),
            _ => null
        };
        if (activated is { } activation && !_contentRegistry.GetSkill(activation.Skill).Tags.HasFlag(SkillTag.Locked) &&
            CardPolicies(_players[activation.Seat], SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill).Any())
            _nextUnlimitedCardOwners.Add(activation.Seat);
        if (payload is CardUseDeclaredEvent declared && _nextUnlimitedCardOwners.Remove(declared.SourceSeat))
            _unlimitedCardUses.Add(declared.ResolutionId);
    }
    private bool HasNextUnlimitedCard(CharacterState owner) => _nextUnlimitedCardOwners.Contains(owner.Seat) &&
        CardPolicies(owner, SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill).Any();
    private int RedBladeDamageBonus(AttackResolution attack)
    {
        if (attack.IsChainPropagation || attack.CardUserSeat != attack.SourceSeat || attack.EffectiveCardKind is not
            (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) return 0;
        var owner = _players[attack.SourceSeat];
        var applies = HasWeaponAbility(owner, CardKind.RedBloodBlade) && IsFarthestInRange(owner, _players[attack.TargetSeat]) &&
            _skillRuntimeState.GetUsage(owner.Seat, "equipment:red-blood-blade", "farthest-damage", SkillUsageScope.Turn) == 0;
        attack.PendingRedBladeDamageBonus = applies;
        return applies ? 1 : 0;
    }
    private bool IsAdvancedActivationTargetAllowed(CharacterState owner, CharacterState target, string skillId, SkillProgramActivation activation) =>
        !activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DamageFarthestCharacter) ||
        IsFarthestInRange(owner, target) && _skillRuntimeState.GetUsage(owner.Seat, skillId, $"target:{target.Seat}", SkillUsageScope.Turn) == 0;
}

