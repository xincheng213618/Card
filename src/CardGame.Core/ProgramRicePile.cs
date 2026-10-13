using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum RicePileStage { DrawChildren, StoreSelection, MovementChildren, RecoveryChildren }
/// <summary>One actual ending, damage point, pile exhaustion or judgment payment.</summary>
public sealed record RicePileReceipt
{
    private IReadOnlyList<int> _selected = [], _cards = [];
    private IReadOnlyList<CardLocation> _from = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long ParentId { get; init; }
    public long? DamageId { get; init; }
    public int OccurrenceIndex { get; init; }
    public int ActualTurn { get; init; }
    public RicePileStage Stage { get; init; }
    public PublicPersistentPileSource Pile { get; init; } = null!;
    public int BeneficiarySeat { get; init; }
    public int DrawActual { get; init; }
    public int StoreRequired { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    public bool RecoveryIssued { get; init; }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RicePileReceipt? RicePile { get; init; } }
public sealed record RicePileStartedEvent(long ProgramFrameId, RicePileReceipt Receipt) : IGameEvent;
public sealed record RicePileDrawIssuedEvent(long ProgramFrameId, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record RicePilePaymentIssuedEvent(long ProgramFrameId, RicePileReceipt Receipt) : IGameEvent;
public sealed record RicePileRecoveryIssuedEvent(long ProgramFrameId, long MovementBatchId, CardConversionSource Source, PublicPersistentPileSource Pile) : IGameEvent;
public sealed record RicePileCompletedEvent(long ProgramFrameId, SkillProgramEffectOp Operation, bool Completed) : IGameEvent;

internal interface IRicePileProgramHost
{ SkillProgramStepOutcome ExecuteRicePile(SkillProgramEffect effect, ProgramSkillFrame frame); }
internal abstract class RicePileDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new RicePileHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.RecoverAfterLastPublicRice ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner,
            effect.Op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? 2 : 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var referenced = Op is SkillProgramEffectOp.OfferRiceAfterDamagePoint or SkillProgramEffectOp.ReplaceJudgmentFromPublicRice;
        if (referenced) r.AllowOnly("op", "target", "skillIds", "condition"); else r.AllowOnly("op", "target", "condition");
        var skills = referenced ? r.RequiredIdentifierArray("skillIds") : [];
        if (referenced && skills.Count != 1) throw new InvalidOperationException(r.Path + ": rice consumers require one exact producer skill.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), skillIds: skills,
            oldCardDestination: Op == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice ? SkillProgramOldJudgmentCardDestination.DiscardPile : null);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op switch
        {
            SkillProgramEffectOp.DrawAndStoreTwoPublicRice => SkillProgramTriggerWindow.TurnEnding,
            SkillProgramEffectOp.OfferRiceAfterDamagePoint => SkillProgramTriggerWindow.AfterDamageApplied,
            SkillProgramEffectOp.ReplaceJudgmentFromPublicRice => SkillProgramTriggerWindow.JudgmentReplacing,
            _ => SkillProgramTriggerWindow.CardsMoved
        })];
}
internal sealed class DrawAndStoreTwoPublicRiceDescriptor : RicePileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAndStoreTwoPublicRice; }
internal sealed class OfferRiceAfterDamagePointDescriptor : RicePileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferRiceAfterDamagePoint; }
internal sealed class ReplaceJudgmentFromPublicRiceDescriptor : RicePileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceJudgmentFromPublicRice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.JudgmentReplacement;
}
internal sealed class RecoverAfterLastPublicRiceDescriptor : RicePileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverAfterLastPublicRice; }
internal sealed class RicePileHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IRicePileProgramHost)host).ExecuteRicePile(e, f);
}
internal static class RicePileContract
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.DrawAndStoreTwoPublicRice or
        SkillProgramEffectOp.OfferRiceAfterDamagePoint or SkillProgramEffectOp.ReplaceJudgmentFromPublicRice or SkillProgramEffectOp.RecoverAfterLastPublicRice;
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsOperation(e.Op))) return;
        if (t.Effects is not [var e] || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null ||
            t.MarkerCost is not null || t.ChoiceGroup is not null || t.OwnerRelation is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            t.DamageCardKinds.Count != 0 || t.ExcludedReasons.Count != 0 || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null)
            throw new InvalidOperationException(path + ": rice requires one exact unmetered, unfiltered operation.");
        bool valid = e.Op switch
        {
            SkillProgramEffectOp.DrawAndStoreTwoPublicRice => t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional,
            SkillProgramEffectOp.OfferRiceAfterDamagePoint => t.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                t.Subject is SkillProgramTriggerSubject.Owner or SkillProgramTriggerSubject.DamageSource && t.Optional && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint,
            SkillProgramEffectOp.ReplaceJudgmentFromPublicRice => t.Window == SkillProgramTriggerWindow.JudgmentReplacing && t.Subject == SkillProgramTriggerSubject.Any && t.Optional,
            SkillProgramEffectOp.RecoverAfterLastPublicRice => t.Window == SkillProgramTriggerWindow.CardsMoved && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
                t.SourceZones.SequenceEqual([CardZoneKind.PublicPersistentPile]) && t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                !t.MovementDiscardOnly && t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && !t.IgnoreOwnSkillMovements,
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": rice lost its actual ending, each damage point, any judgment or exact exhausted pile boundary.");
    }
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var p in programs.Values)
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
        {
            ValidateTrigger(p.Id, t); var e = t.Effects.Single();
            var source = e.Op is SkillProgramEffectOp.DrawAndStoreTwoPublicRice or SkillProgramEffectOp.RecoverAfterLastPublicRice ? p :
                programs.GetValueOrDefault(e.SkillIds.Single());
            if (source is null || source.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.DrawAndStoreTwoPublicRice }]) != 1 ||
                source.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.RecoverAfterLastPublicRice }]) != 1)
                throw new InvalidOperationException(p.Id + ": rice requires its complete exact ending producer and exhaustion recovery pair.");
        }
    }
}
