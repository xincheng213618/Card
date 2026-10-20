using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MansiActualCardsObtainedEvent(long FrameId,CardConversionSource Source,int Before,int ActualObtained,int After):IGameEvent;
internal sealed class DrawAndCountMansiActualBarbarianDamageDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
