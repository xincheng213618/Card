using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class RecastZhubiAndMarkActualDrawDescriptor:LiuBaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RecastZhubiAndMarkActualDraw;}
internal sealed class ExchangeZhubiCoinsAtEndingDescriptor:LiuBaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];}
