namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanIssueWuAnGuoLiyongDuel(int actor, int target) => CanIssueSelectedActorDuel(target, actor);

    private void BeginWuAnGuoLiyongDuel(ProgramSkillFrame frame, WuAnGuoLiyongDuelOrigin origin)
    {
        if (!MatchesWuAnGuoLiyong(frame, origin) || origin.CardUseFrameId != _resolutionSequence + 1 ||
            !CanIssueWuAnGuoLiyongDuel(origin.ActorSeat, origin.TargetSeat) || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Liyong requires its exact accepted Yin operation, frozen native gain and legal opposing Duel actor.");
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var id = BeginCardUse(card, origin.ActorSeat, [origin.TargetSeat], CardKind.Duel, physicalCardIds: [], wuAnGuoLiyong: origin);
        if (id != origin.CardUseFrameId) throw new InvalidOperationException("Liyong native Duel changed its reserved frame.");
        BeginJizhiOrNullificationWindow(id, card, origin.ActorSeat, [origin.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
    }

    private bool IsWuAnGuoLiyongDuelUse(long id) => LifecycleCardUse(id) is
        { WuAnGuoLiyong: not null, CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0 };

    private bool IsWuAnGuoLiyongAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        attack.Card is null && processing.Count == 0 && attack.PhysicalCards.Count == 0 &&
        LifecycleCardUse(attack.ResolutionId)?.WuAnGuoLiyong is { AttackStarted: true } origin &&
        attack.EffectiveCardKind == CardKind.Duel && attack.ProgramSkillCardUseFrameId == origin.ProgramFrameId &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == origin.ProgramFrameId && MatchesWuAnGuoLiyong(f, origin));

    [NativeUseReturn(27010)]
    private void ReturnWuAnGuoLiyongUse(CardUseFrame use)
    {
        if (use.WuAnGuoLiyong is not { AttackStarted: false } origin) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || use.Id != origin.CardUseFrameId || !MatchesWuAnGuoLiyong(frame, origin))
            throw new InvalidOperationException("Liyong lost its canceled native Duel return.");
        FinishWuAnGuoLiyong(frame, true);
    }

    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27010)]
    private bool ReturnWuAnGuoLiyongAttack(AttackCompletionReceipt completion)
    {
        if (completion.WuAnGuoLiyong is not { } origin) return false;
        if (!origin.AttackStarted || completion.ProgramFrameId != origin.ProgramFrameId || completion.ResolutionId != origin.CardUseFrameId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || !MatchesWuAnGuoLiyong(frame, origin))
            throw new InvalidOperationException("Liyong lost its native response, damage or dying completion.");
        FinishWuAnGuoLiyong(frame, true); return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 27010)]
    private void AssertWuAnGuoLiyongNativeUses(ProgramSkillFrame frame)
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.WuAnGuoLiyong?.ProgramFrameId == frame.Id))
        {
            var origin = use.WuAnGuoLiyong!; var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || !MatchesWuAnGuoLiyong(parent, origin) ||
                use.Id != origin.CardUseFrameId || use.CardId != 0 || use.CardKind != CardKind.Duel || use.PhysicalCardIds?.Count != 0 ||
                use.SourceSeat != origin.ActorSeat || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                    EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } action || action.EffectiveKind != CardKind.Duel ||
                action.ActorSeat != origin.ActorSeat || action.ProviderSeat != origin.ActorSeat)
                throw new InvalidOperationException("Liyong requires its exact issued zero-entity native use and original owning Yin operation.");
        }
    }
}
