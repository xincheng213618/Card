using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShenZhongHuiStage { RoundChoice, RoundDamage, RoundMark, DamageChildren, DamageFollowup, Complete, DrawAttempt, DrawChildren, AwakenFinish, LishiChoice, LishiIssue, PhaseChoice, PhaseJudging, PhaseResult, PhaseCleanup, PhaseNext }
public sealed record ShenZhongHuiReceipt(CardConversionSource Source, string GameplayHash, long ParentId,
    SkillProgramEffectOp Operation, ShenZhongHuiStage Stage, int TargetSeat, int Count = 0, int Mask = 0,
    long ActionId = 0, long JudgmentId = 0, int JudgmentIndex = 0, int? ResultId = null,
    Suit ResultSuit = Suit.None, int ResultRank = 0, bool LastHandDiscarded = false,
    long Before = 0, long After = 0, ActualDiscardRecoveryPhaseKey? Phase = null)
{
    private IReadOnlyList<CardKind> _judgments = Array.AsReadOnly(Array.Empty<CardKind>());
    public IReadOnlyList<CardKind> Judgments { get => _judgments; init => _judgments = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenZhongHuiReceipt? ShenZhongHui { get; init; } }
public sealed record ShenZhongHuiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int TargetSeat) : IGameEvent;
public sealed record LinjieRandomDiscardedEvent(long FrameId, int TargetSeat, int CardId, bool LastHand, long Before, long After) : IGameEvent;
public sealed record LinjieTargetSelectedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record ShenZhongHuiDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record LishiPunishmentIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, int Option, long PhaseToken) : IGameEvent;
public sealed record LishiPhaseAppliedEvent(long IssueFrameId, int TargetSeat, int Option, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record LishiJudgmentReturnedEvent(long FrameId, long JudgmentId, int Index, int? CardId, Suit Suit, int Rank) : IGameEvent;
public sealed record LishiDrawPhaseSettledEvent(long FrameId, ActualDiscardRecoveryPhaseKey Phase, int DrawnCount, int DiscardedCount) : IGameEvent;
public sealed record LishiDrawDiscardedEvent(long FrameId, int CardId, ActualDiscardRecoveryPhaseKey Phase, long MovementSequence) : IGameEvent;
public sealed record LishiHandTypeUsedEvent(long ActionId, int ActorSeat, string Category, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record LishiDiscardRedirectedEvent(long MovementSequence, long IssueFrameId, int SourceSeat, int TargetSeat, int CardId, CardLocation OriginalFrom, CardLocation ActualFrom, CardMoveReason Reason, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
internal interface IShenZhongHuiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class ShenZhongHuiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenZhongHuiHandler(op);
    public override ProgramOperationInteraction Interaction => op is SkillProgramEffectOp.ResolveLinjieRound or SkillProgramEffectOp.ChooseLishiPunishments or SkillProgramEffectOp.ResolveLishiPhase ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows(op switch
    {
        SkillProgramEffectOp.ResolveLinjieRound => [SkillProgramTriggerWindow.RoundStarting],
        SkillProgramEffectOp.ResolveLinjieDamage => [SkillProgramTriggerWindow.AfterDamageApplied],
        SkillProgramEffectOp.ResolveDuzhang => [SkillProgramTriggerWindow.CardUseTargetsFinalized],
        SkillProgramEffectOp.AwakenJianghuo => [SkillProgramTriggerWindow.TurnBeforeStart],
        SkillProgramEffectOp.ChooseLishiPunishments => [SkillProgramTriggerWindow.TurnEnding],
        _ => [SkillProgramTriggerWindow.JudgmentPhaseStarting, SkillProgramTriggerWindow.DrawPhaseEnded]
    })];
}
internal sealed class ResolveLinjieRoundDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.ResolveLinjieRound);
internal sealed class ResolveLinjieDamageDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.ResolveLinjieDamage);
internal sealed class ResolveDuzhangDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.ResolveDuzhang);
internal sealed class AwakenJianghuoDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.AwakenJianghuo);
internal sealed class ChooseLishiPunishmentsDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.ChooseLishiPunishments);
internal sealed class ResolveLishiPhaseDescriptor() : ShenZhongHuiDescriptor(SkillProgramEffectOp.ResolveLishiPhase);
internal sealed class ShenZhongHuiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShenZhongHuiHost>().Resolve(f, op); }
internal static class ShenZhongHuiComposition
{
    internal const string Linjie = "anniversary:linjie", Duzhang = "anniversary:duzhang", Jianghuo = "anniversary:jianghuo", Lishi = "anniversary:lishi", Curse = "anniversary:lishi-curse";
    internal const string HappyLinjie = "happy:linjie", HappyDuzhang = "happy:duzhang", HappyJianghuo = "happy:jianghuo", HappyLishi = "happy:lishi", HappyCurse = "happy:lishi-curse";
    internal static bool IsHappy(string id) => id is HappyLinjie or HappyDuzhang or HappyJianghuo or HappyLishi or HappyCurse;
    private static string Canonical(string id) => id switch
    { HappyLinjie => Linjie, HappyDuzhang => Duzhang, HappyJianghuo => Jianghuo, HappyLishi => Lishi, HappyCurse => Curse, _ => id };
    internal static string SameEdition(string source, string anniversary) => !IsHappy(source) ? anniversary : anniversary switch
    { Linjie => HappyLinjie, Duzhang => HappyDuzhang, Jianghuo => HappyJianghuo, Lishi => HappyLishi, Curse => HappyCurse, _ => throw new InvalidOperationException("Unknown Zhong Hui derived skill.") };
    [ProgramCatalogValidation(39300)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is >= SkillProgramEffectOp.ResolveLinjieRound and <= SkillProgramEffectOp.ResolveLishiPhase)) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Any(t => t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])) throw new InvalidOperationException(path + ": Shen Zhong Hui requires his exact original bindings.");
        bool valid = Canonical(p.Id) switch
        {
            Linjie => p.Modifiers.Count == 0 && p.Triggers is [{ Id: "round-lin", Window: SkillProgramTriggerWindow.RoundStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: true, Effects: [{ Op: SkillProgramEffectOp.ResolveLinjieRound }] }, { Id: "marked-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Any, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ResolveLinjieDamage }] }],
            Duzhang => p.Modifiers is [{ Query: SkillRuleQuery.HandLimit, Operation: SkillRuleOperation.Add, ValueExpression: SkillRuleValueExpression.OwnerMarkerCount, ValueMarker: PlayerMarkerKind.LinCold }] && p.Triggers is [{ Id: "sole-black-target", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, OwnerRelation: SkillProgramCardActionOwnerRelation.Observer, Optional: false, UsageScope: SkillUsageScope.Turn, Effects: [{ Op: SkillProgramEffectOp.ResolveDuzhang }] }] && p.Triggers[0].UsageLimit == (IsHappy(p.Id) ? 2 : 1),
            Jianghuo => p.Modifiers.Count == 0 && p.Triggers is [{ Id: "all-living-damaged", Window: SkillProgramTriggerWindow.TurnBeforeStart, Subject: SkillProgramTriggerSubject.Owner, Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1, Effects: [{ Op: SkillProgramEffectOp.AwakenJianghuo }] }],
            Lishi => p.Modifiers.Count == 0 && p.Triggers is [{ Id: "ending-punishments", Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ChooseLishiPunishments }] }],
            Curse => p.Modifiers.Count == 0 && p.Triggers is [{ Id: "judge-debt", Window: SkillProgramTriggerWindow.JudgmentPhaseStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ResolveLishiPhase }] }, { Id: "draw-debt", Window: SkillProgramTriggerWindow.DrawPhaseEnded, Subject: SkillProgramTriggerSubject.Owner, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ResolveLishiPhase }] }],
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": Shen Zhong Hui's full official binding shape changed.");
    }
}
