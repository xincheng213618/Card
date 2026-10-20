namespace CardGame.Core;

internal interface ILvKaiProgramHost
{
    SkillProgramStepOutcome TunanUseRevealedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome BijingPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect);
}
