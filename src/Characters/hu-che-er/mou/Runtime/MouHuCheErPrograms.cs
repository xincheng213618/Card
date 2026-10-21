using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouHuCheErStage { Target, HandSlot, DiscardChildren, SlashIssuing, NativeSlash, SlashChildren, Drawing, DrawChildren }
public sealed record MouHuCheErHand(int Id, int Slot, CardKind Kind, Suit Suit);
public sealed record MouHuCheErReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase)
{
    public MouHuCheErStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public int Shown { get; init; }
    public Suit Suit { get; init; }
    public SkillProgramCardCategory Category { get; init; }
    public int Count { get; init; }
    public int Index { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public bool Hurt { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<MouHuCheErHand> _hand = [];
    public IReadOnlyList<MouHuCheErHand> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _discards = [];
    public IReadOnlyList<int> Discards { get => _discards; init => _discards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouHuCheErSlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Index, int Target, long UseId, long ActionId);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHuCheErReceipt? MouHuCheEr { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHuCheErSlashOrigin? MouHuCheErSlash { get; init; } }
public sealed record MouHuCheErStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouHuCheErTargetEvent(long FrameId, int Target) : IGameEvent;
public sealed record MouHuCheErShownEvent(long FrameId, int Target, int CardId, Suit Suit, SkillProgramCardCategory Category) : IGameEvent;
public sealed record MouHuCheErDiscardedEvent(long FrameId, int Target, int CardId, long Sequence) : IGameEvent;
public sealed record MouHuCheErCountedEvent(long FrameId, int Target, int Count, SkillProgramCardCategory Category) : IGameEvent;
public sealed record MouHuCheErSlashIssuedEvent(MouHuCheErSlashOrigin Origin) : IGameEvent;
public sealed record MouHuCheErSlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouHuCheErDamageEvent(long FrameId, long UseId, int Target, int Amount) : IGameEvent;
public sealed record MouHuCheErDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouHuCheErCompletedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int Target, bool Hurt) : IGameEvent;
public sealed record MouHuCheErPhaseSettledEvent(long FrameId, ActualDiscardRecoveryPhaseKey Phase, int Target, bool Applied) : IGameEvent;
public sealed record MouHuCheErDebtEvent(long FrameId, int Target, int IssuedTurn, long PhaseToken) : IGameEvent;
public sealed record MouHuCheErDebtTurnEvent(long FrameId, int Target, int Turn) : IGameEvent;
public sealed record MouHuCheErDebtExpiredEvent(long FrameId, int Target, int Turn) : IGameEvent;
internal interface IMouHuCheErHost : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f); }
internal interface IMouHuCheErFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool Friendly(int owner, int other); bool HasHp(int seat); int Hp(int seat); int MaxHp(int seat); int HandCount(int seat); string Name(int seat);
    string SkillName(string id); string SkillDescription(string id); SkillProgram Program(string id); bool Owns(ProgramSkillFrame f);
    ActualDiscardRecoveryPhaseKey? Phase(); bool Recorded(ActualDiscardRecoveryPhaseKey phase); IReadOnlyList<int> Targets(int owner);
    IReadOnlyList<MouHuCheErHand> Hand(int target); bool InHand(int target, int id); SkillProgramCardCategory Category(CardKind kind); IReadOnlyList<int> SameSuitDiscards(int target, Suit suit); int CategoryCount(int target, SkillProgramCardCategory category);
    IEnumerable<T> History<T>() where T : IGameEvent;
    MouHuCheErDamageEvent? Damage(IGameEvent payload);
}
internal interface IMouHuCheErNative : IProgramRuntimeOperations
{
    void PublishState(); void Finish(ProgramSkillFrame f); bool Drain(ProgramSkillFrame f); void Reveal(ProgramSkillFrame f); void Discard(ProgramSkillFrame f); bool CanSlash(ProgramSkillFrame f); void Slash(ProgramSkillFrame f);
}
internal sealed class ResolveMouHuCheErKongwuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuCheErKongwu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MouHuCheErHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouHuCheErHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuCheErKongwu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouHuCheErHost>().Resolve(f);
}
internal static class MouHuCheErComposition
{
    internal const string Kongwu = "mou:kongwu";
    [ProgramCatalogValidation(1800)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id != Kongwu && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveMouHuCheErKongwu)) return;
        if (p.Id != Kongwu || p.Activations is not [var a] || a.Id != "show-hand" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerPhase != 1 ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouHuCheErKongwu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0)
            throw new InvalidOperationException(path + ": Kongwu requires its exact native actual-Play reveal, whole-suit discard, counted Slash chain and issued phase-tail debt.");
    }
}
