namespace CardGame.Core;

/// <summary>A scalar receipt for one actual ending boundary; it carries no private cards.</summary>
public sealed record ProgramEndingTurnDamageMarkerAddedEvent(
    long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentFrameId, int ActualTurnNumber, PlayerMarkerKind Marker,
    int FrozenDamage, int CountBefore, int CountAfter) : IGameEvent;

internal static class TurnEndDamageMarkerContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.AddEndingTurnDamageMarker)) return;
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.AddEndingTurnDamageMarker,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            trigger.Window != SkillProgramTriggerWindow.TurnEnding ||
            trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.Optional ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null ||
            trigger.ChoiceGroup is not null || trigger.MarkerCost is not null ||
            trigger.EvaluateConditionAtResolution)
            throw new InvalidOperationException($"Invalid skill program at {path}: ending-turn damage marker requires one unconditional owner instruction in mandatory own TurnEnding without usage or payment qualifiers.");
    }
}

internal interface ITurnEndDamageMarkerProgramHost
{
    void AddEndingTurnDamageMarker(ProgramSkillFrame frame, PlayerMarkerKind marker);
}

public sealed class AddEndingTurnDamageMarkerSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AddEndingTurnDamageMarker;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        if (targetSeat != frame.OwnerSeat || host is not ITurnEndDamageMarkerProgramHost markerHost)
            throw new InvalidOperationException("Ending-turn damage marker requires its actual owner host.");
        markerHost.AddEndingTurnDamageMarker(frame, effect.Marker!.Value);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class AddEndingTurnDamageMarkerProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddEndingTurnDamageMarker;
    public override ISkillProgramEffectHandler Handler { get; } = new AddEndingTurnDamageMarkerSkillProgramEffectHandler();
    // It is an automatic ending obligation; future markers are not immediate hand gain.
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "marker", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), marker: reader.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}
