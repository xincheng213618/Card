using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record TongduGiftIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int DonorSeat,int CardId,CardKind Kind,long HandEntry,long? GiftSequence):IGameEvent;
public sealed record TongduDebtConsumedEvent(long DebtId,long FrameId,long ParentId,int ActualTurn,int PhaseInstance):IGameEvent;
internal sealed class IssueTongduHandGiftDescriptor:LiuBaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.IssueTongduHandGift;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];}
internal sealed class ReturnTongduOriginalCardAtPlayEndDescriptor:LiuBaDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];}
