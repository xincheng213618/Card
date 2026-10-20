using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record YunzhengInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);
public sealed record YunzhengInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EntityCount) : IGameEvent;
public sealed record YunzhengInitialEntityMarkedEvent(long FrameId, int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
internal sealed class InitializeYunzhengInitialEntitiesDescriptor() : LeZouShiDescriptor(SkillProgramEffectOp.InitializeYunzhengInitialEntities){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
