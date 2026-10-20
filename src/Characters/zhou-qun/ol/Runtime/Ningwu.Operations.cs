using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record NingwuJudgmentIssuedEvent(long FrameId, long JudgmentId, long UseId, long ActionId, int ActorSeat, int TargetSeat, int? SlashRank) : IGameEvent;
public sealed record NingwuJudgmentReturnedEvent(long FrameId, long JudgmentId, int? CardId, int? Rank) : IGameEvent;
public sealed record NingwuUseNullifiedEvent(long FrameId, long UseId, long ActionId, long JudgmentId, int ActorSeat, int TargetSeat, int SlashRank, int JudgmentRank) : IGameEvent;
internal sealed class NingwuDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
