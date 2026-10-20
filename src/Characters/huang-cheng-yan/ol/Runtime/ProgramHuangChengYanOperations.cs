namespace CardGame.Core;

internal interface IHuangChengYanProgramHost
{
    SkillProgramStepOutcome JiezhenReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome JiezhenRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZecaiRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YinshiPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YinshiClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}
