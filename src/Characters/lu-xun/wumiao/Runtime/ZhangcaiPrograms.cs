using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WuLuXunZhangcaiStage { Drawing, DrawChildren }
public sealed record WuLuXunZhangcaiQualification(long ActionId, SkillProgramTriggerWindow Window,
    CardConversionSource Source, string GameplayHash, int ActorSeat, int ProviderSeat,
    CardKind EffectiveKind, int? EffectiveRank, int RemainingEights, int DrawCount,
    long? RuxianFrameId, int ActualTurn, int TurnOwner, long MovementSequence);
public sealed record WuLuXunZhangcaiReceipt(WuLuXunZhangcaiQualification Qualification,
    long WindowId, long NativeParentId, WuLuXunZhangcaiStage Stage)
{
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuLuXunZhangcaiReceipt? WuLuXunZhangcai { get; init; }
}
public sealed record WuLuXunZhangcaiQualifiedEvent(WuLuXunZhangcaiQualification Qualification) : IGameEvent;
public sealed record WuLuXunZhangcaiStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long WindowId, long NativeParentId, long ActionId,
    SkillProgramTriggerWindow Window, CardKind EffectiveKind, int? EffectiveRank,
    int RemainingEights, int DrawCount, long? RuxianFrameId, int ActualTurn, int TurnOwner,
    long MovementSequence) : IGameEvent;
public sealed record WuLuXunZhangcaiDrawReturnedEvent(long FrameId, int RequestedCount,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record WuLuXunZhangcaiCompletedEvent(long FrameId, long ActionId) : IGameEvent;

internal interface IWuLuXunZhangcaiHost
{
    SkillProgramStepOutcome Zhangcai(ProgramSkillFrame frame);
    SkillProgramStepOutcome Ruxian(ProgramSkillFrame frame);
}
internal abstract class WuLuXunZhangcaiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuXunZhangcaiHandler(op);
}
internal sealed class ResolveWuLuXunZhangcaiDescriptor() : WuLuXunZhangcaiDescriptor(SkillProgramEffectOp.ResolveWuLuXunZhangcai){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuLuXunZhangcaiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveWuLuXunZhangcai
            ? host.GetCapability<IWuLuXunZhangcaiHost>().Zhangcai(frame)
            : host.GetCapability<IWuLuXunZhangcaiHost>().Ruxian(frame);
}
internal static class WuLuXunZhangcaiComposition
{
    [ProgramCatalogValidation(1140)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("wumiao:zhangcai" or "wumiao:ruxian") && !effects.Any(e => e.Op is
            SkillProgramEffectOp.ResolveWuLuXunZhangcai or SkillProgramEffectOp.ResolveWuLuXunRuxian)) return;
        if (p.Id is not ("wumiao:zhangcai" or "wumiao:ruxian") || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Wu Lu Xun requires his complete native accepted-action and limited actual-Play contracts.");
        if (p.Id == "wumiao:ruxian")
        {
            if (p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != "all-ranks-until-next-turn" ||
                a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
                a.TargetKind != SkillProgramTargetKind.AnyLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
                a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame != 1 ||
                a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath ||
                a.Effects is not [{ Op: SkillProgramEffectOp.ResolveWuLuXunRuxian, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Ruxian is one actual Play activation with a permanent owner limit and next actual own TurnStarted expiry.");
            return;
        }
        if (p.Activations.Count != 0 || p.Triggers.Count != 3 || p.Triggers.Select(t => t.Id).Distinct().Count() != 3)
            throw new InvalidOperationException(path + ": Zhangcai requires real Use commitment, response acceptance and native provider-supply acceptance.");
        foreach (var t in p.Triggers)
            if (t.Id != (t.Window == SkillProgramTriggerWindow.CardUseCommitted ? "accepted-use" :
                    t.Window == SkillProgramTriggerWindow.CardResponseAccepted ? "accepted-response" : "accepted-supply") ||
                t.Window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.CardSupplyCompleted) ||
                t.OwnerRelation != (t.Window == SkillProgramTriggerWindow.CardResponseAccepted ? SkillProgramCardActionOwnerRelation.Observer : SkillProgramCardActionOwnerRelation.Actor) || !t.Optional || t.Subject is not null ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
                t.SourceSkillId is not null || t.SourceViewAsId is not null || t.IncludeResponseUses || t.SingleActionInstance ||
                t.AllowNoEventTarget || t.OnlyDesignatedCardTargets || t.UsageLimit is not null || t.UsageScope is not null ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
                t.MarkerCost is not null || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementOccurrence is not null ||
                t.Suits.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 ||
                t.DamageOccurrence is not null || t.GainPhaseQualification is not null || t.EvaluateConditionAtResolution ||
                t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.NoDyingAtActivation || t.DeferredTurnEndOnly ||
                t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.AllowOwnDiscardPhaseEnded ||
                t.Effects is not [{ Op: SkillProgramEffectOp.ResolveWuLuXunZhangcai, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Zhangcai must observe every exact owner accepted action once and freeze the remaining rank-eight Hand count after payment.");
    }
}
