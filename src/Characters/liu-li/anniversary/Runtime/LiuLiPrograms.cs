using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryLiuLiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryLiuLiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryLiuLiHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli ? [new RequireActivationEntry()] : [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)];
}
internal sealed class AnniversaryLiuLiFuliDescriptor : AnniversaryLiuLiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli; }
internal sealed class AnniversaryLiuLiDehuaDescriptor : AnniversaryLiuLiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua; }
internal sealed class AnniversaryLiuLiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryLiuLiHost>().Begin(f, op);
}
public enum AnniversaryLiuLiStage { Revealing, Category, CostChildren, Drawing, DrawChildren, RangeTarget, UseChoice, IssuingUse, NativeUse, ReturnedUse, SkillChildren, Completed }
public sealed record AnniversaryLiuLiPlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId = null, CardKind? RequiredKind = null, bool Adjusted = false)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryLiuLiReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, int Round, AnniversaryLiuLiStage Stage)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public int Category { get; init; } = -1;
    public int NameSum { get; init; }
    public bool DamageDiscarded { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public AnniversaryLiuLiPlan? Plan { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool HadAttack { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryLiuLiReceipt? AnniversaryLiuLi { get; init; }
}
public sealed record AnniversaryLiuLiStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, int Round) : IGameEvent;
public sealed record AnniversaryLiuLiDiscardPaidEvent(long FrameId, int Category, int Count, int NameSum, bool DamageDiscarded, long Before, long After) : IGameEvent;
public sealed record AnniversaryLiuLiDrawIssuedEvent(long FrameId, int NameSum, int MaximumHandCount, int Count) : IGameEvent;
public sealed record AnniversaryLiuLiDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryLiuLiRangeIssuedEvent(long FrameId, CardConversionSource Source, string Hash, int TargetSeat, int OwnerTurnCount, bool SetZero) : IGameEvent;
public sealed record AnniversaryLiuLiPoolNameEvent(int OwnerSeat, CardKind Name) : IGameEvent;
public sealed record AnniversaryLiuLiNativeIssuedEvent(long FrameId, long UseId, long ActionId, CardKind Kind) : IGameEvent;
public sealed record AnniversaryLiuLiNameChosenEvent(long FrameId, CardConversionSource Source, string Hash, CardKind Name, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryLiuLiDehuaCompletedEvent(long FrameId, int OwnerSeat, int Names) : IGameEvent;
internal static class AnniversaryLiuLiComposition
{
    internal const string Fuli = "anniversary:fuli", Dehua = "anniversary:dehua";
    [ProgramCatalogValidation(43300)] internal static void Validate(string path, SkillProgram p)
    {
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).ToArray();
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli or SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua)) return;
        var valid = p.Id == Fuli ? p.Activations is [{ Id: "reveal-discard-draw", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, UsesPerPhase: 1, TargetKind: SkillProgramTargetKind.OtherLiving,
            Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryLiuLiFuli, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }] && p.Triggers.Count == 0 :
            p.Id == Dehua && p.Activations.Count == 0 && p.Triggers is [{ Id: "round-damage-name", Window: SkillProgramTriggerWindow.RoundStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: false,
            Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryLiuLiDehua, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }];
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Liu Li requires exact native hand-category payment and mandatory independent round damage-name Use.");
    }
}
