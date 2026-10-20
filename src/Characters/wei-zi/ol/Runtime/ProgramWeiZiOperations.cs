namespace CardGame.Core;

internal interface IWeiZiProgramHost
{
    SkillProgramStepOutcome YuanziResolve(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YuanziDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome LiejieSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// Shared estimate hook: an accepted whole-hand gift to the preparation-phase
// turn owner. The recipient's hand gain is priced through the target draw so
// the shared target scoring decides whether the gift is worth the empty hand.
internal sealed partial class ProgramAiEstimateContext
{
    internal void AllHandTurnOwnerGift()
    {
        _targetDraw += _player.HandCount;
        _otherAdjustment -= _player.HandCount * 7d;
        _otherAdjustment += 4d; // the same-turn damage-draw benefit prior
    }
}
