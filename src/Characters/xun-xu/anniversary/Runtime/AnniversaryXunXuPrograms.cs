using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryXunXuHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryXunXuDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryXunXuHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => operation == SkillProgramEffectOp.ResolveAnniversaryXunXuDiyin
        ? [new RequireTriggerWindows([SkillProgramTriggerWindow.DiscardPileReceived, SkillProgramTriggerWindow.AfterDamageApplied])]
        : [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ResolveAnniversaryXunXuDiyinDescriptor() : AnniversaryXunXuDescriptor(SkillProgramEffectOp.ResolveAnniversaryXunXuDiyin);
internal sealed class ResolveAnniversaryXunXuBoqiaCompletionDescriptor() : AnniversaryXunXuDescriptor(SkillProgramEffectOp.ResolveAnniversaryXunXuBoqiaCompletion);
internal sealed class AnniversaryXunXuHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryXunXuHost>().Begin(f, operation);
}
internal static class AnniversaryXunXuComposition
{
    internal const string Diyin = "anniversary:diyin", Boqia = "anniversary:boqia";
    internal static readonly CardKind[] Kinds = [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol,
        CardKind.DrawTwo, CardKind.Dismantlement, CardKind.Snatch, CardKind.PeachGarden, CardKind.FiveGrains, CardKind.IronChain, CardKind.BorrowedSword, CardKind.Nullification];
    internal static bool IsBoqiaRuleId(string id) => id.StartsWith("boqia-", StringComparison.Ordinal);
    [ProgramCatalogValidation(46100)] internal static void Validate(string path, SkillProgram p)
    {
        var marked = p.ViewAs.Any(r => IsBoqiaRuleId(r.Id));
        var operations = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryXunXuDiyin or SkillProgramEffectOp.ResolveAnniversaryXunXuBoqiaCompletion);
        if (p.Id is not (Diyin or Boqia) && !marked && !operations) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0) throw new InvalidOperationException(path + ": Xun Xu requires its exact sourced native pipelines.");
        if (p.Id == Diyin && !marked && p.Triggers.Count == 2 && p.Triggers.Any(t => t.Id == "next-discard" && t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.Subject == SkillProgramTriggerSubject.Owner && t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch) &&
            p.Triggers.Any(t => t.Id == "same-hp" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.Owner && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage) &&
            p.Triggers.All(t => !t.Optional && t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryXunXuDiyin, Target: SkillProgramEffectTarget.Owner }])) return;
        if (p.Id == Boqia && p.ViewAs.Count == Kinds.Length && p.ViewAs.Select(r => r.OutputKind).Distinct().Order().SequenceEqual(Kinds.Order()) &&
            p.ViewAs.All(r => IsBoqiaRuleId(r.Id) && r.InputCount == 3 && !r.VariableInputCount && r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && r.InputKinds.Count == 0 && r.InputSuits.Count == 0 && r.ExtendedUse && r.Condition.Kind == SkillProgramConditionKind.Always &&
                r.ForPlay == (r.OutputKind is not (CardKind.Dodge or CardKind.Nullification)) && r.ForResponse == (r.OutputKind is CardKind.Dodge or CardKind.Peach or CardKind.Alcohol or CardKind.Nullification) && !r.DistanceUnlimited && r.UsesPerPhase is null && r.UsesPerActualTurn is null) &&
            p.Triggers is [{ Id: "completed-use", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, IncludeResponseUses: true, Optional: false, IsSkillInvocation: false, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryXunXuBoqiaCompletion, Target: SkillProgramEffectTarget.Owner }] }]) return;
        throw new InvalidOperationException(path + ": Xun Xu lost his exact seventh-occurrence claim, same-HP damage trigger or complete three-material use and completed-use contract.");
    }
}
public sealed record AnniversaryXunXuReceipt(CardConversionSource Source, string Hash, long ParentId, string Mode, string Stage)
{
    public int Target { get; init; } = -1;
    public int Count { get; init; }
    public int Cursor { get; init; }
    public HappyWeiYanGainRequestSettlement? Gain { get; init; }
    public long ActionId { get; init; }
    public long NativeUseId { get; init; }
    private readonly IReadOnlyList<CardMovementBatchContext> _nativeFinishBatches = [];
    public IReadOnlyList<CardMovementBatchContext> NativeFinishBatches
    {
        get => _nativeFinishBatches;
        init => _nativeFinishBatches = Array.AsReadOnly(value.Select(b => b with { Movements = Array.AsReadOnly(b.Movements.ToArray()),
            SourceCounts = Array.AsReadOnly(b.SourceCounts.ToArray()), DestinationCounts = b.DestinationCounts is null ? null : Array.AsReadOnly(b.DestinationCounts.ToArray()) }).ToArray());
    }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<int> _cards = [], _recipients = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<NativeDrawOutcome> _draws = [];
    public IReadOnlyList<NativeDrawOutcome> Draws { get => _draws; init => _draws = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryXunXuReceipt? AnniversaryXunXu { get; init; } }
public sealed record AnniversaryXunXuLossEvent(CardConversionSource Source, long BatchId, int Occurrence, long SequenceAfter) : IGameEvent;
public sealed record AnniversaryXunXuClaimEvent(CardConversionSource Source, long BatchId, int ThroughSeventh, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record AnniversaryXunXuUseEvent(CardConversionSource Source, long ActionId, int Turn, CardKind Name, bool Reward) : IGameEvent;
public sealed record AnniversaryXunXuRearmedEvent(long FrameId, int OwnerSeat, long ActionId, int Turn, int TargetSeat) : IGameEvent;
public sealed record AnniversaryXunXuStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, string Mode, long ActionId) : IGameEvent;
public sealed record AnniversaryXunXuMovementEvent(long FrameId, string Mode, int Target, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryXunXuDrawEvent(long FrameId, int Ordinal, int Recipient, int Requested, int Actual, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record AnniversaryXunXuNativeBatchEvent(long FrameId, long UseId, long ActionId, long BatchId, long Before, long After) : IGameEvent;
