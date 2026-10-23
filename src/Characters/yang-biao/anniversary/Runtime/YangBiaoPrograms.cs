using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryYangBiaoHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryYangBiaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryYangBiaoHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee ? new(Triggered: ProgramTrackedFact.NativeLogicalDiscard) : default;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)],
        SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan => [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterNormalDraw, SkillProgramTriggerWindow.DrawPhaseEnded])],
        SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering), new SelectSingleTarget(), new CreateChoiceResult("jinjie-pay", ["paid", "decline"])],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]
    };
}
internal sealed class ScheduleAnniversaryYangBiaoZhaohanDescriptor : AnniversaryYangBiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan; }
internal sealed class ResolveAnniversaryYangBiaoZhaohanDescriptor : AnniversaryYangBiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan; }
internal sealed class ResolveAnniversaryYangBiaoJinjieDescriptor : AnniversaryYangBiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie; }
internal sealed class ResolveAnniversaryYangBiaoJueeDescriptor : AnniversaryYangBiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee; }
internal sealed class AnniversaryYangBiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryYangBiaoHost>().Begin(f, op);
}
public enum AnniversaryYangBiaoStage { ZhaohanBranch, Recipient, Materials, Amount, Drawing, DrawChildren, RescuePay, CostChildren, FreezeBudget, NextUse, UsePlan, IssuingUse, NativeUse, ReturnedUse, Completed }
public sealed record AnniversaryYangBiaoPlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId = null, CardKind? RequiredKind = null)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryYangBiaoReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, int Round, AnniversaryYangBiaoStage Stage)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(Array.Empty<CardLocation>());
    private IReadOnlyList<CardKind> _kinds = Array.AsReadOnly(Array.Empty<CardKind>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardKind> RandomKinds { get => _kinds; init => _kinds = Array.AsReadOnly(value.ToArray()); }
    public long DyingId { get; init; }
    public long ArmId { get; init; }
    public int Target { get; init; } = -1;
    public int Amount { get; init; }
    public string Branch { get; init; } = "";
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int Budget { get; init; }
    public int Cursor { get; init; }
    public int DiscardCount { get; init; }
    public int Maximum { get; init; }
    public CardKind? RandomKind { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    public AnniversaryYangBiaoPlan? Plan { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool HadAttack { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryYangBiaoReceipt? AnniversaryYangBiao { get; init; }
}
public sealed record AnniversaryYangBiaoDrawArmedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, long DrawWindowId, long DrawPhaseId = 0) : IGameEvent;
public sealed record AnniversaryYangBiaoStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, int Round, long DyingId, long ArmId) : IGameEvent;
public sealed record AnniversaryYangBiaoPaymentIssuedEvent(long FrameId, string Branch, int Target, int Count, long Before) : IGameEvent;
public sealed record AnniversaryYangBiaoDrawReturnedEvent(long FrameId, int Target, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryYangBiaoRescueSelectedEvent(long FrameId, long DyingId, int Victim, int Count, bool Paid) : IGameEvent;
public sealed record AnniversaryYangBiaoBudgetEvent(long FrameId, int Target, int DiscardCount, int Maximum, int Budget) : IGameEvent;
public sealed record AnniversaryYangBiaoRandomCardEvent(long FrameId, int Ordinal, CardKind Kind) : IGameEvent;
public sealed record AnniversaryYangBiaoNativeIssuedEvent(long FrameId, int Ordinal, long UseId, long ActionId, CardKind Kind, int Target) : IGameEvent;
public sealed record AnniversaryYangBiaoNativeReturnedEvent(long FrameId, int Ordinal, long UseId, bool HadAttack) : IGameEvent;
internal static class AnniversaryYangBiaoComposition
{
    internal const string Zhaohan = "anniversary:zhaohan", Jinjie = "anniversary:jinjie", Juee = "anniversary:juee";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan or SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan or SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie or SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee;
    [ProgramCatalogValidation(43700)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null;
        var valid = p.Id switch
        {
            Zhaohan => p.Triggers is [{ Id: "extra-normal-draw", Window: SkillProgramTriggerWindow.DrawPhaseStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: true, IsSkillInvocation: true, Effects: [{ Op: SkillProgramEffectOp.AdjustNormalDraw, Amount: 2, Target: SkillProgramEffectTarget.Owner }, var arm] },
                { Id: "settle-extra-draw", Window: SkillProgramTriggerWindow.AfterNormalDraw, Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false, Effects: [var settle] },
                { Id: "settle-phase-draw", Window: SkillProgramTriggerWindow.DrawPhaseEnded, Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false, Effects: [var ended] }] && Plain(arm, SkillProgramEffectOp.ScheduleAnniversaryYangBiaoZhaohan) && Plain(settle, SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan) && Plain(ended, SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan),
            Jinjie => p.Triggers is [{ Id: "dying-support", Window: SkillProgramTriggerWindow.DyingEntering, Subject: SkillProgramTriggerSubject.Any, Optional: true, UsageScope: SkillUsageScope.Round, UsageLimit: 1,
                Effects: [var prepare, { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "jinjie-pay", OptionId: "paid" } }] }] && Plain(prepare, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie),
            Juee => p.Triggers is [{ Id: "ending-random-uses", Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.AnyLiving, Optional: true, UsageScope: SkillUsageScope.Round, UsageLimit: 1, Effects: [var ending] }] && Plain(ending, SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee),
            _ => false
        };
        if (!valid || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": current Yang Biao requires complete normal-draw obligation, optional round Dying native recovery and real ending random Use sequence.");
    }
}
