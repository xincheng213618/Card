using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

public enum SkillProgramStepOutcome
{
    Continue,
    AwaitChild,
    AwaitChoice
}

public sealed record SkillProgramActorState(PlayerSkillContext Context, bool IsAlive);

/// <summary>
/// Frozen program and cursor access required by the generic executor. The host
/// retains ownership of resolution-stack mutation and parent continuation.
/// </summary>
public interface ISkillProgramExecutionHost
{
    ProgramSkillFrame? GetActiveFrame(long frameId);
    bool TryStartPostInstructionWindow(long frameId) => false;
    SkillProgram GetProgram(string skillId);
    SkillProgramActorState GetActor(int seat);
    bool IsGameOver { get; }
    bool OwnsSkillInstance(int ownerSeat, string skillId, string skillInstanceId);
    bool OwnsCards(int ownerSeat, IReadOnlyList<int> cardIds, IReadOnlyList<CardZoneKind> sourceZones);
    bool EvaluateCondition(ProgramSkillFrame frame, SkillProgramCondition condition, PlayerSkillContext context);
    void UpdateFrame(ProgramSkillFrame frame);
    void Complete(ProgramSkillFrame frame, bool completed, string? reason = null);
}

/// <summary>Primitive rules operations exposed to reusable effect handlers.</summary>
public interface ISkillProgramEffectHost
{
    int ResolveParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference);
    void Draw(long frameId, int ownerSeat, int targetSeat, int amount,
        SkillProgramNumberExpression? numberExpression, string? resultBind,
        SkillProgramCardSetVisibility visibility, CardMoveReason reason);
    void DrawSelectedTargets(long frameId, int amount, CardMoveReason reason);
    void DrawBoundCardCount(long frameId, int ownerSeat, int targetSeat, string sourceBind,
        string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason);
    void Recover(long frameId, int ownerSeat, int targetSeat, int amount,
        SkillProgramNumberExpression? numberExpression, string? sourceBind);
    void RecoverSelectedTargets(long frameId, int ownerSeat, int amount);
    SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount);
    SkillProgramStepOutcome Damage(ProgramSkillFrame frame, int targetSeat, int amount,
        ProgramParticipantReference? sourceReference = null,
        DamageNature? nature = null);
    void ReplaceJudgment(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ClaimJudgmentCard(ProgramSkillFrame frame);
    SkillProgramStepOutcome ReorderTopCards(ProgramSkillFrame frame, int maximumCards,
        SkillProgramNumberExpression? numberExpression);
    SkillProgramStepOutcome RepeatJudgment(ProgramSkillFrame frame, string reason,
        string resultBind, IReadOnlyList<Suit> successSuits);
    SkillProgramStepOutcome SkipTurnPhases(ProgramSkillFrame frame,
        IReadOnlyList<SkillProgramTurnPhase> phases);
    SkillProgramStepOutcome UseVirtualCard(ProgramSkillFrame frame, int targetSeat,
        CardKind cardKind, bool ignoreDistance);
    SkillProgramStepOutcome UseBoundCardByTarget(ProgramSkillFrame frame, int targetSeat,
        string sourceBind);
    SkillProgramStepOutcome RequestSlashByTarget(ProgramSkillFrame frame, int targetSeat,
        string resultBind);
    SkillProgramStepOutcome RequestNearestSlash(ProgramSkillFrame frame);
    void PendExtraTurn(ProgramSkillFrame frame, int? targetSeat = null);
    void ClaimDeathCleanupCards(ProgramSkillFrame frame);
    SkillProgramStepOutcome BindDiscardPhaseDiscards(ProgramSkillFrame frame, string resultBind);
    SkillProgramStepOutcome Pindian(ProgramSkillFrame frame, int targetSeat);
    SkillProgramStepOutcome MoveSelected(
        ProgramSkillFrame frame,
        int targetSeat,
        IReadOnlyList<int> cardIds,
        bool toDiscard,
        CardMoveReason reason);
    SkillProgramStepOutcome InsertPhase(
        ProgramSkillFrame frame,
        TurnPhase phase,
        SkillProgramPhaseContinuation continuation);
    void RecoverTo(
        long frameId,
        int ownerSeat,
        int targetSeat,
        SkillProgramNumberExpression expression,
        int minimumValue,
        bool clampToMaxHp);
    void DiscardOwnedZoneCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason);
    void SetChainedState(ProgramSkillFrame frame, bool chained, int? targetSeat = null);
    SkillProgramStepOutcome ChooseOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options);
    SkillProgramStepOutcome SelectOwnedCards(ProgramSkillFrame frame, int cardOwnerSeat,
        int amount, SkillProgramNumberExpression? expression, IReadOnlyList<CardZoneKind> zones, string resultBind,
        int minimumCards, int maximumCards, IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> suits);
    SkillProgramStepOutcome HoldTargetCards(ProgramSkillFrame frame, int chooserSeat, int holderSeat,
        IReadOnlyList<CardZoneKind> zones, string resultBind, int minimumCards);
    SkillProgramStepOutcome RevealTargetHandCard(ProgramSkillFrame frame,
        ProgramParticipantReference chooser, ProgramParticipantReference cardOwner,
        string resultBind, SkillProgramRevealMode mode,
        IReadOnlyList<Suit> eligibleSuits, bool allowDecline);
    void CaptureSelectedCards(ProgramSkillFrame frame, string resultBind);
    void RevealBoundCards(ProgramSkillFrame frame, string sourceBind);
    void UseBoundCardAsDyingAlcohol(ProgramSkillFrame frame, string sourceBind, CardMoveReason reason);
    void RevealUniqueRankForDying(ProgramSkillFrame frame, CardZoneKind zone, int rescueHp);
    void RedirectCurrentDamage(ProgramSkillFrame frame, string sourceBind, bool drawLostHpAfterDamage);
    SkillProgramStepOutcome ChooseDifferentCategoryDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        string resultBind,
        CardMoveReason reason);
    void ChangeMaximumHp(ProgramSkillFrame frame, int amount);
    void GrantSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds);
    void GrantTurnSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds);
    SkillProgramStepOutcome UseSelectedCardsAs(
        ProgramSkillFrame frame,
        int targetSeat,
        string viewAsId,
        CardKind outputKind);
    SkillProgramStepOutcome UseAllHandCardsAsOrdinaryTrick(
        ProgramSkillFrame frame,
        string viewAsId);
    SkillProgramStepOutcome StartVirtualDuel(ProgramSkillFrame frame);
    SkillProgramStepOutcome RequestFactionCard(ProgramSkillFrame frame, int targetSeat,
        string providerFactionId, CardKind requiredKind);
    SkillProgramStepOutcome TransferRandomOwnedCard(ProgramSkillFrame frame, int targetSeat, string resultBind);
    SkillProgramStepOutcome ExchangeSelectedTargetHands(ProgramSkillFrame frame);
    void AccumulateSelectedCardCount(ProgramSkillFrame frame, string usageId,
        int threshold, string resultBind);
    void TurnOver(long frameId, int ownerSeat, int targetSeat);
    void SetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown);
    SkillProgramStepOutcome StartJudgment(
        ProgramSkillFrame frame,
        int targetSeat,
        string reason,
        string resultBind,
        SkillProgramCardSetVisibility visibility,
        int sourceSeat);
    SkillProgramStepOutcome ChooseOwnCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference? chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason);
    void RevealTopCards(
        long frameId,
        int ownerSeat,
        int amount,
        SkillProgramNumberExpression? numberExpression,
        string resultBind,
        SkillProgramCardSetVisibility visibility);
    void FilterBoundCards(
        long frameId,
        string sourceBind,
        string resultBind,
        IReadOnlyList<Suit> suits,
        ProgramParticipantReference? effectiveSuitFor = null,
        IReadOnlyList<SkillProgramCardCategory>? categories = null,
        IReadOnlyList<EquipmentSlot>? equipmentSlots = null,
        IReadOnlyList<CardKind>? cardKinds = null,
        string? matchSuitOfBind = null);
    SkillProgramStepOutcome SelectCardSubset(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string resultBind,
        int minimumCards,
        int maximumCards,
        int maximumRankSum,
        SkillProgramSubsetAiOrder aiOrder,
        bool allowFewerWhenInsufficient,
        bool onePerSuit);
    SkillProgramStepOutcome MoveBoundCards(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string? exceptBind,
        SkillProgramCardDestination destination,
        CardZoneKind? destinationZone,
        CardMoveReason reason);
    SkillProgramStepOutcome SelectTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones);
    SkillProgramStepOutcome SelectTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones,
        PlayerMarkerKind? marker,
        ProgramParticipantReference? actorReference = null,
        bool skipIfNoTarget = false);
    void ChangeAttributedMarker(
        ProgramSkillFrame frame,
        ProgramParticipantReference target,
        PlayerMarkerKind marker,
        int amount);
    SkillProgramStepOutcome CauseDeathUnlessBoundCardKind(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<CardKind> excludedCardKinds);
    SkillProgramStepOutcome SelectTargets(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        int minimumTargets,
        int maximumTargets,
        SkillProgramNumberExpression? numberExpression,
        SkillProgramTargetAiOrder aiOrder);
    SkillProgramStepOutcome SelectSourceCard(
        long frameId,
        int ownerSeat,
        SkillProgramCardSource cardSource,
        IReadOnlyList<CardZoneKind> zones,
        string resultBind,
        IReadOnlyList<EquipmentSlot> equipmentSlots,
        bool skipIfNoCards,
        bool allowSameSource);
    SkillProgramStepOutcome GiveBoundCard(
        long frameId,
        int ownerSeat,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        CardMoveReason reason);
    SkillProgramStepOutcome DistributeOwnedCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        bool allowDeclineBeforeFirst,
        CardMoveReason reason);
    SkillProgramStepOutcome RequestAttackRangeAid(
        ProgramSkillFrame frame,
        CardMoveReason reason);
    void ClaimDamageCards(long frameId, int ownerSeat, CardMoveReason reason);
    void TakeRandomHandCardFromSelectedTargets(
        long frameId,
        int ownerSeat,
        int amountPerTarget,
        CardMoveReason reason);
    void TakeRandomCardFromEveryOtherCharacter(
        long frameId,
        int ownerSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason);
    void DeclareHuaShenXinSheng(long frameId, int ownerSeat);
    SkillProgramStepOutcome ChangeHuaShenAvatar(ProgramSkillFrame frame, int ownerSeat,
        IReadOnlyList<SkillTag> declaredSkillTags);
    void AdjustNormalDraw(ProgramSkillFrame frame, int amount);
    void GrantTurnCardDamageModifier(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        int amount,
        SkillProgramDamageModifierExpiration expiration,
        SkillProgramDamageModifierSourceScope sourceScope);
    void GrantTurnCardActionProhibition(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        IReadOnlyList<CardActionType> actionTypes);
    void GrantTurnHandColorRestriction(
        ProgramSkillFrame frame,
        string sourceBind,
        int targetSeat);
    void GrantTurnHandCardProhibition(ProgramSkillFrame frame, int targetSeat);
    void AbolishOwnerAreas(ProgramSkillFrame frame, IReadOnlyList<CardZoneKind> zones);
    void LoseDeathSourceSkills(ProgramSkillFrame frame);
    void PreventCurrentDamage(ProgramSkillFrame frame);
    void NullifyCurrentCardEffect(ProgramSkillFrame frame);
    void NullifySelectedCardEffects(ProgramSkillFrame frame);
    void ProhibitCurrentResponse(ProgramSkillFrame frame);
    void RedirectCurrentAttack(ProgramSkillFrame frame, int targetSeat);
    void GrantTurnRuleModifier(
        ProgramSkillFrame frame,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int amount,
        IReadOnlyList<CardKind> cardKinds);
    void GrantTurnCardTargetRestriction(
        ProgramSkillFrame frame,
        SkillProgramCardTargetRestriction restriction,
        int targetSeat);
    void GrantTurnCardConversion(
        ProgramSkillFrame frame,
        string sourceBind,
        SkillProgramCardColorRelation colorRelation,
        CardKind outputKind);
    SkillProgramStepOutcome SelectAndMoveOwnedCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        SkillProgramCardDestination destination,
        ProgramParticipantReference? destinationRef,
        string? resultBind,
        CardMoveReason reason,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        bool skipIfNoCards = false,
        bool allowSameOwnerHandReturn = false,
        string? coverageResultBind = null,
        bool awaitMovementTriggers = false, bool revealBeforeMove = false,
        IReadOnlyList<CardKind>? cardKinds = null,
        bool prohibitReplacingEquipment = false);
    SkillProgramStepOutcome ChooseOtherOwnedCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason);
    SkillProgramStepOutcome RestorePhaseHandDiscards(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference phaseOwner);
    void RefundCardUseDebit(ProgramSkillFrame frame);
    SkillProgramStepOutcome StartPindian(
        ProgramSkillFrame frame,
        ProgramParticipantReference opponentReference,
        string resultBind,
        SkillProgramCardSetVisibility visibility);
    void SetBooleanState(ProgramSkillFrame frame, string stateId, bool value);
    void ToggleBooleanState(ProgramSkillFrame frame, string stateId);
    void GrantDirectedTurnCardPolicy(
        ProgramSkillFrame frame,
        ProgramParticipantReference actorReference,
        ProgramParticipantReference targetReference,
        IReadOnlyList<CardKind> cardKinds,
        DirectedTurnCardPolicyEffect effects);
}

