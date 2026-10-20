namespace CardGame.Core;

internal interface IYanJunProgramHost
{
    SkillProgramStepOutcome GuanchaoChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome GuanchaoRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome XunxianGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// Public-only pricing for the 逊贤 transfer: the recipient visibly gains the
// settled entity and the owner's board mass shifts by the same public card.
internal sealed partial class ProgramAiEstimateContext
{
    internal void GiftSettledUsedCard(SkillProgramEffect effect)
    {
        _targetDraw += 1d;
        _otherAdjustment += 1d;
    }
}
