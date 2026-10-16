namespace CardGame.Core;

// Scalar facts need no mutable collection projection or pending sidecar.
public sealed record AlternateActualUseColorRecordedEvent(
    int ActualTurn, int TurnOwnerSeat, int ActorSeat, long ActionId,
    long? PreviousActionId) : IGameEvent
{
    // This remains trusted rules state, like the accepted action's hidden cost.
    // Command replay reconstructs it; JSON and public event members do not expose it.
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool? IsRed { get; init; }
    public override string ToString() => $"Actual use {ActionId}, owner {ActorSeat}, turn {ActualTurn}, previous {PreviousActionId}";
}
public sealed record AlternateActualUseColorDrawGrantedEvent(
    int ActualTurn, int OwnerSeat, long ActionId, long PreviousActionId,
    string SkillId, string SkillInstanceId) : IGameEvent;

internal interface IAlternateActualUseColorProgramHost
{
    void GrantAlternateActualUseColorDraw(ProgramSkillFrame frame);
}

internal sealed class GrantAlternateActualUseColorDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantAlternateActualUseColorDraw;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new GrantAlternateActualUseColorDrawHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireCardActionActor()];
}

internal sealed class GrantAlternateActualUseColorDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantAlternateActualUseColorDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IAlternateActualUseColorProgramHost>().GrantAlternateActualUseColorDraw(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class MobileFenyinCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mobile:fenyin";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.Window != SkillProgramTriggerWindow.CardUseCommitted || trigger.Optional ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !trigger.IncludeResponseUses ||
            trigger.Subject is not null || trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.OwnerIsTurnPlayer ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
            trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.RequireNoCardConversion is not null ||
            trigger.Effects is not [
                { Op: SkillProgramEffectOp.GrantAlternateActualUseColorDraw, Target: SkillProgramEffectTarget.Owner,
                    Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1,
                    NumberExpression: null, SourceBind: null, ResultBind: null, TargetReference: null,
                    Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Mobile Fenyin requires its own-turn mandatory committed actual-use grant followed by exactly one ordinary owner draw.");
    }
}

public sealed partial class GameEngine
{
    private bool TracksAlternateActualUseColor => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.GrantAlternateActualUseColorDraw) == true;

    // Called once at the generic committed-window entry, before candidates are
    // captured. Normal Slash commitment precedes its accepted-action output.
    private void RecordAlternateActualUseColor(CardActionContext action)
    {
        if (!TracksAlternateActualUseColor || _turnNumber < 1 || action.ActorSeat != _currentSeat ||
            !IsAlternateColorActualUse(action) || CompleteProgramEventHistory()
                .OfType<AlternateActualUseColorRecordedEvent>().Any(fact => fact.ActionId == action.ActionId)) return;
        var previous = CompleteProgramEventHistory().OfType<AlternateActualUseColorRecordedEvent>()
            .LastOrDefault(fact => fact.ActualTurn == _turnNumber && fact.TurnOwnerSeat == _currentSeat &&
                fact.ActorSeat == action.ActorSeat);
        AdvanceEventRulesAndQueueFact(new AlternateActualUseColorRecordedEvent(
            _turnNumber, _currentSeat, action.ActorSeat, action.ActionId,
            previous?.ActionId) { IsRed = action.EffectiveIsRed });
    }

    private bool IsAlternateColorActualUse(CardActionContext action) =>
        action.Type == CardActionType.Use || action.Type == CardActionType.Response &&
        action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
        (action.EffectiveKind == CardKind.Nullification ||
         IsProgramResponseCardUse(_players[action.ActorSeat], action.EffectiveKind));

    private (AlternateActualUseColorRecordedEvent Current, AlternateActualUseColorRecordedEvent Previous)?
        AlternateActualUseColorPair(int owner, ProgramSkillWindowContext context)
    {
        if (owner != _currentSeat || context is not
            { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } identity } ||
            identity.ActorSeat != owner || context.SourceSeat != owner) return null;
        var history = CompleteProgramEventHistory().OfType<AlternateActualUseColorRecordedEvent>()
            .Where(fact => fact.ActualTurn == _turnNumber && fact.TurnOwnerSeat == owner && fact.ActorSeat == owner)
            .ToArray();
        var current = history.SingleOrDefault(fact => fact.ActionId == identity.CardActionId);
        // The exact predecessor is frozen when this accepted use is recorded.
        // Nested actions and later target windows cannot rewrite that relation.
        var previous = current?.PreviousActionId is { } previousId
            ? history.SingleOrDefault(fact => fact.ActionId == previousId) : null;
        return current is not null && previous is not null && current.IsRed != previous.IsRed
            ? (current, previous) : null;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26000)]
    private bool CanOfferAlternateActualUseColor(ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantAlternateActualUseColorDraw)) return true;
        return AlternateActualUseColorPair(candidate.OwnerSeat, context) is { } pair &&
            !CompleteProgramEventHistory().OfType<AlternateActualUseColorDrawGrantedEvent>().Any(fact =>
                fact.ActualTurn == _turnNumber && fact.OwnerSeat == candidate.OwnerSeat &&
                fact.ActionId == pair.Current.ActionId && fact.SkillId == candidate.SkillId &&
                fact.SkillInstanceId == candidate.SkillInstanceId);
    }

    private void GrantAlternateActualUseColorDraw(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var trigger = GetProgramTrigger(frame);
        if (frame.InstructionIndex != 1 || frame.WindowContext is not { } context ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is not { } window ||
            window.Id != context.ParentFrameId || window.Action.ActionId != context.CardUse?.CardActionId ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !CanOfferAlternateActualUseColor(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!,
                frame.SkillInstanceId, frame.GameplayHash, 0), trigger, context) ||
            AlternateActualUseColorPair(frame.OwnerSeat, context) is not { } pair)
            throw new InvalidOperationException("An alternating-color draw requires its exact current actual-use pair and source instance.");
        // The following ordinary Draw instruction owns its movement and child return.
        AdvanceEventRulesAndQueueFact(new AlternateActualUseColorDrawGrantedEvent(_turnNumber,
            frame.OwnerSeat, pair.Current.ActionId, pair.Previous.ActionId, frame.SkillId, frame.SkillInstanceId));
    }

    [ProgramCapability]
    private sealed class AlternateActualUseColorProgramCapability(GameEngine engine) : IAlternateActualUseColorProgramHost
    {
        public void GrantAlternateActualUseColorDraw(ProgramSkillFrame frame) => engine.GrantAlternateActualUseColorDraw(frame);
    }
}
