namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ActivitySeatOffset(int seat) => (seat - Math.Max(0, _options.HumanSeat) + _playerCount) % _playerCount;
    private string ActivityPlayerTeam => _options.HumanTeamId ?? GetModeTeamIds()[0];
    private bool IsActivityPlayerSlot(int seat) => ActivitySeatOffset(seat) is 0 or 5;
    private bool IsActivitySpiritSlot(int seat) => ActivitySeatOffset(seat) is 2 or 3;

    private IReadOnlyList<string> ActivitySeatPool(int seat)
    {
        var activity = _modeDefinition.TeamActivity!;
        return (IsActivityPlayerSlot(seat) ? activity.PlayerPools : IsActivitySpiritSlot(seat)
            ? activity.SpiritPools : activity.MachinePools)[_players[seat].TeamId!];
    }

    private int ActivityTier(int seat)
    {
        var a = _modeDefinition.TeamActivity!;
        return IsActivityPlayerSlot(seat) ? a.PlayerTier : IsActivitySpiritSlot(seat)
            ? a.SpiritTierMinimum + (int)(((uint)_options.Seed + (uint)ActivitySeatOffset(seat)) % (uint)(a.SpiritTierMaximum - a.SpiritTierMinimum + 1))
            : a.MachineTier;
    }

    private void SetupActivityPlayers()
    {
        var otherTeam = GetModeTeamIds().Single(team => team != ActivityPlayerTeam);
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var team = ActivitySeatOffset(seat) is 0 or 4 or 5 or 6 ? ActivityPlayerTeam : otherTeam;
            _players.Add(new CharacterState
            {
                Seat = seat, Name = seat == _options.HumanSeat ? "你" :
                    $"{(IsActivityPlayerSlot(seat) ? "人机搭档" : IsActivitySpiritSlot(seat) ? "英灵" : "器械")} {seat + 1} · {ActivityTier(seat)}阶",
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
                var general = _availableGenerals.First(g => ActivitySeatPool(player.Seat).Contains(g.Id, StringComparer.Ordinal));
                _availableGenerals.Remove(general);
                player.General = ActivityGeneral(player, general);
                player.GeneralSelected = player.GeneralRevealed = true;
                InitializeActivityHealth(player);
            }
    }

    private GeneralDefinition ActivityGeneral(CharacterState player, GeneralDefinition general) =>
        _modeDefinition.TeamActivity is { } a && IsActivityPlayerSlot(player.Seat)
            ? general with { Skills = general.Skills.Concat(a.PlayerSkillIds[player.TeamId!]
                .Select(id => ToRuntimeSkillDefinition(_contentRegistry.GetSkill(id)))).ToArray() }
            : general;

    private void InitializeActivityHealth(CharacterState player)
    {
        var tier = ActivityTier(player.Seat);
        var bonus = IsActivityPlayerSlot(player.Seat) ? (tier >= 2 ? 1 : 0) + (tier >= 5 ? 1 : 0)
            : (tier >= 2 ? 1 : 0) + (tier >= 3 ? 1 : 0) + (tier >= 4 ? 1 : 0);
        player.MaxHp = checked(player.General.BaseHp + bonus);
        player.Hp = player.General.InitialHp is { } hp ? checked(hp + bonus) : player.MaxHp;
    }

    private bool SamePublicTeam(CharacterState a, CharacterState b) => a.TeamId is not null && a.TeamId == b.TeamId;
    private bool HasActivitySkill(CharacterState player, string id) => player.IsAlive && GetSkillBindingShard(player).HasSkill(id);

    private IEnumerable<RuleQueryContribution> ActivityRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        foreach (var contribution in GuanduActivityRuleContributions(player, query)) yield return contribution;
        foreach (var contribution in ShanheRuleContributions(player, query)) yield return contribution;
        foreach (var contribution in WenheActivityRuleContributions(player, query)) yield return contribution;
        foreach (var contribution in DragonBoatRuleContributions(player, query)) yield return contribution;
        foreach (var contribution in ShenwuActivityRuleContributions(player, query)) yield return contribution;
        foreach (var contribution in BossActivityRuleContributions(player, query)) yield return contribution;
        if (_modeDefinition.TeamActivity is null) yield break;
        var tier = ActivityTier(player.Seat);
        var regular = IsActivityPlayerSlot(player.Seat);
        var value = query switch
        {
            SkillRuleQuery.InitialHandSize => regular ? (tier >= 3 ? 1 : 0) : (tier >= 2 ? 1 : 0) + (tier >= 4 ? 1 : 0) + (tier >= 5 ? 1 : 0),
            SkillRuleQuery.DrawCount => regular ? (tier >= 4 ? 1 : 0) : (tier >= 2 ? 1 : 0) + (tier >= 4 ? 1 : 0),
            SkillRuleQuery.SlashLimit => (tier >= 3 ? 1 : 0) + (!regular && tier >= 5 ? 1 : 0),
            _ => 0
        };
        if (value != 0) yield return new FiniteRuleQueryContribution($"activity:{_modeDefinition.Id}:tier-{query}", SkillRuleOperation.Add, value);
        if (query == SkillRuleQuery.DrawCount && IsRuleWounded(player))
            foreach (var owner in _players.Where(p => !SamePublicTeam(p, player) && HasActivitySkill(p, "jiange:jueji")))
                yield return new FiniteRuleQueryContribution($"jiange:jueji:{owner.Seat}", SkillRuleOperation.Add, -1);
    }

    private int JiangeDistanceModifier(CharacterState source, CharacterState target) =>
        _modeDefinition.TeamActivity is null || SamePublicTeam(source, target) ? 0 :
            -_players.Count(p => p.Seat != source.Seat && SamePublicTeam(p, source) && HasActivitySkill(p, "jiange:jingfan"));

    private (CardUseEffectSource Source, int Cap)? JiangeDamageCap(IDamageAttempt attack)
    {
        if (_modeDefinition.TeamActivity is null) return null;
        var target = _players[attack.TargetSeat];
        var owner = _players.FirstOrDefault(p => SamePublicTeam(p, target) && HasActivitySkill(p, "jiange:chiying"));
        if (owner is null) return null;
        var grant = GetSkillBindingShard(owner).ActiveGrants.First(g => g.SkillId == "jiange:chiying");
        return (new CardUseEffectSource(grant.SkillId, "allied-damage-cap", owner.Seat, grant.SkillInstanceId), 1);
    }

    private string? EvaluateActivityWinner()
    {
        if (_modeDefinition.TeamActivity is null) return null;
        if (!_players.Any(p => IsActivitySpiritSlot(p.Seat) && p.IsAlive)) return ActivityPlayerTeam;
        return !_players.Any(p => p.TeamId == ActivityPlayerTeam && p.IsAlive)
            ? GetModeTeamIds().Single(t => t != ActivityPlayerTeam) : null;
    }
}
