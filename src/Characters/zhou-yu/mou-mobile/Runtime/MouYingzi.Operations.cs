using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouZhouYuMobileYingziIssuedEvent(long FrameId, CardConversionSource Source, MouZhouYuMobileTurn Turn, bool Hand, bool Hp, bool Equipment, int Count) : IGameEvent;
internal sealed class ResolveMouZhouYuYingziDescriptor : MouZhouYuMobileDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuYingzi; }
