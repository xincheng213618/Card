namespace CardGame.Core;

public sealed partial class GameEngine
{
    private GuanduBattleConfiguredEvent? _guanduInitialConfiguration;
    private long _guanduQualificationStamp;
    private (long Underlying, int LivingMask, int FactionHash) _guanduQualificationDependencies;
    private long CaptureGuanduQualificationStamp(long underlying)
    {
        if (!IsGuanduActivity) return underlying;
        var living = 0; var factions = new HashCode();
        foreach (var p in _players)
        {
            if (p.IsAlive) living |= 1 << p.Seat;
            factions.Add(p.General.Id, StringComparer.Ordinal);
            factions.Add(p.General.FactionId, StringComparer.Ordinal);
            factions.Add(p.ChosenFactionId, StringComparer.Ordinal);
            factions.Add(p.NativeEffectiveFactionOverrideId, StringComparer.Ordinal);
        }
        var dependencies = (underlying, living, factions.ToHashCode());
        if (_guanduQualificationStamp == 0 || dependencies != _guanduQualificationDependencies)
        { _guanduQualificationDependencies = dependencies; _guanduQualificationStamp++; }
        return long.MinValue / 4 + _guanduQualificationStamp;
    }
    private bool IsGuanduActivity => _modeDefinition.GuanduActivity is not null;
    private GuanduBattleConfiguredEvent? GuanduConfiguration => IsGuanduActivity
        ? ProgramEventHistory<GuanduBattleConfiguredEvent>().SingleOrDefault() ?? _guanduInitialConfiguration : null;
    private void RecordGuanduSetupFact()
    {
        if (_guanduInitialConfiguration is { } configuration)
            AdvanceEventRulesAndQueueFact(configuration);
    }
    private bool IsGuanduCommander(int seat) => GuanduConfiguration is { } c &&
        (c.CaoCommanderSeat == seat || c.YuanCommanderSeat == seat);
    private bool GuanduAllies(int first, int second) => IsValidPlayerSeat(first) && IsValidPlayerSeat(second) &&
        _players[first].TeamId is { } team && team == _players[second].TeamId;
    private bool IsGuanduCommanderPrintedLordGrant(CharacterState p, SkillGrant g) => IsGuanduCommander(p.Seat) &&
        g.SourceId == CharacterState.PrimarySkillSource && g.LordProjection is null && g.GeneralLibraryProjection is null &&
        p.General.Skills.Any(s => s.ContentId == g.SkillId && s.Tags.HasFlag(SkillTag.Lord));
    private GuanduActivitySnapshot? GetGuanduSnapshot()
    {
        if (GuanduConfiguration is not { } c) return null;
        var cao = c.PlayerTeamId == "team:cao";
        return new(c.Event, _roundNumber, cao ? c.CaoCommanderSeat : c.YuanCommanderSeat,
            cao ? c.YuanCommanderSeat : c.CaoCommanderSeat, ProgramEventHistory<GuanduDeckExhaustedEvent>().Any(),
            c.PlayerTeamId, c.HumanRole, c.LayoutIndex);
    }
    private void InitializeGuanduHealth(CharacterState p)
    {
        var tier = _modeDefinition.GuanduActivity!.PlayerTier;
        var bonus = (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0) + (IsGuanduCommander(p.Seat) ? 1 : 0);
        p.MaxHp = p.General.BaseHp + bonus;
        p.Hp = (p.General.InitialHp ?? p.General.BaseHp) + bonus;
    }
    private void SetupGuanduPlayers()
    {
        var a = _modeDefinition.GuanduActivity!;
        var layoutIndex = _random.Next(a.SeatLayouts.Count);
        var layout = a.SeatLayouts[layoutIndex];
        var playerTeam = _options.HumanTeamId ?? (_random.Next(2) == 0 ? "team:cao" : "team:yuan");
        var humanRole = _options.HumanGuanduRole ?? GuanduSeatRole.General;
        var yuanCommander = Enumerable.Range(1, 7).First(i => layout[i] == "team:yuan");
        var candidates = Enumerable.Range(0, 8).Where(i => layout[i] == playerTeam &&
            ((i == 0 || i == yuanCommander) == (humanRole == GuanduSeatRole.Commander))).ToArray();
        var canonicalHuman = candidates[_random.Next(candidates.Length)];
        var rotation = _options.HumanSeat >= 0 ? (_options.HumanSeat - canonicalHuman + 8) % 8 : 0;
        var configured = new GuanduBattleConfiguredEvent(a.Events[_random.Next(a.Events.Count)], layoutIndex,
            rotation, (yuanCommander + rotation) % 8, playerTeam, humanRole);
        // Construction precedes the command journal. Retain deterministic setup
        // until StartCore can publish the same scalar fact in its first command.
        _guanduInitialConfiguration = configured;
        var usedFamilies = a.Commanders.Values.Select(id => DragonBoatFamily(_generalPool.Single(g => g.Id == id)))
            .ToHashSet(StringComparer.Ordinal);
        for (var seat = 0; seat < 8; seat++)
        {
            var team = layout[(seat - rotation + 8) % 8];
            var commander = seat == configured.CaoCommanderSeat || seat == configured.YuanCommanderSeat;
            var p = new CharacterState { Seat = seat, Name = seat == _options.HumanSeat ? "你" : $"人机 {seat + 1}",
                IsHuman = seat == _options.HumanSeat, TeamId = team, TeamRevealed = true,
                Role = GetTeamRole(team), RoleRevealed = true, General = CreateHiddenGeneral(),
                GeneralSelected = false, GeneralRevealed = false, Hp = 4, MaxHp = 4 };
            var offer = new List<GeneralDefinition>();
            if (commander) offer.Add(_generalPool.Single(g => g.Id == a.Commanders[team]));
            else
            {
                var pool = _generalPool.Where(g => a.GeneralPools[team].Contains(g.Id) && !a.Commanders.Values.Contains(g.Id) &&
                    !usedFamilies.Contains(DragonBoatFamily(g))).ToList();
                // Drafts are private and independent. Only selected character families are exclusive;
                // reserving every unchosen offer would require more than the published nineteen generals.
                _random.Shuffle(pool);
                offer.AddRange(pool.Take(a.CandidateCount));
                if (offer.Count != a.CandidateCount) throw new InvalidOperationException("Guandu has too few eligible candidates.");
            }
            p.GeneralDraftCandidates = offer.ToArray();
            foreach (var id in new[] { "guandu:battle-event" }.Concat(a.PlayerTier >= 5 ? ["guandu:niepan"] : Array.Empty<string>()))
            {
                var grant = $"mode:guandu:{seat}:{id}";
                p.SkillGrants.Grant(new(grant, id, grant, "mode:guandu") { IsModeRule = true });
            }
            _players.Add(p);
        }
        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(_generalPool);
            _selectionOrder.AddRange(_players.OrderByDescending(p => p.IsHuman).ThenBy(p => p.Seat).Select(p => p.Seat));
        }
        else foreach (var p in _players.OrderByDescending(p => IsGuanduCommander(p.Seat)).ThenBy(p => p.Seat))
        {
            var offer = p.GeneralDraftCandidates!.Where(g => IsGuanduCommander(p.Seat) || !usedFamilies.Contains(DragonBoatFamily(g))).ToArray();
            if (offer.Length == 0) offer = _generalPool.Where(g => a.GeneralPools[p.TeamId!].Contains(g.Id) &&
                !usedFamilies.Contains(DragonBoatFamily(g))).ToArray();
            p.General = offer[_random.Next(offer.Length)]; usedFamilies.Add(DragonBoatFamily(p.General));
            p.GeneralDraftCandidates = null; p.GeneralSelected = true; p.GeneralRevealed = true;
            InitializeGuanduHealth(p);
        }
    }
    private IReadOnlyList<(GeneralDefinition General, string CandidateId)> GetGuanduOffers(CharacterState p)
    {
        var taken = _players.Where(q => q.GeneralSelected).Select(q => DragonBoatFamily(q.General)).ToHashSet(StringComparer.Ordinal);
        var offer = (p.GeneralDraftCandidates ?? []).Where(g => IsGuanduCommander(p.Seat) || !taken.Contains(DragonBoatFamily(g))).ToArray();
        // Replenishment happens deterministically from the published pool and does not disclose another seat's offer.
        if (!IsGuanduCommander(p.Seat) && offer.Length < _modeDefinition.GuanduActivity!.CandidateCount)
            offer = offer.Concat(_generalPool.Where(g => _modeDefinition.GuanduActivity.GeneralPools[p.TeamId!].Contains(g.Id) &&
                !_modeDefinition.GuanduActivity.Commanders.Values.Contains(g.Id) && !taken.Contains(DragonBoatFamily(g)) &&
                !offer.Any(o => DragonBoatFamily(o) == DragonBoatFamily(g))).OrderBy(g => g.Id, StringComparer.Ordinal))
                .Take(_modeDefinition.GuanduActivity.CandidateCount).ToArray();
        return offer.Select(g => (g, g.Id)).ToArray();
    }
    private IReadOnlyList<Card> PrepareGuanduDeck(IReadOnlyList<Card> cards) => !IsGuanduActivity ? cards :
        Array.AsReadOnly(cards.Where(c => c.Kind != CardKind.WoodenOx).Select(c => c with { Kind = c.Kind switch
        {
            CardKind.Alcohol => CardKind.Intoxication, CardKind.FiveGrains => CardKind.StoreGrain,
            CardKind.PeachGarden or CardKind.Lightning => CardKind.Reinforcements,
            CardKind.BarbarianAssault => CardKind.UnitedEffort, _ => c.Kind
        } }).ToArray());
    private IEnumerable<RuleQueryContribution> GuanduActivityRuleContributions(CharacterState p, SkillRuleQuery query)
    {
        if (_modeDefinition.GuanduActivity is not { } a) yield break;
        var amount = query switch { SkillRuleQuery.InitialHandSize => a.PlayerTier >= 3 ? 1 : 0,
            SkillRuleQuery.DrawCount => a.PlayerTier >= 4 ? 1 : 0, SkillRuleQuery.SlashLimit => a.PlayerTier >= 3 ? 1 : 0, _ => 0 };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"guandu:tier:{query}", SkillRuleOperation.Add, amount);
        foreach (var contribution in GuanduEventRuleContributions(p, query)) yield return contribution;
        foreach (var contribution in GuanduGeneralRuleContributions(p, query)) yield return contribution;
    }
    private void RecordGuanduDeckExhaustion()
    {
        if (!IsGuanduActivity || ProgramEventHistory<GuanduDeckExhaustedEvent>().Any()) return;
        AdvanceEventRulesAndQueueFact(new GuanduDeckExhaustedEvent(_turnNumber));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 46000)]
    private void ObserveGuanduDeckExhaustion(IGameEvent e)
    {
        if (IsGuanduActivity && _setupComplete && e is CardMovedEvent && _cardZones.Count(CardLocation.DrawPile) == 0)
            RecordGuanduDeckExhaustion();
    }
    private void ApplyGuanduDeathReward(CharacterState victim, CharacterState? killer)
    {
        if (!IsGuanduActivity || killer is not { IsAlive: true } || killer.Seat == victim.Seat) return;
        if (!GuanduAllies(victim.Seat, killer.Seat))
            MoveCards(GetHand(victim).ToArray(), CardLocation.Hand(victim.Seat), CardLocation.Hand(killer.Seat), new("guandu.enemy-kill-hand"));
        else
        {
            MoveLogicalDiscardCards(killer.Seat, killer.Seat, GetHand(killer).ToArray(), CardLocation.Hand(killer.Seat), new("guandu.friendly-kill"));
            MoveLogicalDiscardCards(killer.Seat, killer.Seat, GetEquipment(killer).ToArray(), CardLocation.Equipment(killer.Seat), new("guandu.friendly-kill"));
        }
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 46000)]
    private bool TryCompleteGuanduBattle()
    {
        if (GuanduConfiguration is not { } c || !_setupComplete || !_gameStartingProgramsResolved ||
            _winner != Winner.None || _resolutionStack.Count != 0 || _pendingDecision is { Kind: not DecisionKind.PlayCard }) return false;
        var exhausted = ProgramEventHistory<GuanduDeckExhaustedEvent>().Any();
        if (_players[c.CaoCommanderSeat].IsAlive && _players[c.YuanCommanderSeat].IsAlive && !exhausted) return false;
        if (_pendingCardsMovedBatches.Count != 0 || _pendingHpChanges.Count != 0)
        {
            ClearPendingDecision(); AdvanceRulesAndPublishState(); return true;
        }
        _winnerTeamId = exhausted ? c.PlayerTeamId == "team:cao" ? "team:yuan" : "team:cao" :
            !_players[c.CaoCommanderSeat].IsAlive && !_players[c.YuanCommanderSeat].IsAlive ? null :
            _players[c.CaoCommanderSeat].IsAlive ? "team:cao" : "team:yuan";
        _winner = _winnerTeamId is null ? Winner.Draw : GetWinnerForTeam(_winnerTeamId);
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId));
        CompleteGame(); return true;
    }
}
