using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryZhangHuanHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class AnniversaryZhangHuanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo;
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryZhangHuanHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class AnniversaryZhangHuanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryZhangHuanHost>().Begin(f);
}
internal static class AnniversaryZhangHuanComposition
{
    internal const string Yiju = "anniversary:yiju", Shuguo = "anniversary:shuguo";
    [ProgramCatalogValidation(46300)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo)) return;
        if (p.Id != Shuguo || p.Triggers is not [{ Id: "actual-turn-discard-uses", Window: SkillProgramTriggerWindow.AfterTurnEnded, Subject: SkillProgramTriggerSubject.Owner,
            TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: true, OwnerRelation: null, UsageScope: null, UsageLimit: null, MarkerCost: null,
            Condition.Kind: SkillProgramTriggerConditionKind.Always, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo, Target: SkillProgramEffectTarget.Owner,
                TargetReference: null, ActorReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }] ||
            p.Activations.Count != 0 || p.BooleanStates.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Shuguo requires its optional actual-turn ending, real discarded entities and one owner instruction.");
    }
}
public enum AnniversaryZhangHuanStage { Choice, IssuingUse, NativeUse, NativeTail, Drawing, DrawChildren, Complete }
public sealed record AnniversaryZhangHuanDiscard(long InvocationId, int CardId, CardKind Kind, CardLocation From, int ExecutorSeat, long Sequence);
public sealed record AnniversaryZhangHuanNative(long FrameId, int Instruction, CardConversionSource Source, string Hash, int Turn, int Actor,
    AnniversaryZhangHuanDiscard Discard, LegalAction Action, long UseId, long ActionId)
{
    private readonly LegalAction _action = Freeze(Action);
    public LegalAction Action { get => _action; init => _action = Freeze(value); }
    internal static LegalAction Freeze(LegalAction a) => a with
    {
        TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()),
        AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()),
        SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(t => (IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray())
    };
}
public sealed record AnniversaryZhangHuanReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, AnniversaryZhangHuanStage Stage)
{
    public AnniversaryZhangHuanNative? Native { get; init; }
    public bool Returned { get; init; }
    public bool Stop { get; init; }
    public int TotalDiscards { get; init; }
    public int RequestedDraw { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<int> _used = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Used { get => _used; init => _used = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryZhangHuanReceipt? AnniversaryZhangHuan { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryZhangHuanNative? AnniversaryZhangHuanUse { get; init; } }
public sealed record AnniversaryZhangHuanStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId) : IGameEvent;
public sealed record AnniversaryZhangHuanUseIssuedEvent(long FrameId, long UseId, long ActionId, int CardId, long DiscardSequence, int ExecutorSeat) : IGameEvent;
public sealed record AnniversaryZhangHuanUseReturnedEvent(long FrameId, long UseId, long ActionId, int CardId) : IGameEvent;
public sealed record AnniversaryZhangHuanNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record AnniversaryZhangHuanDrawIssuedEvent(long FrameId, int Discards, int Used, bool UniqueMaximum, int Requested) : IGameEvent;
public sealed record AnniversaryZhangHuanDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryZhangHuanCompletedEvent(long FrameId) : IGameEvent;
