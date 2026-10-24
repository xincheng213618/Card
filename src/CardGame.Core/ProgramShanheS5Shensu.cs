using System.Text.Json.Serialization;

namespace CardGame.Core;

// Ordinary phase keys are zero; each actual extra Draw has its native frame ID.
// A paid reservation and its eventual native skip share the same key.
public sealed record ShanheS5SkippedPhaseRecordedEvent(int OwnerSeat, int ActualTurnNumber,
    SkillProgramTurnPhase Phase, long ExtraDrawFrameId, long? CostProgramFrameId) : IGameEvent;
public sealed record ShanheS5PhaseSkipCostEvent(long FrameId, int InstructionIndex, int OwnerSeat,
    int ActualTurnNumber, SkillProgramTurnPhase Phase, long ExtraDrawFrameId) : IGameEvent;
public sealed record ShanheS5ShensuReceipt(CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, long LifecycleFrameId, SkillProgramTriggerWindow Window,
    int TargetSeat, int SkippedPhaseCount);
public sealed record ShanheS5ShensuPreparedEvent(long FrameId, ShanheS5ShensuReceipt Receipt) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5ShensuReceipt? ShanheS5Shensu { get; init; }
}

internal interface IShanheS5ShensuHost { void PrepareShanheS5Shensu(ProgramSkillFrame frame); }
internal sealed class PrepareShanheS5ShensuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareShanheS5Shensu;
    public override ISkillProgramEffectHandler Handler { get; } = new PrepareShanheS5ShensuHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (_, c) => c.PublicControlValue(4));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
public sealed class PrepareShanheS5ShensuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareShanheS5Shensu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    { h.GetCapability<IShanheS5ShensuHost>().PrepareShanheS5Shensu(f); return SkillProgramStepOutcome.Continue; }
}

internal static class ShanheS5ShensuContract
{
    [ProgramCatalogValidation(59200)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.PrepareShanheS5Shensu)))
            throw new InvalidOperationException(path + ": S5 Shensu preparation is restricted to its native phase triggers.");
        if (program.Id != "shanhe:s5-shensu-14144" && program.Triggers.Any(t =>
                t.Effects.Any(e => e.Op == SkillProgramEffectOp.PrepareShanheS5Shensu)))
            throw new InvalidOperationException(path + ": S5 Shensu preparation belongs to its exact skill program.");
    }

    internal static SkillProgramTurnPhase[] Phases(SkillProgramTriggerWindow window) => window switch
    {
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => [SkillProgramTurnPhase.Judgment, SkillProgramTurnPhase.Draw],
        SkillProgramTriggerWindow.AfterNormalDraw => [SkillProgramTurnPhase.Play],
        SkillProgramTriggerWindow.DiscardPhaseStarting => [SkillProgramTurnPhase.Discard],
        _ => []
    };
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.PrepareShanheS5Shensu)) return;
        var phases = Phases(t.Window);
        var turnover = t.Window == SkillProgramTriggerWindow.DiscardPhaseStarting;
        if (phases.Length == 0 || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null ||
            t.Effects.Count != (turnover ? 5 : 4) ||
            t.Effects[0] is not { Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLivingVirtualSlashTarget } ||
            t.Effects[1].Op != SkillProgramEffectOp.SkipTurnPhases || !t.Effects[1].SkippedPhases.SequenceEqual(phases) ||
            turnover && t.Effects[2].Op != SkillProgramEffectOp.TurnOver ||
            t.Effects[^2].Op != SkillProgramEffectOp.PrepareShanheS5Shensu ||
            t.Effects[^1] is not { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash,
                TargetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget, UseCardActionWindows: true })
            throw new InvalidOperationException(path + ": S5 Shensu requires the exact optional phase cost, optional required turnover, frozen count and native virtual Slash.");
    }
}
