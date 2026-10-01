namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsProgramSlashRedirectTarget(CardAttackHandle attack, int redirectorSeat, int targetSeat)
    {
        if (attack.TargetSeat != redirectorSeat ||
            !_players[redirectorSeat].IsAlive || !_players[targetSeat].IsAlive ||
            targetSeat == redirectorSeat || targetSeat == attack.SourceSeat)
            return false;
        var kind = attack.EffectiveCardKind ?? CardKind.Slash;
        return (ActiveFangtianHalberd is null ||
                !_resolutionStack.OfType<CardUseFrame>()
                    .Single(frame => frame.Id == attack.ResolutionId).TargetSeats.Contains(targetSeat)) &&
               (HasCardDistanceExemption(_players[attack.SourceSeat], _players[targetSeat], kind, attack.ResolutionId) ||
                IsWithinAttackRange(redirectorSeat, targetSeat)) &&
               !IsDirectedCardTargetProhibited(attack.SourceSeat, targetSeat, kind) &&
               !IsSlashProhibited(_players[targetSeat]);
    }

    private void ProhibitCurrentProgramResponse(ProgramSkillFrame frame)
    {
        var attack = ProgramCardAttack ?? throw new InvalidOperationException(
            "A response prohibition requires the active Slash.");
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.SlashBeforeResponse ||
            !SameAttackOwner(ActiveCardAttack, attack) || frame.OwnerSeat != attack.SourceSeat ||
            frame.WindowContext.TargetSeat != attack.TargetSeat)
            throw new InvalidOperationException("Response prohibition lost its frozen attack boundary.");
        attack.ProhibitDodgeBy(_contentRegistry.GetSkill(frame.SkillId).Name);
    }

    private void RedirectCurrentProgramAttack(ProgramSkillFrame frame, int targetSeat)
    {
        var attack = ProgramCardAttack ?? throw new InvalidOperationException(
            "Attack redirection requires the active Slash.");
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting ||
            !SameAttackOwner(ActiveCardAttack, attack) || frame.OwnerSeat != attack.TargetSeat ||
            !IsProgramSlashRedirectTarget(attack, frame.OwnerSeat, targetSeat))
            throw new InvalidOperationException("Attack redirection lost its frozen legal target.");
        var previousSeat = attack.TargetSeat;
        attack.SetDamageParticipants(attack.SourceSeat, targetSeat);
        attack.SetIgnoresArmor(
            HasArmorBypass(_players[attack.SourceSeat]) ||
            HasCardArmorBypass(_players[attack.SourceSeat], _players[targetSeat],
                attack.EffectiveCardKind ?? CardKind.Slash));
        RedirectCardUseTarget(attack.ResolutionId, previousSeat, targetSeat);
        AddLog("SkillTriggered",
            $"{_players[frame.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(frame.SkillId).Name}】，将【杀】转移给 {_players[targetSeat].Name}。",
            frame.OwnerSeat, targetSeat);
    }
}
