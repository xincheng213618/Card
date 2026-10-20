using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BaoXinMutaoGiftEvent(long FrameId,int Ordinal,int CardId,int Recipient,long Sequence):IGameEvent;
public sealed record BaoXinMutaoRetiredEvent(long FrameId,int CardId,long DepartureSequence):IGameEvent;
public sealed record BaoXinMutaoDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat,int SlashCount,int Amount):IGameEvent;
internal sealed class ResolveBaoXinMutaoDescriptor:BaoXinDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveBaoXinMutao;}
