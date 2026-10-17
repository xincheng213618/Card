namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int HellAuxiliarySeat => (Math.Max(0, _options.HumanSeat) + 4) % _playerCount;
    private bool IsHellNpc(CharacterState player) => _modeDefinition.HellActivity is not null &&
        player.TeamId == _modeDefinition.BossActivity!.EnemyTeamId;
    private bool HasHellRemenImmunity(CharacterState player, CardKind kind) => player.IsAlive && !HasArmor(player) &&
        HasCardPolicy(player, SkillProgramCardPolicyKind.HellUnarmoredRemen, kind);
    private bool CanPlanHellDiscard(CardLocation destination) => destination == CardLocation.DiscardPile &&
        HasHellShiyouCapability && _players.Any(player => player.IsAlive && !player.IsHuman &&
            HasCardPolicy(player, SkillProgramCardPolicyKind.HellShiyouIntercept)) &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Discard };
    private bool IsExactHellPreparationTargetContext(int ownerSeat, ProgramSkillWindowContext? context) =>
        context is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, TargetSeat: { } target } &&
        target == _currentSeat && context.SourceSeat == target && context.OwnerSeat == ownerSeat && _players[target].IsAlive &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(window => window.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat == target && parent.Window == context.Window && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        parent.Candidates[parent.CandidateIndex].OwnerSeat == ownerSeat &&
        GetProgramTrigger(parent.Candidates[parent.CandidateIndex]).Effects.Any(effect => effect.Op == SkillProgramEffectOp.UseHellPreparationAlcohol);
    private void RecordHellInterceptedMovement(HellShiyouTransferPlan plan, CardMovementRecord move, Action<CardMovementRecord>? beforeFact)
    {
        if (plan.Redirects.Any(r => r.CardId == move.CardId))
            CompleteHellShiyouDiscardTransfers(plan with { Redirects = Array.AsReadOnly(plan.Redirects.Where(r => r.CardId == move.CardId).ToArray()) }, [move]);
        beforeFact?.Invoke(move);
    }

    private void SetupHellActivityPlayers()
    {
        var activity = _modeDefinition.BossActivity!;
        var hell = _modeDefinition.HellActivity!;
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var npc = seat == BossActivitySeat || seat == HellAuxiliarySeat;
            var auxiliary = seat == HellAuxiliarySeat;
            var team = npc ? activity.EnemyTeamId : activity.PlayerTeamId;
            var player = new CharacterState
            {
                Seat = seat, Name = seat == _options.HumanSeat ? "你" : auxiliary ? "孟婆" : npc ? "十殿阎罗" : $"人机搭档 {seat + 1} · 5阶",
                IsHuman = seat == _options.HumanSeat, Role = GetTeamRole(team), TeamId = team,
                TeamRevealed = true, RoleRevealed = true,
                General = auxiliary ? _generalPool.Single(g => g.Id == hell.AuxiliaryGeneralId) : CreateHiddenGeneral(),
                GeneralSelected = auxiliary, GeneralRevealed = false, IsAlive = !auxiliary, Hp = auxiliary ? 0 : 4, MaxHp = 4
            };
            if (auxiliary) { InitializeHellActivityHealth(player); player.Hp = 0; }
            _players.Add(player);
        }
        _availableGenerals.AddRange(_generalPool.Where(g => g.Id != hell.AuxiliaryGeneralId));
        _random.Shuffle(_availableGenerals);
        if (_options.UseInteractiveSetup)
            _selectionOrder.AddRange(_players.Where(p => p.Seat != HellAuxiliarySeat).OrderBy(p => p.IsHuman ? 0 : 1).ThenBy(p => p.Seat).Select(p => p.Seat));
        else foreach (var player in _players.Where(p => p.Seat != HellAuxiliarySeat))
        {
            var general = _availableGenerals.First(g => BossActivitySeatPool(player.Seat).Contains(g.Id, StringComparer.Ordinal));
            _availableGenerals.Remove(general);
            player.General = BossActivityGeneral(player, general);
            player.GeneralSelected = player.GeneralRevealed = true;
            InitializeHellActivityHealth(player);
        }
    }

    private void InitializeHellActivityHealth(CharacterState player)
    {
        var bonus = IsHellNpc(player) ? _modeDefinition.HellActivity!.BossTier switch { >= 5 => 2, >= 3 => 1, _ => 0 } : 2;
        player.MaxHp = player.General.BaseHp + bonus;
        player.Hp = (player.General.InitialHp ?? player.General.BaseHp) + bonus;
    }

    private IEnumerable<RuleQueryContribution> HellActivityRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        var tier = IsHellNpc(player) ? _modeDefinition.HellActivity!.BossTier : 5;
        var amount = !player.IsAlive ? query == SkillRuleQuery.InitialHandSize ? -_initialHandSize : 0 : query switch
        {
            SkillRuleQuery.InitialHandSize => tier >= 5 || !IsHellNpc(player) && tier >= 4 ? 2 : tier >= (IsHellNpc(player) ? 3 : 2) ? 1 : 0,
            SkillRuleQuery.DrawCount => tier >= 4 ? 1 : 0,
            SkillRuleQuery.SlashLimit => tier >= 3 ? 1 : 0,
            SkillRuleQuery.HandLimit => IsHellNpc(player) && tier >= 4 ? 1 : 0,
            _ => 0
        };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"activity:{_modeDefinition.Id}:tier-{query}", SkillRuleOperation.Add, amount);
    }

    private IReadOnlyList<Card> PrepareHellActivityDeck(IReadOnlyList<Card> cards) => _modeDefinition.HellActivity is null
        ? cards : Array.AsReadOnly(cards.Where(card => card.Kind != CardKind.WoodenOx).ToArray());

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 29)]
    private bool TryAdvanceHellActivity()
    {
        if (_modeDefinition.HellActivity is not { } hell || !_setupComplete || _winner != Winner.None ||
            _resolutionStack.Count != 0 || _pendingDecision is { Kind: not DecisionKind.PlayCard }) return false;
        if (!ProgramEventHistory<HellAuxiliaryScheduledEvent>().Any())
        {
            var scheduled = _random.Next(3);
            AdvanceEventRulesAndQueueFact(new HellAuxiliaryScheduledEvent(scheduled, HellAuxiliarySeat));
            RecordHellAppearance(_players[BossActivitySeat], 0, completed: true);
            if (scheduled == 0) ActivateHellAuxiliary(0, initial: true);
        }
        if (_gameStartingProgramsResolved && TryBeginHellAppearance()) return true;
        if (_players[BossActivitySeat].IsAlive) return false;
        var activity = _modeDefinition.BossActivity!;
        if (BossActivityStageIndex == 3)
        {
            _pendingDecision = null;
            _winnerTeamId = activity.PlayerTeamId;
            _winner = GetWinnerForTeam(_winnerTeamId);
            AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null));
            AdvanceRulesAndPublishState();
            return true;
        }
        if (_turnProgression.OwnerSeat == BossActivitySeat && _phase is not (TurnPhase.NotStarted or TurnPhase.Finished))
        { EndTurn(); return true; }
        var stageIndex = BossActivityStageIndex + 1;
        var pool = activity.Stages[stageIndex].GeneralPoolIds;
        var hidden = stageIndex == 3 && _turnNumber <= 50 &&
            ProgramEventHistory<PlayerDiedEvent>().Any(e => e.VictimSeat == HellAuxiliarySeat);
        var id = stageIndex == 3 ? hidden ? hell.HiddenGeneralId : pool.Single(g => g != hell.HiddenGeneralId) : pool[_random.Next(pool.Count)];
        var previous = _players[BossActivitySeat];
        var boss = new CharacterState
        {
            Seat = previous.Seat, Name = previous.Name, Role = previous.Role, TeamId = previous.TeamId,
            TeamRevealed = true, RoleRevealed = true, GeneralSelected = true, GeneralRevealed = true,
            General = _generalPool.Single(g => g.Id == id), IsHuman = false, Hp = 4, MaxHp = 4
        };
        InitializeHellActivityHealth(boss);
        _players[boss.Seat] = boss;
        if (HasPrivateGeneralLibraryCapability) boss.GeneralLibraryGenderQuery = GetPrivateGeneralLibraryGender;
        _skillBindingIndex.Invalidate(boss.Seat);
        _skillRuntimeState.ResetOwner(boss.Seat);
        foreach (var key in _programUses.Keys.Where(k => k.Seat == boss.Seat).ToArray()) _programUses.Remove(key);
        foreach (var key in _programPhaseUses.Keys.Where(k => k.Seat == boss.Seat).ToArray()) _programPhaseUses.Remove(key);
        foreach (var key in _programContributionUses.Keys.Where(k => k.SkillOwnerSeat == boss.Seat).ToArray()) _programContributionUses.Remove(key);
        foreach (var key in _programBooleanStates.Keys.Where(k => k.OwnerSeat == boss.Seat).ToArray()) _programBooleanStates.Remove(key);
        _observedSkillGrantRevisions[boss.Seat] = boss.SkillGrants.Revision;
        foreach (var skill in EnabledContentSkillIds(boss)) RegisterTaggedConversionSkill(boss, skill);
        _pendingDecision = null;
        AdvanceEventRulesAndQueueFact(new BossActivityStageStartedEvent(stageIndex, boss.Seat, id));
        DrawCards(boss, 4 + (hell.BossTier >= 5 ? 2 : hell.BossTier >= 3 ? 1 : 0), true, CardMoveReasons.InitialDeal);
        RecordHellAppearance(boss, stageIndex);
        if (ProgramEventHistory<HellAuxiliaryScheduledEvent>().Single().StageIndex == stageIndex) ActivateHellAuxiliary(stageIndex, initial: false);
        AddLog("ActivityStageStarted", $"第 {stageIndex + 1}/4 关：{boss.General.Name}登场。", boss.Seat);
        TryBeginHellAppearance();
        AdvanceRulesAndPublishState();
        return true;
    }

    private void RecordHellAppearance(CharacterState npc, int stage, bool completed = false)
    {
        var id = ++_resolutionSequence;
        AdvanceEventRulesAndQueueFact(new HellNpcAppearedEvent(id, stage, npc.Seat, npc.General.Id));
        if (completed) AdvanceEventRulesAndQueueFact(new HellAppearanceCompletedEvent(id));
    }

    private void ActivateHellAuxiliary(int stage, bool initial)
    {
        var auxiliary = _players[HellAuxiliarySeat];
        auxiliary.IsAlive = auxiliary.GeneralRevealed = true;
        InitializeHellActivityHealth(auxiliary);
        var tier = _modeDefinition.HellActivity!.BossTier;
        DrawCards(auxiliary, 4 + (tier >= 5 ? 2 : tier >= 3 ? 1 : 0), true, CardMoveReasons.InitialDeal);
        RecordHellAppearance(auxiliary, stage, completed: initial);
    }

    private bool TryBeginHellAppearance()
    {
        var completed = ProgramEventHistory<HellAppearanceCompletedEvent>().Select(e => e.AppearanceId).ToHashSet();
        foreach (var appearance in ProgramEventHistory<HellNpcAppearedEvent>().Where(e => !completed.Contains(e.AppearanceId)))
        {
            var npc = _players[appearance.Seat];
            var facts = CaptureProgramTriggerFacts(npc);
            var candidates = CollectEligibleProgramTriggerCandidates(npc, SkillProgramTriggerWindow.GameStarting, facts).ToArray();
            if (!npc.IsAlive || candidates.Length == 0)
            { AdvanceEventRulesAndQueueFact(new HellAppearanceCompletedEvent(appearance.AppearanceId)); continue; }
            _pendingDecision = null;
            PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, _currentSeat,
                SkillProgramTriggerWindow.GameStarting, candidates, ProgramLifecycleContinuation.ResumeHellAppearance,
                facts) { ParticipantFacts = new Dictionary<int, SkillProgramTriggerFacts> { [npc.Seat] = facts }, HellAppearanceId = appearance.AppearanceId });
            AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
            return true;
        }
        return false;
    }

    private bool IsExactHellAppearanceWindow(ProgramLifecycleTriggerWindowFrame window) =>
        _modeDefinition.HellActivity is not null && window.Continuation == ProgramLifecycleContinuation.ResumeHellAppearance &&
        window.Window == SkillProgramTriggerWindow.GameStarting && window.HellAppearanceId is { } id &&
        !ProgramEventHistory<HellAppearanceCompletedEvent>().Any(e => e.AppearanceId == id) &&
        ProgramEventHistory<HellNpcAppearedEvent>().SingleOrDefault(e => e.AppearanceId == id) is { } appeared &&
        _players[appeared.Seat].General.Id == appeared.GeneralId && window.Candidates.All(c => c.OwnerSeat == appeared.Seat);

    private bool IsExactHellArrivalProgram(ProgramSkillFrame frame)
    {
        if (_modeDefinition.HellActivity is null || frame.WindowContext is not { Window: SkillProgramTriggerWindow.GameStarting } context ||
            frame.TriggerId is null || _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            !(window.Continuation == ProgramLifecycleContinuation.CompleteGameStarting && _phase == TurnPhase.NotStarted || IsExactHellAppearanceWindow(window)) ||
            window.CandidateIndex >= window.Candidates.Count) return false;
        return MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]);
    }

    private int HellArrivalDamageBonus(IDamageAttempt attack) => _modeDefinition.HellActivity is { BossTier: 5 } &&
        !attack.IsChainPropagation && !attack.IsSourceLess && attack.EffectiveCardKind == CardKind.BarbarianAssault &&
        LifecycleCardUse(attack.ResolutionId)?.VirtualOrdinaryTrickOrigin is { SkillId: "hell:boss-tier" } origin &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == origin.ParentProgramFrameId) is { } parent &&
        IsExactHellArrivalProgram(parent) ? 1 : 0;
}

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? HellAppearanceId { get; init; }
}
