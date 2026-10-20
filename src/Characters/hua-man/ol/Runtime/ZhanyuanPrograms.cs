using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhanyuanCompletedEvent(long FrameId,int OwnerSeat,int? MaleSeat,bool LostMansi):IGameEvent;
internal sealed class AwakenZhanyuanAndOfferXiliDescriptor:HuaManDescriptor{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwakenZhanyuanAndOfferXili;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];}
