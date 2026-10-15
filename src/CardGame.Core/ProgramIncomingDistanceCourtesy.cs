using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum IncomingDistanceCourtesyKind { FactionDistance, Courtesy }
public enum IncomingDistanceCourtesyStage { Choosing, MovementChildren }
public enum EndingCourtesyBenefit { OwnerDrawOne, TurnOwnerDrawTwo, TurnOwnerRecoverOne }
public sealed record IncomingDistanceCourtesyMovement(int StepIndex, int SubjectSeat, int Requested,
    int Actual, long Before, long After, int? CardId = null, CardLocation? From = null, CardLocation? To = null);
public sealed record IncomingDistanceCourtesyReceipt
{
    private IReadOnlyList<EndingCourtesyBenefit> _benefits = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    private IReadOnlyList<IncomingDistanceCourtesyMovement> _movements = [];
    public long WindowFrameId { get; init; }
    public CardUseEffectSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public IncomingDistanceCourtesyKind Kind { get; init; }
    public IncomingDistanceCourtesyStage Stage { get; init; }
    public int NextBenefitIndex { get; init; }
    public IReadOnlyList<EndingCourtesyBenefit> Benefits { get => _benefits; init => _benefits = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
    public IReadOnlyList<IncomingDistanceCourtesyMovement> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed record IncomingDistanceCourtesyOfferedEvent(long ProgramFrameId, IncomingDistanceCourtesyReceipt Receipt) : IGameEvent;
public sealed record IncomingDistanceCourtesySelectedEvent(long ProgramFrameId, IncomingDistanceCourtesyReceipt Receipt) : IGameEvent;
public sealed record IncomingDistanceCourtesyMovementIssuedEvent(long ProgramFrameId, IncomingDistanceCourtesyMovement Movement) : IGameEvent;
public sealed record IncomingDistanceChangedEvent(long ProgramFrameId, int StepIndex, CardUseEffectSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int Amount) : IGameEvent;
public sealed record IncomingDistanceCourtesyCompletedEvent(long ProgramFrameId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IncomingDistanceCourtesyReceipt? IncomingDistanceCourtesy { get; init; }
}
internal interface IIncomingDistanceCourtesyHost
{ SkillProgramStepOutcome BeginIncomingDistanceCourtesy(ProgramSkillFrame frame, IncomingDistanceCourtesyKind kind); }
internal abstract class IncomingDistanceCourtesyDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new IncomingDistanceCourtesyHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(5));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class AdjustIncomingDistanceByLivingFactionsDescriptor : IncomingDistanceCourtesyDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions; }
internal sealed class ChooseEndingCourtesyBenefitsDescriptor : IncomingDistanceCourtesyDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseEndingCourtesyBenefits; }
internal sealed class IncomingDistanceCourtesyHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IIncomingDistanceCourtesyHost>().BeginIncomingDistanceCourtesy(f, op == SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions
            ? IncomingDistanceCourtesyKind.FactionDistance : IncomingDistanceCourtesyKind.Courtesy);
}
internal static class IncomingDistanceCourtesyContract
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions or SkillProgramEffectOp.ChooseEndingCourtesyBenefits;
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsOperation(e.Op))) return;
        if (t.Effects is not [var e] || !IsOperation(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Window != SkillProgramTriggerWindow.TurnEnding || t.Subject != SkillProgramTriggerSubject.Owner ||
            t.TurnOwnerScope != (e.Op == SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving) ||
            t.Optional != (e.Op == SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions) || t.UsageScope is not null || t.UsageLimit is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.IncludeResponseUses || t.AllowNoEventTarget ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.OwnerRelation is not null || t.DeferredTurnEndOnly ||
            t.ChoiceGroup is not null || t.MarkerCost is not null)
            throw new InvalidOperationException($"{path}: incoming distance requires one exact own optional cost or other actual ending locked courtesy instruction.");
    }
}
