using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheEncounterChecks
{
    public static void ActualEncounterRecipesAndConditionalPackagesRecoverCold()
    {
        const string edition = StandardShanheCampaignCatalog.Returning2026EditionId;
        var current = StandardShanheCampaignCatalog.CreateDefinition(1, edition);
        var encounters = current.Nodes.SelectMany(n => n.Encounters ?? []).Where(IsSourceEncounter).ToArray();
        Need(encounters.Length == 41 && StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId)
            .Nodes.SelectMany(n => n.Encounters ?? []).All(e => !IsSourceEncounter(e)), "Only the 2026 edition includes these verified source forms.");
        foreach (var encounter in encounters)
        {
            var expectedEnemies = encounter.Id switch
            {
                "guangcheng:yuanshu-s5" or "huanyuan:taoyuan-s5" or "huanyuan:moushi-s5" or "huanyuan:xiliang-dark-s5" or
                "huanyuan:cao-wei-pioneers-s5" => 3,
                "dagu:xuyou-jushou-s5" or "huanyuan:caoren-zhaoyun-s5" or
                "dagu:gongsun-zan-yue-s5" or "huanyuan:gongsun-zan-zhaoyun-s5" or "dagu:xujing-xuqiu-s5" or
                "dagu:ma-siblings-s5" or "huanyuan:dongwu-veterans-s5" or "dagu:han-emperors-s5" or
                "huanyuan:sunjian-wuguotai-s5" or "huanyuan:xuncan-caoshi-s5" or
                "dagu:dianwei-weizi-s5" or "hulao:xiahou-pair-s5" or "dagu:zuoci-huatuo-s5" => 2,
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
                    $"Explicit skill {skill} from {encounter.Id} is available; reusing an ordinary skill does not import an unearned S5 general package.");
            }
            Need(encounter.Enemies.All(enemy => !ordinary.Generals.ContainsKey(enemy.GeneralId)) &&
                StandardShanheCampaignCatalog.CreateBattleRegistry(plain).ContentHash == ordinaryHash,
                "Warming boss and skill registries cannot change an ordinary recipe's content.");
        }
    }

    private static bool IsCurrentAddition(ShanheEncounterDefinition encounter) => encounter.Id is
        "guangcheng:zhangxiu-s5" or "huanyuan:cao-wei-pioneers-s5" or "dagu:zuoci-huatuo-s5" or "hulao:zhangliao-armed-s5";

    private static bool IsVanguardAddition(ShanheEncounterDefinition encounter) => encounter.Id is
        "dagu:dianwei-weizi-s5" or "hulao:xiahou-pair-s5" or "huanyuan:zhangniujiao-s5" or "luoyang:simayi-s5";

    private static bool IsArmedAddition(ShanheEncounterDefinition encounter) => encounter.Id is
        "dagu:ma-pair-s5" or "dagu:xurong-s5" or "hulao:lvbu-2644-s5" or
        "hulao:lvbu-2645-s5" or "huanyuan:xiliang-dark-s5";

    private static bool IsSourceEncounter(ShanheEncounterDefinition encounter) => IsCurrentAddition(encounter) || IsVanguardAddition(encounter) || IsArmedAddition(encounter) || encounter.Id is
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
