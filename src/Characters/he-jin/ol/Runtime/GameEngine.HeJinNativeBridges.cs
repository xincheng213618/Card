namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanIssueHeJinMouzhuCard(int actor, int target, CardKind kind)
    {
        if (!IsValidPlayerSeat(actor) || !IsValidPlayerSeat(target) || actor == target ||
            !_players[actor].IsAlive || !_players[target].IsAlive) return false;
        if (kind == CardKind.Duel) return CanIssueSelectedActorDuel(target, actor);
        return kind == CardKind.Slash && !IsCardUseForbidden(actor, kind, CardActionType.Use) &&
            !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
            !IsDirectedCardTargetProhibited(actor, target, kind) && !IsCardTargetProhibited(_players[target], kind, Suit.None) &&
            (HasCardDistanceExemption(_players[actor], _players[target], kind) || IgnoresSlashUseDistance(_players[actor]) ||
                IsWithinSpecificSlashRange(_players[actor], _players[target], kind, 0)) && !IsSlashProhibited(_players[target]);
    }
    private void BeginHeJinMouzhuCard(ProgramSkillFrame frame, HeJinMouzhuUseOrigin origin)
    {
        if (!MatchesHeJinMouzhu(frame, origin) || origin.CardUseFrameId != _resolutionSequence + 1 ||
            !CanIssueHeJinMouzhuCard(origin.ActorSeat, origin.TargetSeat, origin.CardKind) || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Mouzhu requires its exact paid gift, choosing donor and native legal assault.");
        var card = new Card(0, origin.CardKind, Suit.None, 0);
        var id = BeginCardUse(card, origin.ActorSeat, [origin.TargetSeat], origin.CardKind, physicalCardIds: [], heJinMouzhu: origin);
        if (id != origin.CardUseFrameId) throw new InvalidOperationException("Mouzhu native use changed its reserved frame.");
        if (origin.CardKind == CardKind.Duel)
        {
            BeginJizhiOrNullificationWindow(id, card, origin.ActorSeat, [origin.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
            return;
        }
        var actor = _players[origin.ActorSeat]; var target = _players[origin.TargetSeat];
        UpdateLifecycleCardUse(id, use => use with { HeJinMouzhu = origin with { AttackStarted = true } });
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null,
            damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: frame.Id);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, target.Seat));
        TryMarkProgramUseCommitted(id);
        var action = LifecycleCardUse(id)!.Action!;
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }
    private bool IsHeJinMouzhuDuelUse(long id) => LifecycleCardUse(id) is
        { HeJinMouzhu.CardKind: CardKind.Duel, CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0 };
    private bool IsHeJinMouzhuAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        attack.Card is null && processing.Count == 0 && attack.PhysicalCards.Count == 0 &&
        LifecycleCardUse(attack.ResolutionId)?.HeJinMouzhu is { AttackStarted: true } origin &&
        attack.EffectiveCardKind == origin.CardKind && attack.ProgramSkillCardUseFrameId == origin.ProgramFrameId &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == origin.ProgramFrameId && MatchesHeJinMouzhu(f, origin));
    [NativeUseReturn(26710)]
    private void ReturnHeJinMouzhuUse(CardUseFrame use)
    {
        if (use.HeJinMouzhu is not { AttackStarted: false } origin) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || use.Id != origin.CardUseFrameId || !MatchesHeJinMouzhu(frame, origin))
            throw new InvalidOperationException("Mouzhu lost its canceled native assault return.");
        FinishHeJinMouzhu(frame, true);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 26710)]
    private bool ReturnHeJinMouzhuAttack(AttackCompletionReceipt completion)
    {
        if (completion.HeJinMouzhu is not { } origin) return false;
        if (!origin.AttackStarted || completion.ProgramFrameId != origin.ProgramFrameId || completion.ResolutionId != origin.CardUseFrameId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || !MatchesHeJinMouzhu(frame, origin))
            throw new InvalidOperationException("Mouzhu lost its native response, damage or dying completion.");
        FinishHeJinMouzhu(frame, true); return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 26710)]
    private void AssertHeJinMouzhuNativeUses(ProgramSkillFrame frame)
    {
        if (frame.HeJinMouzhu is null) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.HeJinMouzhu is not null))
        {
            var origin = use.HeJinMouzhu!; var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || !MatchesHeJinMouzhu(parent, origin) ||
                use.Id != origin.CardUseFrameId || use.CardId != 0 || use.CardKind != origin.CardKind || use.PhysicalCardIds?.Count != 0 ||
                use.SourceSeat != origin.ActorSeat || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                    EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } action || action.EffectiveKind != origin.CardKind ||
                action.ActorSeat != origin.ActorSeat || action.ProviderSeat != origin.ActorSeat)
                throw new InvalidOperationException("Mouzhu requires its exact issued zero-entity native use and original paid owner.");
        }
    }
}
