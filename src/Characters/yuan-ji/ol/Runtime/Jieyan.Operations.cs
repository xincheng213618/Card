using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record YuanJiJieyanDisabledEvent(long FrameId,int OwnerSeat,int ActualTurn,int Round,Suit FirstSuit,Suit SecondSuit):IGameEvent;
internal sealed class ResolveJieyanOppositeEndDrawPairDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair;}
