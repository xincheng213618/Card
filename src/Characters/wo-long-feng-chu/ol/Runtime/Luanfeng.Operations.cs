using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum LuanfengStage { RecoveryChild, DrawChildren }
public sealed record LuanfengReceipt(long FrameId, CardConversionSource Source, string GameplayHash,
    long DyingFrameId, int VictimSeat, LuanfengStage Stage, int RecoveryAmount = 0, int RestoredSlots = 0, int RequestedDraw = 0,
    long Before = 0, long After = 0);
public sealed record LuanfengIssuedEvent(long FrameId, CardConversionSource Source, long DyingFrameId, int VictimSeat) : IGameEvent;
public sealed record LuanfengSlotsRestoredEvent(long FrameId, int VictimSeat, int RestoredSlots, int RequestedDraw) : IGameEvent;
internal sealed class ResolveLuanfengDyingRecoveryDescriptor : WoLongFengChuOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLuanfengDyingRecovery;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, context) => context.PublicControlValue(12));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
