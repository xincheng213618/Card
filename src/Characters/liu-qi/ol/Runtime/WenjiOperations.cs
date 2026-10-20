namespace CardGame.Core;
public sealed record WenjiTurnNameGrant(CardConversionSource Source,string GameplayHash,int ActualTurn,long GiftFrameId,int RecipientSeat,int CardId,CardKind Name,long MovementSequence);
public sealed record WenjiGiftPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int RecipientSeat,int CardId,CardLocation From,CardKind Name,long Before,long After):IGameEvent;
public sealed record WenjiNameArmedEvent(WenjiTurnNameGrant Grant):IGameEvent;
internal sealed class ReceiveWenjiGiftAndArmTurnNameDescriptor:LiuQiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName;
 public override ISkillProgramEffectHandler Handler{get;}=new LiuQiHandler(SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed record WenjiCounterspellQualification(WenjiTurnNameGrant Grant,long WindowId,long ParentFrameId,int ChainDepth,CardActionContext Action);
public sealed record WenjiCounterspellQualifiedEvent(long WindowId,long ParentFrameId,int ChainDepth,long ActionId,WenjiTurnNameGrant Grant):IGameEvent;
