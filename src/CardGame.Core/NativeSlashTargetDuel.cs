using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record NativeSlashTargetDuelTarget(int TargetIndex, int TargetSeat, CardUseEffectSource? Source);
public enum NativeSlashTargetDuelStage { Counterspell, Responding, Canceled }
public sealed record NativeSlashTargetDuelSlice(int Ordinal, int TargetIndex, int TargetSeat,
    CardUseEffectSource Source, string GameplayHash, CardAttackState OriginalAttack,
    NativeSlashTargetDuelStage Stage);

// The original Use owns this immutable target policy and its current native child.
// Original CardKind/Action/materials and the native multi-target cursor never change.
public sealed record NativeSlashTargetDuelPlan(long UseId, long ActionId, int ActorSeat,
    int ProviderSeat, CardKind OriginalKind)
{
    private readonly IReadOnlyList<NativeSlashTargetDuelTarget> _targets = Array.AsReadOnly(Array.Empty<NativeSlashTargetDuelTarget>());
    public IReadOnlyList<NativeSlashTargetDuelTarget> Targets
    { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeSlashTargetDuelSlice? Slice { get; init; }
    public int IssuedCount { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NativeSlashTargetDuelPlanFrozen { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeSlashTargetDuelPlan? NativeSlashTargetDuel { get; init; }
}

public sealed record NativeSlashTargetDuelIssuedEvent(long UseId, long ActionId, int Ordinal, int TargetIndex,
    int ActorSeat, int ProviderSeat, int TargetSeat, CardKind OriginalKind,
    CardUseEffectSource Source, string GameplayHash) : IGameEvent;
public sealed record NativeSlashTargetDuelStartedEvent(long UseId, long ActionId, int TargetIndex,
    int ActorSeat, int TargetSeat) : IGameEvent;
public sealed record NativeSlashTargetDuelCanceledEvent(long UseId, long ActionId,
    int TargetIndex, int TargetSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private void FreezeNativeSlashTargetDuelPlan(CardUseFrame use)
    {
        if (use.NativeSlashTargetDuelPlanFrozen || use.Action is not { Type: CardActionType.Use } action ||
            !IsSlashCard(use.CardKind) || action.EffectiveKind != use.CardKind || action.ActorSeat != use.SourceSeat)
            return;
        var targets = use.TargetSeats.Select((target, index) => new NativeSlashTargetDuelTarget(index,
            target, _programPipelineHooks.SlashTargetDuelSource(use, target))).ToArray();
        var plan = targets.Any(target => target.Source is not null)
            ? new NativeSlashTargetDuelPlan(use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, use.CardKind)
                { Targets = targets }
            : null;
        UpdateLifecycleCardUse(use.Id, current => current with
            { NativeSlashTargetDuelPlanFrozen = true, NativeSlashTargetDuel = plan });
    }

    private bool ValidNativeSlashTargetDuelPlan(CardUseFrame use, NativeSlashTargetDuelPlan plan) =>
        use.NativeSlashTargetDuelPlanFrozen && use.Id == plan.UseId && use.CardKind == plan.OriginalKind &&
        IsSlashCard(plan.OriginalKind) && use.Action is { Type: CardActionType.Use } action &&
        action.ActionId == plan.ActionId && action.ActorSeat == plan.ActorSeat && use.SourceSeat == plan.ActorSeat &&
        action.ProviderSeat == plan.ProviderSeat && action.EffectiveKind == plan.OriginalKind &&
        plan.Targets.Count == use.TargetSeats.Count &&
        plan.Targets.Select(target => target.TargetIndex).SequenceEqual(Enumerable.Range(0, use.TargetSeats.Count)) &&
        plan.Targets.Select(target => target.TargetSeat).SequenceEqual(use.TargetSeats) &&
        action.TargetSeats.SequenceEqual(use.TargetSeats);

    private bool TryBeginNativeSlashTargetDuel(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { NativeSlashTargetDuel: { } plan } use)
            return false;
        if (!ValidNativeSlashTargetDuelPlan(use, plan) || use.TargetIndex < 0 || use.TargetIndex >= plan.Targets.Count)
            throw new InvalidOperationException("A native target effect lost its frozen actual Use or target table.");
        var target = plan.Targets[use.TargetIndex];
        if (target.Source is not { } source) return false;
        if (plan.Slice is { } old && old.TargetIndex == use.TargetIndex && !IsExtraCardEffectRound(use.Id))
            throw new InvalidOperationException("A native Duel target cannot be started twice.");
        if (target.TargetSeat != attack.TargetSeat || attack.CardUserSeat != plan.ActorSeat ||
            attack.SourceSeat != plan.ActorSeat || !IsSlashCard(attack.EffectiveCardKind ?? plan.OriginalKind) ||
            _resolutionStack.LastOrDefault()?.Id != use.Id || !SameAttackOwner(ActiveCardAttack, attack))
            throw new InvalidOperationException("A native Duel slice requires its current original Slash target.");
        var hash = _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash ??
            throw new InvalidOperationException("A native target effect requires its original content binding.");
        CaptureExtraCardEffectAttack(attack);
        plan = LifecycleCardUse(use.Id)!.NativeSlashTargetDuel!;
        var slice = new NativeSlashTargetDuelSlice(checked(plan.IssuedCount + 1), use.TargetIndex, target.TargetSeat, source, hash,
            GetCardAttackState(use.Id), NativeSlashTargetDuelStage.Counterspell);
        UpdateLifecycleCardUse(use.Id, current => current with
            { NativeSlashTargetDuel = plan with { Slice = slice, IssuedCount = slice.Ordinal } });
        // The counterspell owns the original Use; the saved Slash attempt resumes
        // only when this exact target is canceled. No second Use/payment is issued.
        ActiveCardAttack = null;
        ActiveDuel = null;
        AdvanceEventRulesAndQueueFact(new NativeSlashTargetDuelIssuedEvent(use.Id, plan.ActionId,
            slice.Ordinal, slice.TargetIndex, plan.ActorSeat, plan.ProviderSeat, slice.TargetSeat, plan.OriginalKind, source, hash));
        BeginNullificationWindow(use.Id, NativeSlashTargetDuelRepresentation(LifecycleCardUse(use.Id)!), plan.ActorSeat,
            [slice.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return true;
    }

    private bool TryGetNativeSlashTargetDuel(NullificationWindowFrame pending,
        out CardUseFrame use, out NativeSlashTargetDuelPlan plan, out NativeSlashTargetDuelSlice slice)
    {
        use = LifecycleCardUse(pending.ParentFrameId)!;
        plan = use?.NativeSlashTargetDuel!;
        slice = plan?.Slice!;
        if (slice is null || slice.Stage != NativeSlashTargetDuelStage.Counterspell) return false;
        if (!ValidNativeSlashTargetDuelPlan(use!, plan!) || pending.DelayedReturn is not null ||
            pending.ActionKind != LegalActionKind.Duel || pending.EffectCardKind != CardKind.Duel ||
            pending.EffectCardId != use!.CardId || pending.SourceSeat != plan!.ActorSeat ||
            use.TargetIndex != slice.TargetIndex || !pending.TargetSeats.SequenceEqual([slice.TargetSeat]) ||
            plan.Targets[slice.TargetIndex].Source != slice.Source ||
            _contentRegistry.GetSkill(slice.Source.SkillId).Program?.GameplayHash != slice.GameplayHash)
            throw new InvalidOperationException("A native Duel counterspell lost its exact original target slice.");
        return true;
    }

    private bool TryResolveNativeSlashTargetDuel(NullificationWindowFrame pending)
    {
        if (!TryGetNativeSlashTargetDuel(pending, out var use, out var plan, out var slice)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A native Duel slice cannot pass an unfinished child.");
        UpdateLifecycleCardUse(use.Id, current => current with
            { NativeSlashTargetDuel = plan with { Slice = slice with { Stage = NativeSlashTargetDuelStage.Responding } } });
        var original = slice.OriginalAttack;
        var card = original.CardId is { } cardId && cardId > 0 ? GetAttackCard(cardId) with
            { Kind = original.AppearanceKind!.Value, Suit = original.AppearanceSuit!.Value, Rank = original.AppearanceRank!.Value } : null;
        var attack = new CardAttackHandle(this, use.Id, plan.ActorSeat, slice.TargetSeat, card,
            damageAmount: PrepareDamageTargetDuelDamage(use.Id, slice.TargetSeat), playedCardKind: CardKind.Duel,
            physicalCards: original.PhysicalCardIds.Select(GetAttackCard).ToArray(),
            programSkillCardUseFrameId: original.ProgramSkillCardUseFrameId, conversionSource: original.ConversionSource);
        UpdateCardAttackState(use.Id, state => state! with
            { AdjustedSlashReturn = original.AdjustedSlashReturn, ProgramUseInstructionIndex = original.ProgramUseInstructionIndex });
        ActiveCardAttack = attack;
        ActiveDuel = new DuelHandle(this, attack);
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new NativeSlashTargetDuelStartedEvent(use.Id, plan.ActionId,
            slice.TargetIndex, plan.ActorSeat, slice.TargetSeat));
        AddLog("CardEffect", $"此目标的【{CardCatalog.Get(plan.OriginalKind).DisplayName}】改为【决斗】结算。", plan.ActorSeat, slice.TargetSeat);
        NotifyAiOfDuel(_players[plan.ActorSeat], _players[slice.TargetSeat]);
        BeginDuelResponse(ActiveDuel!);
        return true;
    }

    private bool IsNativeSlashTargetDuelZeroUse(long useId) =>
        LifecycleCardUse(useId) is { CardId: 0, PhysicalCardIds.Count: 0,
            Action.PhysicalCards.Count: 0, NativeSlashTargetDuel: { Slice: { } slice } plan } use &&
        ValidNativeSlashTargetDuelPlan(use, plan) && slice.OriginalAttack.CardId is null or 0 &&
        slice.OriginalAttack.PhysicalCardIds.Count == 0 && slice.TargetIndex == use.TargetIndex &&
        slice.TargetIndex >= 0 && slice.TargetIndex < plan.Targets.Count &&
        plan.Targets[slice.TargetIndex].Source == slice.Source;

    private Card NativeSlashTargetDuelRepresentation(CardUseFrame use)
    {
        if (IsNativeSlashTargetDuelZeroUse(use.Id))
            return new Card(0, use.CardKind, use.Action!.EffectiveSuit ?? Suit.None, use.Action.EffectiveRank ?? 0);
        return GetTrickRepresentation(use.Id, use.CardId);
    }

    private CardActionContext NativeSlashTargetDuelEffectAction(CardUseFrame use, NativeSlashTargetDuelSlice slice)
    {
        var action = use.Action!;
        // A same-action effect descriptor, never another acceptance, use ledger
        // or payment. The native effect window designates only this slice.
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat,
            action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            CardKind.Duel, action.TargetSeats, action.PhysicalCards, action.ConversionChain,
            [slice.TargetSeat], action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);
    }

    private bool TryCompleteCanceledNativeSlashTargetDuel(NullificationWindowFrame pending)
    {
        if (!TryGetNativeSlashTargetDuel(pending, out var use, out var plan, out var slice)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id)
            throw new InvalidOperationException("A canceled native Duel must return to its owning Use.");
        UpdateLifecycleCardUse(use.Id, current => current with
            { NativeSlashTargetDuel = plan with { Slice = slice with { Stage = NativeSlashTargetDuelStage.Canceled } } });
        UpdateCardAttackState(use.Id, _ => slice.OriginalAttack with { Active = true });
        ActiveDuel = null;
        ClearPendingDecision();
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new NativeSlashTargetDuelCanceledEvent(use.Id, plan.ActionId,
            slice.TargetIndex, slice.TargetSeat));
        CompleteAttack(new CardAttackHandle(this, use.Id));
        return true;
    }

    // Use membership and the original target index remain stable even when a
    // failed Duel response reverses the actual damage source and recipient.
    private bool IsNativeSlashTargetDuelAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { NativeSlashTargetDuel: { Slice: { Stage: NativeSlashTargetDuelStage.Responding } slice } plan } use)
            return false;
        return ValidNativeSlashTargetDuelPlan(use, plan) && use.TargetIndex == slice.TargetIndex &&
            plan.Targets[slice.TargetIndex].TargetSeat == slice.TargetSeat && plan.Targets[slice.TargetIndex].Source == slice.Source &&
            attack.EffectiveCardKind == CardKind.Duel && attack.CardUserSeat == plan.ActorSeat &&
            (attack.SourceSeat == plan.ActorSeat && attack.TargetSeat == slice.TargetSeat ||
             attack.SourceSeat == slice.TargetSeat && attack.TargetSeat == plan.ActorSeat) &&
            attack.PhysicalCards.Select(card => card.Id).SequenceEqual(slice.OriginalAttack.PhysicalCardIds) &&
            attack.ProgramSkillCardUseFrameId == slice.OriginalAttack.ProgramSkillCardUseFrameId &&
            _contentRegistry.GetSkill(slice.Source.SkillId).Program?.GameplayHash == slice.GameplayHash;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 33720)]
    private void AssertNativeSlashTargetDuelPlans()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.NativeSlashTargetDuel is not { } plan) continue;
            if (!ValidNativeSlashTargetDuelPlan(use, plan))
                throw new InvalidOperationException("A native target-effect plan lost its accepted Use and frozen targets.");
            if (plan.Slice is not { } slice) continue;
            if (!Enum.IsDefined(slice.Stage) || slice.Ordinal <= 0 || slice.Ordinal != plan.IssuedCount ||
                slice.TargetIndex < 0 || slice.TargetIndex >= plan.Targets.Count ||
                plan.Targets[slice.TargetIndex].TargetSeat != slice.TargetSeat ||
                plan.Targets[slice.TargetIndex].Source != slice.Source ||
                slice.OriginalAttack.SourceSeat != plan.ActorSeat || slice.OriginalAttack.CardUserSeat != plan.ActorSeat ||
                slice.OriginalAttack.TargetSeat != slice.TargetSeat || slice.OriginalAttack.EffectiveCardKind != plan.OriginalKind ||
                !slice.OriginalAttack.PhysicalCardIds.SequenceEqual(use.PhysicalCardIds ?? [use.CardId]) ||
                _contentRegistry.GetSkill(slice.Source.SkillId).Program?.GameplayHash != slice.GameplayHash ||
                ProgramEventHistory<NativeSlashTargetDuelIssuedEvent>().Count(e => e == new NativeSlashTargetDuelIssuedEvent(
                    use.Id, plan.ActionId, slice.Ordinal, slice.TargetIndex, plan.ActorSeat, plan.ProviderSeat, slice.TargetSeat,
                    plan.OriginalKind, slice.Source, slice.GameplayHash)) != 1)
                throw new InvalidOperationException("A native Duel slice lost its issued source, materials or original target pair.");
        }
    }
}
