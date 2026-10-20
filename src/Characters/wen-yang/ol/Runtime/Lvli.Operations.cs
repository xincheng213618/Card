using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WenYangLvliIssuedEvent(long FrameId,int OwnerSeat,int ActualTurn,int TurnOwner,long DamageId,long DamageWindowId,string Branch,int Requested,int ExpectedRecovery,int HpBefore):IGameEvent;
internal sealed class ResolveLvliActualDamageChoiceDescriptor():WenYangDescriptor(SkillProgramEffectOp.ResolveLvliActualDamageChoice)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]; }
