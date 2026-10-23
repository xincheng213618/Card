namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQualification, 5744)]
    private bool ShanheS5TongjiAllowsSlash(ProgramSlashTargetQuery query) =>
        !ShanheS5TongjiProhibitsSlashTarget(new(query.Actor.Seat, query.Target.Seat, query.Kind));

    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 5744)]
    private bool ShanheS5TongjiProhibitsSlashTarget(ProgramDirectedTargetQuery query)
    {
        if (!IsSlashCard(query.Kind) || !_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.SlashMustTargetOverHpHandOwner) ||
            !IsValidPlayerSeat(query.ActorSeat) || !IsValidPlayerSeat(query.TargetSeat) || !_players[query.ActorSeat].IsAlive)
            return false;
        return _players.Any(owner => owner.IsAlive && owner.HasHpValue && owner.Seat != query.ActorSeat && owner.Seat != query.TargetSeat &&
            GetHand(owner).Count > owner.Hp && HasCardPolicy(owner, SkillProgramCardPolicyKind.SlashMustTargetOverHpHandOwner) &&
            IsWithinAttackRange(query.ActorSeat, owner.Seat));
    }
}
