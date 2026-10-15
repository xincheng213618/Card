using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record OldHandDispositionReceipt
{
    private IReadOnlyList<int> _old = Array.Empty<int>();
    private IReadOnlyList<int> _disposed = Array.Empty<int>();
    private IReadOnlyList<int> _paid = Array.Empty<int>();
    public int ActualTurn { get; init; }
    public long ParentFrameId { get; init; }
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public string Stage { get; init; } = "mode";
    public string ReturnStage { get; init; } = "";
    public int? TargetSeat { get; init; }
    public int ActualDiscardCount { get; init; }
    public int ForeignDiscardCount { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public IReadOnlyList<int> OldIds { get => _old; init => _old = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DisposedIds { get => _disposed; init => _disposed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> PaidIds { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OldHandDispositionReceipt? OldHandDisposition { get; init; }
}
public sealed record OldHandDispositionPaymentEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, string ReturnStage, int? TargetSeat, IReadOnlyList<int> CardIds, long Before, long After,
    int ActualCount) : IGameEvent
{
    private IReadOnlyList<int> _ids = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
}
internal interface IOldHandDispositionHost
{ SkillProgramStepOutcome ReplaceDiscardWithOldHandDisposition(ProgramSkillFrame frame); }
internal sealed class ReplaceDiscardWithOldHandDispositionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition;
    public override ISkillProgramEffectHandler Handler { get; } = new OldHandDispositionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (e, c) => c.Gift(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];
}
internal sealed class OldHandDispositionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IOldHandDispositionHost)h).ReplaceDiscardWithOldHandDisposition(f);
}
internal static class OldHandDispositionComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition)) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition }] || t.Optional ||
            t.Window != SkillProgramTriggerWindow.DiscardPhaseStarting || t.Subject != SkillProgramTriggerSubject.Owner ||
            t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.UsageScope is not null)
            throw new InvalidOperationException(path + ": old-hand replacement requires its locked own discard starting window.");
    }
}
