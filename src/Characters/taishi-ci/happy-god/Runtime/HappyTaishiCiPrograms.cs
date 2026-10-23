using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyTaishiCiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class HappyTaishiCiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new HappyTaishiCiHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class HappyTaishiCiChongweiDescriptor : HappyTaishiCiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHappyTaishiCiChongwei;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.TurnBeforeStart,
            SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.OtherActualTurnStarted])];
}
internal sealed class HappyTaishiCiRecoverDescriptor : HappyTaishiCiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverHappyTaishiCiDulie;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class HappyTaishiCiAwakenDescriptor : HappyTaishiCiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenHappyTaishiCiPowei;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class HappyTaishiCiStartSlashDescriptor : HappyTaishiCiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnBeforeStart)];
}
internal sealed class HappyTaishiCiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IHappyTaishiCiHost>().Execute(f, Op);
}
public enum HappyTaishiCiStage { GrantChildren, Drawing, MovementChildren }
public sealed record HappyTaishiCiReceipt(CardConversionSource Source, string Hash, long Parent, int Turn, int Round,
    SkillProgramEffectOp Op, int Count, HappyTaishiCiStage Stage, long Before, long After)
{
    public int? CardId { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyTaishiCiReceipt? HappyTaishiCi { get; init; }
}
public sealed record HappyTaishiCiStartedEvent(long FrameId, CardConversionSource Source, string Hash, long Parent, int Turn, int Round, SkillProgramEffectOp Op, int Count) : IGameEvent;
public sealed record HappyTaishiCiWeiRemovedEvent(long FrameId, CardConversionSource Source, int Target, int Count, int Round) : IGameEvent;
public sealed record HappyTaishiCiAwakenedEvent(long FrameId, CardConversionSource Source, int Round, int DrawCount) : IGameEvent;
public sealed record HappyTaishiCiDulieRecoveredEvent(long FrameId, int OwnerSeat, long DyingFrameId, int WeiCount) : IGameEvent;
public sealed record HappyTaishiCiDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After, long? Attempt) : IGameEvent;

internal static class HappyTaishiCiComposition
{
    [ProgramCatalogValidation(41740)]
    internal static void Validate(string path, SkillProgram p)
    {
        var owned = p.Id is "happy:dulie" or "happy:chongwei" or "happy:powei" or "happy:shenzhu";
        if (!owned)
        {
            if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is
                SkillProgramEffectOp.ResolveHappyTaishiCiChongwei or SkillProgramEffectOp.RecoverHappyTaishiCiDulie or
                SkillProgramEffectOp.AwakenHappyTaishiCiPowei or SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash))
                throw new InvalidOperationException(path + ": Happy Taishi Ci operations require their complete native edition.");
            return;
        }
        var ids = p.Id switch
        {
            "happy:dulie" => new[] { "other-slash-heart", "first-dying-recover" },
            "happy:chongwei" => ["initialize", "rotate", "damage-removes-wei", "marked-turn"],
            "happy:powei" => ["awaken"],
            _ => ["start-slash", "physical-slash-choice"]
        };
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 || !p.Triggers.Select(t => t.Id).SequenceEqual(ids) ||
            p.Triggers.Any(t => t.Optional || t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path + ": Happy Taishi Ci requires every native trigger and its unmodified official edition.");
        foreach (var t in p.Triggers)
        {
            var window = t.Id switch
            {
                "other-slash-heart" => SkillProgramTriggerWindow.CardUseTargetsFinalized,
                "first-dying-recover" => SkillProgramTriggerWindow.DyingEntering,
                "initialize" => SkillProgramTriggerWindow.GameStarting,
                "rotate" or "start-slash" => SkillProgramTriggerWindow.TurnBeforeStart,
                "damage-removes-wei" => SkillProgramTriggerWindow.AfterDamageApplied,
                "marked-turn" => SkillProgramTriggerWindow.OtherActualTurnStarted,
                "awaken" => SkillProgramTriggerWindow.AfterTurnEnded,
                _ => SkillProgramTriggerWindow.CardUseCompleted
            };
            if (t.Window != window || t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner))
                throw new InvalidOperationException(path + ": Happy Taishi Ci lost its exact native occurrence or owner.");
            if (t.Id == "other-slash-heart")
            {
                if (t.OwnerRelation != SkillProgramCardActionOwnerRelation.Target || !t.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
                    t.Effects is not [{ Op: SkillProgramEffectOp.StartJudgment, JudgmentReason: "skill.happy.dulie", ResultBind: "dulie-judgment", OutcomeBind: "dulie-outcome", Visibility: SkillProgramCardSetVisibility.Public },
                        { Op: SkillProgramEffectOp.CancelShenTaishiCiDulie }, { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "dulie-judgment", Destination: SkillProgramCardDestination.DiscardPile }] ||
                    t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
                    throw new InvalidOperationException(path + ": Happy Dulie requires its finalized public judgment, actual target removal and judgment-card cleanup.");
            }
            else if (t.Id == "physical-slash-choice")
            {
                if (t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
                    t.IncludeResponseUses || t.Effects is not [
                        { Op: SkillProgramEffectOp.ChooseOption, ResultBind: "shenzhu-choice", Options: [{ Id: "draw-one" }, { Id: "draw-three" }] },
                        { Op: SkillProgramEffectOp.Draw, Amount: 1, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "shenzhu-choice", OptionId: "draw-one" } },
                        { Op: SkillProgramEffectOp.GrantTurnRuleModifier, RuleQuery: SkillRuleQuery.SlashLimit, RuleOperation: SkillRuleOperation.Add, Amount: 1, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "shenzhu-choice", OptionId: "draw-one" } },
                        { Op: SkillProgramEffectOp.Draw, Amount: 3, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "shenzhu-choice", OptionId: "draw-three" } },
                        { Op: SkillProgramEffectOp.GrantTurnCardActionProhibition, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "shenzhu-choice", OptionId: "draw-three" } } ban] ||
                    !ban.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) || !ban.ActionTypes.SequenceEqual([CardActionType.Use]))
                    throw new InvalidOperationException(path + ": Happy Shenzhu requires its two exact native completed-physical-Slash outcomes.");
            }
            else
            {
                var op = t.Id switch
                {
                    "first-dying-recover" => SkillProgramEffectOp.RecoverHappyTaishiCiDulie,
                    "awaken" => SkillProgramEffectOp.AwakenHappyTaishiCiPowei,
                    "start-slash" => SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash,
                    _ => SkillProgramEffectOp.ResolveHappyTaishiCiChongwei
                };
                if (t.Subject != (t.Id == "damage-removes-wei" ? SkillProgramTriggerSubject.Any : SkillProgramTriggerSubject.Owner) ||
                    t.Effects is not [var only] || only.Op != op || only.Condition.Kind != SkillProgramConditionKind.Always ||
                    t.Id == "awaken" && t.TurnOwnerScope != SkillProgramTurnOwnerScope.AnyLiving ||
                    t.Id == "damage-removes-wei" && t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
                    t.Id is "initialize" or "rotate" or "damage-removes-wei" && t.IsSkillInvocation)
                    throw new InvalidOperationException(path + ": Happy Taishi Ci lost its exact initialization, own migration, damage maintenance, first dying, field awakening or start-Slash pipeline.");
            }
        }
    }
}
