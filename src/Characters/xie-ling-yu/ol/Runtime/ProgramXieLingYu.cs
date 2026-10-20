using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum XieLingYuStage { Cost, CostChildren, Drawing, DrawChildren, RemoveName }
public sealed record XieLingYuMaterial(int CardId, CardKind PrintedKind, CardKind BasicName, CardLocation From);
public sealed record XieLingYuGain(int CardId, CardKind PrintedKind, long MovementSequence, bool Replacement);
public sealed record XieLingYuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, XieLingYuStage Stage)
{
    public bool Paid { get; init; }
    public ActualDiscardRecoveryPhaseKey? PlayPhase { get; init; }
    public long OpportunitySequence { get; init; }
    public int NamesMask { get; init; }
    public bool NonUse { get; init; }
    public int Requests { get; init; }
    public bool Stop { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Outcome { get; init; }
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<XieLingYuMaterial> _materials = Array.AsReadOnly(Array.Empty<XieLingYuMaterial>());
    private readonly IReadOnlyList<XieLingYuGain> _gains = Array.AsReadOnly(Array.Empty<XieLingYuGain>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XieLingYuMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XieLingYuGain> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XieLingYuReceipt? XieLingYu { get; init; }
}

// History carries scalar proofs; all private selections remain on the owning program.
public sealed record YuandiDiscardEnteredEvent(long BatchId, int ActualTurn, int TurnOwner, long MovementSequence,
    int CardId, CardKind PrintedKind, CardKind Name, bool FromTrueUse) : IGameEvent;
public sealed record YuandiOpportunityEvent(long BatchId, int OwnerSeat, string SkillInstanceId, string GameplayHash,
    int ActualTurn, int TurnOwner, long CompletionSequence, int NamesMask, bool NonUse, int OccurrenceIndex) : IGameEvent;
public sealed record YuandiPaidEvent(long FrameId, int OwnerSeat, long BatchId, int ActualTurn, int TurnOwner,
    long CompletionSequence, int NamesMask, bool NonUse) : IGameEvent;
public sealed record YuandiNameRemovedEvent(long FrameId, int OwnerSeat, CardKind Name, int BeforeMask, int AfterMask) : IGameEvent;
public sealed record XieLingYuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, long PhaseToken) : IGameEvent;
public sealed record XinyouMaterialFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, CardKind BasicName) : IGameEvent;
public sealed record XinyouPaidEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, long PhaseToken,
    int NamesMask, int MaterialCount, long Before, long After) : IGameEvent;
public sealed record XinyouMaterialDiscardedEvent(long FrameId, int Index, int CardId, long MovementSequence) : IGameEvent;
public sealed record XieLingYuDrawReturnedEvent(long FrameId, int Request, int ActualCount, int ReplacementCount,
    long Before, long After, long? AttemptId, bool Stop) : IGameEvent;
public sealed record XinyouGainedEntityEvent(long FrameId, int Request, int Index, int OwnerSeat, int ActualTurn,
    int TurnOwner, int CardId, CardKind PrintedKind, long MovementSequence, bool Replacement) : IGameEvent;
public sealed record XieLingYuCompletedEvent(long FrameId, bool Paid, int Requests) : IGameEvent;

internal interface IXieLingYuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class XieLingYuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op == SkillProgramEffectOp.ResolveYuandiDiscardNames ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)] : [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new XieLingYuHandler(op);
}
internal sealed class ResolveYuandiDiscardNamesDescriptor() : XieLingYuDescriptor(SkillProgramEffectOp.ResolveYuandiDiscardNames) {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.XieLingYu is { Paid: true } || context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveXinyouBasicCycleDescriptor() : XieLingYuDescriptor(SkillProgramEffectOp.ResolveXinyouBasicCycle) {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.XieLingYu is { Paid: true };
}
internal sealed class XieLingYuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IXieLingYuHost>().Resolve(frame, op);
}
internal static class XieLingYuComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveYuandiDiscardNames or SkillProgramEffectOp.ResolveXinyouBasicCycle;
    [ProgramCatalogValidation(760)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        if (p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Xie Ling Yu requires the exact printed-entity ledger and native draw cycle.");
        if (p.Id == "ol:yuandi")
        {
            if (p.Activations.Count != 0 || p.Triggers is not [var t] || t.Id != "discard-name-group" || t.Window != SkillProgramTriggerWindow.CardsMoved || t.Subject != SkillProgramTriggerSubject.Any || t.Optional || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || !t.DestinationZones.SequenceEqual([CardZoneKind.DiscardPile]) || t.SourceZones.Count != 0 || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.MovementReasons.Count != 0 || t.UsageLimit is not null || t.UsageScope is not null || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveYuandiDiscardNames, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Yuandi requires its issued global discard-name opportunity.");
        }
        else if (p.Id != "ol:xinyou" || p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != "basic-cycle" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveXinyouBasicCycle, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Xinyou requires a zero-input draft and a once-per-actual-Play paid initial Basic batch.");
    }
}
