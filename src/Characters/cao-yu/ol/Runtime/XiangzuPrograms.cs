using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoYuXiangzuSpentEvent(long FrameId,CardConversionSource Source,string GameplayHash,long DyingId,int RecipientSeat,int Count,bool HistoryQualified):IGameEvent;
internal sealed class ResolveXiangzuDyingGiftAndRecoveryDescriptor:CaoYuDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery;}
