using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class MarkUnmarkedOtherBeforeActualTurnDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnBeforeStart)];}
internal sealed class MarkLowestHpUnmarkedOtherAtRoundStartDescriptor():BeiMiHuDescriptor(SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)];}
