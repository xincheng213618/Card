using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyQiaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class HappyQiaoDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new HappyQiaoHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHealthChanged)];
}
internal sealed class HappyQiaoShutuDescriptor() : HappyQiaoDescriptor(SkillProgramEffectOp.ResolveHappyQiaoShutu);
internal sealed class HappyQiaoTongxinDescriptor() : HappyQiaoDescriptor(SkillProgramEffectOp.ResolveHappyQiaoTongxin);
internal sealed class HappyQiaoHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IHappyQiaoHost>().Resolve(frame, operation);
}
internal static class HappyQiaoComposition
{
    internal const string Shuangshu = "happy:shuangshu", Shutu = "happy:shutu", Tongxin = "happy:tongxin";
    [ProgramCatalogValidation(42400)] internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveHappyQiaoShutu or SkillProgramEffectOp.ResolveHappyQiaoTongxin)) return;
        if (program.Activations.Count != 0 || program.Modifiers.Count != 0 || program.ViewAs.Count != 0 || program.CardIdentities.Count != 0 || program.CardPolicies.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0 ||
            program.Triggers is not [{ Id: "pool-health", Window: SkillProgramTriggerWindow.AfterHealthChanged, Subject: SkillProgramTriggerSubject.Owner,
                UsageLimit: null, UsageScope: null, MarkerCost: null, Condition.Kind: SkillProgramTriggerConditionKind.Always } trigger] ||
            trigger.Effects is not [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } effect] ||
            !(program.Id == Shutu && trigger.Optional && effect.Op == SkillProgramEffectOp.ResolveHappyQiaoShutu ||
              program.Id == Tongxin && !trigger.Optional && !trigger.IsSkillInvocation && effect.Op == SkillProgramEffectOp.ResolveHappyQiaoTongxin))
            throw new InvalidOperationException(path + ": Happy Qiao requires one original actual HP-change opportunity and its exact pool/actual red Use qualification.");
    }
}

public enum HappyQiaoStage { SuitChoice, GainChildren, UseChoice, IssuingUse, Declaration, NativeUse, ReturnedUse, HealChoice, HealChildren, DiscardChoice, TargetChoice, DiscardChildren, Damage, DamageChildren, Finished }
public sealed record HappyQiaoPlan(string Form, LegalAction Action, CardKind Kind)
{
    private IReadOnlyList<int> _cards = [], _targets = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HappyQiaoNativeUse(long FrameId, int Instruction, CardConversionSource Source, string Hash, int Turn, HappyQiaoPlan Plan, long UseId, long ActionId)
{
    private IReadOnlyList<CardActionCost> _costs = [];
    private IReadOnlyList<int> _targets = [];
    public IReadOnlyList<CardActionCost> Costs { get => _costs; init => _costs = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HappyQiaoReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long Parent, int Turn, HpChangeContext Change, HappyQiaoStage Stage)
{
    public int Target { get; init; } = -1;
    public int CardId { get; init; }
    public CardLocation? CostFrom { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? CostBatch { get; init; }
    public HappyQiaoNativeUse? Native { get; init; }
    public bool DeclarationPassed { get; init; }
    public long DeclarationId { get; init; }
    public bool HealIssued { get; init; }
    private IReadOnlyList<Suit> _suits = [];
    private IReadOnlyList<int> _gained = [];
    public IReadOnlyList<Suit> Suits { get => _suits; init => _suits = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Gained { get => _gained; init => _gained = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyQiaoReceipt? HappyQiao { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyQiaoNativeUse? HappyQiaoShutuUse { get; init; } }
public sealed record HappyQiaoStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long Parent, int Turn, long ChangeId) : IGameEvent;
public sealed record HappyQiaoRedHealthQualifiedEvent(long ChangeId, int Owner, string Instance, long UseId, long ActionId, int Target, HpChangeKind Kind) : IGameEvent;
public sealed record HappyQiaoBranchEvent(long FrameId, HappyQiaoStage Stage, string Branch) : IGameEvent;
public sealed record HappyQiaoMovementEvent(long FrameId, HappyQiaoStage Stage, long Before, long After, int Count) : IGameEvent;
public sealed record HappyQiaoCostPaidEvent(long FrameId, long BatchId, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record HappyQiaoUseIssuedEvent(long FrameId, long UseId, long ActionId, int Actor) : IGameEvent;
public sealed record HappyQiaoUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record HappyQiaoDeclarationReturnedEvent(long FrameId, long DeclarationId, bool Succeeded) : IGameEvent;
public sealed record HappyQiaoCompletedEvent(long FrameId) : IGameEvent;
