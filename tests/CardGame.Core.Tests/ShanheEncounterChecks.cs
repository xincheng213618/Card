using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheEncounterChecks
{
    public static void PreviousExpeditionsKeepExactRoutesAndPendingRecipesCold()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var catalog = typeof(StandardShanheCampaignCatalog);
        var previous = catalog.GetMethod("CreateReturning2026Definition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var threeBosses = catalog.GetMethod("WithS5ThreeBossEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var coalition = catalog.GetMethod("WithS5CoalitionBossEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var strategy = catalog.GetMethod("WithS5StrategyBossEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var hebei = catalog.GetMethod("WithS5HebeiBossEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var lords = catalog.GetMethod("WithS5LordGroupEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var liGuo = catalog.GetMethod("WithS5LiGuoBossEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var regional = catalog.GetMethod("WithS5RegionalGroupEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var court = catalog.GetMethod("WithS5CourtGroupEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var armed = catalog.GetMethod("WithS5ArmedSourceEncounters", BindingFlags.Static | BindingFlags.NonPublic)!;
        var codex = catalog.GetMethod("WithCodexRewards", BindingFlags.Static | BindingFlags.NonPublic)!;
        var resources = catalog.GetMethod("WithStartingResourcesContract", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var tier in new[] { 1, 2, 3 })
        foreach (var useCodex in new[] { false, true })
        {
            var old = (ShanheCampaignDefinition)previous.Invoke(null, [tier])!;
            old = (ShanheCampaignDefinition)threeBosses.Invoke(null, [old])!;
            old = (ShanheCampaignDefinition)coalition.Invoke(null, [old])!;
            old = (ShanheCampaignDefinition)strategy.Invoke(null, [old])!;
            old = (ShanheCampaignDefinition)hebei.Invoke(null, [old])!;
            old = (ShanheCampaignDefinition)lords.Invoke(null, [old])!;
            old = (ShanheCampaignDefinition)liGuo.Invoke(null, [old])!;
            var earlier = useCodex ? (ShanheCampaignDefinition)codex.Invoke(null, [old, edition])! : old;
            earlier = (ShanheCampaignDefinition)resources.Invoke(null, [earlier])!;
            var unsupportedEarlierHash = ShanheCampaignContent.GetContentHash(earlier);
            old = (ShanheCampaignDefinition)regional.Invoke(null, [old])!;
            var regionalEarlier = useCodex ? (ShanheCampaignDefinition)codex.Invoke(null, [old, edition])! : old;
            regionalEarlier = (ShanheCampaignDefinition)resources.Invoke(null, [regionalEarlier])!;
            var unsupportedRegionalHash = ShanheCampaignContent.GetContentHash(regionalEarlier);
            old = (ShanheCampaignDefinition)court.Invoke(null, [old])!;
            var courtEarlier = useCodex ? (ShanheCampaignDefinition)codex.Invoke(null, [old, edition])! : old;
            courtEarlier = (ShanheCampaignDefinition)resources.Invoke(null, [courtEarlier])!;
            var unsupportedCourtHash = ShanheCampaignContent.GetContentHash(courtEarlier);
            old = (ShanheCampaignDefinition)armed.Invoke(null, [old])!;
            if (useCodex) old = (ShanheCampaignDefinition)codex.Invoke(null, [old, edition])!;
            var unsupportedOlderHash = ShanheCampaignContent.GetContentHash(old);
            old = (ShanheCampaignDefinition)resources.Invoke(null, [old])!;
            var current = useCodex ? StandardShanheCampaignCatalog.CreateCodexDefinition(tier, edition)
                : StandardShanheCampaignCatalog.CreateDefinition(tier, edition);
            Need(ShanheCampaignContent.GetContentHash(old) != ShanheCampaignContent.GetContentHash(current),
                "The new encounter pool has a distinct complete content fingerprint.");
            var session = ShanheCampaignSession.Create(old, new(83, "classic:guan-yu") { UseCodexRewardPool = useCodex },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(session.Submit(new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.First().Id, session.Revision)).Accepted,
                "The previous expedition accepts its real departure command.");
            if (session.Snapshot.Phase == ShanheCampaignPhase.NodeChoice)
                Need(session.Submit(new ChooseShanheNodeOptionCommand(session.Snapshot.NodeChoices.First().Id, null, session.Revision)).Accepted,
                    "The old departure choice enters its real pending encounter.");
            if (session.PendingBattleSetup is null)
                Need(session.Submit(new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.First().Id, session.Revision)).Accepted,
                    "Leaving the departure enters the original route's real battle.");
            Need(session.PendingBattleSetup is not null, "Compatibility is exercised with a prepared battle recipe.");
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var checkpoint = ShanheCampaignJson.Deserialize(encoded);
            var selected = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint, tier);
            var recovered = ShanheCampaignSession.Restore(checkpoint, selected, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheCampaignContent.GetContentHash(selected) == checkpoint.CampaignHash &&
                !selected.Nodes.SelectMany(n => n.Encounters ?? []).Any(IsCurrentAddition) &&
                selected.StartingResourcesExpireOnUse && ShanheCampaignJson.Serialize(recovered.CreateCheckpoint()) == encoded &&
                ShanheBattleSetup.ValueEquals(session.PendingBattleSetup, recovered.PendingBattleSetup),
                "All old routes, current resource semantics, accepted commands and exact pending recipes survive cold recovery without new bosses.");
            var rejected = false;
            try { StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint with { CampaignHash = new string('0', 64) }, tier); }
            catch (ArgumentException) { rejected = true; }
            Need(rejected && ShanheCampaignJson.Serialize(checkpoint) == encoded, "Fallbacks still require the full original fingerprint.");
            foreach (var unsupported in new[] { unsupportedOlderHash, unsupportedEarlierHash, unsupportedRegionalHash, unsupportedCourtHash })
            {
                rejected = false;
                try { StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint with { CampaignHash = unsupported }, tier); }
                catch (ArgumentException) { rejected = true; }
                Need(rejected, "Only the immediately preceding expedition is selected; earlier resource and encounter definitions are not accumulated.");
            }
        }
    }

    public static void ActualEncounterRecipesAndConditionalPackagesRecoverCold()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var current = StandardShanheCampaignCatalog.CreateDefinition(1, edition);
        var encounters = current.Nodes.SelectMany(n => n.Encounters ?? []).Where(IsSourceEncounter).ToArray();
        Need(encounters.Length == 37 && StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId)
            .Nodes.SelectMany(n => n.Encounters ?? []).All(e => !IsSourceEncounter(e)), "Only the 2026 edition includes these verified source forms.");
        foreach (var encounter in encounters)
        {
            var expectedEnemies = encounter.Id switch
            {
                "guangcheng:yuanshu-s5" or "huanyuan:taoyuan-s5" or "huanyuan:moushi-s5" or "huanyuan:xiliang-dark-s5" => 3,
                "dagu:xuyou-jushou-s5" or "huanyuan:caoren-zhaoyun-s5" or
                "dagu:gongsun-zan-yue-s5" or "huanyuan:gongsun-zan-zhaoyun-s5" or "dagu:xujing-xuqiu-s5" or
                "dagu:ma-siblings-s5" or "huanyuan:dongwu-veterans-s5" or "dagu:han-emperors-s5" or
                "huanyuan:sunjian-wuguotai-s5" or "huanyuan:xuncan-caoshi-s5" or
                "dagu:dianwei-weizi-s5" or "hulao:xiahou-pair-s5" => 2,
                _ => 1
            };
            Need(encounter.Enemies.Count == expectedEnemies && (encounter.Allies?.Count ?? 0) == 0,
                "The actual local recipe preserves the source enemy count without an invented companion.");
            var route = new ShanheCampaignDefinition("fixture-shanhe-source-bosses", "S5首领配方", ["classic:guan-yu"], ["boss"],
                [new("boss", "首领", ShanheNodeKind.Battle, [], Encounters: [encounter])], []);
            var session = ShanheCampaignSession.Create(route, new(83, "classic:guan-yu"), StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(session.Submit(new MoveShanheNodeCommand("boss", session.Revision)).Accepted, "The real map command prepares the new encounter.");
            var setup = session.PendingBattleSetup!;
            var encoded = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
            var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(encoded), route, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(ShanheBattleSetup.ValueEquals(setup, restored.PendingBattleSetup) && ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == encoded,
                "Each new pending roster recovers through actual campaign commands.");
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(game.Submit(new StartGameCommand()).Accepted && game.PlayerCount == expectedEnemies + 1 &&
                encounter.Enemies.All(enemy => registry.Generals.ContainsKey(enemy.GeneralId)),
                "Each conditionally composed content package starts its actual native battle.");
            foreach (var enemy in encounter.Enemies.Where(enemy => registry.Generals[enemy.GeneralId].SkillId is null))
            {
                var seat = encounter.Enemies.ToList().FindIndex(member => member.GeneralId == enemy.GeneralId) + 1;
                Need(registry.Generals[enemy.GeneralId].SkillIds.Count == 0 &&
                    game.CreateSnapshot(0).Players[seat].Skills is { Count: 0 },
                    "A source skillless body starts with zero actual skill grants, including the published opponent view.");
            }
            var saved = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            var recovered = GameReplay.Restore(GameCheckpointJson.Deserialize(saved), registry);
            Need(GameCheckpointJson.Serialize(recovered.CreateCheckpoint()) == saved &&
                SnapshotJson.Serialize(recovered.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                "The native game restores its content hash, command prefix and player view.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] };
            var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(plain);
            var ordinaryHash = ordinary.ContentHash;
            foreach (var enemy in encounter.Enemies)
            foreach (var skill in registry.Generals[enemy.GeneralId].SkillIds)
            {
                var granted = StandardShanheCampaignCatalog.CreateBattleRegistry(plain with { ExtraSkillIds = [skill] });
                Need(granted.Skills.ContainsKey(skill) && (!ordinary.Skills.ContainsKey(skill) || !granted.Generals.ContainsKey(enemy.GeneralId)),
                    "Explicit skill grants are available; reusing an ordinary skill does not import an unearned S5 general package.");
            }
            Need(encounter.Enemies.All(enemy => !ordinary.Generals.ContainsKey(enemy.GeneralId)) &&
                StandardShanheCampaignCatalog.CreateBattleRegistry(plain).ContentHash == ordinaryHash,
                "Warming boss and skill registries cannot change an ordinary recipe's content.");
        }
    }

    private static bool IsCurrentAddition(ShanheEncounterDefinition encounter) => encounter.Id is
        "dagu:dianwei-weizi-s5" or "hulao:xiahou-pair-s5" or "huanyuan:zhangniujiao-s5" or "luoyang:simayi-s5";

    private static bool IsArmedAddition(ShanheEncounterDefinition encounter) => encounter.Id is
        "dagu:ma-pair-s5" or "dagu:xurong-s5" or "hulao:lvbu-2644-s5" or
        "hulao:lvbu-2645-s5" or "huanyuan:xiliang-dark-s5";

    private static bool IsSourceEncounter(ShanheEncounterDefinition encounter) => IsCurrentAddition(encounter) || IsArmedAddition(encounter) || encounter.Id is
        "dagu:han-emperors-s5" or "huanyuan:sunjian-wuguotai-s5" or "huanyuan:xuncan-caoshi-s5" or
        "luoyang:dongzhuo-2670-s5" or "luoyang:dongzhuo-2671-s5" or
        "dagu:ma-siblings-s5" or "huanyuan:dongwu-veterans-s5" or "huanyuan:moushi-s5" or "hulao:huaxiong-base-s5" or
        "guangcheng:lijue-s5" or "guangcheng:guosi-s5" or "dagu:xujing-xuqiu-s5" or
        "guangcheng:yuanshu-s5" or "dagu:gongsun-zan-yue-s5" or "huanyuan:gongsun-zan-zhaoyun-s5" or "huanyuan:taoyuan-s5" or
        "guangcheng:zhangliang-s5" or "dagu:yuanshao-s5" or "huanyuan:xuchu-s5" or
        "hulao:lukuang-luxiang-s5" or "huanyuan:chenlin-s5" or "hulao:zhuling-s5" or "huanyuan:zhouyu-s5" or
        "dagu:xuyou-jushou-s5" or "huanyuan:caoren-zhaoyun-s5" or
        "dagu:yanliang-wenchou-s5" or "hulao:quyi-s5" or "huanyuan:zhanghe-s5";

    private static void Need(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
