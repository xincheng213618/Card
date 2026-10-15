using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum CaoYingStage { Target, Guess, DrawChildren, Inspection }
public sealed record CaoYingReceipt(int InstructionIndex, long WindowId, long? UseId, long? ActionId,
    int TurnNumber, int TurnOwner, CaoYingStage Stage, int? Target = null, int FrozenCategories = 0,
    int Guess = -1, int Correct = -1)
{
    private IReadOnlyList<CardSnapshot> _cards = [];
    public IReadOnlyList<CardSnapshot> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public int DrawCount { get; init; }
}
public sealed record CaoYingGuessResolvedEvent(long ProgramFrameId, CardConversionSource Source,
    int TurnNumber, int TurnOwner, long UseId, long? ActionId, int TargetSeat, int CorrectCount) : IGameEvent;
public sealed record CaoYingDamageConsumedEvent(long ProgramFrameId, long DamageFrameId) : IGameEvent;
public sealed record CaoYingInspectionIssuedEvent(long ProgramFrameId, int ViewerSeat, int TargetSeat, int CardCount) : IGameEvent;
public sealed record CaoYingGrantExpiredEvent(int OwnerSeat, string GrantId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoYingReceipt? CaoYing { get; init; } }
internal interface ICaoYingProgramHost
{ SkillProgramStepOutcome BeginCaoYing(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal abstract class CaoYingDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler => new CaoYingHandler(Op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (Op != SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage) return ParseOwnerAlways(r);
        r.AllowOnly("op", "target", "condition", "skillIds"); var ids = r.RequiredIdentifierArray("skillIds");
        if (ids.Count != 2 || ids.Distinct(StringComparer.Ordinal).Count() != 2) throw new InvalidOperationException($"{r.Path}: Lingren requires exactly two distinct executable linked skills.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), skillIds: ids); RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)] : [];
}
internal sealed class GuessTargetCategoriesAndArmTurnDamageDescriptor : CaoYingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage; }
internal sealed class RandomEligibleOtherHandInspectionDescriptor : CaoYingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RandomEligibleOtherHandInspection; }
internal sealed class CaoYingHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<ICaoYingProgramHost>().BeginCaoYing(frame, effect);
}
internal static class CaoYingContract
{
    internal static void ValidateReferences(ContentSkillDefinition skill, IReadOnlyDictionary<string, ContentSkillDefinition> skills)
    {
        if (skill.Program is not { } program) return;
        foreach (var effect in program.Triggers.SelectMany(t => t.Effects).Where(e => e.Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage))
        {
            if (effect.SkillIds.Count != 2 || effect.SkillIds.Any(id => !skills.TryGetValue(id, out var linked) || linked.Program is null || linked.Program.Triggers.Count == 0))
                throw new InvalidOperationException($"Skill '{skill.Id}' requires two registered executable linked damage/death skills.");
            var damage = skills[effect.SkillIds[0]].Program!; var death = skills[effect.SkillIds[1]].Program!;
            if (!damage.Triggers.Any(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Optional && t.Effects is [{ Op: SkillProgramEffectOp.Draw, Amount: 1 }, { Op: SkillProgramEffectOp.AwaitOwnedCardMovement }, { Op: SkillProgramEffectOp.ClaimDamageCards }]) ||
                !death.Triggers.Any(t => t.Window == SkillProgramTriggerWindow.CharacterDied && t.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimDeathCleanupCards)))
                throw new InvalidOperationException($"Skill '{skill.Id}' requires a linked native damage-card recovery skill followed by a native death-card skill.");
        }
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op is SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage or SkillProgramEffectOp.RandomEligibleOtherHandInspection)) return;
        if (t.Effects is not [var e] || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.IncludeResponseUses ||
            (e.Op == SkillProgramEffectOp.GuessTargetCategoriesAndArmTurnDamage
                ? t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.Optional || t.UsageScope != SkillUsageScope.Turn || t.UsageLimit != 1
                : t.Window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding) || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional))
            throw new InvalidOperationException($"{path}: Cao Ying requires its exact current OL actor-use or own preparation/ending boundary.");
    }
}
