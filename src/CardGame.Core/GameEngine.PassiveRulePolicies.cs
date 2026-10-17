namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPublicEnemyParticipantDamageModifiers =>
        _contentRegistry.ProgramDependencies.HasPublicEnemyParticipantDamageModifiers;

    private bool HasEligiblePublicEnemyDamageObserver(IDamageAttempt attack, IReadOnlyList<int> propagatedTargets) =>
        HasPublicEnemyParticipantDamageModifiers && !attack.IsSourceLess && IsValidPlayerSeat(attack.SourceSeat) &&
        _players.Any(player => player.IsAlive && PublicTeamEnemy(player.Seat, _players[attack.SourceSeat]) &&
            propagatedTargets.Prepend(attack.TargetSeat).Any(seat => IsValidPlayerSeat(seat) && seat != attack.SourceSeat &&
                PublicTeamEnemy(player.Seat, _players[seat])) &&
            GetSkillBindingShard(player).ProgramInstances.Any(instance => instance.Program.DamageModifiers.Any(modifier =>
                modifier.SourceScope == SkillProgramDamageModifierSourceScope.AllLivingSkillOwners &&
                modifier.Condition == SkillProgramDamageModifierCondition.BothParticipantsPublicEnemiesOfOwner)));

    private bool HasPerOccurrenceSourceDamageModifiers(int sourceSeat) => IsValidPlayerSeat(sourceSeat) &&
        _players[sourceSeat].IsAlive && GetSkillBindingShard(_players[sourceSeat]).ProgramInstances.Any(instance =>
            instance.Program.DamageModifiers.Any(modifier =>
                modifier.SourceScope == SkillProgramDamageModifierSourceScope.DamageSource &&
                modifier.Condition == SkillProgramDamageModifierCondition.SourceHandBelowHp));

    private RuleQueryEvaluation ApplyPersistentOutgoingDistancePolicy(CharacterState source, CharacterState target,
        RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat) return original;
        var policies = CardPolicies(source, SkillProgramCardPolicyKind.OutgoingDistanceOne).ToArray();
        if (policies.Length == 0) return original;
        // Apply after seat distance, both players' equipment and other directional
        // adjustments. Setting only the outgoing contribution would still let an
        // enemy's defensive horse increase the final distance.
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-persistent-fixed-policy", ConvertRuleValue(original))],
            policies.Select(binding => (RuleQueryContribution)new FiniteRuleQueryContribution(
                $"skill:{binding.Source.SkillId}:{binding.Source.SkillInstanceId}:policy:{binding.Policy.Id}",
                SkillRuleOperation.Set, 1)).ToArray());
    }

    private bool ApplyDelayedJudgmentOutcomePolicies(JudgmentFrame frame, bool succeeded)
    {
        var kind = frame.Continuation switch
        {
            JudgmentContinuationKind.Indulgence => CardKind.Indulgence,
            JudgmentContinuationKind.SupplyShortage => CardKind.SupplyShortage,
            _ => (CardKind?)null
        };
        // This changes the final success fact once, after all card/suit/rank
        // replacements. Multiple enabled copies express the same inversion;
        // they must not toggle the result back again.
        return kind is { } delayed && CardPolicies(_players[frame.TargetSeat],
            SkillProgramCardPolicyKind.ReverseDelayedJudgment, delayed).Any()
            ? !succeeded : succeeded;
    }
}
