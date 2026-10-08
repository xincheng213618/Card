using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record PhaseNamePredictionStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string StateId, int ActualRoundNumber, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int PhaseInstanceId, int TargetSeat, int RequiredDiscardCount) : IGameEvent;
public sealed record PhaseNamePredictionArmedEvent(long FrameId, int CardId, CardKind NameKind) : IGameEvent;
public sealed record PhaseNamePredictionActualUseEvent(long? ActionId, long? NativeCardUseFrameId,
    int ActorSeat, int ActualTurnNumber, int PhaseInstanceId, CardKind NameKind) : IGameEvent;
public sealed record PhaseNamePredictionUseObservedEvent(long OriginalFrameId, long? ActionId,
    int ActorSeat, int ActualTurnNumber, int PhaseInstanceId, CardKind NameKind,
    long? NativeCardUseFrameId = null) : IGameEvent;
public sealed record PhaseNamePredictionSettledEvent(long FrameId, long OriginalFrameId,
    bool PredictedUse, bool ActualUse, bool Correct, int CardId, int TargetSeat) : IGameEvent;
public sealed record PhaseNamePredictionExpiredEvent(long OriginalFrameId) : IGameEvent;
public sealed record PhaseNamePredictionCostPaidEvent(long FrameId, int RequiredCount, int ActualCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;

// An issued immutable rule policy, reconstructed from the accepted private
// command prefix. It is trusted runtime data; no public fact contains Guess.
public sealed record PhaseNamePredictionPolicy(PhaseNamePredictionStartedEvent Origin,
    int CardId, CardKind NameKind, bool Guess, string EndingBindingId);
public enum PhaseNamePredictionStage { ChoosingCost, CostChildren, Revealing, Guessing, ClaimChildren, DamageIssued }
public sealed record PhaseNamePredictionMaterial(int CardId, CardLocation From);
public sealed record PhaseNamePredictionPhaseKey(int ActualTurnNumber, int TurnOwnerSeat, int PhaseInstanceId);
public sealed record ProgramPhaseNamePredictionReceipt
{
    private IReadOnlyList<PhaseNamePredictionMaterial> _eligible = Array.AsReadOnly(Array.Empty<PhaseNamePredictionMaterial>());
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _hand = Array.AsReadOnly(Array.Empty<int>());
    public required PhaseNamePredictionStartedEvent Origin { get; init; }
    public PhaseNamePredictionStage Stage { get; init; }
    public int RequiredCount { get; init; }
    public IReadOnlyList<PhaseNamePredictionMaterial> Eligible
    { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected
    { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> TargetHand
    { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public int? RevealedCardId { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public PhaseNamePredictionPolicy? IssuedPolicy { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPhaseNamePredictionReceipt? PhaseNamePrediction { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PhaseNamePredictionPolicy? PhaseNamePrediction { get; init; }
}
internal interface IPhaseNamePredictionHost
{
    SkillProgramStepOutcome BeginPhaseNamePrediction(ProgramSkillFrame frame, string stateId);
    SkillProgramStepOutcome SettlePhaseNamePrediction(ProgramSkillFrame frame, string stateId);
}
internal abstract class PhaseNamePredictionDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new PhaseNamePredictionHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, context) => context.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.BeginPhaseNamePrediction
            ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class BeginPhaseNamePredictionDescriptor : PhaseNamePredictionDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.BeginPhaseNamePrediction; }
internal sealed class SettlePhaseNamePredictionDescriptor : PhaseNamePredictionDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettlePhaseNamePrediction; }
internal sealed class PhaseNamePredictionHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        Op == SkillProgramEffectOp.BeginPhaseNamePrediction
            ? ((IPhaseNamePredictionHost)host).BeginPhaseNamePrediction(f, e.StateId!)
            : ((IPhaseNamePredictionHost)host).SettlePhaseNamePrediction(f, e.StateId!);
}
internal static class PhaseNamePredictionComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.BeginPhaseNamePrediction or SkillProgramEffectOp.SettlePhaseNamePrediction;
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, bool optional,
        SkillUsageScope? usageScope, int? usageLimit, SkillProgramTurnOwnerScope scope)
    {
        if (!effects.Any(e => IsOperation(e.Op))) return;
        if (effects is not [var e] || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            subject != SkillProgramTriggerSubject.Owner || scope != SkillProgramTurnOwnerScope.OtherLiving ||
            usageScope is not null || usageLimit is not null ||
            (e.Op == SkillProgramEffectOp.BeginPhaseNamePrediction
                ? window != SkillProgramTriggerWindow.PlayPhaseStarting || !optional
                : window != SkillProgramTriggerWindow.PlayEnding || optional))
            throw new InvalidOperationException(path + ": phase prediction requires its exact optional foreign Play start and mandatory issued phase end.");
    }
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var p in programs.Values)
        {
            var ts = p.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))).ToArray();
            foreach (var group in ts.GroupBy(t => t.Effects.Single().StateId, StringComparer.Ordinal))
                if (group.Count() != 2 || group.Count(t => t.Effects.Single().Op == SkillProgramEffectOp.BeginPhaseNamePrediction) != 1 ||
                    group.Count(t => t.Effects.Single().Op == SkillProgramEffectOp.SettlePhaseNamePrediction) != 1 ||
                    group.Any(t => t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null))
                    throw new InvalidOperationException(p.Id + ": each phase prediction needs one unconditional matching producer and resolver.");
        }
    }
}
