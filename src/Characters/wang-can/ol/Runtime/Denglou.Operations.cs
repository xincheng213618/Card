using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ViewDenglouTopAndUseBasicsDescriptor:WangCanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ViewDenglouTopAndUseBasics;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];}
