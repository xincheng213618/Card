using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveTargetChosenRankJudgmentDescriptor():ZhangJiDescriptor(SkillProgramEffectOp.ResolveTargetChosenRankJudgment)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),new CreateCardSet("zhang-ji-rank",1,true),new MoveCardSet("zhang-ji-rank",null,SkillProgramCardDestination.DiscardPile)];}
