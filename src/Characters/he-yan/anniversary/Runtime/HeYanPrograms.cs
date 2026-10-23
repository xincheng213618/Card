using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryHeYanHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryHeYanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryHeYanHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(ProgramTrackedFact.NativeLogicalDiscard, ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai
        ? [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)] : [new RequireActivationEntry()];
}
internal sealed class ResolveAnniversaryHeYanYachaiDescriptor : AnniversaryHeYanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHeYanYachai; }
internal sealed class ResolveAnniversaryHeYanQingtanDescriptor : AnniversaryHeYanDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan; }
internal sealed class AnniversaryHeYanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHeYanHost>().Begin(f, op);
}
public enum AnniversaryHeYanStage { SourceChoice, DiscardChoice, SealedChoice, Revealing, SuitChoice, MovementChildren, DrawReady, Drawing, DrawChildren, DiscardRest, Complete }
public sealed record HeYanHandCard(int Id, CardKind Kind, Suit Suit);
public sealed record HeYanHandPlan(int Seat, IReadOnlyList<HeYanHandCard> Cards, int? SelectedId = null)
{
    private IReadOnlyList<HeYanHandCard> _cards = Array.AsReadOnly(Cards.ToArray());
    public IReadOnlyList<HeYanHandCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HeYanPayment(string Kind, int Executor, long Before, long After, IReadOnlyList<int> CardIds, IReadOnlyList<CardMovementRecord> Movements)
{
    private IReadOnlyList<int> _ids = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<CardMovementRecord> _moves = Array.AsReadOnly(Movements.ToArray());
    public IReadOnlyList<CardMovementRecord> Movements { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryHeYanReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner,
    long? ParentId, long? DamageId, int SourceSeat, AnniversaryHeYanStage Stage)
{
    public int ChooserIndex { get; init; }
    public int DrawIndex { get; init; }
    public int DrawTarget { get; init; }
    public int DrawAmount { get; init; }
    public int RequiredDiscard { get; init; }
    public string? Branch { get; init; }
    public Suit? ChosenSuit { get; init; }
    public AnniversaryHeYanStage Following { get; init; }
    public HeYanPayment? Payment { get; init; }
    private IReadOnlyList<HeYanHandPlan> _plans = Array.AsReadOnly(Array.Empty<HeYanHandPlan>());
    public IReadOnlyList<HeYanHandPlan> Plans { get => _plans; init => _plans = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<NativeDrawOutcome> _draws = Array.AsReadOnly(Array.Empty<NativeDrawOutcome>());
    public IReadOnlyList<NativeDrawOutcome> Draws { get => _draws; init => _draws = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHeYanReceipt? AnniversaryHeYan { get; init; } }
public sealed record AnniversaryHeYanStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner, long? ParentId, long? DamageId, int SourceSeat, int ParticipantCount) : IGameEvent;
// The chosen entity is private until all participants have committed.
public sealed record AnniversaryHeYanChoiceCommittedEvent(long FrameId, int Seat) : IGameEvent;
public sealed record AnniversaryHeYanBranchEvent(long FrameId, int SourceSeat, string Branch) : IGameEvent;
public sealed record AnniversaryHeYanHandUseBannedEvent(long FrameId, CardConversionSource Source, int ActualTurn, int TurnOwner, int AffectedSeat) : IGameEvent;
public sealed record AnniversaryHeYanPaidEvent(long FrameId, string Kind, int Executor, long Before, long After, int Count) : IGameEvent;
public sealed record AnniversaryHeYanDrawReturnedEvent(long FrameId, int Ordinal, int TargetSeat, int Requested, int Actual, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record AnniversaryHeYanCompletedEvent(long FrameId) : IGameEvent;

internal static class AnniversaryHeYanComposition
{
    internal const string Yachai = "anniversary:yachai", Qingtan = "anniversary:qingtan";
    [ProgramCatalogValidation(44200)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Yachai or Qingtan) && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryHeYanYachai or SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan)) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null;
        var valid = p.Id == Yachai ? p.Activations.Count == 0 && p.Triggers is [{ Id: "source-three-choices", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Owner, Optional: true, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Effects: [var y] }] && Plain(y, SkillProgramEffectOp.ResolveAnniversaryHeYanYachai) :
            p.Id == Qingtan && p.Triggers.Count == 0 && p.Activations is [{ Id: "simultaneous-suit-hands", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: 1, UsesPerTurn: null, Effects: [var q] }] && Plain(q, SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan);
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": He Yan requires his sourced-damage three choices and once-per-Play simultaneous Hand reveal, native gain and draws.");
    }
}
