namespace CardGame.Core;



public sealed partial class GameEngine
{

    private SkillProgramStepOutcome LiejieProgramSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        BeginLiejieSourceDiscard(frame, effect);


    [ProgramAiChoice(SkillProgramEffectOp.LiejieSourceDiscard)]
    private PromptChoice SelectAiLiejieSourceDiscardChoice(PendingDecision decision) =>
        SelectAiLiejieDiscardChoice(decision);
}
