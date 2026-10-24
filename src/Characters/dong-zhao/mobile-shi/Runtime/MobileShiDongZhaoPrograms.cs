using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MobileShiDongZhaoStage { Benefit, Payment, Target, GainChildren, CostChildren, ExtraDrawChildren, Finished }
public sealed record MobileShiDongZhaoReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, MobileShiDongZhaoStage Stage)
{
    public int TargetSeat { get; init; } = -1;
    public int CardId { get; init; }
    public string? Branch { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public long GrantFrameId { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiDongZhaoReceipt? MobileShiDongZhao { get; init; }
}
public sealed record MobileShiDongZhaoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int ActualTurn, string TriggerId) : IGameEvent;
public sealed record MobileShiDongZhaoBenefitEvent(long FrameId, string Branch, int CardId, int Before, int After) : IGameEvent;
public sealed record MobileShiDongZhaoCostPaidEvent(long FrameId, int CardId, int Before, int After) : IGameEvent;
public sealed record MobileShiDongZhaoExtraGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int IssuedTurn) : IGameEvent;
public sealed record MobileShiDongZhaoExtraEnteredEvent(long GrantFrameId, long FrameId, int TargetSeat, int UpcomingTurn) : IGameEvent;

internal interface IMobileShiDongZhaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileShiDongZhaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileShiDongZhaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op == SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue
        ? [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.AfterDamageApplied])]
        : [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterTurnEnded, SkillProgramTriggerWindow.TurnBeforeStart])];
}
internal sealed class ResolveMobileShiDongZhaoMiaolueDescriptor() : MobileShiDongZhaoDescriptor(SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue);
internal sealed class ResolveMobileShiDongZhaoYingjiaDescriptor() : MobileShiDongZhaoDescriptor(SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia);
internal sealed class MobileShiDongZhaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiDongZhaoHost>().Resolve(frame, op);
}

internal static class MobileShiDongZhaoComposition
{
    [ProgramCatalogValidation(47700)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue or SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia)) return;
        static bool Plain(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Subject == SkillProgramTriggerSubject.Owner &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = program.Id == "mobile:shi-miaolue" ? program.Triggers.Count == 2 &&
            program.Triggers.Any(t => t.Id == "opening-two" && t.Window == SkillProgramTriggerWindow.GameStarting && !t.Optional && Plain(t, SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue)) &&
            program.Triggers.Any(t => t.Id == "damage-benefit" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Optional &&
                t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && Plain(t, SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue)) :
            program.Id == "mobile:shi-yingjia" && program.Triggers.Count == 2 &&
            program.Triggers.Any(t => t.Id == "same-tricks-ended" && t.Window == SkillProgramTriggerWindow.AfterTurnEnded && t.Optional &&
                t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 && Plain(t, SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia)) &&
            program.Triggers.Any(t => t.Id == "issued-extra-start" && t.Window == SkillProgramTriggerWindow.TurnBeforeStart && !t.Optional &&
                t.UsageLimit is null && Plain(t, SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia));
        if (!valid || program.Activations.Count != 0 || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Shi Dong Zhao requires exact opening, per-damage benefit, limited actual-turn trigger and issued extra-start.");
    }
}