/// <summary>One reusable primitive operation, never one character or skill.</summary>
public interface ISkillProgramEffectHandler
{
    SkillProgramEffectOp Op { get; }
    SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host);
}

public sealed class DrawSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        if (effect.Target == SkillProgramEffectTarget.SelectedTargets)
        {
            host.DrawSelectedTargets(frame.Id, effect.Amount, Reason(frame, effect));
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.TargetReference is { } targetReference)
            targetSeat = host.ResolveParticipant(frame, targetReference);
        if (effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount)
        {
            host.DrawBoundCardCount(frame.Id, frame.OwnerSeat, targetSeat,
                effect.SourceBind ?? throw new InvalidOperationException("boundCardCount draw has no source binding."),
                effect.ResultBind, effect.Visibility, Reason(frame, effect));
        }
        else
        {
            host.Draw(frame.Id, frame.OwnerSeat, targetSeat, effect.Amount,
                effect.NumberExpression, effect.ResultBind, effect.Visibility, Reason(frame, effect));
        }
        return SkillProgramStepOutcome.Continue;
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class SelectTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTarget;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectTarget(
            frame.Id,
            frame.OwnerSeat,
            effect.TargetKind ?? throw new InvalidOperationException("selectTarget has no target kind."),
            effect.Zones,
            effect.Marker,
            effect.ActorReference,
            effect.SkipIfNoTarget);
}

