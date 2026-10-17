namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanHulaoWeaponRecast(Card card) => _modeDefinition.HulaoActivity is not null &&
        EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon;
    // This signal never crosses the command session or persists in a checkpoint.
    // It unwinds synchronous draw/damage callers before they can open further
    // effects belonging to the interrupted form. The accepted command commits
    // the paid prefix and the explicit cancellation facts together.
    private sealed class HulaoTurnInterruption(HulaoTransformationCause cause) : Exception
    {
        internal HulaoTransformationCause Cause { get; } = cause;
    }

    private int HulaoShuffleCount => CompleteProgramEventHistory().OfType<HulaoDeckShuffledEvent>()
        .Select(e => e.ShuffleNumber).DefaultIfEmpty().Last();

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27600)]
    private void ObserveHulaoTransformation(IGameEvent fact)
    {
        if (_modeDefinition.HulaoActivity is not { } h || !_setupComplete ||
            _turnNumber <= 0 || _winner != Winner.None || BossActivityStageIndex != 0) return;
        if (fact is HulaoDeckShuffledEvent { ShuffleNumber: 1 })
            throw new HulaoTurnInterruption(HulaoTransformationCause.FirstShuffle);
        if (fact is DamageAppliedEvent or ProgramSkillHpLostEvent or MaximumHpChangedEvent or DamageReplacedWithHpLossEvent &&
            _players[BossActivitySeat].Hp <= h.TransformationHp)
            throw new HulaoTurnInterruption(HulaoTransformationCause.HealthThreshold);
    }

    private void RecordHulaoDeckShuffle()
    {
        if (_modeDefinition.HulaoActivity is not null)
            AdvanceEventRulesAndQueueFact(new HulaoDeckShuffledEvent(HulaoShuffleCount + 1, _turnNumber));
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27601)]
    private void EndHulaoAfterShuffleLimit(TurnEndedEvent ended)
    {
        if (_modeDefinition.HulaoActivity is not { } h || HulaoShuffleCount < h.ShuffleLimit ||
            _winner != Winner.None) return;
        _winnerTeamId = _modeDefinition.BossActivity!.EnemyTeamId;
        _winner = GetWinnerForTeam(_winnerTeamId);
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null));
    }

    private void CompleteHulaoTurnInterruption(HulaoTurnInterruption interruption)
    {
        if (_modeDefinition.HulaoActivity is null || BossActivityStageIndex != 0 || _winner != Winner.None)
            throw new InvalidOperationException("A Hulao interruption lost its first-form owner.");
        var previous = _players[_currentSeat];
        var stage = _modeDefinition.BossActivity!.Stages[1];
        var boss = _players[BossActivitySeat];
        var generalId = stage.GeneralPoolIds[_random.Next(stage.GeneralPoolIds.Count)];
        var general = _generalPool.Single(g => g.Id == generalId);
        boss.General = general;
        boss.MaxHp = general.BaseHp;
        boss.Hp = general.InitialHp ?? general.BaseHp;
        _skillBindingIndex.Invalidate(boss.Seat);
        foreach (var id in EnabledContentSkillIds(boss)) RegisterTaggedConversionSkill(boss, id);
        // Record the new form before cleanup raises any further rule facts.
        AdvanceEventRulesAndQueueFact(new BossActivityStageStartedEvent(1, boss.Seat, general.Id));
        _pendingDecision = null;
        _status = EngineStatus.Running;
        CancelHulaoRuntimeFrames();
        _pendingCardsMovedBatches.Clear();
        _pendingHpChanges.Clear();
        _pendingCharacterStateChanges.Clear();
        MoveAllCards(CardLocation.Processing, CardLocation.DiscardPile,
            new CardMoveReason("activity.hulao.interrupted-processing"));
        CloseCurrentPlayPhaseSkillGrants("hulao-transformation");
        CancelScheduledProgramPhase();
        ClearActualDiscardRecoveryPhase();
        ExpireActualTurnEndEffects(previous);
        _phase = TurnPhase.Finished;
        AdvanceEventRulesAndQueueFact(new HulaoTurnInterruptedEvent(_turnNumber, previous.Seat, interruption.Cause));
        AdvanceEventRulesAndQueueFact(new TurnEndedEvent(_turnNumber, previous.Seat));

        // The 2021 channel guide explicitly uses interruption to retain a large
        // hand. Preserve existing zones; do not import the older 2018 redeal.
        var firstAlly = Enumerable.Range(0, _playerCount).Select(i => (Math.Max(0, _options.HumanSeat) + i) % _playerCount)
            .First(seat => seat != BossActivitySeat && _players[seat].IsAlive);
        _currentSeat = interruption.Cause == HulaoTransformationCause.FirstShuffle ? BossActivitySeat : firstAlly;
        _turnProgression = new(ActualTurnKind.Normal, _currentSeat, _turnNumber);
        _phase = TurnPhase.NotStarted;
        AddLog("HulaoTransformed", $"神吕布化为【{general.Name}】，当前回合中断，第二阶段开始。", boss.Seat);
        AdvanceRulesAndPublishState();
    }

    private void CancelHulaoRuntimeFrames()
    {
        // Cancellation deliberately has no ordinary child return: the entire
        // actual-turn subtree is retired. Paid physical cards remain paid and
        // are moved from Processing by the owning activity transition.
        while (_resolutionStack.LastOrDefault() is { } top)
        {
            _resolutionStack.CompleteTop(top.Id, top.Kind);
            AdvanceEventRulesAndQueueFact(new HulaoResolutionCancelledEvent(top.Id, top.Kind, _turnNumber));
        }
    }
}
