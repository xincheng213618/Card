using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BaoXinYimouQualification(CardConversionSource Source,string GameplayHash,long WindowId,long DamageFrameId,long AttackId,int VictimSeat,int Amount,int Distance,int? SourceSeat,int OccurrenceIndex);
public sealed record BaoXinYimouBranchEvent(long FrameId,int Branch,int VictimSeat):IGameEvent;
public sealed record BaoXinYimouDrawIssuedEvent(long FrameId,int VictimSeat):IGameEvent;
internal sealed class ResolveBaoXinYimouDescriptor:BaoXinDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveBaoXinYimou;}
