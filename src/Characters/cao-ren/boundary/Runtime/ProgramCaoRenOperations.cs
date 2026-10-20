namespace CardGame.Core;

internal interface ICaoRenProgramHost
{
    SkillProgramStepOutcome DiscardHandOrUseEquipment(ProgramSkillFrame frame);
    SkillProgramStepOutcome MoveFieldEquipment(ProgramSkillFrame frame);
}
