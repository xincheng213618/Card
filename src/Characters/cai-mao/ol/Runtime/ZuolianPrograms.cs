using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaiMaoExchange(int HolderSeat,int OldCardId,CardKind OldKind,long HandEntry,int NewCardId,CardKind NewKind,CardLocation Supply);
public sealed record CaiMaoExchangeChosenEvent(long FrameId,bool Accepted):IGameEvent;
public sealed record CaiMaoExchangeFrozenEvent(long FrameId,int Index,CaiMaoExchange Entity):IGameEvent;
public sealed record CaiMaoExchangePaidEvent(long FrameId,long BatchId,long Before,long After,int Count):IGameEvent;
internal sealed class ResolveZuolianWholeRevealExchangeDescriptor:CaiMaoDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveZuolianWholeRevealExchange;}
