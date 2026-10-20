using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record BeifenLossSource(CardConversionSource Source, string GameplayHash);
public enum BeifenStage { ObtainChildren }
public sealed record BeifenSearchedEntity(Suit Suit, int? CardId);

public sealed record BeifenReceipt(CardConversionSource Source, string GameplayHash,
    long LossBatchId, int InstructionIndex, int MarkedHandCount, long SequenceBefore)
{
    public BeifenStage Stage { get; init; }
    public long ObtainBatchId { get; init; }
    public long SequenceAfter { get; init; }
    private IReadOnlyList<BeifenSearchedEntity> _search = Array.AsReadOnly(Array.Empty<BeifenSearchedEntity>());
    public IReadOnlyList<BeifenSearchedEntity> Search
    { get => _search; init => _search = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeifenReceipt? Beifen { get; init; }
}

// This qualification lives on the original declaration payment until that exact
// successfully accepted native use claims it. It is not an additional payment.
public sealed record BeifenUseQualification(CardConversionSource Source, string GameplayHash,
    int ActorSeat, int MarkedHandCount, int OtherHandCount, long SequenceBefore);
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeifenUseQualification? Beifen { get; init; }
}
public sealed record BeifenUseReceipt(long UseId, long ActionId, CardKind OriginalKind,
    BeifenUseQualification Qualification, long? DeclarationId);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeifenUseReceipt? BeifenUse { get; init; }
}

public sealed record BeifenLossQualifiedEvent(long BatchId, CardConversionSource Source, string GameplayHash,
    int CardId, CardLocation From, CardLocation To, long MovementSequence, int ActualTurn) : IGameEvent;
public sealed record BeifenStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long LossBatchId, int MarkedHandCount, int MissingSuitCount, long SequenceBefore) : IGameEvent;
public sealed record BeifenSuitSearchedEvent(long FrameId, Suit Suit, int? CardId) : IGameEvent;
public sealed record BeifenObtainedEvent(long FrameId, int CardId, CardKind PrintedKind,
    Suit Suit, long MovementSequence, long BatchId) : IGameEvent;
public sealed record BeifenCompletedEvent(long FrameId, long LossBatchId, int ObtainedCount) : IGameEvent;
public sealed record BeifenUseAcceptedEvent(long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActorSeat, int MarkedHandCount, int OtherHandCount,
    CardKind OriginalKind, long SequenceBefore, long? DeclarationId) : IGameEvent;
public sealed record BeifenDeclarationQualifiedEvent(long DeclarationId, long OwnerFrameId,
    int ProviderSeat, int CardId, CardLocation From, CardKind DeclaredKind,
    BeifenUseQualification Qualification) : IGameEvent;

internal interface ILeCaiWenJiBeifenHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveBeifenLostMarkedSuitGainsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new LeCaiWenJiBeifenHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class LeCaiWenJiBeifenHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<ILeCaiWenJiBeifenHost>().Resolve(frame);
}

internal static class BeifenComposition
{
    [ProgramCatalogValidation(1210)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:beifen" && !p.Triggers.SelectMany(t => t.Effects)
            .Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains)) return;
        if (p.Id != "le:beifen" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [var t] || t.Id != "lost-hujia-suits" || t.Window != SkillProgramTriggerWindow.CardsMoved ||
            t.Subject != SkillProgramTriggerSubject.Owner || t.Optional ||
            !t.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) ||
            t.MovementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null ||
            t.ChoiceGroup is not null || t.OrderGroup is not null || t.EvaluateConditionAtResolution ||
            t.OwnerRelation is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.DamageCardKinds.Count != 0 || t.Suits.Count != 0 ||
            t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.ExcludedReasons.Count != 0 || t.MovementDiscardOnly || t.IncludeResponseUses || t.SingleActionInstance ||
            t.DeferredTurnEndOnly || t.IgnoreOwnSkillMovements || t.GainPhaseQualification is not null ||
            t.DamageOccurrence is not null || t.NoDyingAtActivation || t.RequireDamageSource is not null ||
            t.RequireNoCardConversion is not null || t.AllowNoEventTarget || t.OnlyDesignatedCardTargets ||
            t.AllowOwnDiscardPhaseEnded || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Beifen requires one mandatory original HE marked-loss batch and its complete suit obtains; its live use policy has no independent quota.");
    }
}
