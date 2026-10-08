using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum RecipientCategoryMarkKind { Rescue, Discard, ExtraTargets }
public enum RecipientCategoryMarkStage { ChoosingGift, GiftChildren, ChoosingTarget, ChoosingCards, RecoveryChildren, DrawChildren, DiscardChildren, ChoosingSlashTargets, Complete }
public sealed record RecipientCategoryMarkToken(long TokenId, int HolderSeat, string SourceSkillId, string StateId,
    RecipientCategoryMarkKind Kind, string DerivedSkillId, CardConversionSource Origin, string OriginGameplayHash, long GiftSequence);
public sealed record RecipientCategoryMarkMaterial(int CardId, CardLocation From, int SlotIndex, bool Hidden);
public sealed record ProgramRecipientCategoryMarkReceipt
{
    private IReadOnlyList<RecipientCategoryMarkMaterial> _materials = Array.AsReadOnly(Array.Empty<RecipientCategoryMarkMaterial>());
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>()), _candidates = Array.AsReadOnly(Array.Empty<int>()),
        _baseTargets = Array.AsReadOnly(Array.Empty<int>()), _added = Array.AsReadOnly(Array.Empty<int>());
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string SourceSkillId { get; init; }
    public required string StateId { get; init; }
    public SkillProgramEffectOp Operation { get; init; }
    public RecipientCategoryMarkStage Stage { get; init; }
    public int HolderSeat { get; init; }
    public long ParentWindowId { get; init; }
    public long? DyingFrameId { get; init; }
    public DyingSuitsOriginalCursor? OriginalDyingCursor { get; init; }
    public long? CardUseFrameId { get; init; }
    public long? ActionId { get; init; }
    public RecipientCategoryMarkToken? Token { get; init; }
    public bool Consumed { get; init; }
    public int? TargetSeat { get; init; }
    public IReadOnlyList<RecipientCategoryMarkMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedCardIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> CandidateSeats { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> BaseTargetSeats { get => _baseTargets; init => _baseTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargetSeats { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? BatchId { get; init; }
    public bool RecoveryIssued { get; init; }
    public int RecoveryAmount { get; init; }
    public bool DrawIssued { get; init; }
    public int DrawActual { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRecipientCategoryMarkReceipt? RecipientCategoryMark { get; init; }
}
public sealed record RecipientCategoryMarkStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string SourceSkillId, string StateId, SkillProgramEffectOp Operation, int HolderSeat, long ParentWindowId, long? TokenId) : IGameEvent;
public sealed record RecipientCategoryMarkGrantedEvent(long FrameId, RecipientCategoryMarkToken Token) : IGameEvent;
public sealed record RecipientCategoryMarkConsumedEvent(long FrameId, long TokenId, int HolderSeat, string SourceSkillId,
    string StateId, RecipientCategoryMarkKind Kind) : IGameEvent;
public sealed record RecipientCategoryMarkMovementPaidEvent(long FrameId, int SubjectSeat, int Count, long SequenceBefore,
    long SequenceAfter, long BatchId) : IGameEvent;
public sealed record RecipientCategoryMarkRecoveryIssuedEvent(long FrameId, int HolderSeat, int Amount) : IGameEvent;
public sealed record RecipientCategoryMarkDyingCursorIssuedEvent(long FrameId, long DyingFrameId, string CursorHash) : IGameEvent;
public sealed record RecipientCategoryMarkDrawIssuedEvent(long FrameId, int HolderSeat, int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record RecipientCategoryMarkCompletedEvent(long FrameId, SkillProgramEffectOp Operation, bool Consumed) : IGameEvent;
public sealed record RecipientCategorySlashTargetsResolvedEvent : IGameEvent
{
    private IReadOnlyList<int> _before = Array.AsReadOnly(Array.Empty<int>()), _added = Array.AsReadOnly(Array.Empty<int>()), _result = Array.AsReadOnly(Array.Empty<int>());
    public long FrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long ActionId { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public long TokenId { get; init; }
    public IReadOnlyList<int> BeforeTargets { get => _before; init => _before = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ResultTargets { get => _result; init => _result = Array.AsReadOnly(value.ToArray()); }
}
internal interface IRecipientCategoryMarksProgramHost
{
    SkillProgramStepOutcome ExecuteRecipientCategoryMark(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class RecipientCategoryMarkDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => Op switch {
        SkillProgramEffectOp.GiveHandAndGrantCategoryMark => new GiveHandAndGrantCategoryMarkHandler(),
        SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => new SpendCategoryMarkForDyingRecoveryHandler(),
        SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => new SpendCategoryMarkForAreaDiscardHandler(),
        _ => new SpendCategoryMarkForSlashTargetsHandler() };
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.RecipientCategoryMark(e));
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var gift = Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark;
        if (gift) r.AllowOnly("op", "target", "stateId", "skillIds", "condition");
        else r.AllowOnly("op", "target", "stateId", "sourceSkillId", "condition");
        var ids = gift ? r.RequiredIdentifierArray("skillIds") : Array.Empty<string>();
        if (gift && (ids.Count != 3 || ids.Distinct(StringComparer.Ordinal).Count() != 3))
            throw new InvalidOperationException("A category mark gift requires its three distinct rescue/discard/targets skills.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition(),
            sourceBind: gift ? null : r.RequiredIdentifier("sourceSkillId"), stateId: r.RequiredIdentifier("stateId"), skillIds: ids);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op switch {
            SkillProgramEffectOp.GiveHandAndGrantCategoryMark => SkillProgramTriggerWindow.DrawPhaseEnded,
            SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => SkillProgramTriggerWindow.DyingEntering,
            SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => SkillProgramTriggerWindow.PlayPhaseStarting,
            _ => SkillProgramTriggerWindow.CardUseTargetsFinalized })];
}
internal sealed class GiveHandAndGrantCategoryMarkDescriptor : RecipientCategoryMarkDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveHandAndGrantCategoryMark; }
internal sealed class SpendCategoryMarkForDyingRecoveryDescriptor : RecipientCategoryMarkDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery; }
internal sealed class SpendCategoryMarkForAreaDiscardDescriptor : RecipientCategoryMarkDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard; }
internal sealed class SpendCategoryMarkForSlashTargetsDescriptor : RecipientCategoryMarkDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForSlashTargets; }
public abstract class RecipientCategoryMarkHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IRecipientCategoryMarksProgramHost)host).ExecuteRecipientCategoryMark(f, e);
}
public sealed class GiveHandAndGrantCategoryMarkHandler : RecipientCategoryMarkHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveHandAndGrantCategoryMark; }
public sealed class SpendCategoryMarkForDyingRecoveryHandler : RecipientCategoryMarkHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery; }
public sealed class SpendCategoryMarkForAreaDiscardHandler : RecipientCategoryMarkHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard; }
public sealed class SpendCategoryMarkForSlashTargetsHandler : RecipientCategoryMarkHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendCategoryMarkForSlashTargets; }
internal static class RecipientCategoryMarksComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.GiveHandAndGrantCategoryMark or
        SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery or SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard or SkillProgramEffectOp.SpendCategoryMarkForSlashTargets;
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var p in programs.Values)
        {
            if (p.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op)))) throw new InvalidOperationException("Recipient marks are trigger-only.");
            foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
            {
                if (t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always } e] ||
                    !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
                    t.MarkerCost is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null ||
                    t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 ||
                    t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementOccurrence is not null || t.DamageOccurrence is not null ||
                    t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.EvaluateConditionAtResolution)
                    throw new InvalidOperationException("Recipient marks require a sole unconditional optional owner instruction without quota.");
                var valid = e.Op switch {
                    SkillProgramEffectOp.GiveHandAndGrantCategoryMark => t.Window == SkillProgramTriggerWindow.DrawPhaseEnded && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && t.Subject == SkillProgramTriggerSubject.Owner,
                    SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => t.Window == SkillProgramTriggerWindow.DyingEntering && t.Subject == SkillProgramTriggerSubject.Owner,
                    SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
                    _ => t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                        t.SingleActionInstance && !t.IncludeResponseUses };
                if (!valid) throw new InvalidOperationException($"Invalid recipient category mark trigger '{p.Id}/{t.Id}'.");
                if (e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark)
                {
                    var expected = new[] { SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery, SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard, SkillProgramEffectOp.SpendCategoryMarkForSlashTargets };
                    for (var i = 0; i < 3; i++)
                        if (e.SkillIds[i] == p.Id || !programs.TryGetValue(e.SkillIds[i], out var derived) || derived.Activations.Count != 0 || derived.Triggers.Count != 1 ||
                            derived.Triggers[0].Effects is not [var use] || use.Op != expected[i] || use.SourceBind != p.Id || use.StateId != e.StateId)
                            throw new InvalidOperationException("Category mark mapping requires three nonrecursive exact derived skills.");
                }
                else if (!programs.TryGetValue(e.SourceBind!, out var source) || !source.Triggers.Any(b => b.Effects is [{ Op: SkillProgramEffectOp.GiveHandAndGrantCategoryMark } gift] &&
                    gift.StateId == e.StateId && gift.SkillIds[(int)(e.Op == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery ? RecipientCategoryMarkKind.Rescue :
                        e.Op == SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard ? RecipientCategoryMarkKind.Discard : RecipientCategoryMarkKind.ExtraTargets)] == p.Id))
                    throw new InvalidOperationException("A derived category mark skill requires its exact gift mapping.");
            }
        }
    }
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void RecipientCategoryMark(SkillProgramEffect e)
    {
        if (e.Op is SkillProgramEffectOp.GiveHandAndGrantCategoryMark or SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery)
            Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition));
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery) _ownerRecovery += Math.Max(0, 1 - _player.Hp);
        // Bounded public opportunity prior; no foreign hidden hand identities are read.
        if (e.Op is SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard or SkillProgramEffectOp.SpendCategoryMarkForSlashTargets) _otherAdjustment += 8d;
    }
}
