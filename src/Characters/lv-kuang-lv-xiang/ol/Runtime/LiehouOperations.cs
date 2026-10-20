using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record LiehouPayment(int CardId,CardLocation From,int Recipient,long Before,long After,long BatchId);
public sealed record LiehouHandPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Ordinal,int Giver,int Recipient,long Before,long After,long BatchId):IGameEvent;
internal sealed class ResolveLiehouRangeHandRelayDescriptor():LvKuangLvXiangDescriptor(SkillProgramEffectOp.ResolveLiehouRangeHandRelay){public override IReadOnlyList<ProgramResourceOperation>Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];}
