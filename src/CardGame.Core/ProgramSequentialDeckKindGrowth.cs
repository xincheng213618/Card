using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SequentialDeckKindGrowthStage { Ready, GainChildren, Finished }

/// <summary>One attempted physical entity. Success follows its paid movement, never its later hand location.</summary>
public sealed record SequentialDeckKindGrowthStepReceipt(int Index, int CardId, long BatchId,
    long SequenceBefore, long SequenceAfter, int ActualObtained)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<CardMovementRecord> Movements
    { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}

public sealed record ProgramSequentialDeckKindGrowthReceipt(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, long PreparationWindowId, int TurnNumber,
    CardKind OutputKind, int RequestedCount)
{
    public SequentialDeckKindGrowthStage Stage { get; init; }
    // Only failed entities are excluded from later attempts. A successfully obtained
    // entity returned to the pile by a real gain child may be obtained again.
    private readonly IReadOnlyList<SequentialDeckKindGrowthStepReceipt> _steps = Array.AsReadOnly(Array.Empty<SequentialDeckKindGrowthStepReceipt>());
    public IReadOnlyList<SequentialDeckKindGrowthStepReceipt> Steps
    {
        get => _steps;
        init => _steps = Array.AsReadOnly(value.Select(step => step with { Movements = step.Movements }).ToArray());
    }
    public int ActualObtained => Steps.Sum(step => step.ActualObtained);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Shortfall { get; init; }
    public bool Cancelled { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramSequentialDeckKindGrowthReceipt? SequentialDeckKindGrowth { get; init; }
}

public sealed record SequentialDeckKindGrowthStartedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, long PreparationWindowId, int TurnNumber, CardKind OutputKind, int RequestedCount) : IGameEvent;
public sealed record SequentialDeckKindGrowthStepPaidEvent(long ProgramFrameId,
    SequentialDeckKindGrowthStepReceipt Step) : IGameEvent;
public sealed record SequentialDeckKindGrowthGrantedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, long PreparationWindowId, int TurnNumber, CardKind OutputKind,
    int RequestedCount, int ActualObtained, int Amount) : IGameEvent;
public sealed record SequentialDeckKindGrowthCompletedEvent(long ProgramFrameId, int Attempts,
    int ActualObtained, int Shortfall, bool Cancelled) : IGameEvent;

internal interface ISequentialDeckKindGrowthHost
{
    SkillProgramStepOutcome AcquireDeckKindSequentiallyAndGrowOnShortfall(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class AcquireDeckKindSequentiallyAndGrowOnShortfallDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall;
    public override ISkillProgramEffectHandler Handler { get; } = new AcquireDeckKindSequentiallyAndGrowOnShortfallHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "outputKind", "numberExpression", "amount", "capGrowthByInitialPlayerCount", "condition");
        var kind = reader.RequiredEnum<CardKind>("outputKind");
        var expression = reader.Has("numberExpression")
            ? reader.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        var amount = reader.Has("amount") ? reader.RequiredInt("amount") : 0;
        if (kind != CardKind.Duel || reader.Has("amount") == reader.Has("numberExpression") ||
            (expression is not null ? expression != SkillProgramNumberExpression.OwnerMaxHp : amount is < 1 or > 20))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: sequential deck acquisition requires Duel and either ownerMaxHp or one positive fixed amount.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), amount,
            reader.Condition(), outputKind: kind, numberExpression: expression)
        {
            CapGrowthByInitialPlayerCount = reader.Has("capGrowthByInitialPlayerCount")
                ? reader.RequiredBool("capGrowthByInitialPlayerCount") : null
        };
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];

    internal static bool IsTrigger(SkillProgramTrigger trigger) => trigger.Effects is
        [{ Op: SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall }];

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
            trigger.NamedUsageGroup is not null || trigger.ChoiceGroup is not null || trigger.GainPhaseQualification is not null ||
            trigger.RequiredPolarity is not null || trigger.MarkerCost is not null ||
            trigger.Effects[0] is not { Target: SkillProgramEffectTarget.Owner, TargetReference: null,
                OutputKind: CardKind.Duel, Condition.Kind: SkillProgramConditionKind.Always } ||
            !(trigger.Effects[0] is { NumberExpression: SkillProgramNumberExpression.OwnerMaxHp, Amount: 0 } or
                { NumberExpression: null, Amount: > 0 and <= 20 }))
            throw new InvalidOperationException($"Invalid skill program at {path}: sequential deck acquisition/growth requires one unlimited mandatory own-preparation instruction.");
    }
}

public sealed class AcquireDeckKindSequentiallyAndGrowOnShortfallHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<ISequentialDeckKindGrowthHost>().AcquireDeckKindSequentiallyAndGrowOnShortfall(frame, effect);
}