public sealed class SelectTargetsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTargets;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectTargets(
            frame.Id,
            frame.OwnerSeat,
            effect.TargetKind ?? throw new InvalidOperationException("selectTargets has no target kind."),
            effect.MinimumTargets,
            effect.MaximumTargets,
            effect.NumberExpression,
            effect.TargetAiOrder ?? throw new InvalidOperationException("selectTargets has no AI order."));
}

public sealed class TakeRandomHandCardFromSelectedTargetsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.TakeRandomHandCardFromSelectedTargets(
            frame.Id,
            frame.OwnerSeat,
            effect.Amount,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class TakeRandomCardFromEveryOtherCharacterSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomCardFromEveryOtherCharacter;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.TakeRandomCardFromEveryOtherCharacter(
            frame.Id,
            frame.OwnerSeat,
            effect.Zones,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SelectSourceCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectSourceCard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectSourceCard(
            frame.Id,
            frame.OwnerSeat,
            effect.CardSource,
            effect.Zones,
            effect.ResultBind ?? throw new InvalidOperationException("selectSourceCard has no result bind."),
            effect.EquipmentSlots, effect.SkipIfNoCards, effect.AllowSameSource);
}

public sealed class GiveBoundCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundCard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.GiveBoundCard(
            frame.Id,
            frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("giveBoundCard has no source bind."),
            effect.TargetKind ?? throw new InvalidOperationException("giveBoundCard has no target kind."),
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class ClaimDamageCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDamageCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ClaimDamageCards(
            frame.Id,
            frame.OwnerSeat,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class RecoverSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Recover;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        if (effect.Target == SkillProgramEffectTarget.SelectedTargets)
            host.RecoverSelectedTargets(frame.Id, frame.OwnerSeat, effect.Amount);
        else
        {
            if (effect.TargetReference is { } targetReference)
                targetSeat = host.ResolveParticipant(frame, targetReference);
            host.Recover(frame.Id, frame.OwnerSeat, targetSeat, effect.Amount,
                effect.NumberExpression, effect.SourceBind);
        }
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class LoseHpSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHp;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.LoseHp(frame.Id, frame.SkillId, targetSeat, effect.Amount);
}

public sealed class DistributeOwnedCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DistributeOwnedCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.DistributeOwnedCards(
            frame,
            effect.Zones,
            effect.SourceBind ?? throw new InvalidOperationException("distributeOwnedCards has no source bind."),
            effect.TargetKind ?? throw new InvalidOperationException("distributeOwnedCards has no target kind."),
            effect.AllowDeclineBeforeFirst,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class RequestAttackRangeAidSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestAttackRangeAid;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.RequestAttackRangeAid(
            frame,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class DamageSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Damage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.Damage(frame,
            effect.TargetReference is { } reference ? host.ResolveParticipant(frame, reference) : targetSeat,
            effect.Amount, effect.ActorReference, effect.DamageNature);
}

public sealed class ReplaceJudgmentSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceJudgment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.ReplaceJudgment(frame, effect);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class PindianSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Pindian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.Pindian(frame, targetSeat);
}

public sealed class ChangeMaximumHpSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChangeMaximumHp;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.ChangeMaximumHp(frame, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantSkillsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GrantSkills(frame, effect.SkillIds);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnSkillsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnSkills;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GrantTurnSkills(frame, effect.SkillIds);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class UseSelectedCardsAsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseSelectedCardsAs;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.UseSelectedCardsAs(
            frame,
            targetSeat,
            effect.SourceBind ?? throw new InvalidOperationException("A selected-card use lost its view-as binding."),
            effect.OutputKind ?? throw new InvalidOperationException("A selected-card use lost its output kind."));
}

public sealed class UseAllHandCardsAsOrdinaryTrickSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.UseAllHandCardsAsOrdinaryTrick(
            frame,
            effect.SourceBind ?? throw new InvalidOperationException(
                "An all-hand ordinary-trick use lost its view-as identity."));
}

public sealed class GiveSelectedSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelected;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        return host.MoveSelected(
            frame,
            targetSeat,
            frame.SelectedCardIds,
            toDiscard: false,
            Reason(frame, effect));
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class DiscardSelectedSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSelected;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        return host.MoveSelected(
            frame,
            targetSeat,
            frame.SelectedCardIds,
            toDiscard: true,
            Reason(frame, effect));
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class InsertPhaseSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InsertPhase;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.InsertPhase(
            frame,
            effect.Phase ?? throw new InvalidOperationException("insertPhase has no phase."),
            effect.PhaseContinuation ??
            throw new InvalidOperationException("insertPhase has no continuation."));
}

