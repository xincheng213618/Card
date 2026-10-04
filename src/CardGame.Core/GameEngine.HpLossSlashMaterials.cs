namespace CardGame.Core;

public sealed record HpLossSlashSelection(CardConversionSource PolicySource, string PolicyHash, int FrozenMaximum,
    CardKind OutputKind, long? CardUseFrameId = null);
public sealed record HpLossMaterialSlashReturn(long CardUseFrameId, long ActionId, CardConversionSource PolicySource,
    string PolicyHash, int FrozenMaximum, long? ParentProgramFrameId, string? ParentGameplayHash,
    string? ParentSkillInstanceId, int ParentInstructionIndex, bool IsZhangba, long PaidFirst, long PaidLast = 0);
public sealed record HpLossMaterialSlashIssuedEvent(long CardUseFrameId, long ActionId, CardConversionSource PolicySource,
    string PolicyHash, int FrozenMaximum, long? ParentProgramFrameId, bool IsZhangba) : IGameEvent;

public sealed partial class GameEngine
{
    private (CardConversionSource Source, string Hash)? HpLossSlashTargetSource(CharacterState owner)
    {
        var value = CardPolicies(owner, SkillProgramCardPolicyKind.SlashExtraTargetsByLostHp).FirstOrDefault();
        return value.Source is null ? null : (new(value.Source.SkillId, value.Policy.Id, owner.Seat, value.Source.SkillInstanceId), value.Source.Program.GameplayHash);
    }
    private bool HasHpLossSlashTargets(CharacterState owner) => HpLossSlashTargetSource(owner) is not null;
    private int HpLossSlashMaximum(CharacterState owner, CardKind kind, int original = 1) =>
        Math.Min(_players.Count(p => p.IsAlive && p.Seat != owner.Seat), original + ConvertRuleValue(EvaluateCardTargetCount(owner, kind)) - 1 +
            (CurrentNextActualUseAdjustment(owner.Seat) is not null ? 1 : 0));
    private static SkillProgramEffect? HpLossSlashProgramProducer(ProgramExecutionPlan plan) =>
        plan.Activation is { MinTargets: 1, MaxTargets: 1 } && plan.Instructions is
            [{ Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.Slash or CardKind.FireSlash } effect] ? effect : null;
    private int HpLossSlashProgramMaximum(CharacterState owner, ProgramExecutionPlan plan, int original) =>
        HasHpLossSlashTargets(owner) && HpLossSlashProgramProducer(plan) is { OutputKind: { } kind }
            ? Math.Max(original, HpLossSlashMaximum(owner, kind)) : original;
    private CommandError? ValidateHpLossSlashProgramSelection(LegalAction action, ProgramExecutionPlan plan,
        IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        var owner = _players[_currentSeat];
        if (targets.Count <= plan.Activation!.MaxTargets || !HasHpLossSlashTargets(owner)) return null;
        var effect = HpLossSlashProgramProducer(plan);
        var source = effect is null ? null : new CardConversionSource(action.ProgramSkillId!, effect.SourceBind!, owner.Seat,
            GetRuntimeSkillInstanceId(owner, action.ProgramSkillId!));
        var selection = source is null ? null : FindProgramMultiCardViewAsSelection(owner, ids, effect!.OutputKind!.Value, false, source);
        return selection is not null && selection.Cards.All(c => !c.IsGeneralWeapon) && targets.Count <= HpLossSlashMaximum(owner, selection.OutputKind) &&
            targets.Distinct().Count() == targets.Count && targets.All(t => IsValidPlayerSeat(t) && CanUseNextActualUseSelectedSlash(owner, _players[t], selection))
            ? null : new(CommandErrorCode.InvalidTarget, "The HP-based Slash targets lost their real materials or current legal target count.");
    }
    private HpLossSlashSelection? FreezeHpLossSlashSelection(CharacterState owner, ProgramExecutionPlan plan, IReadOnlyList<int> targets) =>
        targets.Count > plan.Activation!.MaxTargets && HpLossSlashProgramProducer(plan) is { OutputKind: { } kind } &&
        HpLossSlashTargetSource(owner) is { } policy ? new(policy.Source, policy.Hash, HpLossSlashMaximum(owner, kind), kind) : null;
    private bool HasExactHpLossSlashSelection(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.HpLossSlashSelection is not { } r || f.TriggerId is not null || HpLossSlashProgramProducer(plan) is not { OutputKind: { } kind } ||
            kind != r.OutputKind || r.PolicySource.OwnerSeat != f.OwnerSeat || r.FrozenMaximum < 2 ||
            f.SelectedTargetSeats.Count < 2 || f.SelectedTargetSeats.Count > r.FrozenMaximum ||
            f.SelectedTargetSeats.Distinct().Count() != f.SelectedTargetSeats.Count || _contentRegistry.GetSkill(r.PolicySource.SkillId).Program is not { } p ||
            p.GameplayHash != r.PolicyHash || !p.CardPolicies.Any(x => x.Id == r.PolicySource.BindingId && x.Kind == SkillProgramCardPolicyKind.SlashExtraTargetsByLostHp)) return false;
        if (r.CardUseFrameId is null) return HpLossSlashTargetSource(_players[f.OwnerSeat]) is { } current &&
            current.Source == r.PolicySource && current.Hash == r.PolicyHash;
        if (LifecycleCardUse(r.CardUseFrameId.Value) is { HpLossMaterialSlashReturn: { } useReturn })
            return useReturn.ParentProgramFrameId == f.Id && useReturn.ParentGameplayHash == f.GameplayHash && useReturn.ParentSkillInstanceId == f.SkillInstanceId;
        return CompleteProgramEventHistory().OfType<HpLossMaterialSlashIssuedEvent>().Count(e => e.CardUseFrameId == r.CardUseFrameId &&
            e.ParentProgramFrameId == f.Id && e.PolicySource == r.PolicySource && e.PolicyHash == r.PolicyHash && !e.IsZhangba) == 1 &&
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.CardUseFrameId) == 1;
    }
    private void AddHpLossSlashEquipmentActions(List<LegalAction> actions, CharacterState owner)
    {
        if (!HasHpLossSlashTargets(owner)) return;
        for (var i = 0; i < actions.Count; i++)
            if (actions[i] is { Kind: LegalActionKind.UseEquipmentEffect, EquipmentKind: CardKind.ZhangbaSerpentSpear } spear)
                actions[i] = spear with { MaxTargetCount = Math.Max(spear.MaxTargetCount, HpLossSlashMaximum(owner, CardKind.Slash)) };
    }
    private CommandError? ValidateHpLossSlashZhangba(CharacterState owner, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (targets.Count <= 1) return null;
        var cards = GetZhangbaSlashPairs(owner).FirstOrDefault(p => p.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        return HasHpLossSlashTargets(owner) && cards is not null && CanUseZhangbaSerpentSpear(owner) &&
            targets.Count <= HpLossSlashMaximum(owner, CardKind.Slash) && targets.Distinct().Count() == targets.Count && targets.All(t =>
                IsValidPlayerSeat(t) && CanUseVirtualSlashTarget(owner, _players[t], CardKind.Slash, PhysicalGroupSuit(owner, cards),
                    PhysicalGroupColor(owner, cards), ZhangbaSpecificSlashRank(owner, cards), ids) &&
                !IsDirectedCardTargetProhibited(owner.Seat, t, CardKind.Slash) &&
                !IsCardTargetProhibited(_players[t], CardKind.Slash, PhysicalGroupSuit(owner, cards), PhysicalGroupColor(owner, cards)) &&
                !HasBeneficiarySuitShield(owner.Seat, t, PhysicalGroupSuit(owner, cards)))
            ? null : new(CommandErrorCode.InvalidTarget, "The HP-based spear use lost its two real materials and legal targets.");
    }
    private void ResolveHpLossSlashZhangba(CharacterState owner, IReadOnlyList<int> targets, IReadOnlyList<Card> cards)
    {
        if (ValidateHpLossSlashZhangba(owner, cards.Select(c => c.Id).ToArray(), targets) is { } error) throw new InvalidOperationException(error.Message);
        BeginHpLossMaterialSlash(owner, cards, CardKind.Slash, targets, null, null);
    }
    private SkillProgramStepOutcome BeginHpLossSelectedSlash(ProgramSkillFrame f, ProgramMultiCardViewAsSelection selection)
    {
        var owner = _players[f.OwnerSeat]; var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (!HasExactHpLossSlashSelection(f, plan) || f.HpLossSlashSelection!.CardUseFrameId is not null ||
            HpLossSlashTargetSource(owner) is not { } policy || policy.Source != f.HpLossSlashSelection.PolicySource ||
            f.SelectedTargetSeats.Count > HpLossSlashMaximum(owner, selection.OutputKind) ||
            f.SelectedTargetSeats.Any(t => !CanUseNextActualUseSelectedSlash(owner, _players[t], selection)))
            throw new InvalidOperationException("The selected HP-based material Slash expired before actual payment.");
        BeginHpLossMaterialSlash(owner, selection.Cards, selection.OutputKind, f.SelectedTargetSeats, selection.Source, f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void BeginHpLossMaterialSlash(CharacterState owner, IReadOnlyList<Card> cards, CardKind kind,
        IReadOnlyList<int> seats, CardConversionSource? conversion, ProgramSkillFrame? parent)
    {
        var policy = HpLossSlashTargetSource(owner) ?? throw new InvalidOperationException("The target-count source expired before payment.");
        // BeginCardUse emits the true declaration and can consume an existing
        // next-use adjustment. Freeze this maximum before that producer runs.
        var frozenMaximum = parent?.HpLossSlashSelection?.FrozenMaximum ?? HpLossSlashMaximum(owner, kind);
        var armor = HasArmorBypass(owner);
        var id = BeginCardUse(cards[0], owner.Seat, seats, kind, ignoresArmor: armor, physicalCardIds: cards.Select(c => c.Id).ToArray(),
            conversionSource: conversion, isTrueZhangbaSlash: parent is null);
        var action = LifecycleCardUse(id)!.Action ?? throw new InvalidOperationException("A material Slash must keep its true accepted action.");
        var receipt = new HpLossMaterialSlashReturn(id, action.ActionId, policy.Source, policy.Hash, frozenMaximum,
            parent?.Id, parent?.GameplayHash, parent?.SkillInstanceId, parent?.InstructionIndex ?? 0, parent is null, SlashTargetPenaltySequence + 1);
        UpdateLifecycleCardUse(id, use => use with { HpLossMaterialSlashReturn = receipt, TargetsAdjusted = true });
        if (parent is not null) ReplaceRuntimeFrame(parent.Id, parent with { HpLossSlashSelection = parent.HpLossSlashSelection! with { CardUseFrameId = id } });
        var nuzhan = GetNuzhanModifiers(id, owner);
        foreach (var card in cards) MoveCard(card, FindOwnedCardLocation(owner, card), CardLocation.Processing, CardMoveReasons.Use);
        receipt = receipt with { PaidLast = SlashTargetPenaltySequence };
        UpdateLifecycleCardUse(id, use => use with { HpLossMaterialSlashReturn = receipt });
        AdvanceEventRulesAndQueueFact(new HpLossMaterialSlashIssuedEvent(id, action.ActionId, policy.Source, policy.Hash, receipt.FrozenMaximum, parent?.Id, parent is null));
        var counted = LifecycleCardUse(id)?.UnlimitedUse != true && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(owner, conversion) &&
            _phase == TurnPhase.Play && owner.Seat == _currentSeat;
        if (counted) RecordSlashUseDebit(id, owner.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, kind);
        var damage = (owner.HasAlcoholEffect ? 2 : 1) + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(id, owner); owner.HasAlcoholEffect = false;
        var pending = new FangtianHalberdHandle(this, id, owner.Seat, cards[0], kind, armor, damage, seats, false, counted, conversion);
        ActiveFangtianHalberd = pending;
        if (conversion is not null) AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, conversion.SkillId, conversion.BindingId,
            owner.Seat, Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), kind, IsUse: true, seats));
        else AdvanceEventRulesAndQueueFact(new ZhangbaSerpentSpearConvertedEvent(id, owner.Seat, Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), true, seats[0]));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(cards[0].Id, kind, owner.Seat, seats[0], IgnoresArmor: armor));
        foreach (var seat in seats) NotifyAiOfSlash(owner, _players[seat]);
        BeginNextFangtianHalberdTarget(pending);
    }
    private bool MatchesHpLossMaterialSlash(CardUseFrame use)
    {
        if (use.HpLossMaterialSlashReturn is not { } r || use.Action is not { Type: CardActionType.Use } action ||
            r.CardUseFrameId != use.Id || r.ActionId != action.ActionId || !IsSlashCard(use.CardKind) ||
            use.PhysicalCardIds is not { Count: >= 2 } ids || !ids.SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
            action.ProviderSeat != r.PolicySource.OwnerSeat || r.FrozenMaximum < 2 || r.PaidFirst <= 0 || r.PaidLast < r.PaidFirst ||
            use.TargetSeats.Count == 0 || !MatchesSlashTargetBenefitUse(new(use.Id, action.ActionId, r.PolicySource.OwnerSeat,
                action.ProviderSeat, use.CardKind, use.TargetSeats[0], _turnNumber, _currentSeat, null)) ||
            _contentRegistry.GetSkill(r.PolicySource.SkillId).Program is not { } policy ||
            policy.GameplayHash != r.PolicyHash || !policy.CardPolicies.Any(p => p.Id == r.PolicySource.BindingId && p.Kind == SkillProgramCardPolicyKind.SlashExtraTargetsByLostHp) ||
            action.PhysicalCards.Any(c => _cardMovements.Count(m => m.Sequence >= r.PaidFirst && m.Sequence <= r.PaidLast && m.CardId == c.CardId &&
                m.From == c.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) != 1) ||
            CompleteProgramEventHistory().OfType<HpLossMaterialSlashIssuedEvent>().Count(e => e.CardUseFrameId == use.Id && e.ActionId == action.ActionId &&
                e.PolicySource == r.PolicySource && e.PolicyHash == r.PolicyHash && e.FrozenMaximum == r.FrozenMaximum &&
                e.ParentProgramFrameId == r.ParentProgramFrameId && e.IsZhangba == r.IsZhangba) != 1) return false;
        // A genuine Cheng Pu committed producer may append only its frozen
        // conversion after changing this already issued Slash to Fire Slash.
        // Its mature proof validates every current target and changed fact.
        var issuedAction = action;
        if (use.CurrentSlashFirePolicy is { Converted: true } fire)
        {
            if (!IsCurrentSlashFireChangedUse(use)) return false;
            AssertCurrentSlashFirePolicy(use); issuedAction = fire.OriginalAction;
        }
        if (r.IsZhangba) return r.ParentProgramFrameId is null && r.ParentGameplayHash is null && r.ParentSkillInstanceId is null && r.ParentInstructionIndex == 0 &&
            ids.Count == 2 && issuedAction.EffectiveKind == CardKind.Slash && issuedAction.ConversionChain.Count == 0 && CompleteProgramEventHistory().OfType<ZhangbaSerpentSpearConvertedEvent>().Any(e =>
                e.ResolutionId == use.Id && e.UserSeat == r.PolicySource.OwnerSeat && e.IsUse && e.PhysicalCardIds.SequenceEqual(ids));
        var f = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == r.ParentProgramFrameId);
        return f is not null && f.OwnerSeat == r.PolicySource.OwnerSeat && f.GameplayHash == r.ParentGameplayHash && f.SkillInstanceId == r.ParentSkillInstanceId &&
            f.InstructionIndex == r.ParentInstructionIndex && f.HpLossSlashSelection is { } selection && selection.CardUseFrameId == use.Id &&
            selection.PolicySource == r.PolicySource && selection.PolicyHash == r.PolicyHash && selection.FrozenMaximum == r.FrozenMaximum &&
            f.SelectedCardIds.Order().SequenceEqual(ids.Order()) && issuedAction.ConversionChain is [var materialSource] &&
            materialSource.SkillId == f.SkillId && materialSource.SkillInstanceId == f.SkillInstanceId && materialSource.OwnerSeat == f.OwnerSeat &&
            materialSource.BindingId == ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
                .GetPausedInstruction(f.InstructionIndex).Effect.SourceBind &&
            HasExactHpLossSlashSelection(f, ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!));
    }
    private long? HpLossMaterialSlashParent(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { HpLossMaterialSlashReturn: { } r } use) return null;
        if (!MatchesHpLossMaterialSlash(use)) throw new InvalidOperationException("The multi-material Slash lost its exact issued policy and typed parent.");
        return r.ParentProgramFrameId;
    }
    private bool HasExactHpLossMaterialReturn(CardUseFrame use)
    {
        if (use.HpLossMaterialSlashReturn is null) return false;
        if (!MatchesHpLossMaterialSlash(use)) throw new InvalidOperationException("The finished multi-material Slash lost its original payment/producer.");
        return use.HpLossMaterialSlashReturn.ParentProgramFrameId is not null;
    }
    private bool IsExactHpLossMaterialSlash(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { HpLossMaterialSlashReturn: not null } use || !MatchesHpLossMaterialSlash(use) ||
            !attack.PhysicalCards.Select(c => c.Id).SequenceEqual(use.PhysicalCardIds!)) return false;
        var retained = use.PhysicalCardIds!.Where(id => !IsCurrentUsePhysicalCardClaim(use.Id, id)).ToHashSet();
        return processing.All(c => retained.Contains(c.Id)) && retained.All(id => _cardZones.GetLocation(id).Zone is CardZoneKind.Processing or
            CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile);
    }
    private void AssertHpLossMaterialSlashes()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.HpLossMaterialSlashReturn is not null))
            if (!MatchesHpLossMaterialSlash(use)) throw new InvalidOperationException("The HP-based multi-material use lost its immutable issuance or real costs.");
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.HpLossSlashSelection is not null))
            if (!HasExactHpLossSlashSelection(f, ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)))
                throw new InvalidOperationException("The HP-based selected producer lost its exact target selection.");
    }
}
