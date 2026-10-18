namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouGuquSkill = "mou-ol:guqu";
    private const string MouGuquBinding = "completed-que-use";
    private bool HasMouGuquCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouZhouYuGuqu);
    private bool MouGuquTurnOpen => HasMouGuquCapability && _setupComplete && _turnNumber > 0 &&
        IsValidPlayerSeat(_turnProgression.OwnerSeat) && !ProgramEventHistory<TurnEndedEvent>().Any(e =>
            e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat);
    private IReadOnlyList<(CardConversionSource Source, string Hash)> CurrentMouGuquSources()
    {
        if (!MouGuquTurnOpen || !_players[_turnProgression.OwnerSeat].IsAlive) return [];
        return GetSkillBindingShard(_players[_turnProgression.OwnerSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted)
            .Where(b => b.SkillId == MouGuquSkill && b.Trigger.Id == MouGuquBinding &&
                b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveMouZhouYuGuqu }])
            .Select(b => (new CardConversionSource(b.SkillId, b.Trigger.Id, _turnProgression.OwnerSeat, b.SkillInstanceId), b.Program.GameplayHash))
            .Distinct().ToArray();
    }
    private bool IsCurrentMouGuquSource(CardConversionSource source, string hash) =>
        CurrentMouGuquSources().Any(s => s.Source == source && s.Hash == hash);
    private bool IsMouGuquQueForSource(CardConversionSource source, int cardId) => MouGuquTurnOpen &&
        ProgramEventHistory<GuquQueMarkedEvent>().Any(e => e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat &&
            e.Source == source && e.GameplayHash == _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash && e.CardId == cardId);
    private bool HasMouGuquEntityMark(int cardId) => HasMouGuquCapability &&
        ProgramEventHistory<GuquQueMarkedEvent>().Any(e => e.CardId == cardId && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat) &&
        CurrentMouGuquSources().Any(s => IsMouGuquQueForSource(s.Source, cardId));
    private bool HasMouGuquHandEntityMark(int holder, int cardId) => IsValidPlayerSeat(holder) &&
        GetHand(_players[holder]).Any(c => c.Id == cardId) && HasMouGuquEntityMark(cardId);
    private IReadOnlyList<(CardConversionSource Source, string Hash)> MouGuquRestrictingSources(int actor) =>
        !IsValidPlayerSeat(actor) || !HasMouGuquCapability || !ProgramEventHistory<GuquQueMarkedEvent>().Any()
            ? [] : CurrentMouGuquSources().Where(s => s.Source.OwnerSeat != actor &&
                GetHand(_players[actor]).Any(c => IsMouGuquQueForSource(s.Source, c.Id))).ToArray();

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29021)]
    private void CaptureMouGuquPublicHandReveals(IGameEvent payload)
    {
        if (!HasMouGuquCapability) return;
        var (frame, ids) = payload switch
        {
            CardsRevealedEvent e => (e.ResolutionId, e.Cards.Select(c => c.Id).ToArray()),
            ProgramCardsRevealedEvent e => (e.FrameId, e.Cards.Select(c => c.Id).ToArray()),
            FireAttackCardRevealedEvent e => (e.ResolutionId, new[] { e.CardId }),
            ProgramHandComparisonCardRevealedEvent e => (e.FrameId, new[] { e.CardId }),
            ProgramHandComparisonSourceRevealedEvent e => (e.FrameId, new[] { e.CardId }),
            ProgramHandDraftRevealedEvent e => (e.FrameId, MiuyanDraftHandIds(e).ToArray()),
            ProgramQingliangRevealEvent e => (e.FrameId, MiuyanQingliangHandIds(e).ToArray()),
            ProgramJuanxiaTrickUsedEvent { TrickKind: nameof(CardKind.FireAttack), CardId: > 0 } e => (e.FrameId, MiuyanJuanxiaHandIds(e).ToArray()),
            _ => (0L, Array.Empty<int>())
        };
        if (ids.Length == 0) return;
        var sources = CurrentMouGuquSources(); if (sources.Count == 0) return;
        var shown = ids.ToHashSet();
        // A public show can carry deck/equipment/host-only cards too. Only
        // entities actually in a real player's Hand at this exact show qualify.
        // Private look/peek, ordinary Use/Played and pindian reveal are distinct.
        foreach (var player in _players)
        foreach (var card in GetHand(player).Where(c => shown.Contains(c.Id)))
        foreach (var source in sources)
        {
            if (ProgramEventHistory<GuquQueMarkedEvent>().Any(e => e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat &&
                e.Source == source.Source && e.GameplayHash == source.Hash && e.CardId == card.Id)) continue;
            AdvanceEventRulesAndQueueFact(new GuquQueMarkedEvent(source.Source, source.Hash, _turnNumber,
                _turnProgression.OwnerSeat, frame, player.Seat, card.Id, card.Kind, _movementSequence));
        }
    }
}