public sealed class RecoverToSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverTo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RecoverTo(frame.Id, frame.OwnerSeat, targetSeat,
            effect.NumberExpression ??
            throw new InvalidOperationException("recoverTo has no numeric expression."),
            effect.MinimumValue, effect.ClampToMaxHp);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class DiscardOwnedZoneCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOwnedZoneCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.DiscardOwnedZoneCards(
            frame,
            effect.Zones,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SetChainedStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetChainedState;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.SetChainedState(
            frame,
            effect.Chained ?? throw new InvalidOperationException("setChainedState has no chained value."), targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class TurnOverSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TurnOver;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        var flipSeat = effect.TargetReference is { } reference
            ? host.ResolveParticipant(frame, reference)
            : targetSeat;
        host.TurnOver(frame.Id, frame.OwnerSeat, flipSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SetFaceStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetFaceState;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.SetFaceState(
            frame.Id,
            frame.OwnerSeat,
            targetSeat,
            effect.FaceDown ?? throw new InvalidOperationException("setFaceState has no faceDown value."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class StartJudgmentSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.StartJudgment;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.StartJudgment(
            frame,
            targetSeat,
            effect.JudgmentReason ??
            throw new InvalidOperationException("startJudgment has no stable reason."),
            effect.ResultBind ??
            throw new InvalidOperationException("startJudgment has no result bind."),
            effect.Visibility,
            effect.SourceRef is { } sourceRef
                ? host.ResolveParticipant(frame, sourceRef)
                : frame.OwnerSeat);
}

public sealed class RevealTopCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTopCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RevealTopCards(frame.Id, frame.OwnerSeat, effect.Amount, effect.NumberExpression,
            effect.ResultBind ?? throw new InvalidOperationException("revealTopCards has no result bind."),
            effect.Visibility);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class FilterBoundCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FilterBoundCards;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.FilterBoundCards(
            frame.Id,
            effect.SourceBind ?? throw new InvalidOperationException("filterBoundCards has no source bind."),
            effect.ResultBind ?? throw new InvalidOperationException("filterBoundCards has no result bind."),
            effect.Suits,
            effect.TargetReference,
            effect.CardCategories,
            effect.EquipmentSlots,
            effect.CardKinds, effect.MatchSuitOfBind);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SelectCardSubsetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectCardSubset;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.SelectCardSubset(frame.Id, frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("selectCardSubset has no source bind."),
            effect.ResultBind ?? throw new InvalidOperationException("selectCardSubset has no result bind."),
            effect.MinimumCards, effect.MaximumCards, effect.MaximumRankSum,
            effect.AiOrder ?? throw new InvalidOperationException("selectCardSubset has no AI order."),
            effect.AllowFewerWhenInsufficient, effect.OnePerSuit);
}

public sealed class MoveBoundCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.MoveBoundCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        return host.MoveBoundCards(frame.Id, frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("moveBoundCards has no source bind."),
            effect.ExceptBind,
            effect.Destination ?? throw new InvalidOperationException("moveBoundCards has no destination."),
            effect.DestinationZone,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
    }
}

public sealed class AdjustNormalDrawSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustNormalDraw;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        var amount = effect.NumberExpression == SkillProgramNumberExpression.SelectedTargetCount
            ? -frame.SelectedTargetSeats.Count : effect.Amount;
        host.AdjustNormalDraw(frame, amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardDamageModifierSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardDamageModifier;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardDamageModifier(frame, effect.CardKinds, effect.Amount,
            effect.DamageModifierExpiration, effect.DamageModifierSourceScope);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardActionProhibitionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardActionProhibition;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardActionProhibition(frame, effect.CardKinds, effect.ActionTypes);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnHandColorRestrictionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandColorRestriction;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnHandColorRestriction(
            frame,
            effect.SourceBind ?? throw new InvalidOperationException("A hand-color restriction lost its source bind."),
            targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnRuleModifierSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnRuleModifier;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnRuleModifier(
            frame,
            effect.RuleQuery ?? throw new InvalidOperationException("A turn rule modifier lost its query."),
            effect.RuleOperation ?? throw new InvalidOperationException("A turn rule modifier lost its operation."),
            effect.Amount,
            effect.CardKinds);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardTargetRestrictionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardTargetRestriction;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardTargetRestriction(
            frame,
            effect.TargetRestriction ??
            throw new InvalidOperationException("A turn card-target restriction lost its policy."),
            targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardConversionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardConversion;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardConversion(
            frame,
            effect.SourceBind ?? throw new InvalidOperationException("A turn conversion lost its source bind."),
            effect.ColorRelation ?? throw new InvalidOperationException("A turn conversion lost its color relation."),
            effect.OutputKind ?? throw new InvalidOperationException("A turn conversion lost its output kind."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class PreventCurrentDamageSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentDamage;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.PreventCurrentDamage(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SelectAndMoveOwnedCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectAndMoveOwnedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.SelectAndMoveOwnedCard(
        frame,
        effect.ChooserRef ?? throw new InvalidOperationException("selectAndMoveOwnedCard has no chooserRef."),
        effect.CardOwnerRef ?? throw new InvalidOperationException("selectAndMoveOwnedCard has no cardOwnerRef."),
        effect.Zones,
        effect.Destination ?? throw new InvalidOperationException("selectAndMoveOwnedCard has no destination."),
        effect.TargetReference,
        effect.ResultBind,
        new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"), effect.CardCategories,
        effect.SkipIfNoCards, effect.AllowSameOwnerHandReturn,
        effect.CoverageResultBind, effect.AwaitMovementTriggers, effect.RevealBeforeMove,
        effect.CardKinds, effect.ProhibitReplacingEquipment);
}

public sealed class ChooseOtherOwnedCardDiscardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOtherOwnedCardDiscard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) => host.ChooseOtherOwnedCardDiscard(
        frame,
        effect.ChooserRef ?? throw new InvalidOperationException("chooseOtherOwnedCardDiscard has no chooserRef."),
        effect.Zones,
        new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class RestorePhaseHandDiscardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RestorePhaseHandDiscards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) => host.RestorePhaseHandDiscards(
        frame,
        effect.ChooserRef ?? throw new InvalidOperationException("restorePhaseHandDiscards has no chooserRef."),
        effect.SourceRef ?? throw new InvalidOperationException("restorePhaseHandDiscards has no phaseOwnerRef."));
}

public sealed class ChooseOwnCardDiscardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOwnCardDiscard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) => host.ChooseOwnCardDiscard(
        frame,
        effect.ChooserRef,
        effect.Zones,
        new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class ChooseOptionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOption;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.ChooseOption(frame,
        effect.ChooserRef is { } chooser ? host.ResolveParticipant(frame, chooser) : targetSeat,
        effect.ResultBind ?? throw new InvalidOperationException("chooseOption requires a result binding."),
        effect.Options);
}

public sealed class RefundCardUseDebitSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RefundCardUseDebit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RefundCardUseDebit(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

/// <summary>
/// A deterministic catalog of primitive handlers. Reflection is scoped to one
/// explicit assembly and never scans the AppDomain or filesystem.
/// </summary>
public sealed class SkillProgramEffectCatalog
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<Type>> DiscoveredTypes = new();
    private static readonly Lazy<SkillProgramEffectCatalog> BuiltIn = new(
        () => Discover(typeof(SkillProgramEffectCatalog).Assembly),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IReadOnlyDictionary<SkillProgramEffectOp, ISkillProgramEffectHandler> _byOp;

    public SkillProgramEffectCatalog(IEnumerable<ISkillProgramEffectHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var ordered = handlers
            .Select(handler => handler ?? throw new InvalidOperationException("A skill-program effect handler is null."))
            .OrderBy(handler => handler.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        var byOp = new Dictionary<SkillProgramEffectOp, ISkillProgramEffectHandler>();
        foreach (var handler in ordered)
        {
            SkillProgramEffectOp op;
            try { op = handler.Op; }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Skill-program effect handler '{handler.GetType().FullName}' failed to declare its operation.",
                    exception);
            }
            if (!Enum.IsDefined(op))
            {
                throw new InvalidOperationException(
                    $"Skill-program effect handler '{handler.GetType().FullName}' declares invalid operation '{op}'.");
            }
            if (byOp.TryGetValue(op, out var duplicate))
            {
                throw new InvalidOperationException(
                    $"Skill-program effect operation '{op}' is handled by both " +
                    $"'{duplicate.GetType().FullName}' and '{handler.GetType().FullName}'.");
            }
            byOp.Add(op, handler);
        }
        _byOp = new ReadOnlyDictionary<SkillProgramEffectOp, ISkillProgramEffectHandler>(byOp);
        Handlers = Array.AsReadOnly(ordered);
    }

    public static SkillProgramEffectCatalog Default => BuiltIn.Value;

    public IReadOnlyList<ISkillProgramEffectHandler> Handlers { get; }

    public static SkillProgramEffectCatalog Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var types = DiscoveredTypes.GetOrAdd(assembly, static source => source.GetTypes()
            .Where(type => type.IsVisible && type.IsClass && !type.IsAbstract &&
                           !type.ContainsGenericParameters &&
                           typeof(ISkillProgramEffectHandler).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray());
        var handlers = types.Select(Create).ToArray();
        return new SkillProgramEffectCatalog(handlers);
    }

    public ISkillProgramEffectHandler Resolve(SkillProgramEffectOp op)
    {
        if (!Enum.IsDefined(op) || !_byOp.TryGetValue(op, out var handler))
            throw new InvalidOperationException($"No skill-program effect handler is registered for operation '{op}'.");
        return handler;
    }

    private static ISkillProgramEffectHandler Create(Type type)
    {
        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null || !constructor.IsPublic)
        {
            throw new InvalidOperationException(
                $"Skill-program effect handler type '{type.FullName}' requires a public parameterless constructor.");
        }
        try
        {
            return (ISkillProgramEffectHandler)constructor.Invoke(null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"Failed to construct skill-program effect handler type '{type.FullName}'.",
                exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to construct skill-program effect handler type '{type.FullName}'.",
                exception);
        }
    }
}

/// <summary>
/// Stateless instruction runner. It owns validation and cursor semantics while
/// the narrow hosts own rules state, primitive mutations and parent resumption.
/// </summary>
public sealed class StartVirtualDuelSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.StartVirtualDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.StartVirtualDuel(frame);
}

public sealed class ClaimJudgmentCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimJudgmentCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        return host.ClaimJudgmentCard(frame);
    }
}

