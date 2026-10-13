namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TryGetOtherParticipantHpReplacement(IDamageAttempt attack, out CardUseEffectSource source)
    {
        source = null!;
        if (!_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss) ||
            attack.IsSourceLess || attack.IsDelayedJudgmentDamage || attack.SourceSeat == attack.TargetSeat ||
            !IsValidPlayerSeat(attack.SourceSeat) || !IsValidPlayerSeat(attack.TargetSeat) || !_players[attack.TargetSeat].IsAlive || attack.DamageAmount <= 0) return false;
        // Card-use base additions (e.g. Alcohol) already belong to the attempt.
        // Replace before damage-only modifiers, prevention, armor and elemental chain issuance.
        foreach (var seat in new[] { attack.SourceSeat, attack.TargetSeat })
        {
            if (!_players[seat].IsAlive) continue;
            var binding = CardPolicies(_players[seat], SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss).FirstOrDefault();
            if (binding.Source is null) continue;
            source = new(binding.Source.SkillId, binding.Policy.Id, seat, binding.Source.SkillInstanceId);
            return true;
        }
        return false;
    }
}
