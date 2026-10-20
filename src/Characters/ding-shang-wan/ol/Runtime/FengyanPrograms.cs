using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveFengyanCounterpartDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.ResolveFengyanCounterpart)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramTriggerWindow.CardResponseCompleted])]; }
