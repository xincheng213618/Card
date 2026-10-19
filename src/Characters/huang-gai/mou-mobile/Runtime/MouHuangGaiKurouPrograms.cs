using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouHuangGaiKurouStage { Choosing, GiftChildren, LossRequested, LossChildren, Complete }

public sealed record MouHuangGaiKurouGift(int CardId, CardKind PrintedKind, CardKind EffectiveKind,
    CardLocation From, CardLocation To, int RecipientSeat, long MovementSequence, int RequestedHpLoss);
public sealed record MouHuangGaiKurouLoss(long HpChangeId, int HpBefore, int HpAfter,
    int RequestedHpLoss, int ActualLost, bool IdentityPrevented = false);
public sealed record MouHuangGaiKurouReceipt(CardConversionSource Source, string GameplayHash,
    long OriginalParentId, int ActualTurnNumber, int ActualTurnOwnerSeat, MouHuangGaiKurouStage Stage,
    MouHuangGaiKurouGift? Gift = null, long GiftSequenceAfter = 0, MouHuangGaiKurouLoss? Loss = null);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuangGaiKurouReceipt? MouHuangGaiKurou { get; init; }
}

public sealed record MouHuangGaiKurouStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long OriginalParentId, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record MouHuangGaiKurouGiftPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, MouHuangGaiKurouGift Gift) : IGameEvent;
public sealed record MouHuangGaiKurouLossRequestedEvent(long FrameId, CardConversionSource Source,
    int RequestedHpLoss) : IGameEvent;
public sealed record MouHuangGaiKurouHpLossPaidEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, MouHuangGaiKurouLoss Loss) : IGameEvent;
public sealed record MouHuangGaiKurouCompletedEvent(long FrameId, CardConversionSource Source,
    long? GiftMovementSequence, int ActualLost) : IGameEvent;
public sealed record MouHuangGaiKurouArmorGrantedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long HpChangeId, int OccurrenceIndex, int Requested, int Actual,
    int ArmorBefore, int ArmorAfter, int? Maximum) : IGameEvent;

internal interface IMouHuangGaiKurouHost
{
    SkillProgramStepOutcome GiveBeforeLoss(ProgramSkillFrame frame);
    void GrantLossArmor(ProgramSkillFrame frame);
}

internal abstract class MouHuangGaiKurouDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveMouHuangGaiKurouGift
            ? SkillProgramTriggerWindow.PlayPhaseStarting : SkillProgramTriggerWindow.AfterHpLost)];
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMouHuangGaiKurouGift
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(op == SkillProgramEffectOp.ResolveMouHuangGaiKurouGift
                ? ProgramOperationAiSemantic.LoseHp : ProgramOperationAiSemantic.GrantArmor,
            (effect, context) => context.MouKurouHealthPrior(effect, op));
    public override ISkillProgramEffectHandler Handler { get; } = new MouHuangGaiKurouHandler(op);
}
internal sealed class ResolveMouHuangGaiKurouGiftDescriptor() :
    MouHuangGaiKurouDescriptor(SkillProgramEffectOp.ResolveMouHuangGaiKurouGift);
internal sealed class GrantMouHuangGaiLossArmorDescriptor() :
    MouHuangGaiKurouDescriptor(SkillProgramEffectOp.GrantMouHuangGaiLossArmor);
internal sealed class MouHuangGaiKurouHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<IMouHuangGaiKurouHost>();
        if (op == SkillProgramEffectOp.ResolveMouHuangGaiKurouGift) return capability.GiveBeforeLoss(frame);
        capability.GrantLossArmor(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void MouKurouHealthPrior(SkillProgramEffect effect, SkillProgramEffectOp op)
    {
        // The native optional decision prices the exact owned-card candidates.
        // Other composition consumers still see the unavoidable minimum loss
        // and the same unbounded Armor2 benefit as the native grant.
        if (op == SkillProgramEffectOp.ResolveMouHuangGaiKurouGift)
            LoseHp(new(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.Owner, 1, effect.Condition));
        if (_estimatedHp > 0)
            GrantArmor(new(SkillProgramEffectOp.GrantArmor, SkillProgramEffectTarget.Owner,
                2, effect.Condition));
    }
}

internal static class MouHuangGaiKurouComposition
{
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var uses = program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.ResolveMouHuangGaiKurouGift or SkillProgramEffectOp.GrantMouHuangGaiLossArmor);
        if (program.Id != "mou-mobile:kurou" && !uses) return;
        if (program.Id != "mou-mobile:kurou" || program.Activations.Count != 0 || program.ViewAs.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 ||
            program.HpSkillGrants.Count != 0 || program.EquipmentSuitSkillGrants.Count != 0 ||
            program.DamageModifiers.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0 ||
            program.LordSkillProjection || program.CannotChallengeDeclarations || program.Triggers.Count != 2 ||
            program.Triggers.SingleOrDefault(t => t.Id == "give-before-loss") is not { } gift ||
            program.Triggers.SingleOrDefault(t => t.Id == "loss-armor") is not { } armor ||
            gift.Window != SkillProgramTriggerWindow.PlayPhaseStarting || !gift.Optional ||
            gift.HpChangeOccurrence != SkillProgramHpChangeOccurrence.PerEvent || armor.Window != SkillProgramTriggerWindow.AfterHpLost || armor.Optional ||
            armor.HpChangeOccurrence != SkillProgramHpChangeOccurrence.PerPoint ||
            gift.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouHuangGaiKurouGift,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            armor.Effects is not [{ Op: SkillProgramEffectOp.GrantMouHuangGaiLossArmor,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            !Plain(gift) || !Plain(armor))
            throw new InvalidOperationException(path + ": mobile KuRou requires its optional own Play-start real gift and mandatory per-point explicit HP-loss armor.");
    }

    private static bool Plain(SkillProgramTrigger trigger) =>
        trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
        trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always && trigger.Priority == 0 &&
        trigger.UsageScope is null && trigger.UsageLimit is null && trigger.DynamicUsageLimit is null &&
        trigger.NamedUsageGroup is null && trigger.MarkerCost is null && trigger.ChoiceGroup is null && trigger.OrderGroup is null &&
        trigger.OwnerRelation is null && trigger.SourceSkillId is null && trigger.SourceViewAsId is null &&
        trigger.CardKinds.Count == 0 && trigger.CardCategories.Count == 0 && trigger.DamageCardKinds.Count == 0 &&
        trigger.Suits.Count == 0 && trigger.SourceZones.Count == 0 && trigger.DestinationZones.Count == 0 &&
        trigger.MovementReasons.Count == 0 && trigger.ExcludedMovementReasons.Count == 0 && trigger.ExcludedReasons.Count == 0 &&
        !trigger.IncludeResponseUses && !trigger.SingleActionInstance && !trigger.EvaluateConditionAtResolution &&
        !trigger.DeferredTurnEndOnly && !trigger.IgnoreOwnSkillMovements && trigger.GainPhaseQualification is null &&
        trigger.DamageOccurrence is null && !trigger.NoDyingAtActivation && trigger.RequireDamageSource is null &&
        trigger.RequireNoCardConversion is null && !trigger.AllowNoEventTarget && !trigger.OnlyDesignatedCardTargets &&
        !trigger.AllowOwnDiscardPhaseEnded && trigger.JudgmentReasons.Count == 0 && trigger.JudgmentSource is null &&
        trigger.MinimumRank == 1 && trigger.MaximumRank == 13 && trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
        trigger.MovementOccurrence is null && !trigger.MovementDiscardOnly;
}
