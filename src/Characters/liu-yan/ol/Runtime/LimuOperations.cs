using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record LiuYanLimuUseOrigin(long ProgramFrameId,long CardUseFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int PhaseInstance,int ActorSeat,int CardId,CardKind PrintedKind,CardLocation From,Suit Suit,int Rank);
public sealed record LiuYanLimuIssuedEvent(LiuYanLimuUseOrigin Origin):IGameEvent;
public sealed record LiuYanLimuPaidEvent(LiuYanLimuUseOrigin Origin,long Sequence):IGameEvent;
public sealed record LiuYanLimuReturnedEvent(LiuYanLimuUseOrigin Origin):IGameEvent;
public sealed record LiuYanLimuRecoveryIssuedEvent(long FrameId,int OwnerSeat,int Amount):IGameEvent;
internal sealed class UseLimuSelfIndulgenceAndRecoverDescriptor:LiuYanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ConsumeSelectedCards(1)];}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
internal sealed class LiuYanLimuCardFilter:IProgramSelectionCardFilter
{public bool Allows(ProgramSelectionContext c,Card card,IProgramSelectionQueries q)=>q.Get<ILiuYanSelectionQuery>().CanUse(c.OwnerSeat,card,c.SkillId);}
[ProgramSelectionModule(ProgramSelectionStage.CardSuit,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
internal sealed class LiuYanLimuSuit:IProgramSelectionSuitProvider
{public Suit GetSuit(ProgramSelectionContext c,Card card,IProgramSelectionQueries q)=>q.Get<IProgramSelectionCardState>().EffectiveSuit(c.OwnerSeat,card);}
