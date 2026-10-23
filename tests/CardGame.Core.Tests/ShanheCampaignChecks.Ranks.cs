using System.Text.Json;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static partial class ShanheCampaignChecks
{
    public static void GeneralRankBonusesFreezeOnceAndLegacyRecipesStayUnchanged()
    {
        ShanheTacticBonuses[] privileges = [new(), new(InitialHand: 1), new(MaxHp: 1, InitialHand: 1, Slash: 1),
            new(MaxHp: 1, InitialHand: 2, Draw: 1, Slash: 1), new(MaxHp: 2, InitialHand: 2, Draw: 1, Slash: 1)];
        var definition = RankDefinition() with { StartingBonuses = new(1, 1, 1, 1) };
        foreach (var rank in new int?[] { null, 1, 2, 3, 4, 5 })
        {
            var privilege = privileges[(rank ?? 1) - 1];
            Require(ShanheGeneralRanks.GetBonuses(rank ?? 1) == privilege, "Rank privileges must match the verified tier table.");
            var session = ShanheCampaignSession.Create(definition, new(71, Player) { PlayerRank = rank }, Registry);
            var expected = new ShanheTacticBonuses(1 + privilege.MaxHp, 1 + privilege.InitialHand, 1 + privilege.Draw, 1 + privilege.Slash);
            Require(session.Snapshot.PlayerRank == (rank ?? 1) && !session.Snapshot.RebirthUsed &&
                session.Snapshot.Bonuses == expected && session.Snapshot.Hp == 4 + expected.MaxHp &&
                session.Snapshot.MaxHp == session.Snapshot.Hp && session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.ExtraSkillSlots == 0,
                "The selected rank adds to starting bonuses once, starts at its persistent maximum and spends no learned-skill slot.");
            var source = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            session = RestoreRankRun(session, definition);
            Require(session.Snapshot.Bonuses == expected && session.CreateCheckpoint().Options.PlayerRank == rank,
                "Cold reconstruction must retain the original nullable rank and cannot apply its growth again.");
            Accept(session, new MoveShanheNodeCommand("first", session.Revision));
            var setup = session.PendingBattleSetup!;
            Require(setup.PlayerRank == rank && setup.RebirthAvailable == (rank == 5) &&
                setup.ExtraSkillIds.Contains(ShanheGeneralRanks.RebirthSkillId) == (rank == 5) && session.Snapshot.ExtraSkillIds.Count == 0,
                "Only rank five adds the separate unspent rebirth to the actual battle recipe, keeping it outside replaceable skill slots.");
            var battle = CreateBattle(setup);
            var player = battle.CreateSnapshot(0).Players[0];
            Require(player.MaxHp == 4 + expected.MaxHp && player.Hp == player.MaxHp && player.HandCount == 4 + expected.InitialHand,
                "The native encounter receives the exact once-added rank health and opening-hand bonuses.");
            Cold(session, definition);
            if (rank is null)
                Require(!source.Contains("\"PlayerRank\"", StringComparison.Ordinal) &&
                    !JsonSerializer.Serialize(setup).Contains("\"PlayerRank\"", StringComparison.Ordinal) &&
                    !JsonSerializer.Serialize(setup).Contains("\"RebirthAvailable\"", StringComparison.Ordinal),
                    "Unranked historical options and battle recipes omit the new fields instead of changing legacy checkpoint text.");
        }
        MustThrow(() => ShanheCampaignSession.Create(definition, new(71, Player) { PlayerRank = 0 }, Registry));
        MustThrow(() => ShanheCampaignSession.Create(definition, new(71, Player) { PlayerRank = 6 }, Registry));
    }

    public static void FifthRankRebirthUsageSurvivesTwoBattlesAndColdVictoryOrDefeat()
    {
        var definition = RankDefinition();
        foreach (var useFirstBattle in new[] { true, false })
        {
            var session = ShanheCampaignSession.Create(definition, new(71, Player) { PlayerRank = 5 }, Registry);
            Accept(session, new MoveShanheNodeCommand("first", session.Revision));
            var first = BeginRankBattle(session.PendingBattleSetup!);
            if (useFirstBattle) first = UseRankRebirth(first);
            FinishRankBattle(first, victory: true);
            Require(session.CompleteBattle(first, session.Revision).Accepted && session.Snapshot.Phase == ShanheCampaignPhase.Map &&
                session.Snapshot.RebirthUsed == useFirstBattle && session.Snapshot.ExtraSkillIds.Count == 0,
                "A replay-verified first victory records only an actually consumed rebirth, never an unused grant or learned-skill slot.");
            session = RestoreRankRun(session, definition);
            Accept(session, new MoveShanheNodeCommand("second", session.Revision));
            var next = session.PendingBattleSetup!;
            Require(next.RebirthAvailable == !useFirstBattle &&
                next.ExtraSkillIds.Contains(ShanheGeneralRanks.RebirthSkillId) == !useFirstBattle && session.Snapshot.RebirthUsed == useFirstBattle,
                "The next encounter carries an unused campaign rebirth, while a spent one is absent even after cold journal reconstruction.");
            session = RestoreRankRun(session, definition);
            var second = BeginRankBattle(session.PendingBattleSetup!);
            if (!useFirstBattle) second = UseRankRebirth(second);
            FinishRankBattle(second, victory: useFirstBattle);
            Require(session.CompleteBattle(second, session.Revision).Accepted && session.Snapshot.RebirthUsed &&
                session.Snapshot.Phase == (useFirstBattle ? ShanheCampaignPhase.Won : ShanheCampaignPhase.Lost) &&
                session.Snapshot.Bonuses == ShanheGeneralRanks.GetBonuses(5),
                "Actual rebirth consumption survives both final victory and a later genuine defeat, with no second application of rank growth.");
            RestoreRankRun(session, definition);
        }
    }

    private static ShanheCampaignDefinition RankDefinition() => new("fixture:rank-campaign", "升阶征程", [Player], ["first"],
        [new("first", "首战", ShanheNodeKind.Battle, ["second"], [new("guard", "守军", [new(Enemy)])]),
         new("second", "再战", ShanheNodeKind.Battle, [], [new("guard", "守军", [new(Enemy)])])], [], ExtraSkillSlots: 0);

    private static ShanheCampaignSession RestoreRankRun(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var checkpoint = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(checkpoint), definition, Registry);
        Require(ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == checkpoint && Snapshot(restored) == Snapshot(session),
            "The original rank options, battle proofs, current recipe and expedition rebirth consumption must all derive identically from cold command replay.");
        return restored;
    }

    private static GameEngine BeginRankBattle(ShanheBattleSetup setup)
    {
        var battle = CreateBattle(setup);
        Accept(battle, new StartGameCommand());
        ReachRankBoundary(battle, () => battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return battle;
    }

    private static GameEngine UseRankRebirth(GameEngine battle)
    {
        var hand = battle.CreateSnapshot(0).Players[0].HandCount;
        Accept(battle, new UseProgramSkillCommand(0, Force, "surrender", [], [], battle.Revision, battle.PendingDecision!.PromptId));
        static bool Rebirth(PromptChoice choice) => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            choice.Parameters.GetValueOrDefault("skill-id") == ShanheGeneralRanks.RebirthSkillId;
        ReachRankBoundary(battle, () => battle.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } prompt && prompt.Choices.Any(Rebirth));
        var checkpoint = GameCheckpointJson.Serialize(battle.CreateCheckpoint());
        var before = SnapshotJson.Serialize(battle.CreateSnapshot(0));
        battle = GameReplay.Restore(GameCheckpointJson.Deserialize(checkpoint), Registry(battle.CreateCheckpoint().Options.ShanheBattle!));
        Require(SnapshotJson.Serialize(battle.CreateSnapshot(0)) == before && GameCheckpointJson.Serialize(battle.CreateCheckpoint()) == checkpoint,
            "A real self-dying rebirth offer survives cold battle replay before its once-only usage is consumed.");
        var decision = battle.PendingDecision!;
        Accept(battle, new AnswerPromptCommand(0, decision.PromptId, decision.Choices.Single(Rebirth).Id, battle.Revision));
        ReachRankBoundary(battle, () => battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(battle.CreateSnapshot(0).Players[0] is { Hp: 5 } player && player.HandCount == hand + 5 &&
            battle.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Count(e => e.SkillOwnerSeat == 0 &&
                e.SkillId == ShanheGeneralRanks.RebirthSkillId && e.Scope == SkillUsageScope.Game && e.Count == 1) == 1,
            "The real production rebirth produces one native game usage receipt and gains five cards while recovering to five HP.");
        return battle;
    }

    private static void FinishRankBattle(GameEngine battle, bool victory)
    {
        Accept(battle, new UseProgramSkillCommand(0, Force, victory ? "defeat" : "surrender", [], victory ? [1] : [],
            battle.Revision, battle.PendingDecision!.PromptId));
        ReachRankBoundary(battle, () => battle.State.Status == EngineStatus.Completed);
    }

    private static void ReachRankBoundary(GameEngine battle, Func<bool> arrived)
    {
        for (var step = 0; step < 60; step++)
        {
            if (arrived()) return;
            Require(battle.PendingDecision?.PlayerSeat != 0, "Unexpected player input before the bounded rank fixture reached its native boundary.");
            Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        }
        throw new InvalidOperationException("The bounded rank fixture did not reach its expected native boundary.");
    }
}
