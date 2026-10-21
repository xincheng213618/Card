using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IShenJiangWeiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class ShenJiangWeiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ShenJiangWeiHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => op == SkillProgramEffectOp.ResolveShenJiangWeiTianren ? new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry) : default;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch {
        SkillProgramEffectOp.ResolveShenJiangWeiTianren => [new RequireTriggerWindows([SkillProgramTriggerWindow.DiscardPileReceived, SkillProgramTriggerWindow.AfterHealthChanged])],
        SkillProgramEffectOp.ResolveShenJiangWeiJiufa => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseCompleted, SkillProgramTriggerWindow.CardResponseCompleted]), new RequireContext(ProgramContextCapability.CardAction)],
        _ => [] };
}
internal sealed class ResolveShenJiangWeiTianrenDescriptor() : ShenJiangWeiDescriptor(SkillProgramEffectOp.ResolveShenJiangWeiTianren);
internal sealed class ResolveShenJiangWeiJiufaDescriptor() : ShenJiangWeiDescriptor(SkillProgramEffectOp.ResolveShenJiangWeiJiufa);
internal sealed class ResolveShenJiangWeiPingxiangDescriptor() : ShenJiangWeiDescriptor(SkillProgramEffectOp.ResolveShenJiangWeiPingxiang);
internal sealed class ShenJiangWeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IShenJiangWeiHost>().Execute(f, op);
}
public enum ShenJiangWeiStage { Threshold, GrowthChildren, DrawAttempt, DrawChildren, JiufaOffer, RevealChildren, RankChoice, GainChildren, DiscardChildren, MaximumChildren, SlashChoice, NativeIssuing, NativeUse, SlashChildren, LostSkillChildren }
public sealed record ShenJiangWeiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int InstructionIndex, long ParentId, ShenJiangWeiStage Stage)
{
    public long ActionId { get; init; }
    public long MovementSequence { get; init; }
    public int Cycle { get; init; }
    public int MaximumBefore { get; init; }
    public int MaximumAfter { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int SlashCount { get; init; }
    public int TargetSeat { get; init; } = -1;
    public long UseId { get; init; }
    public bool NativeReturned { get; init; }
    private IReadOnlyList<int> _revealed = Array.AsReadOnly(Array.Empty<int>()), _selected = Array.AsReadOnly(Array.Empty<int>()), _ranks = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Revealed { get => _revealed; init => _revealed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Ranks { get => _ranks; init => _ranks = Array.AsReadOnly(value.ToArray()); }
    public int RankIndex { get; init; }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenJiangWeiReceipt? ShenJiangWei { get; init; } }
public sealed record ShenJiangWeiSlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Ordinal, long UseId, long ActionId, int TargetSeat);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenJiangWeiSlashOrigin? ShenJiangWeiPingxiang { get; init; } }
public sealed record ShenJiangWeiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, long ActionId, long MovementSequence) : IGameEvent;
public sealed record ShenJiangWeiDiscardOriginEvent(long BatchId, int CardId, int MovementSequence, bool FromUse) : IGameEvent;
public sealed record ShenJiangWeiNameRecordedEvent(long FrameId, CardConversionSource Source, long ActionId, CardKind Name, int Cycle, int DistinctCount) : IGameEvent;
public sealed record ShenJiangWeiMarkReceivedEvent(long FrameId, int OwnerSeat, long MovementSequence) : IGameEvent;
public sealed record ShenJiangWeiGrowthPaidEvent(long FrameId, int Cycle, int MarkerBefore, int Removed, int MaximumBefore, int MaximumAfter) : IGameEvent;
public sealed record ShenJiangWeiDrawReturnedEvent(long FrameId, int Cycle, int ActualCount, long? AttemptId, long Before, long After) : IGameEvent;
public sealed record ShenJiangWeiRankChosenEvent(long FrameId, int Rank, int CardId) : IGameEvent;
public sealed record ShenJiangWeiMovementPaidEvent(long FrameId, ShenJiangWeiStage Stage, long Before, long After, int Count) : IGameEvent;
public sealed record ShenJiangWeiPingxiangPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int MaximumBefore, int MaximumAfter) : IGameEvent;
public sealed record ShenJiangWeiPingxiangCompletedEvent(long FrameId, int OwnerSeat, int SlashCount, int RemovedGrants) : IGameEvent;
public sealed record ShenJiangWeiSlashIssuedEvent(ShenJiangWeiSlashOrigin Origin) : IGameEvent;
public sealed record ShenJiangWeiSlashReturnedEvent(long FrameId, int Ordinal, long UseId, long ActionId) : IGameEvent;

