namespace CardGame.Core;

internal sealed partial class ProgramAiEstimateContext
{
    internal void RecoverOtherDyingVictimTo(SkillProgramEffect effect)
    {
        // The engine publishes the exact public DyingEntering victim as the
        // selected target. Existing role attitudes then price this support.
        var victim = _publicContext.SelectedTarget;
        if (victim is null || victim.Seat == _player.Seat || victim.Hp > 0)
        {
            _otherAdjustment -= 1000d;
            return;
        }
        var recovered = Math.Max(0, Math.Min(victim.MaxHp, effect.Amount) - victim.Hp);
        _targetRecovery += recovered;
        if (recovered > 0) _targetAdjustment += 32d;
    }
}
