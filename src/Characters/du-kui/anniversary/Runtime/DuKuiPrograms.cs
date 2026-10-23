using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryDuKuiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryDuKuiDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryDuKuiHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(operation == SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ResolveAnniversaryDuKuiFanyinDescriptor() : AnniversaryDuKuiDescriptor(SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin);
internal sealed class ResolveAnniversaryDuKuiPeiqiDescriptor() : AnniversaryDuKuiDescriptor(SkillProgramEffectOp.ResolveAnniversaryDuKuiPeiqi);
internal sealed class AnniversaryDuKuiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryDuKuiHost>().Begin(frame, operation);
}
internal static class AnniversaryDuKuiComposition
{
    internal const string Fanyin = "anniversary:fanyin", Peiqi = "anniversary:peiqi";
    [ProgramCatalogValidation(43200)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin or SkillProgramEffectOp.ResolveAnniversaryDuKuiPeiqi)) return;
        if (program.Triggers is not [var t] || program.Activations.Count != 0 || program.ViewAs.Count != 0 || program.CardPolicies.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0 ||
            !t.Optional || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.UsageScope is not null || t.UsageLimit is not null ||
            t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            !(program.Id == Fanyin && t.Id == "rank-doubling" && t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && e.Op == SkillProgramEffectOp.ResolveAnniversaryDuKuiFanyin ||
              program.Id == Peiqi && t.Id == "move-and-check-all-ranges" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && e.Op == SkillProgramEffectOp.ResolveAnniversaryDuKuiPeiqi))
            throw new InvalidOperationException(path + ": Du Kui requires the exact optional own Play-start rank loop or actual damaged-owner field moves.");
    }
}
public enum DuKuiStage { RevealMovement, Choice, DeclineMovement, NativeIssuing, NativeUse, NativeTail, MoveChoice, MoveChildren }
public sealed record DuKuiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, DuKuiStage Stage)
{
    public int Iteration { get; init; }
    public int Rank { get; init; }
    public int CardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long MovementSequence { get; init; }
    public int Moves { get; init; }
    public CardLocation? From { get; init; }
    public CardLocation? To { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public LegalActionKind? UseAction { get; init; }
    public CardKind? UseKind { get; init; }
    public int? TargetCardId { get; init; }
    private readonly IReadOnlyList<int> _useTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> UseTargets { get => _useTargets; init => _useTargets = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<long> _nativeBatches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatches { get => _nativeBatches; init => _nativeBatches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DuKuiNativeOrigin(long FrameId, int Instruction, CardConversionSource Source, string GameplayHash, int Iteration, int ActualTurn, int CardId, long UseId, long ActionId);
public sealed record DuKuiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long ParentId, int ActualTurn, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record DuKuiRevealedEvent(long FrameId, int Iteration, int CardId, int Rank, long Sequence) : IGameEvent;
public sealed record DuKuiTargetGrantedEvent(long FrameId, int Iteration, int CardId) : IGameEvent;
public sealed record DuKuiFieldMovedEvent(long FrameId, int Index, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record DuKuiNativeIssuedEvent(DuKuiNativeOrigin Origin) : IGameEvent;
public sealed record DuKuiNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record DuKuiNativeBatchEvent(long FrameId, long UseId, long BatchId, int Count) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuKuiReceipt? DuKui { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuKuiNativeOrigin? DuKuiOrigin { get; init; }
}