public sealed class ReorderTopCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReorderTopCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.ReorderTopCards(frame, effect.Amount, effect.NumberExpression);
}

public sealed class RepeatJudgmentSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatJudgment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.RepeatJudgment(frame, effect.JudgmentReason!, effect.ResultBind!, effect.Suits);
}

public sealed class SkipTurnPhasesSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SkipTurnPhases;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.SkipTurnPhases(frame, effect.SkippedPhases);
}

public sealed class UseVirtualCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseVirtualCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.UseVirtualCard(frame, targetSeat,
        effect.OutputKind!.Value,
        effect.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget);
}

public sealed class UseBoundCardByTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseBoundCardByTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.UseBoundCardByTarget(frame, targetSeat,
        effect.SourceBind!);
}

public sealed class RequestFactionCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestFactionCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.RequestFactionCard(frame, targetSeat,
        effect.ProviderFactionId!, effect.OutputKind!.Value);
}

public sealed class TransferRandomOwnedCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TransferRandomOwnedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
        => host.TransferRandomOwnedCard(frame, targetSeat, effect.ResultBind!);
}

public sealed class ExchangeSelectedTargetHandsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeSelectedTargetHands;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
        => host.ExchangeSelectedTargetHands(frame);
}

public sealed class AccumulateSelectedCardCountSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulateSelectedCardCount;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.AccumulateSelectedCardCount(frame, effect.StateId!, effect.Amount, effect.ResultBind!);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SkillProgramExecutor
{
    private readonly SkillProgramEffectCatalog? _effects;

