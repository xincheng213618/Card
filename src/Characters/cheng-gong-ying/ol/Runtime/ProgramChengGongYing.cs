using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IChengGongYingHost { SkillProgramStepOutcome Adjust(ProgramSkillFrame frame); }
internal sealed class AdjustKuangxiangHandToFourDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustKuangxiangHandToFour;
    public override ISkillProgramEffectHandler Handler { get; } = new ChengGongYingHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ChengGongYingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustKuangxiangHandToFour;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IChengGongYingHost>().Adjust(frame);
}
public enum ChengGongYingStage { TargetChoice, DiscardChoice, DrawChildren, DiscardChildren }
public sealed record ChengGongYingReshuffle(long BatchId, long ParentFrameId, long Before, long After)
{
    private IReadOnlyList<CardMovementRecord> _moves = [];
    public IReadOnlyList<CardMovementRecord> Movements { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ChengGongYingReceipt(CardConversionSource Source, string GameplayHash, long WindowId, int Turn, int TurnOwner,
    ChengGongYingStage Stage, int? Recipient = null, int InitialHandCount = 0, int Count = 0, long Before = 0, long After = 0, int ActualCount = 0)
{
    private IReadOnlyList<int> _candidates = [], _selected = [];
    private IReadOnlyList<ChengGongYingReshuffle> _shuffles = [];
    public IReadOnlyList<int> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ChengGongYingReshuffle> Reshuffles { get => _shuffles; init => _shuffles = Array.AsReadOnly(value.Select(s => s with { Movements = s.Movements }).ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ChengGongYingReceipt? ChengGongYing { get; init; } }
public sealed record ChengGongYingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, int Turn, int TurnOwner) : IGameEvent;
public sealed record KuangxiangTargetChosenEvent(long FrameId, int Recipient, int InitialHandCount, int RequestedCount, bool Draw) : IGameEvent;
public sealed record KuangxiangAdjustmentIssuedEvent(long FrameId, int Recipient, bool Draw, long Before, long After, int ActualCount) : IGameEvent;
public sealed record KuangxiangCardObtainedEvent(long FrameId, CardConversionSource Source, int Recipient, int CardId, long MovementSequence, Suit EffectiveSuit) : IGameEvent;
public sealed record KuangxiangRedDrawIssuedEvent(long FrameId, int Recipient, int Count, long LastActualPhaseToken) : IGameEvent;
public sealed record KuangxiangRedDrawConsumedEvent(long GrantFrameId, int Recipient, ActualDiscardRecoveryPhaseKey Phase, int Count) : IGameEvent;
public sealed record KuangxiangDrawConfirmedEvent(ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record KuangxiangReshuffleCapturedEvent(long FrameId, long BatchId, long Before, long After, int Count) : IGameEvent;
public sealed record KuangxiangBlackUseBoundEvent(long UseId, long? ActionId, long GrantFrameId, int ActorSeat, int ProviderSeat, int CardId, long ObtainedSequence, CardKind EffectiveKind) : IGameEvent;
public sealed record KuangxiangLightningBoundEvent(long JudgmentId, long UseId, long GrantFrameId, int CardId) : IGameEvent;
public sealed record ChengGongYingCompletedEvent(long FrameId, bool Applied) : IGameEvent;
public sealed record KuangxiangBlackUseReceipt(long UseId, long? ActionId, long GrantFrameId, int ActorSeat, int ProviderSeat, int CardId, long ObtainedSequence, CardKind EffectiveKind);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KuangxiangBlackUseReceipt? KuangxiangBlack { get; init; } }
public sealed partial record JudgmentFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KuangxiangBlackUseReceipt? KuangxiangBlack { get; init; } }

internal static class ChengGongYingComposition
{
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (!program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustKuangxiangHandToFour)) && !program.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustKuangxiangHandToFour))) return;
        if (program.Id != "ol:kuangxiang" || program.Triggers is not [var t] || program.Activations.Count != 0 || program.Modifiers.Count != 0 || program.ViewAs.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0 || program.DamageModifiers.Count != 0 || program.CardPolicies.Count != 0 || program.HpSkillGrants.Count != 0 || program.EquipmentSuitSkillGrants.Count != 0 || program.LordSkillProjection || program.CannotChallengeDeclarations ||
            t.Effects is not [{ Op: SkillProgramEffectOp.AdjustKuangxiangHandToFour, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] || t.Id != "preparation-adjust-hand" ||
            t.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || t.Subject != SkillProgramTriggerSubject.Owner || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || !t.Optional || t.Priority != 0 ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.OwnerRelation is not null || t.DamageOccurrence is not null || t.MovementOccurrence is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.GainPhaseQualification is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            t.IncludeResponseUses || t.SingleActionInstance || t.OnlyDesignatedCardTargets || t.AllowNoEventTarget || t.DeferredTurnEndOnly || t.NoDyingAtActivation || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly)
            throw new InvalidOperationException(path + ": Kuangxiang requires its complete optional actual-Preparation hand-adjustment trigger.");
    }
}
