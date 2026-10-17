namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int? _winnerSeat;
    private WenheRuleKind? CurrentWenheRule => _modeDefinition.WenheActivity is null ? null :
        ProgramEventHistory<WenheRoundRuleChangedEvent>().LastOrDefault()?.Rule;
    private WenheRoundRuleSnapshot? GetWenheRuleSnapshot() => _modeDefinition.WenheActivity is null ? null :
        ProgramEventHistory<WenheRoundRuleChangedEvent>().LastOrDefault() is { } fact ? new(fact.RoundNumber, fact.Rule) : null;
    private int? WenheSurvivalRank(int seat) => !IsSurvivalMode ? null :
        ProgramEventHistory<WenhePlayerRankedEvent>().LastOrDefault(e => e.Seat == seat)?.Rank;
    private bool WenheChampionMarkUsed(int seat) => ProgramEventHistory<WenheChampionMarkUsedEvent>().Any(e => e.Seat == seat);
    private int WenheGoalRank(int seat) => seat == _options.HumanSeat ? _options.WenheAspirationRank ?? 1 : 1;
    private WenheAspirationSnapshot? GetWenheAspirationSnapshot(int viewer) =>
        _modeDefinition.WenheActivity is { EnableAspirations: true } && IsValidPlayerSeat(viewer)
            ? new(WenheGoalRank(viewer), _players[viewer].IsAlive && WenheGoalRank(viewer) == 1 && !WenheChampionMarkUsed(viewer), WenheChampionMarkUsed(viewer)) : null;

    private bool IsWenhePlayerSkillVisible(CharacterState player, GeneralSkillDefinition skill, int viewer, bool revealAll)
    {
        if (_modeDefinition.WenheActivity is null || skill.ContentId != "wenhe:champion-mark" || revealAll ||
            WenheChampionMarkUsed(player.Seat) || viewer == player.Seat && WenheGoalRank(player.Seat) == 1) return true;
        // A separately acquired/printed same-name skill remains public. Only
        // the unused mode mark is private; its grant is not a public marker.
        return EnabledRuntimeSkillGrants(player).Any(grant => grant.SkillId == skill.ContentId && !grant.IsModeRule);
    }

    private void GrantWenheModeRules(CharacterState player)
    {
        var a = _modeDefinition.WenheActivity!;
        foreach (var id in new[] { "wenhe:rules", "wenhe:chaos-resolution" }
            .Concat(a.PlayerTier >= 5 ? new[] { "wenhe:niepan" } : [])
            .Concat(a.EnableAspirations ? new[] { "wenhe:champion-mark" } : []))
        {
            var grantId = $"mode:wenhe:{player.Seat}:{id}";
            player.SkillGrants.Grant(new(grantId, id, grantId, "mode:wenhe") { IsModeRule = true });
        }
    }

    private void InitializeWenheHealth(CharacterState player)
    {
        var tier = _modeDefinition.WenheActivity!.PlayerTier;
        var bonus = (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0);
        player.MaxHp = player.General.BaseHp + bonus;
        player.Hp = (player.General.InitialHp ?? player.General.BaseHp) + bonus;
    }
    private void SetupWenhePlayers()
    {
        if (_modeDefinition.WenheActivity is null) throw new InvalidOperationException("Survival requires an explicit activity definition.");
        var generals = _generalPool.ToList();
        _random.Shuffle(generals);
        if (generals.Count < _playerCount) throw new InvalidOperationException("Wenhe needs eight eligible generals.");
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var p = new CharacterState { Seat = seat, Name = seat == _options.HumanSeat ? "你" : $"人机对手 {seat + 1}",
                IsHuman = seat == _options.HumanSeat, Role = Role.Contestant, RoleRevealed = true,
                General = _options.UseInteractiveSetup ? CreateHiddenGeneral() : generals[seat],
                GeneralSelected = !_options.UseInteractiveSetup, GeneralRevealed = !_options.UseInteractiveSetup, Hp = 4, MaxHp = 4 };
            _players.Add(p);
            GrantWenheModeRules(p);
            if (!_options.UseInteractiveSetup) InitializeWenheHealth(p);
        }
        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(generals);
            _selectionOrder.AddRange(_players.OrderByDescending(p => p.IsHuman).ThenBy(p => p.Seat).Select(p => p.Seat));
        }
    }
    private IEnumerable<RuleQueryContribution> WenheActivityRuleContributions(CharacterState p, SkillRuleQuery query)
    {
        if (_modeDefinition.WenheActivity is not { } a) yield break;
        var amount = query switch { SkillRuleQuery.InitialHandSize => a.PlayerTier >= 3 ? 1 : 0,
            SkillRuleQuery.DrawCount => a.PlayerTier >= 4 ? 1 : 0,
            SkillRuleQuery.SlashLimit => a.PlayerTier >= 3 ? 1 : 0, _ => 0 };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"wenhe:tier:{query}", SkillRuleOperation.Add, amount);
    }
    private IReadOnlyList<Card> PrepareWenheActivityDeck(IReadOnlyList<Card> cards) => _modeDefinition.WenheActivity is null ? cards :
        Array.AsReadOnly(cards.Where(c => c.Kind != CardKind.Indulgence).Select(c => c with { Kind = c.Kind switch
        {
            CardKind.PeachGarden or CardKind.WoodenOx => CardKind.StarShift,
            CardKind.Nullification => c.Suit is Suit.Spade or Suit.Club ? CardKind.BeamSwap : CardKind.HandRedistribution,
            CardKind.SupplyShortage => CardKind.WenheChaos, _ => c.Kind
        } }).ToArray());

    private void RecordWenheDeathRank(long deathId, int seat)
    {
        if (WenheSurvivalRank(seat) is not null) throw new InvalidOperationException("A Wenhe death cannot award its rank twice.");
        AdvanceEventRulesAndQueueFact(new WenhePlayerRankedEvent(deathId, seat, _players.Count(p => p.IsAlive) + 1));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 41000)]
    private bool TryCompleteWenheSurvival()
    {
        if (!IsSurvivalMode || !_setupComplete || !_gameStartingProgramsResolved || _winner != Winner.None ||
            _resolutionStack.Count != 0 ||
            _pendingDecision is { Kind: not DecisionKind.PlayCard }) return false;
        var living = _players.Where(p => p.IsAlive).ToArray();
        if (living.Length > 1) return false;
        // A paid departure may still own a native health/movement boundary even
        // after its last card frame retired. Settle that work before selecting
        // the survivor; in particular a public Poison can kill the last actor.
        if (_pendingCardsMovedBatches.Count != 0 || _pendingHpChanges.Count != 0)
        {
            ClearPendingDecision();
            AdvanceRulesAndPublishState();
            if (_resolutionStack.Count != 0 || _pendingCardsMovedBatches.Count != 0 || _pendingHpChanges.Count != 0) return true;
            living = _players.Where(p => p.IsAlive).ToArray();
            if (living.Length > 1) return true;
        }
        _winnerSeat = living.SingleOrDefault()?.Seat;
        _winner = _winnerSeat is null ? Winner.Draw : Winner.SoleSurvivor;
        if (_winnerSeat is { } winner && WenheSurvivalRank(winner) is null)
            AdvanceEventRulesAndQueueFact(new WenhePlayerRankedEvent(0, winner, 1));
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner) { WinnerSeat = _winnerSeat });
        CompleteGame();
        return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 41000)]
    private void ObserveWenheRound(IGameEvent payload)
    {
        if (_modeDefinition.WenheActivity is null || payload is not RoundStartedEvent round) return;
        var rule = round.RoundNumber == 1 ? WenheRuleKind.Chaos : (WenheRuleKind)_random.Next(7);
        AdvanceEventRulesAndQueueFact(new WenheRoundRuleChangedEvent(round.RoundNumber, rule));
        AddLog("WenheRule", $"第{round.RoundNumber}轮规则：{WenheRuleName(rule)}。", round.ActorSeat);
    }
    private static string WenheRuleName(WenheRuleKind rule) => rule switch { WenheRuleKind.Chaos => "乱武", WenheRuleKind.Bounty => "重赏",
        WenheRuleKind.LastStand => "破釜沉舟", WenheRuleKind.Equipment => "横刀跃马", WenheRuleKind.Sweep => "横扫千军",
        WenheRuleKind.Famine => "饿莩载道", WenheRuleKind.Poison => "宴安鸩毒", _ => throw new ArgumentOutOfRangeException(nameof(rule)) };
}
