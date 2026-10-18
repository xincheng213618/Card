namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ApplyExtraCapableAlcoholEffect(long id, CharacterState target)
    {
        if (IsExtraCardEffectRound(id)) target.AlcoholDamageBonus = checked(target.AlcoholDamageBonus + 1);
        else target.HasAlcoholEffect = true;
    }
    private bool RestoreExtraGroupDamageSource(GroupCardHandle group)
    {
        if (LifecycleCardUse(group.ResolutionId) is not { } use ||
            NativeExtraEffectState(use) is not { WholeGroupRepeated: true, FrozenGroupDamageSourceSeat: { } source }) return false;
        group.DamageSourceSeat = source; return true;
    }
    private void CaptureExtraGroupDamageSource(GroupCardHandle group)
    {
        if (LifecycleCardUse(group.ResolutionId) is not { } use ||
            NativeExtraEffectState(use) is not { WholeGroupRepeated: false }) return;
        UpdateNativeExtraEffectState(group.ResolutionId, r => r with { FrozenGroupDamageSourceSeat = group.DamageSourceSeat });
    }
    private bool IsExtraCardEffectRound(long id) => IsMouBingcaiExtraRound(id) || IsMoQuanyuExtraRound(id) || IsZixiExtraEffectRound(id) || IsFeiYiExtraEffectRound(id) || LifecycleCardUse(id)?.ExtraCardEffect?.WholeGroupRepeated == true;
    private bool HasUnstartedExtraCardEffect(long id) => HasUnstartedMouBingcaiExtraEffect(id) || HasUnstartedMoQuanyuExtraRound(id) || HasUnstartedZixiExtraEffect(id) || HasUnstartedFeiYiExtraEffect(id) || LifecycleCardUse(id) is { ExtraCardEffect: { WholeGroupRepeated: false } } use &&
        use.Action is { } action && use.ExtraCardEffect.ActionId == action.ActionId && !IsNamedAmbushUseIneffective(use) && _winner == Winner.None;
    private void CaptureExtraCardEffectAttack(CardAttackHandle attack)
    {
        CaptureMouBingcaiExtraEffectAttack(attack);
        CaptureZixiExtraEffectAttack(attack);
        CaptureMoQuanyuExtraAttack(attack);
        if (LifecycleCardUse(attack.ResolutionId) is not { ExtraCardEffect: { WholeGroupRepeated: false } r, CardAttack: { } original } use ||
            !IsSlashCard(use.CardKind) || r.OriginalAttacks.Any(s => s.TargetIndex == use.TargetIndex)) return;
        UpdateLifecycleCardUse(use.Id, current => current with { ExtraCardEffect = r with {
            OriginalAttacks = r.OriginalAttacks.Append(new(use.TargetIndex, original)).ToArray(),
            Fangtian = current.Continuations.FangtianHalberd ?? r.Fangtian } });
    }
    private bool TryBeginExtraCardEffectRound(long id, bool afterAttack = false)
    {
        CompleteMouBingcaiExtraEffectRound(id);
        CompleteZixiExtraEffectRound(id);
        CompleteMoQuanyuExtraRound(id);
        if (TryBeginFeiYiExtraEffectRound(id, afterAttack)) return true;
        if (TryBeginZixiExtraEffectRound(id, afterAttack)) return true;
        if (TryBeginMoQuanyuExtraRound(id, afterAttack)) return true;
        if (TryBeginMouBingcaiExtraEffectRound(id, afterAttack)) return true;
        if (!HasUnstartedExtraCardEffect(id) || LifecycleCardUse(id) is not { ExtraCardEffect: { } r, Action: { } action } use) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("An extra effect cannot pass an unfinished child.");
        if (r.Trick is not null && HasRemainingSequentialTrickTargets(id) || r.Simple is not null && HasRemainingAdjustedSimpleTargets(id)) return false;
        if (r.Source.SkillId == "guandu:battle-event" && !GuanduRepeatTargetsRemainLegal(use)) return false;
        // The source/targets were already accepted, finalized and paid by this
        // use. Rechecking distance, target legality, skill ownership or quota
        // here would turn an extra effect into another card use.
        ReplaceRuntimeFrame(id, use with { ExtraCardEffect = r with { WholeGroupRepeated = true }, TargetIndex = 0,
            Step = ResolutionFrameStep.ResolvingEffect, FireAttackSelection = null, ColorFireAttack = null, UnexpectedAssaultReveal = null,
            VirtualBasicEffectApplied = false });
        AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id, action.ActionId, 0,
            use.TargetSeats.FirstOrDefault(use.SourceSeat), use.CardKind));
        if (afterAttack) { ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision(); }
        if (IsSlashCard(use.CardKind))
        {
            var first = r.OriginalAttacks.OrderBy(s => s.TargetIndex).FirstOrDefault(s => _players[s.Attack.TargetSeat].IsAlive &&
                !IsCardEffectIneffective(id, s.Attack.TargetSeat));
            if (first is null) return false;
            var prepared = use.PreparedTargetAttacks?.Select((state, index) => r.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == index)?.Attack ?? state).ToArray();
            UpdateLifecycleCardUse(id, current => current with { TargetIndex = first.TargetIndex,
                PreparedTargetAttacks = prepared is null ? null : Array.AsReadOnly(prepared) });
            if (r.Fangtian is { } fang)
            {
                UpdateCardContinuations(id, state => state with { FangtianHalberd = fang with { Active = true, CurrentAttackOwnerId = id } });
                ActiveFangtianHalberd = new(this, id);
            }
            UpdateCardAttackState(id, _ => ResetExtraCardEffectAttack(first.Attack));
            var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack;
            ContinueSlashDefense(attack); return true;
        }
        if (r.Trick is { } trick)
        {
            ActiveGroupCard = null; ActiveBorrowedSword = null;
            var card = GetTrickRepresentation(id, use.CardId, requireProcessing: true);
            BeginNullificationWindow(id, card, use.SourceSeat, use.TargetSeats,
                trick.ActionKind, targetCardId: null, trick.RequiredCardKind, use.CardKind); return true;
        }
        if (r.Simple is { } simple)
        {
            if (use.TargetSeats.Count > 1 && simple.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol or SimpleCardUseEffect.GuanduIntoxication)
                UpdateLifecycleCardUse(id, current => current with { AdjustedSimpleContinuation = simple });
            ContinueSimpleCardUse(id, simple); return true;
        }
        throw new InvalidOperationException("An issued extra effect lacks its original native effect continuation.");
    }
    private static CardAttackState ResetExtraCardEffectAttack(CardAttackState original) => original with {
        Active = true, CixiongDoubleSwordsResolved = true, DamageWasApplied = false, DamageAmountFinalized = false,
        BeforeDamageProgramsResolved = false, ResolvedDyingDamageFrameId = null, ChainedTargetSeats = [], ChainedTargetIndex = 0,
        IsChainPropagation = false, SuccessfulDodgeResponses = 0, IceSwordAttempted = false, QilinBowAttempted = false,
        FactionDefenseAttempted = false, FactionSlashAttempted = false, DamageRedirected = false, RecipientDamageScope = null,
        DamageTransferFollowup = null
    };
    private bool ContinueExtraCardEffectFangtian(CardAttackHandle attack)
    {
        if (ContinueMouBingcaiExtraFangtian(attack)) return true;
        if (ContinueMoQuanyuFangtian(attack)) return true;
        if (LifecycleCardUse(attack.ResolutionId) is not { ExtraCardEffect: { WholeGroupRepeated: true } r } use) return false;
        var seed = r.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == use.TargetIndex);
        if (seed is null) { CompleteAttack(attack); return true; }
        UpdateCardAttackState(use.Id, _ => ResetExtraCardEffectAttack(seed.Attack));
        ContinueSlashDefense(attack); return true;
    }
    private bool TryBeginExtraAttackRound(CardAttackHandle attack)
    {
        // All chain propagation and all native target cursors must have returned
        // before FinishAttack calls this bridge. The accepted use stays live.
        return TryBeginExtraCardEffectRound(attack.ResolutionId, afterAttack: true);
    }
    private void AssertExtraCardEffectReceipts()
    {
        AssertZixiExtraEffectRounds();
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.ExtraCardEffect is not null))
        {
            var r = use.ExtraCardEffect!;
            if (use.Action is not { Type: CardActionType.Use } action || action.ActionId != r.ActionId || !ExtraEffectCardKind(use.CardKind) ||
                (r.AutomaticGameplayHash is { } automaticHash
                    ? CompleteProgramEventHistory().OfType<AutomaticExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == use.Id &&
                        e.ActionId == r.ActionId && e.ProgramFrameId == r.ProgramFrameId && e.Source == r.Source &&
                        e.GameplayHash == automaticHash && _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash == automaticHash)
                    : CompleteProgramEventHistory().OfType<ExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == use.Id &&
                        e.ActionId == r.ActionId && e.ProgramFrameId == r.ProgramFrameId && e.Source == r.Source && e.PaidCardId == r.PaidCardId)) != 1 ||
                r.OriginalAttacks.GroupBy(s => s.TargetIndex).Any(g => g.Count() != 1) ||
                r.OriginalAttacks.Any(s => s.TargetIndex < 0 || s.TargetIndex >= use.TargetSeats.Count) ||
                r.WholeGroupRepeated && !CompleteProgramEventHistory().OfType<ExtraCardEffectStartedEvent>().Any(e => e.CardUseFrameId == use.Id && e.ActionId == r.ActionId))
                throw new InvalidOperationException("An extra effect lost its one real use, paid source or native target seeds.");
        }
    }
}
