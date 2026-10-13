using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DamageSourceHandChoiceStage { Branch, DiscardSelection, MovementChildren, DamageIssued, Completed }
public sealed record DamageSourceHandChoiceReceipt
{
    private IReadOnlyList<int> _selectedCards = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int InstructionIndex { get; init; }
    public long WindowFrameId { get; init; }
    public long DamageFrameId { get; init; }
    public int DamageSourceSeat { get; init; }
    public int DamageAmount { get; init; }
    public int SourceHandCount { get; init; }
    public int OwnerHandCount { get; init; }
    public DamageSourceHandChoiceStage Stage { get; init; }
    public bool Draw { get; init; }
    public int DrawActual { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public IReadOnlyList<int> SelectedCards { get => _selectedCards; init => _selectedCards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record DamageSourceHandChoiceOfferedEvent(long ProgramFrameId, DamageSourceHandChoiceReceipt Receipt) : IGameEvent;
public sealed record DamageSourceHandChoicePaidEvent(long ProgramFrameId, DamageSourceHandChoiceReceipt Receipt) : IGameEvent;
public sealed record DamageSourceHandChoiceCompletedEvent(long ProgramFrameId, long OriginalDamageFrameId, int DamageSourceSeat, bool Draw) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DamageSourceHandChoiceReceipt? DamageSourceHandChoice { get; init; } }
internal interface IDamageSourceHandChoiceHost
{ SkillProgramStepOutcome ResolveDamageSourceHandChoice(ProgramSkillFrame frame); }
internal sealed class ResolveDamageSourceHandChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDamageSourceHandChoice;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveDamageSourceHandChoiceHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(8));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDamageSourceHandChoice)) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.ResolveDamageSourceHandChoice, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional ||
            t.RequireDamageSource != true || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null)
            throw new InvalidOperationException($"{path}: the source hand choice requires one optional sourced after-damage owner operation.");
    }
}
internal sealed class ResolveDamageSourceHandChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDamageSourceHandChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IDamageSourceHandChoiceHost)h).ResolveDamageSourceHandChoice(f);
}
