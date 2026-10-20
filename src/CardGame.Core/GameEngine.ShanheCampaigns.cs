using CardGame.Core.Campaigns;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static GameOptions FreezeShanheBattleOptions(GameOptions options) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ShanheCampaigns.FreezeShanheBattleOptions(options);

    private static void ValidateShanheBattleOptions(GameOptions options, ContentRegistry registry, ContentModeDefinition mode)
    {
        var isCampaignMode = mode.Id == ShanheBattleSetup.BattleModeId;
        if (options.ShanheBattle is not { } setup)
        {
            if (isCampaignMode) throw new ArgumentException("A Shanhe encounter requires its campaign opening recipe.");
            return;
        }
        GodFactionSelectionRules.ValidateDefault(setup.DefaultGodFactionId);
        var playerCount = 1 + setup.EnemyRoster.Count + setup.AllyRoster.Count;
        if (!isCampaignMode || mode.ModeKind != ContentModeKind.Team || options.Seed != setup.BattleSeed ||
            options.PlayerCount != playerCount || options.HumanSeat is not (0 or -1) || options.UseInteractiveSetup ||
            mode.DualGenerals || mode.BossActivity is not null || mode.TeamActivity is not null ||
            mode.ShenwuActivity is not null || mode.NianActivity is not null || mode.HellActivity is not null ||
            mode.WenheActivity is not null || mode.DragonBoatActivity is not null ||
            options.HumanRole is not null || options.HumanTeamId is not (null or ShanheBattleSetup.PlayerTeamId) ||
            mode.TeamCounts is null || mode.TeamCounts.Count != 2 ||
            mode.TeamCounts.GetValueOrDefault(ShanheBattleSetup.PlayerTeamId) != 1 + setup.AllyRoster.Count ||
            mode.TeamCounts.GetValueOrDefault(ShanheBattleSetup.EnemyTeamId) != setup.EnemyRoster.Count ||
            setup.EnemyRoster.Count == 0 || setup.EncounterOrdinal < 1 || string.IsNullOrWhiteSpace(setup.NodeId) ||
            string.IsNullOrWhiteSpace(setup.CampaignHash))
            throw new ArgumentException("The Shanhe opening recipe does not match this encounter mode.");

        ValidateMember(new(setup.PlayerGeneralId, setup.MaxHpBonus, setup.Hp, setup.InitialHandBonus,
            setup.DrawBonus, setup.SlashBonus, setup.ExtraSkillIds));
        var tacticIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in setup.TacticSkillIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !registry.Skills.TryGetValue(id, out var skill) ||
                skill.ImplementationStatus != SkillImplementationStatus.Complete || skill.Program is null)
                throw new ArgumentException("Shanhe tactics require complete registered skill programs.");
            var acquisition = CampaignTacticAcquisitionRules.Resolve(skill);
            if (!tacticIdentities.Add(acquisition.IdentityId) && !acquisition.Repeatable)
                throw new ArgumentException("A unique campaign tactic cannot be acquired more than once.");
        }
        foreach (var member in setup.EnemyRoster.Concat(setup.AllyRoster)) ValidateMember(member);
        foreach (var card in setup.StartingCards.Concat(setup.StartingEquipment))
        {
            if (!Enum.IsDefined(card.Kind) || !Enum.IsDefined(card.Suit) || card.Suit == Suit.None ||
                card.Rank is < 1 or > 13 || !registry.Cards.Values.Any(c => c.LegacyKind == card.Kind))
                throw new ArgumentException("A Shanhe starting card is not registered physical content.");
        }
        if (setup.StartingCards.Count > 24 || setup.StartingEquipment.Count > 5 ||
            setup.StartingEquipment.Any(c => !EquipmentCatalog.IsEquipment(c.Kind)) ||
            setup.StartingEquipment.Select(c => EquipmentCatalog.Get(c.Kind).Slot).Distinct().Count() != setup.StartingEquipment.Count)
            throw new ArgumentException("Shanhe starting equipment needs one registered card per equipment slot.");

        void ValidateMember(ShanheCombatantDefinition member)
        {
            if (!registry.Generals.TryGetValue(member.GeneralId, out var general) ||
                !registry.IsGeneralPlayable(member.GeneralId) || mode.GeneralPoolIds?.Contains(member.GeneralId) != true ||
                member.MaxHpBonus is < 0 or > 40 || member.Hp is <= 0 || member.Hp > general.BaseHp + member.MaxHpBonus ||
                member.InitialHandBonus is < 0 or > 20 || member.DrawBonus is < 0 or > 10 || member.SlashBonus is < 0 or > 10 ||
                (member.ExtraSkillIds?.Count ?? 0) > 8 ||
                (member.ExtraSkillIds ?? []).Distinct(StringComparer.Ordinal).Count() != (member.ExtraSkillIds?.Count ?? 0) ||
                (member.ExtraSkillIds ?? []).Any(id => !registry.Skills.TryGetValue(id, out var skill) ||
                    skill.ImplementationStatus != SkillImplementationStatus.Complete))
                throw new ArgumentException("A Shanhe combatant has invalid general, health, bonuses or skills.");
            ValidateShanhePrintedLordQualifications(member, general, registry);
            if (member.StartingHand is { } hand && (hand.Count > 24 || hand.Any(card => card is null ||
                    !Enum.IsDefined(card.Kind) || !Enum.IsDefined(card.Suit) || card.Suit == Suit.None ||
                    card.Rank is < 1 or > 13 || !registry.Cards.Values.Any(c => c.LegacyKind == card.Kind))))
                throw new ArgumentException("A Shanhe combatant has an invalid fixed physical starting hand.");
        }
    }

    private void SetupShanhePlayers()
    {
        var setup = _options.ShanheBattle!;
        var members = new[] { new ShanheCombatantDefinition(setup.PlayerGeneralId, setup.MaxHpBonus, setup.Hp,
            setup.InitialHandBonus, setup.DrawBonus, setup.SlashBonus, setup.ExtraSkillIds) }
            .Concat(setup.AllyRoster).Concat(setup.EnemyRoster).ToArray();
        for (var seat = 0; seat < members.Length; seat++)
        {
            var member = members[seat];
            var general = _generalPool.Single(g => g.Id == member.GeneralId);
            var team = seat <= setup.AllyRoster.Count ? ShanheBattleSetup.PlayerTeamId : ShanheBattleSetup.EnemyTeamId;
            var player = new CharacterState
            {
                Seat = seat, Name = seat == 0 ? "你" : $"{(team == ShanheBattleSetup.PlayerTeamId ? "同行" : "敌军")} · {general.Name}",
                IsHuman = seat == _options.HumanSeat, Role = GetTeamRole(team), TeamId = team,
                TeamRevealed = true, RoleRevealed = true, General = general, GeneralSelected = true, GeneralRevealed = true,
                MaxHp = general.BaseHp + member.MaxHpBonus, Hp = member.Hp ?? general.InitialHp ?? general.BaseHp + member.MaxHpBonus
            };
            foreach (var id in member.ExtraSkillIds ?? [])
            {
                if (general.Skills.Any(skill => skill.ContentId == id)) continue;
                var grantId = $"campaign:shanhe:{seat}:{id}";
                player.SkillGrants.Grant(new(grantId, id, grantId, "campaign:shanhe"));
            }
            if (seat == 0)
            {
                var tacticPrograms = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < setup.TacticSkillIds.Count; index++)
                {
                    var id = setup.TacticSkillIds[index];
                    var grantId = tacticPrograms.Add(id) ? $"campaign:shanhe-tactic:{id}" :
                        $"campaign:shanhe-tactic-copy:{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{id}";
                    player.SkillGrants.Grant(new(grantId, id, grantId, "campaign:shanhe-tactic"));
                }
            }
            _players.Add(player);
        }
    }

    private IEnumerable<RuleQueryContribution> ShanheRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (_options.ShanheBattle is not { } setup) yield break;
        var member = player.Seat == 0 ? new ShanheCombatantDefinition(setup.PlayerGeneralId,
            setup.MaxHpBonus, setup.Hp, setup.InitialHandBonus, setup.DrawBonus, setup.SlashBonus) :
            player.Seat <= setup.AllyRoster.Count ? setup.AllyRoster[player.Seat - 1] :
            setup.EnemyRoster[player.Seat - setup.AllyRoster.Count - 1];
        var bonus = query switch
        {
            SkillRuleQuery.InitialHandSize => member.InitialHandBonus,
            SkillRuleQuery.DrawCount => member.DrawBonus,
            SkillRuleQuery.SlashLimit => member.SlashBonus,
            _ => 0
        };
        if (bonus != 0) yield return new FiniteRuleQueryContribution($"campaign:shanhe:{query}", SkillRuleOperation.Add, bonus);
    }

    private void PlaceShanheStartingCards()
    {
        if (_options.ShanheBattle is not { } setup) return;
        foreach (var item in setup.StartingCards) PlaceShanhePhysicalCard(item, 0, equipment: false);
        foreach (var item in setup.StartingEquipment) PlaceShanhePhysicalCard(item, 0, equipment: true);
    }

    private IReadOnlyList<ShanheStartingCard>? GetShanheStartingHand(int seat)
    {
        if (_options.ShanheBattle is not { } setup || seat == 0) return null;
        return seat <= setup.AllyRoster.Count ? setup.AllyRoster[seat - 1].StartingHand :
            setup.EnemyRoster[seat - setup.AllyRoster.Count - 1].StartingHand;
    }

    private bool TryDealShanheStartingHandCard(int seat, int index)
    {
        if (GetShanheStartingHand(seat) is not { } hand || index >= hand.Count) return false;
        PlaceShanhePhysicalCard(hand[index], seat, equipment: false);
        return true;
    }

    private void PlaceShanhePhysicalCard(ShanheStartingCard item, int seat, bool equipment)
    {
        var card = new Card(CreateCardZoneDiagnostics().Select(c => c.CardId).DefaultIfEmpty(0).Max() + 1,
            item.Kind, item.Suit, item.Rank);
        _cardZones.AddGeneratedCard(card);
        _generatedPhysicalCardIds.Add(card.Id);
        if (equipment) PlaceAdvancedEquipment(card, seat);
        else MoveCard(card, CardLocation.OutsideGame, CardLocation.Hand(seat), CardMoveReasons.InitialDeal);
    }
}
