namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int BossActivitySeat => (Math.Max(0, _options.HumanSeat) + 3) % _playerCount;
    private int BossActivityStageIndex => CompleteProgramEventHistory().OfType<BossActivityStageStartedEvent>()
        .Select(e => e.StageIndex).DefaultIfEmpty(0).Last();
    private IReadOnlyList<string> BossActivitySeatPool(int seat) => _modeDefinition.HellActivity is { } hell && seat == HellAuxiliarySeat ? [hell.AuxiliaryGeneralId] : seat == BossActivitySeat
        ? _modeDefinition.NianActivity is { } nian ? [nian.FormGeneralIds[0]] : _modeDefinition.BossActivity!.Stages[0].GeneralPoolIds
        : _modeDefinition.BossActivity!.PlayerPoolIds;

    private GeneralDefinition BossActivityGeneral(CharacterState player, GeneralDefinition general) =>
        _modeDefinition.BossActivity is { } a && player.TeamId == a.PlayerTeamId
            ? general with { Skills = general.Skills.Concat(a.PlayerSkillIds.Select(id =>
                ToRuntimeSkillDefinition(_contentRegistry.GetSkill(id)))).ToArray() } : general;

    private void InitializeBossActivityHealth(CharacterState player)
    {
        if (_modeDefinition.HellActivity is not null) { InitializeHellActivityHealth(player); return; }
        var tier = _modeDefinition.BossActivity!.PlayerTier;
        var bonus = player.Seat == BossActivitySeat ? 0 : (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0);
        player.MaxHp = checked(player.General.BaseHp + bonus);
        player.Hp = checked((player.General.InitialHp ?? player.General.BaseHp) + bonus);
    }

    private void SetupBossActivityPlayers()
    {
        if (_modeDefinition.HellActivity is not null) { SetupHellActivityPlayers(); return; }
        var activity = _modeDefinition.BossActivity!;
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var team = seat == BossActivitySeat ? activity.EnemyTeamId : activity.PlayerTeamId;
            _players.Add(new CharacterState
            {
                Seat = seat, Name = seat == _options.HumanSeat ? "你" : seat == BossActivitySeat ?
                    _modeDefinition.HulaoActivity is not null ? "神吕布" : _modeDefinition.NianActivity is not null ? "年兽" : "鬼怪首领" : $"人机搭档 {seat + 1} · {activity.PlayerTier}阶",
                IsHuman = seat == _options.HumanSeat, Role = GetTeamRole(team), TeamId = team,
                TeamRevealed = true, RoleRevealed = true, General = CreateHiddenGeneral(),
                GeneralSelected = false, GeneralRevealed = false, Hp = 4, MaxHp = 4
            });
        }
        _availableGenerals.AddRange(_generalPool);
        _random.Shuffle(_availableGenerals);
        if (_options.UseInteractiveSetup)
            _selectionOrder.AddRange(_players.OrderBy(p => p.IsHuman ? 0 : 1).ThenBy(p => p.Seat).Select(p => p.Seat));
        else
            foreach (var player in _players)
            {
                var general = _availableGenerals.First(g => BossActivitySeatPool(player.Seat).Contains(g.Id, StringComparer.Ordinal));
                _availableGenerals.Remove(general);
                player.General = BossActivityGeneral(player, general);
                player.GeneralSelected = player.GeneralRevealed = true;
                InitializeBossActivityHealth(player);
            }
    }

    private IEnumerable<RuleQueryContribution> BossActivityRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (_modeDefinition.BossActivity is not { } a) yield break;
        if (_modeDefinition.HellActivity is not null)
        { foreach (var contribution in HellActivityRuleContributions(player, query)) yield return contribution; yield break; }
        var tier = a.PlayerTier;
        var amount = player.Seat == BossActivitySeat
            ? query == SkillRuleQuery.InitialHandSize ? a.Stages[BossActivityStageIndex].InitialHandSize - _initialHandSize : 0
            : query switch
            {
                SkillRuleQuery.InitialHandSize => (tier >= 3 ? 1 : 0) +
                    (_modeDefinition.HulaoActivity is not null && BossActivityStageIndex == 0 ?
                        3 + (player.Seat - Math.Max(0, _options.HumanSeat) + _playerCount) % _playerCount - _initialHandSize : 0),
                SkillRuleQuery.DrawCount => tier >= 4 ? 1 : 0,
                SkillRuleQuery.SlashLimit => tier >= 3 ? 1 : 0,
                _ => 0
            };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"activity:{_modeDefinition.Id}:tier-{query}", SkillRuleOperation.Add, amount);
    }

    private string? EvaluateBossActivityWinner()
    {
        var activity = _modeDefinition.BossActivity!;
        if (!_players.Any(p => p.TeamId == activity.PlayerTeamId && p.IsAlive)) return activity.EnemyTeamId;
        // The final boss's death skills may still defeat the allied team.
        // Allied victory is decided at the same empty-stack boundary as a new stage.
        return null;
    }

    // Change encounters only after the complete killing action, death skills,
    // and every child resolution return. A pending play prompt is refreshed.
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 30)]
    private bool TryAdvanceBossActivityStage()
    {
        if (_modeDefinition.HellActivity is not null) return false;
        if (_modeDefinition.BossActivity is not { } activity || !_setupComplete || _winner != Winner.None ||
            _players[BossActivitySeat].IsAlive || _resolutionStack.Count != 0 ||
            _pendingDecision is { Kind: not DecisionKind.PlayCard }) return false;
        if (_modeDefinition.HulaoActivity is not null && BossActivityStageIndex == 0) return false;
        if (BossActivityStageIndex + 1 >= activity.Stages.Count)
        {
            _pendingDecision = null;
            _winnerTeamId = activity.PlayerTeamId;
            _winner = GetWinnerForTeam(_winnerTeamId);
            AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null));
            AdvanceRulesAndPublishState();
            return true;
        }
        if (_currentSeat == BossActivitySeat && _phase is not (TurnPhase.NotStarted or TurnPhase.Finished))
        {
            // A boss killed by a counterattack must finish its actual dead
            // turn before another character can occupy this seat.
            EndTurn();
            return true;
        }
        var stageIndex = BossActivityStageIndex + 1;
        var stage = activity.Stages[stageIndex];
        var generalId = stage.GeneralPoolIds[_random.Next(stage.GeneralPoolIds.Count)];
        var general = _generalPool.Single(g => g.Id == generalId);
        var previous = _players[BossActivitySeat];
        var boss = new CharacterState
        {
            Seat = previous.Seat, Name = previous.Name, IsHuman = false, Role = previous.Role,
            TeamId = previous.TeamId, TeamRevealed = true, RoleRevealed = true,
            General = general, GeneralSelected = true, GeneralRevealed = true,
            Hp = general.InitialHp ?? general.BaseHp, MaxHp = general.BaseHp
        };
        if (HasPrivateGeneralLibraryCapability) boss.GeneralLibraryGenderQuery = GetPrivateGeneralLibraryGender;
        _players[boss.Seat] = boss;
        _skillBindingIndex.Invalidate(boss.Seat);
        _skillRuntimeState.ResetOwner(boss.Seat);
        foreach (var key in _programUses.Keys.Where(key => key.Seat == boss.Seat).ToArray()) _programUses.Remove(key);
        foreach (var key in _programPhaseUses.Keys.Where(key => key.Seat == boss.Seat).ToArray()) _programPhaseUses.Remove(key);
        foreach (var key in _programContributionUses.Keys.Where(key => key.SkillOwnerSeat == boss.Seat).ToArray()) _programContributionUses.Remove(key);
        foreach (var key in _programBooleanStates.Keys.Where(key => key.OwnerSeat == boss.Seat).ToArray()) _programBooleanStates.Remove(key);
        _observedSkillGrantRevisions[boss.Seat] = boss.SkillGrants.Revision;
        foreach (var id in EnabledContentSkillIds(boss)) RegisterTaggedConversionSkill(boss, id);
        _pendingDecision = null;
        AdvanceEventRulesAndQueueFact(new BossActivityStageStartedEvent(stageIndex, boss.Seat, general.Id));
        DrawCards(boss, stage.InitialHandSize, log: true, CardMoveReasons.InitialDeal);
        AddLog("ActivityStageStarted", $"第 {stageIndex + 1}/{activity.Stages.Count} 关：{stage.Name}，{general.Name}登场。", boss.Seat);
        AdvanceRulesAndPublishState();
        return true;
    }
}
