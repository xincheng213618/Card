namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly string[] DragonBoatTeams = ["team:wei", "team:shu", "team:wu", "team:qun"];
    private bool IsDragonBoatActivity => _modeDefinition.DragonBoatActivity is not null;
    private CharacterState DragonBoatTeammate(int seat) => _players.Single(p => p.Seat != seat && p.TeamId == _players[seat].TeamId);
    private int DragonBoatTeamScore(string teamId) => ProgramEventHistory<DragonBoatTreasureChangedEvent>()
        .LastOrDefault(e => e.TeamId == teamId)?.Current ?? 0;
    private string DragonBoatFamily(GeneralDefinition general) => _contentRegistry.Generals.GetValueOrDefault(general.Id)?.CharacterId ?? general.Id;

    private IReadOnlyList<DragonBoatTeamSnapshot>? GetDragonBoatStandings(bool finalizing = false)
    {
        if (!IsDragonBoatActivity) return null;
        var completed = ProgramEventHistory<DragonBoatTeamRankedEvent>().ToArray();
        if (completed.Length != 0) return Array.AsReadOnly(completed.OrderBy(e => e.Rank)
            .Select(e => new DragonBoatTeamSnapshot(e.TeamId, e.Treasures, e.Rank, e.ReachedSequence, e.TangZiVictory)).ToArray());
        var rows = DragonBoatTeams.Select(team =>
        {
            var fact = ProgramEventHistory<DragonBoatTreasureChangedEvent>().LastOrDefault(e => e.TeamId == team);
            var score = fact?.Current ?? 0;
            var priority = score == 12 && _players.Any(p => p.TeamId == team && p.IsAlive && HasActivitySkill(p, "dragonboat:xingzhao"));
            return new DragonBoatTeamSnapshot(team, score, 0, fact?.ReachedSequence ?? 0, priority);
        }).OrderByDescending(e => finalizing && e.TangZiVictory).ThenByDescending(e => e.Treasures)
            .ThenBy(e => e.ReachedSequence).ThenBy(e => Array.IndexOf(DragonBoatTeams, e.TeamId)).ToArray();
        return Array.AsReadOnly(rows.Select((row, index) => row with { Rank = index + 1 }).ToArray());
    }

    private DragonBoatDraftSnapshot? GetDragonBoatDraft(int viewer)
    {
        if (!IsDragonBoatActivity || _setupComplete || !IsValidPlayerSeat(viewer)) return null;
        var teammate = DragonBoatTeammate(viewer);
        return new(teammate.Seat) { GeneralIds = teammate.GeneralSelected ? [teammate.General.Id] :
            teammate.GeneralDraftCandidates?.Select(g => g.Id).ToArray() ?? [] };
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 40000)]
    private void AwardDragonBoatTreasure(IGameEvent payload)
    {
        if (!IsDragonBoatActivity || payload is not DamageAppliedEvent { SourceLess: false, Amount: > 0 } damage ||
            !IsValidPlayerSeat(damage.SourceSeat) || !IsValidPlayerSeat(damage.TargetSeat) ||
            _players[damage.SourceSeat].TeamId is not { } team || team == _players[damage.TargetSeat].TeamId) return;
        var before = DragonBoatTeamScore(team);
        var after = Math.Min(12, before + damage.Amount);
        if (after == before) return;
        var order = ProgramEventHistory<DragonBoatTreasureChangedEvent>().LongCount() + 1;
        AdvanceEventRulesAndQueueFact(new DragonBoatTreasureChangedEvent(team, before, after, damage.SourceSeat, damage.TargetSeat, order));
        AddLog("DragonBoatTreasure", $"{GetFactionName(team[5..])}队获得{after - before}枚龙船至宝，现有{after}/12。", damage.SourceSeat, damage.TargetSeat);
    }

    private bool TryCompleteDragonBoatRace()
    {
        if (!IsDragonBoatActivity || _winner != Winner.None || _resolutionStack.Count != 0 ||
            !ProgramEventHistory<RoundEndedEvent>().Any(e => e.RoundNumber == _modeDefinition.DragonBoatActivity!.Rounds)) return false;
        var ranks = GetDragonBoatStandings(finalizing: true)!;
        foreach (var rank in ranks) AdvanceEventRulesAndQueueFact(new DragonBoatTeamRankedEvent(
            rank.TeamId, rank.Rank, rank.Treasures, rank.ReachedSequence, rank.TangZiVictory));
        _winnerTeamId = ranks[0].TeamId;
        _winner = Winner.RankedTeams;
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId));
        CompleteGame();
        return true;
    }

    private void InitializeDragonBoatHealth(CharacterState player)
    {
        var tier = _modeDefinition.DragonBoatActivity!.PlayerTier;
        var bonus = (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0);
        player.MaxHp = player.General.BaseHp + bonus;
        player.Hp = (player.General.InitialHp ?? player.General.BaseHp) + bonus;
    }

    private IEnumerable<RuleQueryContribution> DragonBoatRuleContributions(CharacterState p, SkillRuleQuery query)
    {
        if (_modeDefinition.DragonBoatActivity is not { } a) yield break;
        var amount = query switch { SkillRuleQuery.InitialHandSize => a.PlayerTier >= 3 ? 1 : 0,
            SkillRuleQuery.DrawCount => a.PlayerTier >= 4 ? 1 : 0, SkillRuleQuery.SlashLimit => a.PlayerTier >= 3 ? 1 : 0, _ => 0 };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"dragonboat:tier:{query}", SkillRuleOperation.Add, amount);
    }

    private IReadOnlyList<Card> PrepareDragonBoatDeck(IReadOnlyList<Card> cards) => !IsDragonBoatActivity ? cards :
        Array.AsReadOnly(cards.Where(c => c.Kind is not (CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning))
            .Select(c => c with { Kind = c.Kind switch
            {
                CardKind.Peach => CardKind.Zong, CardKind.Alcohol => CardKind.RealgarWine,
                CardKind.DrawTwo => CardKind.SameBoat, CardKind.FiveGrains or CardKind.PeachGarden => CardKind.StriveUpstream,
                CardKind.BarbarianAssault or CardKind.ArrowBarrage => CardKind.AgainstCurrent, _ => c.Kind
            } }).ToArray());

    private void SetupDragonBoatPlayers()
    {
        var offset = _options.HumanSeat >= 0 && _options.HumanTeamId is { } requested
            ? (Array.IndexOf(DragonBoatTeams, requested) - _options.HumanSeat % 4 + 4) % 4 : 0;
        for (var seat = 0; seat < 8; seat++)
        {
            var p = new CharacterState { Seat = seat, Name = seat == _options.HumanSeat ? "你" : $"人机 {seat + 1}",
                IsHuman = seat == _options.HumanSeat, TeamId = DragonBoatTeams[(seat + offset) % 4],
                TeamRevealed = true, Role = Role.Contestant, RoleRevealed = true, General = CreateHiddenGeneral(),
                GeneralSelected = false, GeneralRevealed = false, Hp = 4, MaxHp = 4 };
            foreach (var id in new[] { "dragonboat:aiye" }.Concat(_modeDefinition.DragonBoatActivity!.PlayerTier >= 5 ? ["dragonboat:niepan"] : Array.Empty<string>()))
            {
                var grant = $"mode:dragonboat:{seat}:{id}";
                p.SkillGrants.Grant(new(grant, id, grant, "mode:dragonboat") { IsModeRule = true });
            }
            _players.Add(p);
        }
        var matches = _random.Next(2) == 0
            ? new[] { "tang-zi", "huang-quan", "su-fei", "liu-qi" }
            : new[] { "huang-quan", "liu-qi", "tang-zi", "su-fei" };
        var reserved = new Dictionary<int, GeneralDefinition>();
        for (var index = 0; index < 4; index++)
        {
            var seats = _players.Where(p => p.TeamId == DragonBoatTeams[index]).ToArray();
            reserved[seats[_random.Next(2)].Seat] = _generalPool.Single(g => g.Id == $"dragonboat:{matches[index]}");
        }
        var used = reserved.Values.Select(DragonBoatFamily).ToHashSet(StringComparer.Ordinal);
        foreach (var p in _players)
        {
            var offer = new List<GeneralDefinition>();
            if (reserved.TryGetValue(p.Seat, out var special)) offer.Add(special);
            var pool = _modeDefinition.DragonBoatActivity!.PlayerPoolIds[p.TeamId!];
            var ordinary = _generalPool.Where(g => pool.Contains(g.Id) && !g.Id.StartsWith("dragonboat:", StringComparison.Ordinal))
                .GroupBy(DragonBoatFamily, StringComparer.Ordinal).Where(g => !used.Contains(g.Key)).ToList();
            while (offer.Count < 6 && ordinary.Count > 0)
            {
                var at = _random.Next(ordinary.Count); var family = ordinary[at].ToArray(); ordinary.RemoveAt(at);
                var g = family[_random.Next(family.Length)]; offer.Add(g); used.Add(DragonBoatFamily(g));
            }
            if (offer.Count != 6) throw new InvalidOperationException("Dragon boat needs six distinct unclaimed candidate families per seat.");
            _random.Shuffle(offer); p.GeneralDraftCandidates = offer.ToArray();
        }
        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(_generalPool);
            _selectionOrder.AddRange(_players.OrderByDescending(p => p.IsHuman).ThenBy(p => p.Seat).Select(p => p.Seat));
        }
        else foreach (var p in _players)
        {
            p.General = p.GeneralDraftCandidates![_random.Next(6)]; p.GeneralDraftCandidates = null;
            p.GeneralSelected = true; p.GeneralRevealed = true; InitializeDragonBoatHealth(p);
        }
    }

    private IReadOnlyList<(GeneralDefinition General, string CandidateId)> GetDragonBoatOffers(CharacterState player) =>
        (player.GeneralDraftCandidates ?? []).Select(g => (g, g.Id)).ToArray();
}
