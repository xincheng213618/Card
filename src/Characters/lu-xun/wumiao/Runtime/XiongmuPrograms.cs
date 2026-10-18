using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum XiongmuStage { Drawing, DrawChildren, Selection, InsertChildren, ObtainChildren }
public sealed record XiongmuMaterial(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record XiongmuInsertion(int Index, int CardId, int PileCountBefore, int Position,
    uint RandomBefore, uint RandomAfter);
public sealed record XiongmuReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int RoundNumber, int ActualTurn, int TurnOwner, int InstructionIndex, int RequestedDraw,
    int OriginalHandCount, int OriginalMaxHp, long SequenceBefore)
{
    public XiongmuStage Stage { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long PutBatchId { get; init; }
    public long PutBefore { get; init; }
    public long PutAfter { get; init; }
    public long PutTailAfter { get; init; } = -1;
    public int InsertedCount { get; init; } = -1;
    public long ObtainBatchId { get; init; }
    public long ObtainBefore { get; init; }
    public long ObtainAfter { get; init; }
    public int ActualObtained { get; init; } = -1;
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _deckBefore = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<XiongmuMaterial> _put = Array.AsReadOnly(Array.Empty<XiongmuMaterial>());
    private readonly IReadOnlyList<XiongmuMaterial> _obtain = Array.AsReadOnly(Array.Empty<XiongmuMaterial>());
    private readonly IReadOnlyList<XiongmuInsertion> _insertions = Array.AsReadOnly(Array.Empty<XiongmuInsertion>());
    private readonly IReadOnlyList<CardMovementRecord> _putMoves = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    private readonly IReadOnlyList<CardMovementRecord> _obtainMoves = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    private readonly IReadOnlyList<CardMovementRecord> _spill = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DeckBefore { get => _deckBefore; init => _deckBefore = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiongmuMaterial> PutMaterials { get => _put; init => _put = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiongmuMaterial> ObtainMaterials { get => _obtain; init => _obtain = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiongmuInsertion> Insertions { get => _insertions; init => _insertions = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> PutMovements { get => _putMoves; init => _putMoves = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> ObtainMovements { get => _obtainMoves; init => _obtainMoves = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> WoodenOxSpill { get => _spill; init => _spill = Array.AsReadOnly(value.ToArray()); }
}
public sealed record XiongmuDamageReceipt(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long AttackId, int TargetSeat, int ChainCursor, int AmountBefore, int AmountAfter,
    int ActualTurn, int TurnOwner, int? TransferredTargetSeat = null, long? TransferWindowId = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiongmuReceipt? Xiongmu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiongmuDamageReceipt? XiongmuDamage { get; init; }
}
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiongmuDamageReceipt? XiongmuDamage { get; init; } }
public sealed partial record JudgmentFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiongmuDamageReceipt? XiongmuDamage { get; init; } }

public sealed record XiongmuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int RoundNumber, int ActualTurn, int TurnOwner, int RequestedDraw,
    int OriginalHandCount, int OriginalMaxHp, long SequenceBefore) : IGameEvent;
public sealed record XiongmuDrawReturnedEvent(long FrameId, int Requested, int ActualDrawCount,
    int ReplacementCount, long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record XiongmuDeckEntityFrozenEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record XiongmuPutSelectedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From) : IGameEvent;
public sealed record XiongmuPutPaidEvent(long FrameId, long BatchId, int SelectedCount, int InsertedCount,
    long Before, long After, uint RandomBefore, uint RandomAfter) : IGameEvent;
public sealed record XiongmuPutTailCapturedEvent(long FrameId, long BatchId, int SpillCount, long After) : IGameEvent;
public sealed record XiongmuInsertedEvent(long FrameId, int Index, int CardId, int PileCountBefore,
    int Position, uint RandomBefore, uint RandomAfter) : IGameEvent;
public sealed record XiongmuObtainSelectedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From) : IGameEvent;
public sealed record XiongmuObtainPaidEvent(long FrameId, long BatchId, int Requested, int ActualCount,
    long Before, long After) : IGameEvent;
public sealed record XiongmuEntityObtainedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int Index, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long MovementSequence,
    long BatchId) : IGameEvent;
public sealed record XiongmuCompletedEvent(long FrameId, int InsertedCount, int ActualObtained) : IGameEvent;
public sealed record XiongmuFirstPositiveDamageEvent(int ActualTurn, int TurnOwner, long AttackId,
    int SourceSeat, int TargetSeat, int Amount, DamageNature Nature, bool SourceLess) : IGameEvent;
public sealed record XiongmuFirstDamageReducedEvent(XiongmuDamageReceipt Receipt, int HandCount, int Hp) : IGameEvent;
public sealed record XiongmuDamageRetiredEvent(long AttackId, long WindowId, long ProgramFrameId, int TargetSeat, int ChainCursor) : IGameEvent;
public sealed record XiongmuCangxinDamageRetiredEvent(long AttackId, long WindowId, long ProgramFrameId, int TargetSeat, int ChainCursor) : IGameEvent;
public sealed record XiongmuChainRebasedEvent(long AttackId, long WindowId, int TargetSeat, int ChainCursor,
    int RestoredAmount) : IGameEvent;
public sealed record XiongmuCangxinChainRebasedEvent(long AttackId, int TargetSeat, int ChainCursor,
    long XiongmuFrameId, long XiongmuWindowId, long CangxinFrameId, long CangxinWindowId,
    bool XiongmuWasFirst, int RestoredAmount) : IGameEvent;
public sealed record XiongmuDamageTransferredEvent(long AttackId, long WindowId, int OriginalTarget,
    int Target, int ChainCursor, long TransferWindowId) : IGameEvent;

internal interface IWuLuXunXiongmuHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class WuLuXunXiongmuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveWuLuXunXiongmu ?
            SkillProgramTriggerWindow.RoundStarting : SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuXunXiongmuHandler(op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(3));
}
internal sealed class ResolveWuLuXunXiongmuDescriptor() : WuLuXunXiongmuDescriptor(SkillProgramEffectOp.ResolveWuLuXunXiongmu)
{ public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class ResolveWuLuXunXiongmuFirstDamageDescriptor() : WuLuXunXiongmuDescriptor(SkillProgramEffectOp.ResolveWuLuXunXiongmuFirstDamage) { }
internal sealed class WuLuXunXiongmuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IWuLuXunXiongmuHost>().Resolve(frame, op);
}
internal static class XiongmuComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveWuLuXunXiongmu or SkillProgramEffectOp.ResolveWuLuXunXiongmuFirstDamage;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "wumiao:xiongmu" && !p.Triggers.Any(t => t.Effects.Any(e => IsOperation(e.Op))) &&
            !p.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op)))) return;
        if (p.Id != "wumiao:xiongmu" || p.Triggers.Count != 2 || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.HpSkillGrants.Count != 0)
            throw new InvalidOperationException(path + ": Xiongmu requires both its optional round-start and automatic first-damage contracts.");
        foreach (var t in p.Triggers)
        {
            var round = t.Id == "round-start";
            if ((!round && t.Id != "first-damage") || t.Window != (round ? SkillProgramTriggerWindow.RoundStarting : SkillProgramTriggerWindow.BeforeDamageApplied) ||
                t.Subject != (round ? SkillProgramTriggerSubject.Owner : SkillProgramTriggerSubject.DamageTarget) || t.Optional != round ||
                t.Effects is not [var e] || e.Op != (round ? SkillProgramEffectOp.ResolveWuLuXunXiongmu : SkillProgramEffectOp.ResolveWuLuXunXiongmuFirstDamage) ||
                e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
                t.OrderGroup is not null || t.OwnerRelation is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.DamageCardKinds.Count != 0 || t.Suits.Count != 0 ||
                t.MinimumRank != 1 || t.MaximumRank != 13 || t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 ||
                t.JudgmentSource is not null || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementOccurrence is not null ||
                t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.DamageOccurrence is not null ||
                t.RequireNoCardConversion is not null || t.RequireDamageSource is not null || t.GainPhaseQualification is not null ||
                t.IncludeResponseUses || t.SingleActionInstance || t.AllowNoEventTarget || t.OnlyDesignatedCardTargets ||
                t.NoDyingAtActivation || t.EvaluateConditionAtResolution || t.DeferredTurnEndOnly || t.AllowOwnDiscardPhaseEnded ||
                t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
                t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
                throw new InvalidOperationException(path + ": Xiongmu cannot silently ignore added policies, zones, limits or alternate windows.");
        }
    }
}
