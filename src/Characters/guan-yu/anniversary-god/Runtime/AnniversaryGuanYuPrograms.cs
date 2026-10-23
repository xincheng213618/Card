namespace CardGame.Core;

internal interface IAnniversaryGuanYuNightmareHost
{
    void RecordNightmare(ProgramSkillFrame frame);
}

internal sealed class RecordAnniversaryGuanYuNightmareDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordAnniversaryGuanYuNightmare;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new RecordAnniversaryGuanYuNightmareHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DamageAppliedBeforeDying)];
}

internal sealed class RecordAnniversaryGuanYuNightmareHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecordAnniversaryGuanYuNightmare;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IAnniversaryGuanYuNightmareHost>().RecordNightmare(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal static class AnniversaryGuanYuComposition
{
    [ProgramCatalogValidation(40825)]
    internal static void Validate(string path, SkillProgram program)
    {
        var markers = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordAnniversaryGuanYuNightmare)).ToArray();
        if (markers.Length == 0) return;
        if (program.Id != "anniversary-god:wuhun" || markers.Length != 2 ||
            markers.Any(t => t.Window != SkillProgramTriggerWindow.DamageAppliedBeforeDying || t.Optional ||
                t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamagePoint || t.MarkerCost is not null ||
                t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.Effects is not [{ Op: SkillProgramEffectOp.RecordAnniversaryGuanYuNightmare,
                    Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]) ||
            !markers.Any(t => t.Id == "outgoing-nightmare" && t.Subject == SkillProgramTriggerSubject.DamageSource) ||
            !markers.Any(t => t.Id == "incoming-nightmare" && t.Subject == SkillProgramTriggerSubject.Owner))
            throw new InvalidOperationException(path + ": anniversary Wuhun requires exact per-point source and incoming damage windows before dying.");
    }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class AnniversaryGuanYuNightmareHost(GameEngine engine) : IAnniversaryGuanYuNightmareHost
    {
        public void RecordNightmare(ProgramSkillFrame frame) => engine.RecordAnniversaryGuanYuNightmare(frame);
    }

    private void RecordAnniversaryGuanYuNightmare(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DamageAppliedBeforeDying, Amount: > 0 } context ||
            ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId)
            throw new InvalidOperationException("Wuhun lost its actual applied-damage point.");
        var target = frame.TriggerId == "outgoing-nightmare" ? context.TargetSeat : context.SourceSeat;
        if (target is { } seat && IsValidPlayerSeat(seat))
            MutateParticipantMarker(frame, seat, PlayerMarkerKind.Nightmare, 1);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40825, SkillProgramEffectOp.Draw)]
    private bool AllowsAnniversaryGuanYuHeartSlashDraw(ProgramTriggerLegalityContext gate) =>
        gate.SkillId != "anniversary-god:wushen" ||
        gate.Context.Facts is { DamageCardSuit: Suit.Heart, DamageCardIsSlash: true, DamageSourceIsOwner: true } &&
        ActiveDamageTrigger is { } damage && damage.Id == gate.Context.ParentFrameId &&
        GetDamageTriggerAttack(damage).CardUserSeat == gate.OwnerSeat;
}
