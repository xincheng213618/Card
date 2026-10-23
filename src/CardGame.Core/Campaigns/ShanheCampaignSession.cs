using System.Text.Json;

namespace CardGame.Core.Campaigns;

/// <summary>
/// Owns one local expedition. Commands prepare a detached next state before committing;
/// battle rewards require a completed, independently replayed command checkpoint.
/// </summary>
public sealed partial class ShanheCampaignSession
{
    private const int MaximumCommands = 20_000;
    private readonly ShanheCampaignOptions _options;
    private readonly Func<ShanheBattleSetup, ContentRegistry> _battleRegistryFactory;
    private readonly Dictionary<string, ShanheNodeDefinition> _nodes;
    private readonly Dictionary<string, ShanheRewardDefinition> _rewards;
    private readonly HashSet<string>? _unlockedRewardIds;
    private readonly int _baseMaxHp;
    private readonly IReadOnlyList<string> _printedSkillIds;
    private readonly List<ShanheCampaignCommand> _commands = [];
    private RunState _state;
    private ShanheCampaignSnapshot _snapshot;
    private bool _submitting;

    public ShanheCampaignDefinition Definition { get; }
    public string ContentHash { get; }
    public long Revision => _commands.Count;
    public ShanheCampaignSnapshot Snapshot => _snapshot;
    public ShanheBattleSetup? PendingBattleSetup => _snapshot.PendingBattleSetup;
    public IReadOnlyList<ShanheCampaignCommand> AcceptedCommands => ShanheCampaignContent.Freeze(_commands);

    private sealed record RunState
    {
        public ShanheCampaignPhase Phase { get; init; } = ShanheCampaignPhase.Map;
        public string? CurrentNodeId { get; init; }
        public string? EncounterId { get; init; }
        public int EncounterOrdinal { get; init; }
        public int Hp { get; init; }
        public int Gold { get; init; }
        public int ExtraSkillSlots { get; init; }
        public bool RebirthUsed { get; init; }
        public ShanheTacticBonuses Bonuses { get; init; } = new();
        public IReadOnlyList<string> ExtraSkillIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> TacticIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ShanheStartingCard> StartingCards { get; init; } = Array.Empty<ShanheStartingCard>();
        public IReadOnlyList<ShanheStartingCard> StartingEquipment { get; init; } = Array.Empty<ShanheStartingCard>();
        public IReadOnlyList<string> AvailableNodeIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RewardOfferIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<BossRewardGroupState> BossRewardGroups { get; init; } = Array.Empty<BossRewardGroupState>();
        public IReadOnlyList<string> OpeningDraftTacticIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> OpeningSkillOfferIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> OpeningResumeNodeIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> PurchasedChoiceIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ShanheNodeChoiceDefinition> ShopStock { get; init; } = Array.Empty<ShanheNodeChoiceDefinition>();
        public int ShopRefreshCount { get; init; }
        public int ShopTacticPurchases { get; init; }
        public int ShopEquipmentPurchases { get; init; }
        public int ShopCardPurchases { get; init; }
        public IReadOnlyList<string> CompletedNodeIds { get; init; } = Array.Empty<string>();
        public ShanheBattleSetup? BattleSetup { get; init; }
    }

    private ShanheCampaignSession(ShanheCampaignDefinition definition, ShanheCampaignOptions options,
        Func<ShanheBattleSetup, ContentRegistry> battleRegistryFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(battleRegistryFactory);
        var rankBonuses = ShanheGeneralRanks.GetBonuses(options.PlayerRank ?? 1);
        Definition = ShanheCampaignContent.FreezeAndValidate(definition);
        ContentHash = ShanheCampaignContent.GetContentHash(Definition);
        ShanheCampaignContent.Require(Definition.PlayerGeneralIds.Contains(options.PlayerGeneralId, StringComparer.Ordinal), "The selected general is not available in this campaign.");
        _options = options with { };
        _battleRegistryFactory = battleRegistryFactory;
        _nodes = Definition.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _rewards = Definition.Rewards.ToDictionary(r => r.Id, StringComparer.Ordinal);
        _unlockedRewardIds = options.UnlockedRewardIds?.ToHashSet(StringComparer.Ordinal);
        Require(options.EquippedTacticIds.Count <= ShanheCampaignOptions.MaximumEquippedTactics, "At most six tactics may be equipped before an expedition.");
        var equippedPrograms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in options.EquippedTacticIds)
        {
            Require(id is not null && _rewards.TryGetValue(id, out var equipped) &&
                (!string.IsNullOrWhiteSpace(equipped.TacticSkillId) || equipped.OpeningSkillRewardIds is not null),
                "Only a known tactic reward may be equipped before an expedition.");
            var reward = _rewards[id!];
            Require(_unlockedRewardIds is null || _unlockedRewardIds.Contains(id!), "An equipped tactic must first be unlocked in the codex.");
            Require(equippedPrograms.Add(reward.TacticSkillId ?? "$opening:" + reward.Id), "The same tactic program cannot be equipped more than once.");
        }