    public SkillProgramExecutor(SkillProgramEffectCatalog? effects = null) =>
        _effects = effects;

    public void Run(
        long frameId,
        ISkillProgramExecutionHost state,
        ISkillProgramEffectHost effects)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effects);
        while (state.GetActiveFrame(frameId) is { } frame)
        {
            if (state.TryStartPostInstructionWindow(frameId)) return;
            if (frame.Id != frameId)
                throw new InvalidOperationException(
                    $"Active skill-program frame '{frame.Id}' does not match requested frame '{frameId}'.");
            var actor = state.GetActor(frame.OwnerSeat);
            var program = state.GetProgram(frame.SkillId);
            if (!string.Equals(program.GameplayHash, frame.GameplayHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Running skill program '{frame.SkillId}' changed its gameplay hash.");
            if (!state.OwnsSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId))
            {
                state.Complete(frame, completed: false, "技能实例在结算前已失效，剩余步骤取消。");
                return;
            }
            var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
            var instructions = plan.Instructions;
            if (frame.InstructionIndex < 0 || frame.InstructionIndex > instructions.Count)
                throw new InvalidOperationException(
                    $"Running skill program '{frame.SkillId}' has an invalid instruction cursor.");

            var allowsDeadOwner = frame.WindowContext?.Window == SkillProgramTriggerWindow.OwnerDied;
            if ((!actor.IsAlive && !allowsDeadOwner) || state.IsGameOver ||
                frame.InstructionIndex >= instructions.Count)
            {
                state.Complete(
                    frame,
                    completed: (actor.IsAlive || allowsDeadOwner) &&
                        frame.InstructionIndex >= instructions.Count);
                return;
            }

            var effect = plan.GetInstruction(frame.InstructionIndex).Effect;
            var handler = _effects?.Resolve(effect.Op) ?? ProgramOperationCatalog.Default.Resolve(effect.Op).Handler;
            // Commit the cursor before any primitive can suspend into a child.
            frame = frame with { InstructionIndex = frame.InstructionIndex + 1 };
            state.UpdateFrame(frame);
            if (!state.EvaluateCondition(frame, effect.Condition, actor.Context)) continue;
            if (frame.WindowContext?.JudgmentReplacement is { } replacement &&
                effect.Op != SkillProgramEffectOp.ReplaceJudgment &&
                (replacement.ReplacementSuit is not { } replacementSuit ||
                 !effect.ReplacementSuits.Contains(replacementSuit) ||
                 replacement.ReplacementRank is not { } replacementRank ||
                 replacementRank < effect.MinimumReplacementRank ||
                 replacementRank > effect.MaximumReplacementRank)) continue;
            if (effect.Op == SkillProgramEffectOp.Damage && effect.SkipIfNoTarget &&
                effect.Target == SkillProgramEffectTarget.SelectedTarget && frame.SelectedTargetSeats.Count == 0)
                continue;

            var targetSeat = effect.Op is SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets
                ? frame.OwnerSeat
                : effect.Op == SkillProgramEffectOp.Damage &&
                  effect.TargetReference is { } targetReference
                ? effects.ResolveParticipant(frame, targetReference)
                : effect.Target switch
                {
                    SkillProgramEffectTarget.Owner => frame.OwnerSeat,
                    SkillProgramEffectTarget.Actor => frame.WindowContext?.CardUse?.ActorSeat ??
                        throw new InvalidOperationException(
                            $"Skill program '{frame.SkillId}' requires a frozen card-action actor."),
                    SkillProgramEffectTarget.SelectedTarget => frame.SelectedTargetSeats.Single(),
                    SkillProgramEffectTarget.SelectedTargets => frame.OwnerSeat,
                    _ => throw new InvalidOperationException(
                        $"Skill program '{frame.SkillId}' uses unsupported target '{effect.Target}'.")
                };
            var target = state.GetActor(targetSeat);
            var activation = program.Activations.SingleOrDefault(item => item.Id == frame.ActivationId);
            if (effect.Op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected &&
                (activation is null || !target.IsAlive ||
                 !state.OwnsCards(frame.OwnerSeat, frame.SelectedCardIds, activation.SourceZones)))
            {
                state.Complete(
                    frame,
                    completed: false,
                    "所选牌或接收者在结算中已失效，技能剩余步骤取消。");
                return;
            }
            if (!target.IsAlive && !(allowsDeadOwner && effect.Op is
                    (SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets or
                     SkillProgramEffectOp.LoseDeathSourceSkills)))
            {
                if (effect.Op is SkillProgramEffectOp.ChooseOption or SkillProgramEffectOp.SelectOwnedCards)
                {
                    state.Complete(frame, completed: false, reason: "选择者已死亡，技能剩余步骤取消。");
                    return;
                }
                continue;
            }

            var outcome = handler.Execute(effect, frame, targetSeat, effects);
            if (outcome is SkillProgramStepOutcome.AwaitChild or SkillProgramStepOutcome.AwaitChoice) return;
            if (outcome != SkillProgramStepOutcome.Continue)
                throw new InvalidOperationException(
                    $"Skill-program effect '{effect.Op}' returned unsupported outcome '{outcome}'.");
        }
    }
}
