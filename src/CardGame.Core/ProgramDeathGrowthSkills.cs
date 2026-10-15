using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DeathGrowthSkillStage { DrawChildren = 21400, ChooseSkill = 21401 }
public sealed record DeathGrowthSkillReceipt(int InstructionIndex, long WindowFrameId, long DeathFrameId,
    int VictimSeat, CardConversionSource Source, string GameplayHash, IReadOnlyList<string> SkillIds,
    int RequestedDraw, long MovementBefore, long MovementAfter, int DrawCount,
    DeathGrowthSkillStage Stage = DeathGrowthSkillStage.DrawChildren,
    string OriginalDeathHash = "", long? OriginalDyingFrameId = null, string? OriginalDyingHash = null,
    string? OriginalDamageHash = null);
public sealed record DeathGrowthDrawIssuedEvent(long ProgramFrameId, DeathGrowthSkillReceipt Receipt) : IGameEvent;
public sealed record DeathGrowthSkillGrantedEvent(long ProgramFrameId, long DeathFrameId, int OwnerSeat,
    int VictimSeat, CardConversionSource Source, string GrantedSkillId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeathGrowthSkillReceipt? DeathGrowthSkill { get; init; }
}
internal interface IDeathGrowthSkillHost
{ SkillProgramStepOutcome DrawAfterOtherDeathThenChooseMissingSkill(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal sealed class DrawAfterOtherDeathThenChooseMissingSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new DeathGrowthSkillHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => { context.Draw(effect); context.PublicControlValue(6); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "skillIds", "condition");
        var amount = reader.RequiredInt("amount"); var skills = reader.RequiredIdentifierArray("skillIds");
        if (amount is < 1 or > 20 || skills.Count == 0 || skills.Distinct(StringComparer.Ordinal).Count() != skills.Count)
            throw new InvalidOperationException($"{reader.Path}: death growth needs a positive draw and distinct permanent skill candidates.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), amount,
            reader.Condition(), skillIds: skills); RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];
}
internal sealed class DeathGrowthSkillHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IDeathGrowthSkillHost>().DrawAfterOtherDeathThenChooseMissingSkill(frame, effect);
}
internal static class DeathGrowthSkillContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill)) return;
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill }] ||
            trigger.Window != SkillProgramTriggerWindow.CharacterDied || trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.ChoiceGroup is not null || trigger.MarkerCost is not null)
            throw new InvalidOperationException($"{path}: death growth requires one optional owner characterDied binding, with no payment or usage quota.");
    }
}
