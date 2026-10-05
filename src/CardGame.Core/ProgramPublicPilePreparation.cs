using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum PublicPilePreparationStage { Choosing, PaymentChildren, RecoveryChildren, DrawChildren }
public sealed record PublicPilePreparationReceipt
{
    private IReadOnlyList<int> _selected = Array.Empty<int>(), _paid = Array.Empty<int>();
    private IReadOnlyList<CardLocation> _locations = Array.Empty<CardLocation>();
    public int InstructionIndex { get; init; }
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Issuer { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurn { get; init; }
    public long ParentId { get; init; }
    public PublicPilePreparationStage Stage { get; init; }
    public PublicPersistentPileSource? Pile { get; init; }
    public int Branch { get; init; }
    public int BeneficiarySeat { get; init; } = -1;
    public IReadOnlyList<int> SelectedIds { get => _selected; init => _selected = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<int> PaidIds { get => _paid; init => _paid = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<CardLocation> PaidFrom { get => _locations; init => _locations = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public long Before { get; init; }
    public long After { get; init; }
    public int FrozenCount { get; init; }
    public bool RecoveryIssued { get; init; }
    public int DrawRequested { get; init; }
    public int DrawActual { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicPilePreparationReceipt? PublicPilePreparation { get; init; }
}
public sealed record PublicPilePreparationStartedEvent(long ProgramFrameId, SkillProgramEffectOp Operation,
    CardConversionSource Issuer, string GameplayHash, int ActualTurn, long ParentId, PublicPersistentPileSource? Pile) : IGameEvent;
public sealed record PublicPilePreparationPaidEvent(long ProgramFrameId, int Branch, int BeneficiarySeat,
    PublicPersistentPileSource? Pile, IReadOnlyList<int> CardIds, IReadOnlyList<CardLocation> From, long Before, long After) : IGameEvent
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(CardIds.ToArray());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(From.ToArray());
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record PublicPilePreparationRecoveryIssuedEvent(long ProgramFrameId, int BeneficiarySeat, int Requested) : IGameEvent;
public sealed record PublicPilePreparationDrawIssuedEvent(long ProgramFrameId, int BeneficiarySeat,
    int Requested, int Actual, long Before, long After) : IGameEvent;

internal interface IPublicPilePreparationHost
{
    SkillProgramStepOutcome ExecutePublicPilePreparation(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal abstract class PublicPilePreparationDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new PublicPilePreparationHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) =>
        {
            // The same bounded public approximation used by existing public-pile
            // producers keeps optional storage eligible for the normal AI scorer.
            if (effect.Op != SkillProgramEffectOp.RemovePublicPileAfterAttackDamage)
                context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition));
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var referenced = Op != SkillProgramEffectOp.StoreNonBasicOwnedPublicPile;
        if (referenced) r.AllowOnly("op", "target", "skillIds", "condition");
        else r.AllowOnly("op", "target", "condition");
        var skills = referenced ? r.RequiredIdentifierArray("skillIds") : [];
        if (referenced && skills.Count != 1) throw new InvalidOperationException("A preparation pile requires one exact referenced source skill.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), skillIds: skills);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? SkillProgramTriggerWindow.TurnEnding :
            Op == SkillProgramEffectOp.RemovePublicPileAfterAttackDamage ? SkillProgramTriggerWindow.AfterDamageApplied :
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class StoreNonBasicOwnedPublicPileDescriptor : PublicPilePreparationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreNonBasicOwnedPublicPile; }
internal sealed class RemovePublicPileAfterAttackDamageDescriptor : PublicPilePreparationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RemovePublicPileAfterAttackDamage; }
internal sealed class ResolvePreparationPublicPileDescriptor : PublicPilePreparationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePreparationPublicPile; }
internal sealed class PublicPilePreparationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IPublicPilePreparationHost)h).ExecutePublicPilePreparation(e, f);
}
internal static class PublicPilePreparationComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.StoreNonBasicOwnedPublicPile or
        SkillProgramEffectOp.RemovePublicPileAfterAttackDamage or SkillProgramEffectOp.ResolvePreparationPublicPile;
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var p in programs.Values)
            foreach (var e in p.Triggers.SelectMany(t => t.Effects).Where(e => IsOperation(e.Op) && e.Op != SkillProgramEffectOp.StoreNonBasicOwnedPublicPile))
                if (!programs.TryGetValue(e.SkillIds.Single(), out var source) || !source.Triggers.Any(t =>
                    t.Effects is [{ Op: SkillProgramEffectOp.StoreNonBasicOwnedPublicPile }]))
                    throw new InvalidOperationException(p.Id + ": preparation references an actual nonbasic public-pile producer.");
    }
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (a.Effects.Any(e => IsOperation(e.Op))) throw new InvalidOperationException(path + ": preparation pile operations require their real lifecycle or damage window.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope,
        bool optional, IReadOnlyList<CardKind> damageKinds, SkillProgramDamageOccurrence? occurrence)
    {
        if (!effects.Any(e => IsOperation(e.Op))) return;
        if (effects is not [var e] || subject != SkillProgramTriggerSubject.Owner || scope != SkillProgramTurnOwnerScope.Own)
            throw new InvalidOperationException(path + ": preparation pile requires one exact owner producer.");
        if (e.Op == SkillProgramEffectOp.StoreNonBasicOwnedPublicPile ? window != SkillProgramTriggerWindow.TurnEnding || !optional :
            e.Op == SkillProgramEffectOp.ResolvePreparationPublicPile ? window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || optional :
            window != SkillProgramTriggerWindow.AfterDamageApplied || optional || occurrence != SkillProgramDamageOccurrence.PerDamage ||
                !damageKinds.SequenceEqual(new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel }))
            throw new InvalidOperationException(path + ": preparation pile lost its exact ending, start or effective Slash/Duel damage boundary.");
    }
}
