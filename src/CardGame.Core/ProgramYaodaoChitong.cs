using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record YaodaoChitongReceipt(CardConversionSource Source, string GameplayHash, int WeaponCardId,
    long CardUseId, long ActionId, long WindowId, int TargetIndex, int OriginalTargetSeat, bool Resolved = false);
public sealed record YaodaoChitongStartedEvent(long FrameId, YaodaoChitongReceipt Receipt) : IGameEvent;
public sealed record YaodaoChitongResolvedEvent(long FrameId, long CardUseId, long ActionId, int TargetIndex,
    int OriginalTargetSeat, int? NewTargetSeat, long JudgmentFrameId, Suit? FinalSuit,
    int CandidateCount) : IGameEvent;
/// <summary>Trusted owning-frame receipt; never included in a public game event.</summary>
public sealed record YaodaoChitongRandomPayment(uint Before, uint After, int CandidateCount, int? SelectedTargetSeat);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public YaodaoChitongReceipt? YaodaoChitong { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public YaodaoChitongRandomPayment? YaodaoChitongRandom { get; init; }
}

internal interface IYaodaoChitongHost
{
    SkillProgramStepOutcome BeginYaodaoChitong(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveYaodaoChitong(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class BeginYaodaoChitongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BeginYaodaoChitong;
    public override ISkillProgramEffectHandler Handler { get; } = new BeginYaodaoChitongHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartJudgment,
        static (_, _) => { }); // The following native judgment supplies its own estimate.
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashTargetRedirecting), new RequireCardActionActor()];
}
internal sealed class ResolveYaodaoChitongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYaodaoChitong;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveYaodaoChitongHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RedirectCurrentAttack,
        static (_, context) => context.RedirectCurrentAttack());
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction | ProgramContextCapability.Judgment;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashTargetRedirecting), new RequireCardActionActor()];
}
public sealed class BeginYaodaoChitongHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BeginYaodaoChitong;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IYaodaoChitongHost>().BeginYaodaoChitong(f);
}
public sealed class ResolveYaodaoChitongHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYaodaoChitong;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IYaodaoChitongHost>().ResolveYaodaoChitong(f, e);
}
internal static class YaodaoChitongContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op is SkillProgramEffectOp.BeginYaodaoChitong or SkillProgramEffectOp.ResolveYaodaoChitong)) return;
        if (t.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            t.Optional || !ProgramCardKindMatch.IsFullSlashFamily(t.CardKinds) ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.MarkerCost is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.BeginYaodaoChitong },
                { Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.Owner, OutcomeBind: { } outcome, ResultBind: { } cards,
                    JudgmentReason: "shanhe.yaodao-chitong", Visibility: SkillProgramCardSetVisibility.Public, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.ResolveYaodaoChitong, SourceBind: { } source },
                { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: { } cleanup, Destination: SkillProgramCardDestination.DiscardPile,
                    Condition.Kind: SkillProgramConditionKind.Always }] || outcome != source || cards != cleanup)
            throw new InvalidOperationException(path + ": Yaodao Chitong requires the exact mandatory actor Slash judgment, redirect and physical-result cleanup sequence.");
    }
}
