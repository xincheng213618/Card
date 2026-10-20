using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record XingzuoEndingIssuedEvent(long DebtId,CardConversionSource Source,string GameplayHash,int ActualTurn):IGameEvent;
public sealed record XingzuoEndingConsumedEvent(long DebtId,long FrameId,long ParentId):IGameEvent;
public sealed record XingzuoBottomOrderedEvent(long FrameId,int Index,int CardId,bool Ending):IGameEvent;
public sealed record XingzuoHpLossPaidEvent(long FrameId,int RemainingHp):IGameEvent;
internal sealed class ResolveXingzuoPrivateBottomExchangeDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange;}
internal sealed class ResolveXingzuoIssuedEndingExchangeDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange;}
