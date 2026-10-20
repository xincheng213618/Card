namespace CardGame.Core;

internal interface IChenDengProgramHost
{
    SkillProgramStepOutcome FengjiRoundChoice(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome XuanhuiSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// Public-only pricing: the choice's guaranteed floor is one declined +1; the
// redistribution path and the swap's +2 landing need private target knowledge.
internal sealed partial class ProgramAiEstimateContext
{
    internal void FengjiRoundChoice(SkillProgramEffect effect)
    {
        _ownerDraw += 1d;
    }

    internal void XuanhuiSwapEffects(SkillProgramEffect effect)
    {
        _ownerDraw += 2d;
    }
}
