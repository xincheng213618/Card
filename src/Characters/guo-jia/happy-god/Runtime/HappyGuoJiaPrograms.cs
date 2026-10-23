using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum HappyGuoJiaStage { GrowChildren, Recipient, GainChildren, CleanupChildren, Target, Awakening, Drawing, DrawChildren, CostChildren, UseChoice, IssuingUse, NativeUse, ReturnedUse }
public sealed record HappyGuoJiaReceipt(CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, int Turn, int TurnOwner, HappyGuoJiaStage Stage)
{
    public int Target { get; init; } = -1;
    public int Benefactor { get; init; } = -1;
    public int SuitCount { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int MaximumBefore { get; init; }
    public int MaximumAfter { get; init; }
    public ShenGuoJiaPlan? Plan { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyGuoJiaReceipt? HappyGuoJia { get; init; }
}
public sealed record HappyGuoJiaBenefactorIssuedEvent(long FrameId, CardConversionSource Source,
    int Recipient, string GrantedInstance, string GrantSource) : IGameEvent;
internal interface IHappyGuoJiaHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class HappyGuoJiaDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new HappyGuoJiaHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (Op != SkillProgramEffectOp.ResolveHappyGuoJiaHuishi) return ParseOwnerAlways(r);
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveHappyGuoJiaHuishi => [new GiftCardSet(e.SourceBind!)],
        SkillProgramEffectOp.ResolveHappyGuoJiaTianyi => [new ReadSelectedTarget()],
        _ => []
    };
}
internal sealed class ResolveHappyGuoJiaHuishiDescriptor() : HappyGuoJiaDescriptor(SkillProgramEffectOp.ResolveHappyGuoJiaHuishi);
internal sealed class ResolveHappyGuoJiaTianyiDescriptor() : HappyGuoJiaDescriptor(SkillProgramEffectOp.ResolveHappyGuoJiaTianyi);
internal sealed class ResolveHappyGuoJiaHuishiGrantDescriptor() : HappyGuoJiaDescriptor(SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant);
internal sealed class ResolveHappyGuoJiaZuoxingDescriptor() : HappyGuoJiaDescriptor(SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing);
internal sealed class HappyGuoJiaHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IHappyGuoJiaHost>().Resolve(f, op);
}
internal static class HappyGuoJiaComposition
{
    [ProgramCatalogValidation(40900)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Own(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveHappyGuoJiaHuishi or SkillProgramEffectOp.ResolveHappyGuoJiaTianyi or SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant or SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(Own)) return;
        if (p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Happy Guo Jia requires only his native instructions.");
        if (p.Id == "happy:tianyi")
        {
            if (p.Activations.Count != 0 || p.Triggers is not [{ Id: "all-living-damaged", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1 } t] ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects is not [
                    { Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: 2, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.AnyLiving, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.ResolveHappyGuoJiaTianyi, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Happy Tianyi requires its complete native preparation awakening.");
            return;
        }
        if (p.Triggers.Count != 0 || p.Activations is not [var a] || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerTurn is not null)
            throw new InvalidOperationException(path + ": Happy Guo Jia requires his zero-input native activation.");
        var valid = p.Id switch
        {
            "happy:huishi" => a.Id == "five-suit-reveal" && a.UsesPerPhase == 1 && a.UsesPerGame is null && a.Effects is [
                { Op: SkillProgramEffectOp.RevealTopCards, Target: SkillProgramEffectTarget.Owner, Amount: 5, ResultBind: "huishi-five", Visibility: SkillProgramCardSetVisibility.Public, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.ResolveHappyGuoJiaHuishi, Target: SkillProgramEffectTarget.Owner, SourceBind: "huishi-five", Condition.Kind: SkillProgramConditionKind.Always }],
            "happy:huishi-gift" => a.Id == "grant-awakening" && a.UsesPerGame == 1 && a.UsesPerPhase is null && a.Effects is [{ Op: SkillProgramEffectOp.ResolveHappyGuoJiaHuishiGrant, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }],
            "happy:zuoxing" => a.Id == "borrow-ordinary-trick" && a.UsesPerPhase is null && a.UsesPerGame is null && a.Effects is [{ Op: SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }],
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": Happy Guo Jia requires his exact published reveal, grant and turn-name budgets.");
    }
}
