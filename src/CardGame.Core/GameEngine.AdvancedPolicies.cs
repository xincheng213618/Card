namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly HashSet<int> _nextUnlimitedCardOwners = [];
    private void ObserveAdvancedLifecycleEvent(IGameEvent payload)
    {
        if (payload is TurnStartedEvent) { _nextUnlimitedCardOwners.Clear(); }
        (int Seat, string Skill)? activated = payload switch
        {
            ProgramSkillResolvedEvent { Completed: true } item => (item.OwnerSeat, item.SkillId),
            ProgramBindingResolvedEvent { Activated: true } item => (item.OwnerSeat, item.SkillId),
            ProgramViewAsConvertedEvent item => (item.OwnerSeat, item.SkillId),
            PindianTopSourceChosenEvent item => (item.OwnerSeat,item.Source.SkillId),
            _ => null
        };
        if (activated is { } activation && !_contentRegistry.GetSkill(activation.Skill).Tags.HasFlag(SkillTag.Locked) &&
            CardPolicies(_players[activation.Seat], SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill).Any())
            _nextUnlimitedCardOwners.Add(activation.Seat);
        if (payload is CardUseDeclaredEvent declared)
        {
            var friendUnlimited = _friendQihuiUnlimited.Remove(declared.SourceSeat);
            if (_nextUnlimitedCardOwners.Remove(declared.SourceSeat) || friendUnlimited) TryMarkUnlimitedUse(declared.ResolutionId);
            if (friendUnlimited) UpdateLifecycleCardUse(declared.ResolutionId, u => u with { QuotaOnlyUseLimitExemption = true });
        }
        if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } response && IsFriendActualCardUse(response.Action))
            _friendQihuiUnlimited.Remove(response.Action.ActorSeat);
    }
    private bool HasNextUnlimitedCard(CharacterState owner) => _nextUnlimitedCardOwners.Contains(owner.Seat) &&
        CardPolicies(owner, SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill).Any();
    private bool HasFriendQihuiAllowance(CharacterState owner) => _friendQihuiUnlimited.Contains(owner.Seat);
    private int RedBladeDamageBonus(CardAttackHandle attack)
    {
        if (attack.IsChainPropagation || attack.CardUserSeat != attack.SourceSeat || attack.EffectiveCardKind is not
            (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) return 0;
        var owner = _players[attack.SourceSeat];
        var applies = HasWeaponAbility(owner, CardKind.RedBloodBlade) && IsFarthestInRange(owner, _players[attack.TargetSeat]) &&
            _skillRuntimeState.GetUsage(owner.Seat, "equipment:red-blood-blade", "farthest-damage", SkillUsageScope.Turn) == 0;
        attack.PendingRedBladeDamageBonus = applies;
        return applies ? 1 : 0;
    }
    private bool IsAdvancedActivationTargetAllowed(CharacterState owner, CharacterState target, string skillId, ProgramInstructionFeatures features) =>
        !features.HasOperation(SkillProgramEffectOp.DamageFarthestCharacter) ||
        IsFarthestInRange(owner, target) && _skillRuntimeState.GetUsage(owner.Seat, skillId, $"target:{target.Seat}", SkillUsageScope.Turn) == 0;
}

