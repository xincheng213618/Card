using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiDaStage { Amount, Recipients, Drawing, DrawChildren, Rewards, RewardChildren, Hearts, HeartChildren, Cost, CostChildren, Targets, NativeUse, NativeChildren, Finished }
public sealed record ShiDaReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, int TurnOwner, long WindowId, ShiDaStage Stage)
{
    public int Amount { get; init; } = -1;
    public int Cursor { get; init; }
    public int RewardCursor { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public CardConversionSource? Zaoxian { get; init; }
    public long NativeUseId { get; init; }
    public long NativeActionId { get; init; }
    public ProgramVirtualOrdinaryTrickOrigin? Origin { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int CardId { get; init; }
    public CardLocation? CardFrom { get; init; }
    public long PaymentSequence { get; init; }
    public int RandomIndex { get; init; } = -1;
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    private IReadOnlyList<int> _targets = [], _eligible = [], _history = [];
    private IReadOnlyList<long> _nativeBatches = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Eligible { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> History { get => _history; init => _history = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<long> NativeBatches { get => _nativeBatches; init => _nativeBatches = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiDaReceipt? ShiDengAi { get; init; } }
public sealed record ShiDaEquipmentLossReceipt(long UseId, long ActionId, CardConversionSource Source, string Hash, long BatchId,
    int CardId, CardKind PrintedKind, CardLocation From, long PaymentSequence, long? PlacementSequence = null, bool Settled = false);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiDaEquipmentLossReceipt? ShiDengAiEquipmentLoss { get; init; } }
public sealed record ShiDaEquipmentCostCapturedEvent(long UseId, long ActionId, CardConversionSource Source, string Hash, long BatchId,
    int CardId, CardKind PrintedKind, CardLocation From, long PaymentSequence) : IGameEvent;
public sealed record ShiDaEquipmentPlacedEvent(long UseId, long ActionId, int CardId, long PaymentSequence, long PlacementSequence, long BatchId) : IGameEvent;
public sealed record ShiDaEquipmentLossSettledEvent(long UseId, long ActionId, int CardId, long PaymentSequence, long? PlacementSequence, bool Lost) : IGameEvent;
public sealed record ShiDaChargeChangedEvent(long FrameId, CardConversionSource Source, int Before, int After, int CapacityBefore, int CapacityAfter, long MovementBatchId) : IGameEvent;
public sealed record ShiDaStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, int TurnOwner, long WindowId) : IGameEvent;
public sealed record ShiDaTargetRecordedEvent(int Turn, int TurnOwner, int Actor, long UseId, long ActionId, int Target) : IGameEvent;
public sealed record ShiDaSpentEvent(long FrameId, CardConversionSource Source, int Amount, int Recipients, CardConversionSource? Zaoxian) : IGameEvent;
public sealed record ShiDaRecipientEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiDaRandomCandidateEvent(long FrameId, int Ordinal, int Index, int CardId) : IGameEvent;
public sealed record ShiDaRandomIssuedEvent(long FrameId, int Ordinal, int Recipient, int CardId, int Index, int CandidateCount, uint Before, uint After) : IGameEvent;
public sealed record ShiDaCardMovedEvent(long FrameId, ShiDaStage Stage, int Cursor, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record ShiDaCostPaidEvent(long FrameId, long BatchId, int Owner, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record ShiDaThresholdObtainedEvent(long FrameId, CardConversionSource Source, int Threshold, CardKind Kind, int CardId) : IGameEvent;
public sealed record ShiDaDrawPaidEvent(long FrameId, long? DrawId, int ActualCount, long Before, long After) : IGameEvent;
public sealed record ShiDaSnatchIssuedEvent(long FrameId, long UseId, long ActionId, ProgramVirtualOrdinaryTrickOrigin Origin) : IGameEvent;
public sealed record ShiDaSnatchReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record ShiDaNativeBatchEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
public sealed record ShiDaCompletedEvent(long FrameId) : IGameEvent;

internal interface IShiDengAiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShiDengAiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler => new ShiDengAiHandler(op);
    public override ProgramOperationInteraction Interaction => op is SkillProgramEffectOp.ResolveMobileShiDengAiTuntian or SkillProgramEffectOp.ResolveMobileShiDengAiJixi ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance, Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiDengAiGain => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],
        SkillProgramEffectOp.ResolveMobileShiDengAiFull => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.ResolveMobileShiDengAiJixi => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)],
        _ => []
    };
}
internal sealed class ResolveMobileShiDengAiGainDescriptor() : ShiDengAiDescriptor(SkillProgramEffectOp.ResolveMobileShiDengAiGain);
internal sealed class ResolveMobileShiDengAiFullDescriptor() : ShiDengAiDescriptor(SkillProgramEffectOp.ResolveMobileShiDengAiFull);
internal sealed class ResolveMobileShiDengAiTuntianDescriptor() : ShiDengAiDescriptor(SkillProgramEffectOp.ResolveMobileShiDengAiTuntian);
internal sealed class ResolveMobileShiDengAiJixiDescriptor() : ShiDengAiDescriptor(SkillProgramEffectOp.ResolveMobileShiDengAiJixi);
internal sealed class ResolveMobileShiDengAiTargetsDescriptor() : ShiDengAiDescriptor(SkillProgramEffectOp.ResolveMobileShiDengAiTargets);
internal sealed class ShiDengAiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<IShiDengAiHost>().Resolve(frame, op);
}
internal static class ShiDengAiComposition
{
    [ProgramCatalogValidation(50300)] internal static void Validate(string path, SkillProgram program)
    {
        static bool Plain(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } effect] && effect.Op == op;
        var operations = program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects));
        if (!operations.Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiDengAiGain or SkillProgramEffectOp.ResolveMobileShiDengAiFull or SkillProgramEffectOp.ResolveMobileShiDengAiTuntian or SkillProgramEffectOp.ResolveMobileShiDengAiJixi or SkillProgramEffectOp.ResolveMobileShiDengAiTargets)) return;
        var valid = program.Id == "mobile:shi-tuntian" ? program.Activations is [{ Id: "spend-charge", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, UsesPerPhase: 1 } a] && Plain(a.Effects, SkillProgramEffectOp.ResolveMobileShiDengAiTuntian) &&
            program.Triggers.Count == 2 && program.Triggers.Any(t => t is { Id: "non-damage-loss", Window: SkillProgramTriggerWindow.CardsMoved, Subject: SkillProgramTriggerSubject.Owner, Optional: false, MovementOccurrence: SkillProgramMovementOccurrence.PerOwnerBatch } &&
                t.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) && Plain(t.Effects, SkillProgramEffectOp.ResolveMobileShiDengAiGain)) &&
            program.Triggers.Any(t => t is { Id: "full-charge", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: false } && Plain(t.Effects, SkillProgramEffectOp.ResolveMobileShiDengAiFull)) :
            program.Id == "mobile:shi-jixi" && program.Activations.Count == 0 && program.Triggers.Count == 3 && program.Triggers.Any(t => t is { Id: "ending-jixi", Window: SkillProgramTriggerWindow.AfterTurnEnded, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: true } && Plain(t.Effects, SkillProgramEffectOp.ResolveMobileShiDengAiJixi)) &&
            program.Triggers.Where(t => t.Id != "ending-jixi").All(t => t.Window is SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCompleted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !t.Optional && !t.IncludeResponseUses && !t.AllowNoEventTarget && Plain(t.Effects, SkillProgramEffectOp.ResolveMobileShiDengAiTargets));
        if (!valid || program.ViewAs.Count != 0 || program.CardPolicies.Count != 0) throw new InvalidOperationException(path + ": Shi Deng Ai requires native owner losses, once-Play charge payment and a true after-turn multi-target Snatch.");
    }
}
