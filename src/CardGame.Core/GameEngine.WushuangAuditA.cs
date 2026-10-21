namespace CardGame.Core;

public sealed record WushuangTiaoxinPhaseExpandedEvent(long FrameId, int OwnerSeat, string SkillInstanceId,
    int ActualTurn) : IGameEvent;

internal interface IWushuangTiaoxinExpansionHost
{
    void Record(ProgramSkillFrame frame);
}
internal sealed class RecordWushuangTiaoxinPhaseExpansionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordWushuangTiaoxinPhaseExpansion;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new RecordWushuangTiaoxinPhaseExpansionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
}
internal sealed class RecordWushuangTiaoxinPhaseExpansionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecordWushuangTiaoxinPhaseExpansion;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IWushuangTiaoxinExpansionHost>().Record(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private const string WushuangAuditedTiaoxin = "wushuang:skill-3001";
    [ProgramCapability]
    private sealed class WushuangTiaoxinExpansionCapability(GameEngine engine) : IWushuangTiaoxinExpansionHost
    {
        public void Record(ProgramSkillFrame frame) => engine.RecordWushuangTiaoxinExpansion(frame);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 63000)]
    private bool AllowsWushuangTiaoxinPhase(ProgramActivationLegalityContext gate)
    {
        if (gate.SkillId != WushuangAuditedTiaoxin) return true;
        return _programPhaseUses.GetValueOrDefault((gate.OwnerSeat, gate.SkillId, gate.Activation.UsageGroup)) == 0 ||
            EventsSinceLastBoundary(e => e is PhaseChangedEvent or TurnStartedEvent)
                .OfType<WushuangTiaoxinPhaseExpandedEvent>().Any(e => e.OwnerSeat == gate.OwnerSeat &&
                    e.ActualTurn == _turnNumber && e.SkillInstanceId == GetRuntimeSkillInstanceId(gate.Owner, gate.SkillId));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 63000)]
    private bool AllowsWushuangBeigeCost(ProgramTriggerLegalityContext gate) =>
        gate.SkillId != "wushuang:skill-3140" || gate.Trigger.Id != "slash-damage-prompt" ||
        GuanduDiscardable(gate.Owner.Seat, gate.Owner.Seat).Length > 0;
    private void RecordWushuangTiaoxinExpansion(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != WushuangAuditedTiaoxin || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.RecordWushuangTiaoxinPhaseExpansion ||
            frame.ChoiceBindings.SingleOrDefault(b => b.Name == "tiaoxin-answer") is not { } answer)
            throw new InvalidOperationException("The official Tiaoxin expansion lost its exact completed requested Slash.");
        if (answer.OptionId == RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption && frame.RequestedSlashDamagedOwner == true) return;
        if (!ProgramEventHistory<WushuangTiaoxinPhaseExpandedEvent>().Any(e => e.FrameId == frame.Id))
            AdvanceEventRulesAndQueueFact(new WushuangTiaoxinPhaseExpandedEvent(frame.Id, frame.OwnerSeat,
                frame.SkillInstanceId, _turnNumber));
    }
}
