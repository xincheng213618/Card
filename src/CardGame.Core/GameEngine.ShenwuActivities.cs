namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Constructor draft data is deterministic from the seed. Queue its public
    // facts only after Start, when the command session can retain them.
    private sealed record ShenwuDraftSetup(IReadOnlyList<string> SharedGods, string LeaderId);
    private ShenwuDraftSetup? _shenwuDraft;
    private int ShenwuSeat(int offset) => (Math.Max(0, _options.HumanSeat) + offset) % _playerCount;
    private int ShenwuSeatOffset(int seat) => (seat - Math.Max(0, _options.HumanSeat) + _playerCount) % _playerCount;
    private IReadOnlyList<int> ShenwuPlayerSlots => _modeDefinition.ShenwuActivity!.PlayerSeatOffsets.Select(ShenwuSeat).ToArray();
    private int ShenwuLeaderSeat => ShenwuSeat(_modeDefinition.ShenwuActivity!.LeaderSeatOffset);
    private bool IsShenwuPlayerSlot(int seat) => _modeDefinition.ShenwuActivity is { } activity &&
        activity.PlayerSeatOffsets.Contains(ShenwuSeatOffset(seat));

    private void SetupShenwuPlayers()
    {
        var activity = _modeDefinition.ShenwuActivity!;
        var gods = activity.GodPoolIds.ToList();
        _random.Shuffle(gods);
        var leader = activity.LeaderPoolIds[_random.Next(activity.LeaderPoolIds.Count)];
        _shenwuDraft = new(Array.AsReadOnly(gods.Take(3).ToArray()), leader);
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var allied = IsShenwuPlayerSlot(seat);
            var team = allied ? activity.PlayerTeamId : activity.EnemyTeamId;
            _players.Add(new CharacterState
            {
                Seat = seat, Name = seat == _options.HumanSeat ? "你" : allied ? $"人机搭档 {seat + 1} · {activity.PlayerTier}阶"
                    : seat == ShenwuLeaderSeat ? "凶兽首领" : $"烛阴 {seat + 1}",
                IsHuman = seat == _options.HumanSeat, TeamId = team, Role = GetTeamRole(team),
                TeamRevealed = true, RoleRevealed = true, General = CreateHiddenGeneral(),
                GeneralSelected = false, GeneralRevealed = false,
                Hp = 4, MaxHp = 4
            });
        }
        _availableGenerals.AddRange(_generalPool);
        _random.Shuffle(_availableGenerals);
        var order = ShenwuPlayerSlots.Concat(Enumerable.Range(0, _playerCount).Where(seat => !IsShenwuPlayerSlot(seat))).ToArray();
        if (_options.UseInteractiveSetup) _selectionOrder.AddRange(order);
        else
            foreach (var seat in order)
            {
                var player = _players[seat];
                var general = GetShenwuGeneralOffers(player).First().General;
                _availableGenerals.Remove(general);
                player.General = ShenwuActivityGeneral(player, general);
                player.GeneralSelected = player.GeneralRevealed = true;
                InitializeShenwuHealth(player);
            }
    }

    private void RecordShenwuDraftFacts()
    {
        if (_shenwuDraft is not { } draft) return;
        AdvanceEventRulesAndQueueFact(new ShenwuSharedGodCandidatesEvent(draft.SharedGods));
        AdvanceEventRulesAndQueueFact(new ShenwuLeaderChosenEvent(ShenwuLeaderSeat, draft.LeaderId));
    }

    private IReadOnlyList<(GeneralDefinition General, string CandidateId)> GetShenwuGeneralOffers(CharacterState player, bool fullRoster = false)
    {
        var activity = _modeDefinition.ShenwuActivity!;
        if (!IsShenwuPlayerSlot(player.Seat))
        {
            var id = player.Seat == ShenwuLeaderSeat
                ? _shenwuDraft!.LeaderId
                : activity.MinionGeneralIds[activity.MinionSeatOffsets.ToList().IndexOf(ShenwuSeatOffset(player.Seat))];
            return _availableGenerals.Where(general => general.Id == id).Select(general => (general, general.Id)).ToArray();
        }
        var ordinary = _availableGenerals.Where(general => activity.PlayerPoolIds.Contains(general.Id, StringComparer.Ordinal));
        var representatives = ordinary.DistinctBy(general => _contentRegistry.Generals[general.Id].CharacterId ?? general.Id,
            StringComparer.Ordinal).Take(fullRoster ? int.MaxValue : 5).ToArray();
        var offers = representatives.SelectMany(representative => ordinary.Where(general =>
                (_contentRegistry.Generals[general.Id].CharacterId ?? general.Id) ==
                (_contentRegistry.Generals[representative.Id].CharacterId ?? representative.Id)),
            (representative, general) => (General: general, CandidateId: representative.Id)).ToList();
        var shared = _shenwuDraft!.SharedGods;
        offers.AddRange(_availableGenerals.Where(general => shared.Contains(general.Id, StringComparer.Ordinal))
            .Select(general => (general, general.Id)));
        return offers;
    }

    private PromptChoice WithShenwuGeneralChoice(PromptChoice choice, GeneralDefinition general, string candidateId)
    {
        if (_modeDefinition.ShenwuActivity is not { } activity) return choice;
        var god = activity.GodPoolIds.Contains(general.Id, StringComparer.Ordinal);
        var healthBonus = (activity.PlayerTier >= 2 ? 1 : 0) + (activity.PlayerTier >= 5 ? 1 : 0);
        return choice with
        {
            Parameters = new Dictionary<string, string>(choice.Parameters, StringComparer.Ordinal)
            {
                ["candidate-general-id"] = candidateId,
                ["shenwu-shared-god"] = god ? "true" : "false",
                ["health-preview"] = $"本地{(activity.PlayerTier == 5 ? "五" : activity.PlayerTier.ToString(System.Globalization.CultureInfo.InvariantCulture))}阶：初始体力 {(general.InitialHp ?? general.BaseHp) + healthBonus}/{general.BaseHp + healthBonus}。" +
                    (god ? "此神将来自三人共享候选；选择后其他队友不能再选。" : "普通候选仅对你可见。")
            }
        };
    }

    private GeneralDefinition ShenwuActivityGeneral(CharacterState player, GeneralDefinition general) =>
        _modeDefinition.ShenwuActivity is { } activity && IsShenwuPlayerSlot(player.Seat)
            ? general with { Skills = general.Skills.Concat(activity.PlayerSkillIds.Select(id =>
                ToRuntimeSkillDefinition(_contentRegistry.GetSkill(id)))).ToArray() } : general;

    private void InitializeShenwuHealth(CharacterState player)
    {
        var tier = _modeDefinition.ShenwuActivity!.PlayerTier;
        var bonus = IsShenwuPlayerSlot(player.Seat) ? (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0) : 0;
        player.MaxHp = checked(player.General.BaseHp + bonus);
        player.Hp = checked((player.General.InitialHp ?? player.General.BaseHp) + bonus);
    }

    private IEnumerable<RuleQueryContribution> ShenwuActivityRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (_modeDefinition.ShenwuActivity is not { } activity) yield break;
        var tier = activity.PlayerTier;
        var amount = IsShenwuPlayerSlot(player.Seat) ? query switch
        {
            SkillRuleQuery.InitialHandSize => tier >= 3 ? 1 : 0,
            SkillRuleQuery.DrawCount => tier >= 4 ? 1 : 0,
            SkillRuleQuery.SlashLimit => tier >= 3 ? 1 : 0,
            _ => 0
        } : query == SkillRuleQuery.InitialHandSize && player.Seat == ShenwuLeaderSeat ? 8 - _initialHandSize : 0;
        if (amount != 0) yield return new FiniteRuleQueryContribution($"activity:shenwu:tier-{query}", SkillRuleOperation.Add, amount);
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27700)]
    private void GrantShenwuHalfHpSkill(IGameEvent fact)
    {
        if (_modeDefinition.ShenwuActivity is not { } activity || !_setupComplete ||
            fact is not (DamageAppliedEvent or ProgramSkillHpLostEvent or MaximumHpChangedEvent or DamageReplacedWithHpLossEvent)) return;
        var leader = _players[ShenwuLeaderSeat];
        if (!leader.IsAlive || !leader.GeneralSelected || leader.Hp * 2 > leader.MaxHp ||
            CompleteProgramEventHistory().OfType<ShenwuLeaderEnragedEvent>().Any()) return;
        var skill = activity.HalfHpSkillIds[leader.General.Id];
        var grant = $"activity:shenwu:half-health:{leader.Seat}:{skill}";
        leader.SkillGrants.Grant(new(grant, skill, grant, grant));
        _skillBindingIndex.Invalidate(leader.Seat);
        AdvanceEventRulesAndQueueFact(new ShenwuLeaderEnragedEvent(leader.Seat, skill, leader.Hp, leader.MaxHp));
        AddLog("ShenwuEnraged", $"{leader.General.Name}首次降至半血，获得【{_contentRegistry.GetSkill(skill).Name}】。", leader.Seat);
    }

    private string? EvaluateShenwuWinner() => _players.Any(player => IsShenwuPlayerSlot(player.Seat) && player.IsAlive)
        ? null : _modeDefinition.ShenwuActivity!.EnemyTeamId;

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 32)]
    private bool TryCompleteShenwuEncounter()
    {
        if (_modeDefinition.ShenwuActivity is not { } activity || !_setupComplete || _winner != Winner.None ||
            _players[ShenwuLeaderSeat].IsAlive || _resolutionStack.Count != 0 ||
            _pendingDecision is { Kind: not DecisionKind.PlayCard }) return false;
        _pendingDecision = null;
        _winnerTeamId = _players.Any(player => IsShenwuPlayerSlot(player.Seat) && player.IsAlive)
            ? activity.PlayerTeamId : activity.EnemyTeamId;
        _winner = GetWinnerForTeam(_winnerTeamId);
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null));
        AdvanceRulesAndPublishState();
        return true;
    }
}
