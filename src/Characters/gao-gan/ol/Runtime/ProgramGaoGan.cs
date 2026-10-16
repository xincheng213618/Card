namespace CardGame.Core;

public sealed record JuguanUseReceipt(long CardUseFrameId, long CardActionId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int PhaseInstance, int MaterialCardId, CardKind DeclaredKind);
public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public JuguanUseReceipt? JuguanUse { get; init; }
}
public sealed record JuguanUseIssuedEvent(JuguanUseReceipt Receipt) : IGameEvent;
public sealed record JuguanVictimDamagedEvent(long CardUseFrameId, long CardActionId, int OwnerSeat,
    int VictimSeat, long DamageFrameId, int ActualTurn, int ActualDamageSourceSeat, int Amount) : IGameEvent;
public sealed record JuguanRetaliationRecordedEvent(long CardUseFrameId, int OwnerSeat, int VictimSeat,
    long DamageFrameId, int ActualTurn) : IGameEvent;
public sealed record JuguanDeadlineSettledEvent(int OwnerSeat, int ActualTurn, bool Qualified) : IGameEvent;
public sealed record JuguanDrawPhaseBoundEvent(int OwnerSeat, int DeadlineTurn, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record JuguanSkippedDrawExpiredEvent(int OwnerSeat, int ActualTurn, long DrawPhaseFrameId) : IGameEvent;

internal sealed class ObserveJuguanDrawObligationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObserveJuguanDrawObligation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseSkipped)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.AdjustNormalDraw, static (_, _) => { });
    public override ISkillProgramEffectHandler Handler { get; } = new ObserveJuguanDrawObligationHandler();
}
internal sealed class ObserveJuguanDrawObligationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObserveJuguanDrawObligation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => SkillProgramStepOutcome.Continue;
}
