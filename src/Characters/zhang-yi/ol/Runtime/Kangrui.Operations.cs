using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class ResolveKangruiFirstOwnTurnDamageDescriptor() : ZhangYiDescriptor(SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed record ZhangYiKangruiDrawIssuedEvent(long FrameId, int OwnerSeat, int RecipientSeat, int ActualTurnNumber, int SequenceBefore) : IGameEvent;
public sealed record ZhangYiKangruiChoiceEvent(long FrameId, CardConversionSource Source, int RecipientSeat, int ActualTurnNumber, string Choice, int RecoveryRequested) : IGameEvent;
public sealed record ZhangYiKangruiArmedEvent(long FrameId, CardConversionSource Source, int RecipientSeat, int ActualTurnNumber, bool DamageBonus) : IGameEvent;
public sealed record ZhangYiKangruiDamageConsumedEvent(long GrantFrameId, long AttackFrameId, int RecipientSeat, int TargetSeat, int ActualTurnNumber,
    bool ChainPropagation = false, int ChainCursor = 0) : IGameEvent;
public sealed record ZhangYiKangruiHandLimitSetEvent(long GrantFrameId, long DamageFrameId, int RecipientSeat, int ActualTurnNumber) : IGameEvent;