        // Resolve printed health through the same content registry used by real battles.
        // A probe creates no engine and consumes no run randomness or journal revision.
        var firstNode = Definition.Nodes.First(n => ShanheCampaignContent.IsBattle(n.Kind));
        var firstEncounter = firstNode.Encounters![0];
        var startingBonuses = Definition.StartingBonuses!;
        var bonuses = new ShanheTacticBonuses(checked(startingBonuses.MaxHp + rankBonuses.MaxHp),
            checked(startingBonuses.InitialHand + rankBonuses.InitialHand), checked(startingBonuses.Draw + rankBonuses.Draw),
            checked(startingBonuses.Slash + rankBonuses.Slash));
        Require(bonuses.MaxHp <= 40 && bonuses.InitialHand <= 20 && bonuses.Draw <= 10 && bonuses.Slash <= 10,
            "General rank and starting bonuses exceed the campaign bonus limits.");
        var rankSkills = options.PlayerRank == 5 ? new[] { ShanheGeneralRanks.RebirthSkillId } : [];
        var probe = ShanheBattleSetup.Freeze(new(options.Seed, 1, firstNode.Id, options.PlayerGeneralId,
            1, bonuses.MaxHp, bonuses.InitialHand, bonuses.Draw, bonuses.Slash,
            ShanheCampaignContent.Freeze(Definition.Rewards.Select(reward => reward.SkillId)
                .OfType<string>().Concat(rankSkills).Distinct(StringComparer.Ordinal)), [], [],
            firstEncounter.Enemies, firstEncounter.Allies ?? [], ContentHash)
        {
            DefaultGodFactionId = Definition.DefaultGodFactionId,
            PlayerRank = options.PlayerRank,
            RebirthAvailable = options.PlayerRank == 5,
            StartingResourcesExpireOnUse = Definition.StartingResourcesExpireOnUse,
            // This registry-only probe validates every reward, including skill
            // programs imported only by recipes that reference them. No engine
            // is created and no unearned skill is granted to a battle player.
            TacticSkillIds = ShanheCampaignContent.Freeze(Definition.Rewards.Select(reward => reward.TacticSkillId)
                .OfType<string>().Distinct(StringComparer.Ordinal))
        });
        var registry = battleRegistryFactory(probe) ?? throw new ArgumentException("The battle registry factory returned null.", nameof(battleRegistryFactory));
        if (!registry.Generals.TryGetValue(options.PlayerGeneralId, out var general) || !registry.IsGeneralPlayable(options.PlayerGeneralId))
            throw new ArgumentException("The battle content does not contain the player's general.", nameof(options));
        _baseMaxHp = general.BaseHp;
        _printedSkillIds = ShanheCampaignContent.Freeze(general.SkillIds);
        foreach (var skill in Definition.Rewards.SelectMany(reward => new[] { reward.SkillId, reward.TacticSkillId }).OfType<string>())
            if (!registry.Skills.TryGetValue(skill, out var skillDefinition) ||
                skillDefinition.ImplementationStatus != SkillImplementationStatus.Complete)
                throw new ArgumentException($"Campaign reward references unknown skill '{skill}'.", nameof(definition));
        foreach (var tactic in Definition.Rewards.Select(reward => reward.TacticSkillId).OfType<string>())
            if (registry.GetSkill(tactic).Program is null)
                throw new ArgumentException($"Campaign tactic '{tactic}' requires a complete skill program.", nameof(definition));
        _tacticAcquisitionRules = CreateTacticAcquisitionRules(registry);
        TacticAcquisitionHash = CreateTacticAcquisitionHash(registry);
        ValidateBossAcquisitionGroups();
        var equippedIdentities = new HashSet<(string Kind, string Id)>();
        foreach (var id in options.EquippedTacticIds)
        {
            var reward = _rewards[id];
            var identity = reward.TacticSkillId is { } tactic
                ? ("tactic", _tacticAcquisitionRules[tactic].IdentityId) : ("opening", reward.Id);
            Require(equippedIdentities.Add(identity),
                "The same canonical tactic cannot be equipped more than once before an expedition.");
        }
        _state = new()
        {
            Hp = checked(_baseMaxHp + bonuses.MaxHp), Gold = Definition.StartingGold,
            ExtraSkillSlots = Definition.ExtraSkillSlots, Bonuses = bonuses,
            AvailableNodeIds = Definition.RandomStartingNode
                ? ShanheCampaignContent.Freeze(new[] { Definition.StartingNodeIds[new DeterministicRandom(ShanheBattleSetup.DeriveSeed(options.Seed, Definition.Id + ":entry", 0)).Next(Definition.StartingNodeIds.Count)] })
                : Definition.StartingNodeIds
        };
        foreach (var id in _options.EquippedTacticIds) _state = ApplyReward(_state, _rewards[id], null);
        _state = BeginOpeningSkillDraft(_state);
        _snapshot = CreateSnapshot(_state, 0);
    }

    public static ShanheCampaignSession Create(ShanheCampaignDefinition definition, ShanheCampaignOptions options,
        Func<ShanheBattleSetup, ContentRegistry> battleRegistryFactory) => new(definition, options, battleRegistryFactory);

    public static ShanheCampaignSession Restore(ShanheCampaignCheckpoint checkpoint, ShanheCampaignDefinition definition,
        Func<ShanheBattleSetup, ContentRegistry> battleRegistryFactory)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.SchemaVersion != ShanheCampaignCheckpoint.CurrentSchemaVersion || checkpoint.Options is null ||
            checkpoint.Commands is null || checkpoint.Commands.Count > MaximumCommands ||
            checkpoint.Revision != checkpoint.Commands.Count || checkpoint.Revision < 0)
            throw new ArgumentException("Campaign checkpoint metadata is invalid.", nameof(checkpoint));
        var session = Create(definition, checkpoint.Options, battleRegistryFactory);
        if (!string.Equals(checkpoint.CampaignId, session.Definition.Id, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.CampaignHash, session.ContentHash, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.TacticAcquisitionHash, session.TacticAcquisitionHash, StringComparison.Ordinal) ||
            (checkpoint.PlayerGeneralIds is { } savedRoster && !savedRoster.SequenceEqual(session.Definition.PlayerGeneralIds, StringComparer.Ordinal)))
            throw new ArgumentException("Campaign checkpoint content fingerprint does not match.", nameof(checkpoint));
        foreach (var command in checkpoint.Commands)
        {
            var result = session.Submit(command);
            if (!result.Accepted) throw new InvalidOperationException($"Campaign replay command {session.Revision} was rejected: {result.Error}");
        }
        return session;
    }

    public static ShanheCampaignSession Replay(ShanheCampaignDefinition definition, ShanheCampaignOptions options,
        IEnumerable<ShanheCampaignCommand> commands, Func<ShanheBattleSetup, ContentRegistry> battleRegistryFactory)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var session = Create(definition, options, battleRegistryFactory);
        foreach (var command in commands)
        {
            var result = session.Submit(command);
            if (!result.Accepted) throw new InvalidOperationException($"Campaign replay command {session.Revision} was rejected: {result.Error}");
        }
        return session;
    }

    public ShanheCampaignCheckpoint CreateCheckpoint() => new(ShanheCampaignCheckpoint.CurrentSchemaVersion,
        _options, ShanheCampaignContent.Freeze(_commands), Revision, Definition.Id, ContentHash)
    {
        PlayerGeneralIds = Definition.PlayerGeneralIds, TacticAcquisitionHash = TacticAcquisitionHash
    };

    public ShanheCampaignResult CompleteBattle(GameEngine battle, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(battle);
        return Submit(new CompleteShanheBattleCommand(GameCheckpointJson.Serialize(battle.CreateCheckpoint()), expectedRevision));
    }

    public ShanheCampaignResult Submit(ShanheCampaignCommand? command)
    {
        if (_submitting) return Reject("Campaign commands cannot re-enter the same session.");
        if (command is null) return Reject("A campaign command is required.");
        if (command.ExpectedRevision != Revision) return Reject("The campaign revision is stale.");
        if (Revision >= MaximumCommands) return Reject("The campaign journal is full.");
        if (_state.Phase is ShanheCampaignPhase.Won or ShanheCampaignPhase.Lost) return Reject("The expedition has already ended.");
        _submitting = true;
        try
        {
            var next = command switch
            {
                MoveShanheNodeCommand move => MoveToNode(move),
                ChooseShanheRewardCommand reward => ChooseReward(reward),
                ChooseShanheOpeningSkillCommand skill => ChooseOpeningSkill(skill),
                ChooseShanheBossRewardCommand reward => ChooseBossReward(reward),
                ConfirmShanheBossRewardCommand => ConfirmBossReward(),
                SkipShanheRewardCommand => SkipReward(),
                ChooseShanheNodeOptionCommand choice => ChooseOption(choice),
                RefreshShanheShopCommand => RefreshShop(),
                CompleteShanheBattleCommand battle => CompleteBattle(battle),
                _ => throw new ArgumentException("Unsupported campaign command.")
            };
            var snapshot = CreateSnapshot(next, checked(Revision + 1));
            // The command records contain only primitive values and immutable checkpoint text.
            // All potentially failing validation, battle replay and projection precede commit.
            _commands.EnsureCapacity(checked(_commands.Count + 1));
            _commands.Add(command);
            _state = next;
            _snapshot = snapshot;
            return new(true, null, Revision, snapshot);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or OverflowException or KeyNotFoundException)
        {
            return Reject(exception.Message);
        }
        finally { _submitting = false; }
    }

    private ShanheCampaignResult Reject(string message) => new(false, message, Revision, _snapshot);
    private RunState MoveToNode(MoveShanheNodeCommand command)
    {
        Require(_state.Phase == ShanheCampaignPhase.Map && _state.AvailableNodeIds.Contains(command.NodeId, StringComparer.Ordinal), "Choose an available map node.");
        var node = _nodes[command.NodeId];
        var state = _state with { CurrentNodeId = node.Id, AvailableNodeIds = [], PurchasedChoiceIds = [], RewardOfferIds = [], BossRewardGroups = [] };
        if (!ShanheCampaignContent.IsBattle(node.Kind))
        {
            state = state with { Phase = ShanheCampaignPhase.NodeChoice };
            if (_options.UseCodexRewardPool && node.Kind == ShanheNodeKind.Shop)
            {
                state = state with { ShopRefreshCount = 0, ShopTacticPurchases = 0, ShopEquipmentPurchases = 0, ShopCardPurchases = 0 };
                state = state with { ShopStock = CreateShopStock(state, node) };
            }
            return state;
        }
        var ordinal = checked(state.EncounterOrdinal + 1);
        var random = new DeterministicRandom(ShanheBattleSetup.DeriveSeed(_options.Seed, node.Id, ordinal));
        var encounter = node.Encounters![random.Next(node.Encounters.Count)];
        var bonuses = state.Bonuses;
        var rebirthAvailable = _options.PlayerRank == 5 && !state.RebirthUsed;
        var setup = ShanheBattleSetup.Freeze(new(_options.Seed, ordinal, node.Id, _options.PlayerGeneralId,
            state.Hp, bonuses.MaxHp, bonuses.InitialHand, bonuses.Draw, bonuses.Slash,
            rebirthAvailable ? ShanheCampaignContent.Freeze(state.ExtraSkillIds.Append(ShanheGeneralRanks.RebirthSkillId)) : state.ExtraSkillIds,
            state.StartingCards, state.StartingEquipment, encounter.Enemies, encounter.Allies ?? [], ContentHash)
        {
            TacticSkillIds = ShanheCampaignContent.Freeze(state.TacticIds.Select(id => _rewards[id].TacticSkillId).OfType<string>()),
            DefaultGodFactionId = Definition.DefaultGodFactionId,
            PlayerRank = _options.PlayerRank,
            RebirthAvailable = rebirthAvailable,
            StartingResourcesExpireOnUse = Definition.StartingResourcesExpireOnUse
        });
        return state with { Phase = ShanheCampaignPhase.Battle, EncounterOrdinal = ordinal, EncounterId = encounter.Id, BattleSetup = setup };
    }

    private RunState CompleteBattle(CompleteShanheBattleCommand command)
    {
        Require(_state.Phase == ShanheCampaignPhase.Battle && _state.BattleSetup is not null, "There is no pending campaign battle.");
        Require(!string.IsNullOrWhiteSpace(command.CheckpointJson) && command.CheckpointJson.Length <= 32 * 1024 * 1024, "The battle checkpoint is empty or too large.");
        var checkpoint = GameCheckpointJson.Deserialize(command.CheckpointJson);
        var setup = _state.BattleSetup!;
        var battleOptions = checkpoint.Options ?? throw new ArgumentException("The battle checkpoint has no setup options.");
        Require(battleOptions.HumanSeat == 0 &&
            battleOptions.PlayerCount == 1 + setup.EnemyRoster.Count + setup.AllyRoster.Count &&
            battleOptions.Seed == setup.BattleSeed && checkpoint.ModeId == ShanheBattleSetup.BattleModeId &&
            battleOptions.ModeId == ShanheBattleSetup.BattleModeId &&
            ShanheBattleSetup.ValueEquals(battleOptions.ShanheBattle, setup), "The battle belongs to a different encounter or expedition.");
        Require(checkpoint.Commands is { Count: <= 100_000 }, "The battle checkpoint has too many commands.");
        var registry = _battleRegistryFactory(setup) ?? throw new InvalidOperationException("The battle registry factory returned null.");
        Require(battleOptions.DeckId is null || battleOptions.DeckId == registry.Modes[ShanheBattleSetup.BattleModeId].DeckId,
            "The battle did not use this encounter's registered deck.");
        var battle = GameReplay.Restore(checkpoint, registry);
        var result = battle.CreateSnapshot(0);
        Require(result.Status == EngineStatus.Completed && result.Winner != Winner.None && result.PendingDecision is null &&
            result.ProcessingCardCount == 0 && battle.ResolutionStack.Count == 0, "The battle has not finished all of its resolutions.");
        var player = result.Players.Single(p => p.IsHuman && p.Seat == 0);
        Require(player.GeneralId == setup.PlayerGeneralId, "The completed player does not match this campaign setup.");
        // Character skills may change maximum health within this battle. Only campaign
        // tactic bonuses persist between encounters; remaining health is capped at that
        // persistent maximum rather than adding the battle's growth a second time.
        var rebirthUsed = _state.RebirthUsed || setup.RebirthAvailable && battle.Events.Select(e => e.Payload)
            .OfType<SkillUsageConsumedEvent>().Any(e => e.SkillOwnerSeat == 0 && e.SkillId == ShanheGeneralRanks.RebirthSkillId &&
                e.Scope == SkillUsageScope.Game && e.Count > 0);
        var state = _state with
        {
            Hp = Math.Max(0, Math.Min(player.Hp, _baseMaxHp + _state.Bonuses.MaxHp)), BattleSetup = null,
            RebirthUsed = rebirthUsed
        };
        if (result.WinnerTeamId != ShanheBattleSetup.PlayerTeamId || !player.IsAlive || player.Hp <= 0)
            return state with { Phase = ShanheCampaignPhase.Lost, AvailableNodeIds = [], RewardOfferIds = [] };
        var node = _nodes[state.CurrentNodeId!];
        var encounter = node.Encounters!.Single(e => e.Id == state.EncounterId);
        state = state with
        {
            Gold = checked(state.Gold + (node.BossRewards?.FixedGold ?? encounter.VictoryGold)),
            Hp = Math.Min(_baseMaxHp + state.Bonuses.MaxHp, checked(state.Hp + encounter.HealAfterVictory)),
            ExtraSkillSlots = checked(state.ExtraSkillSlots + (node.BossRewards?.SkillSlots ?? node.AddSkillSlotsOnVictory))
        };
        Require(state.ExtraSkillSlots <= 8, "This victory exceeds the extra skill limit.");
        if (node.BossRewards is not null) return BeginBossRewards(state, node);
        var rewards = (node.RewardIds ?? []).Where(id => CanReceiveReward(state, _rewards[id])).ToList();
        if (rewards.Count == 0) return FinishNode(state);
        var random = new DeterministicRandom(ShanheBattleSetup.DeriveSeed(_options.Seed, node.Id + ":rewards", state.EncounterOrdinal));
        random.Shuffle(rewards);
        return state with { Phase = ShanheCampaignPhase.Reward, RewardOfferIds = ShanheCampaignContent.Freeze(rewards.Take(node.RewardChoiceCount)) };
    }

    private RunState ChooseReward(ChooseShanheRewardCommand command)
    {
        Require(_state.BossRewardGroups.Count == 0, "A chapter boss requires grouped choices and final confirmation.");
        Require(_state.Phase == ShanheCampaignPhase.Reward && _state.RewardOfferIds.Contains(command.RewardId, StringComparer.Ordinal), "Choose one of the published battle rewards.");
        return FinishNode(ApplyReward(_state, _rewards[command.RewardId], command.ReplaceSkillId));
    }

    private RunState SkipReward()
    {
        Require(_state.BossRewardGroups.Count == 0, "Chapter boss reward groups cannot be skipped for ordinary compensation.");
        Require(_state.Phase == ShanheCampaignPhase.Reward && _state.RewardOfferIds.Count > 0,
            "There is no published battle reward to decline.");
        var gold = _nodes[_state.CurrentNodeId!].RewardSkipGold;
        Require(gold > 0, "This reward offer does not grant compensation for declining.");
        return FinishNode(_state with { Gold = checked(_state.Gold + gold) });
    }

    private RunState ChooseOption(ChooseShanheNodeOptionCommand command) =>
        PrepareNodeOption(_state, command.ChoiceId, command.ReplaceSkillId);

    private RunState RefreshShop()
    {
        Require(_options.UseCodexRewardPool && _state.Phase == ShanheCampaignPhase.NodeChoice &&
            _state.CurrentNodeId is { } id && _nodes[id].Kind == ShanheNodeKind.Shop, "There is no refreshable shop.");
        Require(_state.ShopRefreshCount == 0, "This shop has no refreshes remaining.");
        Require(_state.Gold >= 100, "Refreshing this shop costs 100 copper coins.");
        var next = _state with { Gold = checked(_state.Gold - 100), ShopRefreshCount = 1 };
        return next with { ShopStock = CreateShopStock(next, _nodes[next.CurrentNodeId!]) };
    }

    private RunState PrepareNodeOption(RunState current, string choiceId, string? replaceSkillId)
    {
        Require(current.Phase == ShanheCampaignPhase.NodeChoice, "There is no pending story, shop or camp choice.");
        var node = _nodes[current.CurrentNodeId!];
        if (choiceId == "$leave" && (node.Kind == ShanheNodeKind.Shop || AvailableChoices(current, node).Length == 0))
        {
            Require(replaceSkillId is null, "Leaving a shop cannot replace a skill.");
            return FinishNode(current);
        }
        var choice = NodeStock(current, node).SingleOrDefault(c => c.Id == choiceId);
        Require(choice is not null && !current.PurchasedChoiceIds.Contains(choiceId, StringComparer.Ordinal), "Choose an available node option.");
        choice = WithShopPrice(current, node, choice!);
        Require(choice!.RewardId is not { } offeredId || CanReceiveReward(current, _rewards[offeredId]), "The general already has this skill; no gold, health or extra slot was spent.");
        Require(current.Gold >= choice!.GoldCost && current.Hp > choice.HpCost, "The choice costs more gold or health than you can pay.");
        var state = current with { Gold = checked(current.Gold - choice.GoldCost), Hp = checked(current.Hp - choice.HpCost) };
        if (choice.RewardId is { } rewardId) state = ApplyReward(state, _rewards[rewardId], replaceSkillId);
        else Require(replaceSkillId is null, "This choice grants no skill to replace.");
        if (choice.LeavesNode) return FinishNode(state);
        var category = _options.UseCodexRewardPool && node.Kind == ShanheNodeKind.Shop && choice.RewardId is { } boughtId
            ? ShopCategory(_rewards[boughtId]) : ShopRewardCategory.Supply;
        return state with
        {
            PurchasedChoiceIds = ShanheCampaignContent.Freeze(state.PurchasedChoiceIds.Append(choice.Id)),
            ShopTacticPurchases = checked(state.ShopTacticPurchases + (category == ShopRewardCategory.Tactic ? 1 : 0)),
            ShopEquipmentPurchases = checked(state.ShopEquipmentPurchases + (category == ShopRewardCategory.Equipment ? 1 : 0)),
            ShopCardPurchases = checked(state.ShopCardPurchases + (category == ShopRewardCategory.Card ? 1 : 0))
        };
    }

    private RunState ApplyReward(RunState state, ShanheRewardDefinition reward, string? replaceSkillId, bool allowLockedOpeningSkill = false)
    {
        Require(allowLockedOpeningSkill || IsRewardUnlocked(reward), "This skill or equipment reward must first be unlocked in the codex.");
        Require(reward.OpeningSkillRewardIds is null || !state.TacticIds.Contains(reward.Id, StringComparer.Ordinal), "This initial tactic is already equipped.");
        Require(reward.TacticSkillId is not { } tacticSkill || CanAcquireTactic(state, tacticSkill), "This unique tactic is already acquired; no resources were spent.");
        var add = reward.Bonuses!;
        var bonuses = new ShanheTacticBonuses(checked(state.Bonuses.MaxHp + add.MaxHp),
            checked(state.Bonuses.InitialHand + add.InitialHand), checked(state.Bonuses.Draw + add.Draw), checked(state.Bonuses.Slash + add.Slash));
        Require(bonuses.MaxHp <= 40 && bonuses.InitialHand <= 20 && bonuses.Draw <= 10 && bonuses.Slash <= 10, "This reward exceeds the campaign bonus limits.");
        var slots = checked(state.ExtraSkillSlots + reward.AddSkillSlots);
        Require(slots <= 8, "This reward exceeds the extra skill limit.");
        var skills = state.ExtraSkillIds.ToList();
        if (reward.SkillId is { } skill)
        {
            Require(!HasSkill(state, skill), "The general already has this skill; no gold, health or extra slot was spent.");
            if (skills.Count < slots)
                Require(replaceSkillId is null, "A free slot is available; replacing another skill is unnecessary.");
            else
            {
                Require(replaceSkillId is not null && skills.Contains(replaceSkillId, StringComparer.Ordinal), "Choose an equipped extra skill to replace.");
                skills.Remove(replaceSkillId!);
            }
            Require(skills.Count < slots, "No extra skill slot is available.");
            skills.Add(skill);
        }
        else Require(replaceSkillId is null, "This reward grants no skill to replace.");
        var cards = state.StartingCards.Concat(reward.StartingCards ?? []).ToArray();
        Require(cards.Length <= 24, "The starting hand recipe is full.");
        // A newly acquired equipment piece replaces the old item in its slot.
        var equipment = state.StartingEquipment.ToList();
        foreach (var card in reward.StartingEquipment ?? [])
        {
            var slot = EquipmentCatalog.Get(card.Kind).Slot;
            equipment.RemoveAll(old => EquipmentCatalog.Get(old.Kind).Slot == slot);
            equipment.Add(card);
        }
        return state with
        {
            Hp = Math.Min(_baseMaxHp + bonuses.MaxHp, checked(state.Hp + reward.Heal)),
            Gold = checked(state.Gold + reward.Gold), Bonuses = bonuses, ExtraSkillSlots = slots,
            TacticIds = reward.TacticSkillId is null && reward.OpeningSkillRewardIds is null ? state.TacticIds : ShanheCampaignContent.Freeze(state.TacticIds.Append(reward.Id)),
            ExtraSkillIds = ShanheCampaignContent.Freeze(skills), StartingCards = ShanheCampaignContent.Freeze(cards),
            StartingEquipment = ShanheCampaignContent.Freeze(equipment)
        };
    }

    private RunState FinishNode(RunState state)
    {
        var node = _nodes[state.CurrentNodeId!];
        return state with
        {
            Phase = node.NextNodeIds.Count == 0 ? ShanheCampaignPhase.Won : ShanheCampaignPhase.Map,
            AvailableNodeIds = node.NextNodeIds, RewardOfferIds = [], BossRewardGroups = [], BattleSetup = null,
            CompletedNodeIds = ShanheCampaignContent.Freeze(state.CompletedNodeIds.Append(node.Id))
        };
    }

    private ShanheCampaignSnapshot CreateSnapshot(RunState state, long revision)
    {
        ShanheNodeChoiceDefinition[] choices = state.Phase != ShanheCampaignPhase.NodeChoice ? [] :
            AvailableChoices(state, _nodes[state.CurrentNodeId!]);
        if (state.Phase == ShanheCampaignPhase.NodeChoice && (choices.Length == 0 || _nodes[state.CurrentNodeId!].Kind == ShanheNodeKind.Shop))
            choices = [.. choices, new("$leave", _nodes[state.CurrentNodeId!].Kind == ShanheNodeKind.Shop ? "离开商店" : "继续行军")];
        return new(Definition.Id, Definition.Name, state.Phase, revision, state.CurrentNodeId,
            state.Hp, checked(_baseMaxHp + state.Bonuses.MaxHp), state.Gold, state.ExtraSkillSlots, state.Bonuses,
            state.ExtraSkillIds, ShanheCampaignContent.Freeze(state.AvailableNodeIds.Select(id => _nodes[id])),
            ShanheCampaignContent.Freeze(state.RewardOfferIds.Select(id => _rewards[id])), ShanheCampaignContent.Freeze(choices),
            state.CompletedNodeIds, state.BattleSetup,
            state.CurrentNodeId is { } current ? _nodes[current].StoryText : Definition.IntroText,
            state.Phase == ShanheCampaignPhase.Won ?
                state.CurrentNodeId is { } endingNode && !string.IsNullOrEmpty(_nodes[endingNode].VictoryText) ? _nodes[endingNode].VictoryText : Definition.VictoryText
                : state.Phase == ShanheCampaignPhase.Lost ? Definition.DefeatText : "")
        {
            PlayerGeneralId = _options.PlayerGeneralId, StartingCards = state.StartingCards,
            PlayerRank = _options.PlayerRank ?? 1, RebirthUsed = state.RebirthUsed,
            StartingEquipment = state.StartingEquipment, EncounterOrdinal = state.EncounterOrdinal, TacticIds = state.TacticIds,
            ChapterNumber = state.CurrentNodeId is { } chapterNode ? _nodes[chapterNode].ChapterNumber : 0,
            ChapterTitle = state.CurrentNodeId is { } titleNode ? _nodes[titleNode].ChapterTitle : null,
            BossRewardSummary = CreateBossRewardSummary(state, revision),
            OpeningSkillOffers = ShanheCampaignContent.Freeze(state.OpeningSkillOfferIds.Select(id => _rewards[id])),
            ShopItems = CreateShopItems(state, revision),
            ShopRefreshRemaining = IsRandomShop(state) ? 1 - state.ShopRefreshCount : 0,
            ShopRefreshCost = IsRandomShop(state) ? 100 : 0,
            RewardSkipGold = state.Phase == ShanheCampaignPhase.Reward && state.BossRewardGroups.Count == 0
                ? _nodes[state.CurrentNodeId!].RewardSkipGold : 0
        };
    }

    private IReadOnlyList<ShanheShopItemSnapshot> CreateShopItems(RunState state, long revision)
    {
        if (state.Phase != ShanheCampaignPhase.NodeChoice || state.CurrentNodeId is not { } nodeId ||
            _nodes[nodeId] is not { Kind: ShanheNodeKind.Shop } node) return [];
        return ShanheCampaignContent.Freeze(NodeStock(state, node)
            .Where(choice => choice.RewardId is not { } id || IsRewardUnlocked(_rewards[id]))
            .Select(choice => WithShopPrice(state, node, choice)).Select(choice =>
        {
            var reward = choice.RewardId is { } id ? _rewards[id] : null;
            var purchased = state.PurchasedChoiceIds.Contains(choice.Id, StringComparer.Ordinal);
            var owned = reward is not null && !CanReceiveReward(state, reward);
            var affordable = state.Gold >= choice.GoldCost && state.Hp > choice.HpCost;
            var replacement = reward?.SkillId is not null && !owned &&
                state.ExtraSkillIds.Count >= state.ExtraSkillSlots + reward.AddSkillSlots;
            var replacements = new List<string>();
            var available = false;
            if (!purchased && !owned && affordable)
                foreach (var candidate in replacement ? state.ExtraSkillIds.Cast<string?>() : new string?[] { null })
                {
                    try
                    {
                        // Use exactly the command's pure preparation path, including all
                        // build caps and replacement rules; queries never issue a command.
                        _ = PrepareNodeOption(state, choice.Id, candidate);
                        available = true;
                        if (candidate is not null) replacements.Add(candidate);
                    }
                    catch (Exception exception) when (exception is ArgumentException or OverflowException) { }
                }
            var status = purchased ? ShanheShopItemState.Purchased : owned ? ShanheShopItemState.AlreadyOwned :
                !affordable ? ShanheShopItemState.InsufficientResources : available ? ShanheShopItemState.Available :
                replacement && state.ExtraSkillIds.Count == 0 ? ShanheShopItemState.NoSkillSlot : ShanheShopItemState.RewardLimitReached;
            return new ShanheShopItemSnapshot(choice.Id, choice.Name,
                string.IsNullOrWhiteSpace(choice.Description) ? reward?.Description ?? "" : choice.Description,
                choice.GoldCost, choice.HpCost, reward, status, affordable, replacement, revision)
            { ReplaceableSkillIds = replacements };
        }));
    }

    private bool HasSkill(RunState state, string skillId) =>
        _printedSkillIds.Contains(skillId, StringComparer.Ordinal) || state.ExtraSkillIds.Contains(skillId, StringComparer.Ordinal);
    private bool HasTactic(RunState state, string skillId) => state.TacticIds.Any(id => _rewards[id].TacticSkillId is { } existing &&
        _tacticAcquisitionRules[existing].IdentityId == _tacticAcquisitionRules[skillId].IdentityId);
    private bool IsRewardUnlocked(ShanheRewardDefinition reward) =>
        _unlockedRewardIds is null ||
        (reward.SkillId is null && (reward.StartingEquipment?.Count ?? 0) == 0) ||
        _unlockedRewardIds.Contains(reward.Id);
    private bool CanReceiveReward(RunState state, ShanheRewardDefinition reward) =>
        IsRewardUnlocked(reward) &&
        (reward.SkillId is not { } skill || !HasSkill(state, skill)) &&
        (reward.OpeningSkillRewardIds is null || !state.TacticIds.Contains(reward.Id, StringComparer.Ordinal)) &&
        (reward.TacticSkillId is not { } tacticSkill || CanAcquireTactic(state, tacticSkill));
    private ShanheNodeChoiceDefinition[] AvailableChoices(RunState state, ShanheNodeDefinition node) =>
        NodeStock(state, node).Where(choice => !state.PurchasedChoiceIds.Contains(choice.Id, StringComparer.Ordinal) &&
            (choice.RewardId is not { } rewardId || CanReceiveReward(state, _rewards[rewardId])))
            .Select(choice => WithShopPrice(state, node, choice)).ToArray();

    private bool IsRandomShop(RunState state) => _options.UseCodexRewardPool &&
        state.Phase == ShanheCampaignPhase.NodeChoice && state.CurrentNodeId is { } id && _nodes[id].Kind == ShanheNodeKind.Shop;

    private IReadOnlyList<ShanheNodeChoiceDefinition> NodeStock(RunState state, ShanheNodeDefinition node) =>
        _options.UseCodexRewardPool && node.Kind == ShanheNodeKind.Shop ? state.ShopStock : node.Choices ?? [];

    private enum ShopRewardCategory { Supply, Tactic, Skill, Equipment, Card }
    private static ShopRewardCategory ShopCategory(ShanheRewardDefinition reward) =>
        reward.TacticSkillId is not null || reward.OpeningSkillRewardIds is not null || reward.Bonuses is { } bonus &&
            (bonus.MaxHp != 0 || bonus.InitialHand != 0 || bonus.Draw != 0 || bonus.Slash != 0) ? ShopRewardCategory.Tactic :
        reward.SkillId is not null ? ShopRewardCategory.Skill :
        reward.StartingEquipment?.Count > 0 ? ShopRewardCategory.Equipment :
        reward.StartingCards?.Count > 0 ? ShopRewardCategory.Card : ShopRewardCategory.Supply;

    private ShanheNodeChoiceDefinition WithShopPrice(RunState state, ShanheNodeDefinition node, ShanheNodeChoiceDefinition choice)
    {
        if (!_options.UseCodexRewardPool || node.Kind != ShanheNodeKind.Shop || choice.RewardId is not { } id) return choice;
        var purchases = ShopCategory(_rewards[id]) switch
        {
            ShopRewardCategory.Tactic => state.ShopTacticPurchases,
            ShopRewardCategory.Equipment => state.ShopEquipmentPurchases,
            ShopRewardCategory.Card => state.ShopCardPurchases,
            _ => 0
        };
        return choice with { GoldCost = checked(choice.GoldCost + purchases * 100) };
    }

    private IReadOnlyList<ShanheNodeChoiceDefinition> CreateShopStock(RunState state, ShanheNodeDefinition node)
    {
        var random = new DeterministicRandom(ShanheBattleSetup.DeriveSeed(_options.Seed, node.Id + ":shop", state.ShopRefreshCount));
        // Supply choices retain their authored prices and once-per-shop purchase state.
        var stock = (node.Choices ?? []).Where(choice => choice.RewardId is not { } id ||
            ShopCategory(_rewards[id]) == ShopRewardCategory.Supply).ToList();
        var previous = state.ShopStock.Select(choice => choice.RewardId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var category in new[] { ShopRewardCategory.Tactic, ShopRewardCategory.Skill, ShopRewardCategory.Equipment, ShopRewardCategory.Card })
        {
            var pricedRewardIds = (node.Choices ?? []).Where(choice => choice.RewardId is not null && choice.GoldCost != 9999)
                .Select(choice => choice.RewardId!).ToHashSet(StringComparer.Ordinal);
            var candidates = Definition.Rewards.Where(reward => pricedRewardIds.Contains(reward.Id) &&
                    ShopCategory(reward) == category && CanReceiveReward(state, reward))
                .DistinctBy(RewardAcquisitionIdentity).ToList();
            random.Shuffle(candidates);
            // Prefer new faces on refresh when the real catalogue has enough stock.
            if (state.ShopRefreshCount > 0) candidates = candidates.OrderBy(reward => previous.Contains(reward.Id)).ToList();
            foreach (var reward in candidates.Take(3 + random.Next(2)))
            {
                var authored = node.Choices!.First(choice => choice.RewardId == reward.Id && choice.GoldCost != 9999);
                stock.Add(new($"$shop:{state.ShopRefreshCount}:{reward.Id}", reward.Name, authored.GoldCost,
                    authored.HpCost, reward.Id, LeavesNode: false) { Description = reward.Description });
            }
        }
        return ShanheCampaignContent.Freeze(stock);
    }

    private static void Require(bool condition, string message) => ShanheCampaignContent.Require(condition, message);
}
