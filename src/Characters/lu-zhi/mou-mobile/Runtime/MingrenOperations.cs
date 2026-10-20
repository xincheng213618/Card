using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class InitializeMouLuZhiMingrenDescriptor : MouLuZhiDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouLuZhiMingren; }
internal sealed class ExchangeMouLuZhiMingrenDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeMouLuZhiMingren; }