internal interface IShenJiangWeiSelection : IProgramSelectionQuery { bool CanPay(int owner); }
[ProgramSelectionModule(ProgramSelectionStage.Availability, 38400, SkillProgramEffectOp.ResolveShenJiangWeiPingxiang)]
internal sealed class ShenJiangWeiSelection : IProgramSelectionCandidateFilter
{ public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates draft, IProgramSelectionQueries q) => q.Get<IShenJiangWeiSelection>().CanPay(c.OwnerSeat); }

internal static class ShenJiangWeiComposition
{
    internal const string Tianren = "anniversary:tianren", Jiufa = "anniversary:jiufa", Pingxiang = "anniversary:pingxiang";
    [ProgramCatalogValidation(38400)] internal static void Validate(string path, SkillProgram p)
    {
        static bool Owned(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveShenJiangWeiTianren or SkillProgramEffectOp.ResolveShenJiangWeiJiufa or SkillProgramEffectOp.ResolveShenJiangWeiPingxiang;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Owned(e.Op))) return;
        static bool Effect(IReadOnlyList<SkillProgramEffect> es, SkillProgramEffectOp op) => es is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
        static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op) => t.Id == id && t.Window == window && !t.Optional && t.UsageLimit is null && t.UsageScope is null && Effect(t.Effects, op);
        var valid = p.Id switch {
            Tianren => p.Activations.Count == 0 && p.Triggers is [var d, var h] && Trigger(d,"nonuse-discard",SkillProgramTriggerWindow.DiscardPileReceived,SkillProgramEffectOp.ResolveShenJiangWeiTianren) && d.Subject == SkillProgramTriggerSubject.Owner && d.MovementOccurrence is null && d.Condition.Kind == SkillProgramTriggerConditionKind.Always && d.CardKinds.Count == 0 && d.CardCategories.Count == 0 && d.SourceZones.Count == 0 && d.MovementReasons.Count == 0 && !d.MovementDiscardOnly && Trigger(h,"maximum-threshold",SkillProgramTriggerWindow.AfterHealthChanged,SkillProgramEffectOp.ResolveShenJiangWeiTianren) && h.Subject == SkillProgramTriggerSubject.Owner && h.Condition.Kind == SkillProgramTriggerConditionKind.HealthChangeKindIs && h.Condition.HealthChangeKind == HpChangeKind.MaximumHp,
            Jiufa => p.Activations.Count == 0 && p.Triggers is [var u, var r] && Trigger(u,"completed-use-name",SkillProgramTriggerWindow.CardUseCompleted,SkillProgramEffectOp.ResolveShenJiangWeiJiufa) && Trigger(r,"completed-response-name",SkillProgramTriggerWindow.CardResponseCompleted,SkillProgramEffectOp.ResolveShenJiangWeiJiufa) && u.SingleActionInstance && !r.SingleActionInstance && p.Triggers.All(t => t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !t.IncludeResponseUses && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Condition.Kind == SkillProgramTriggerConditionKind.Always),
            Pingxiang => p.Triggers.Count == 0 && p.Activations is [{ Id: "nine-fire-slashes", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerTurn: null, UsesPerGame: 1, UsesPerPhase: null } a] && a.Condition.Kind == SkillProgramConditionKind.Always && Effect(a.Effects,SkillProgramEffectOp.ResolveShenJiangWeiPingxiang),
            _ => false };
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0)
            throw new InvalidOperationException(path + ": Shen Jiang Wei requires his exact formal three-skill native programs.");
    }
}
