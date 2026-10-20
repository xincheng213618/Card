using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ChooseTanbeiTargetPactDescriptor:GuoSiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChooseTanbeiTargetPact;
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
