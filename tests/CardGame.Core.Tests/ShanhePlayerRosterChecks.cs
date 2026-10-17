using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanhePlayerRosterChecks
{
    private static readonly string[] LegacyRoster =
    [
        "classic:guan-yu", "classic:zhao-yun", "classic:zhang-fei", "classic:ma-chao",
        "classic:sun-quan", "classic:cao-cao", "classic:huang-yueying", "classic:zhou-yu"
    ];
    private const string ExpandedPlayer = "classic:lu-xun";

    public static void OrdinaryRosterStartsRealExpandedGeneralBattleAndRestoresCold()
    {
        var registry = StandardShanheCampaignCatalog.PlayerRosterRegistry;
        var roster = StandardShanheCampaignCatalog.GetPlayerGeneralIds();
        var ordinary = new[] { "identity:classic-8", "identity:classic-boundary-8" }
            .SelectMany(id => registry.Modes[id].GeneralPoolIds!).ToHashSet(StringComparer.Ordinal);
        Require(roster.Count > LegacyRoster.Length && roster.Take(LegacyRoster.Length).SequenceEqual(LegacyRoster) &&
            roster.Distinct(StringComparer.Ordinal).Count() == roster.Count &&
            roster.All(id => ordinary.Contains(id) && registry.IsGeneralPlayable(id)),
            "The expanded roster must preserve the original eight first and contain unique playable ordinary identity generals.");
        Require(roster.Contains(ExpandedPlayer) && !LegacyRoster.Contains(ExpandedPlayer) &&
            !roster.Contains(StandardShanheBossPackage.ShenguiLvbuId) && !roster.Contains(StandardShanheBossPackage.FenghuangId) &&
            !roster.Any(id => id.StartsWith("fan:", StringComparison.Ordinal) || id.StartsWith("national:", StringComparison.Ordinal) ||
                id.StartsWith("composed:", StringComparison.Ordinal) || id.Contains("demo-", StringComparison.Ordinal)),
            "Encounter forms, fan-made generals, national-war identities and demonstration generals are not expedition choices.");
        RejectMutation(roster);

        var definition = StandardShanheCampaignCatalog.CreateDefinition();
        Require(definition.PlayerGeneralIds.SequenceEqual(roster), "Both catalogue and UI must consume the same frozen roster.");
        var session = Create(definition, ExpandedPlayer);
        Accept(session, new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.Single().Id, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("supplies", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand(session.Snapshot.AvailableNodes.Single().Id, session.Revision));
        var setup = session.PendingBattleSetup ?? throw new InvalidOperationException("The real first battle was not prepared.");
        var battleRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        Require(roster.All(battleRegistry.IsGeneralPlayable), "Every advertised choice must exist in the actual battle content source.");
        var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var start = battle.Submit(new StartGameCommand());
        Require(start.Accepted && battle.CreateSnapshot(0).Players.Single(player => player.IsHuman).GeneralId == ExpandedPlayer &&
            battle.State.Status != EngineStatus.NotStarted,
            "An added general must enter a real battle and pass its actual start command.");
        var coldBattle = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(battle.CreateCheckpoint())), battleRegistry);
        Require(JsonSerializer.Serialize(battle.CreateSnapshot(0)) == JsonSerializer.Serialize(coldBattle.CreateSnapshot(0)),
            "The added general's real started battle retains the ordinary replay fingerprint gate.");
        var restored = Restore(session.CreateCheckpoint());
        Require(JsonSerializer.Serialize(session.Snapshot) == JsonSerializer.Serialize(restored.Snapshot) &&
            ShanheBattleSetup.ValueEquals(session.PendingBattleSetup!, restored.PendingBattleSetup!),
            "A cold campaign replay must preserve the expanded player's route and exact pending battle identity.");
    }

    public static void SavedRostersFreezeLegacyCompatibilityAndRejectForgedPools()
    {
        foreach (var edition in new[] { StandardShanheCampaignCatalog.LocalEditionId, StandardShanheCampaignCatalog.Returning2026EditionId })
        {
            var current = StandardShanheCampaignCatalog.CreateDefinition(1, edition);
            var legacy = Create(current with { PlayerGeneralIds = LegacyRoster }, LegacyRoster[0]).CreateCheckpoint() with { PlayerGeneralIds = null };
            var legacyJson = ShanheCampaignJson.Serialize(legacy);
            Require(!legacyJson.Contains("\"PlayerGeneralIds\"", StringComparison.Ordinal),
                "The additive roster field must remain absent from a genuine fixed-roster legacy checkpoint.");
            var restoredLegacy = Restore(ShanheCampaignJson.Deserialize(legacyJson));
            Require(restoredLegacy.Definition.PlayerGeneralIds.SequenceEqual(LegacyRoster) && restoredLegacy.ContentHash == legacy.CampaignHash,
                "A legacy checkpoint restores only its exact eight-general definition and original campaign fingerprint.");

            var savedRoster = LegacyRoster.Append(ExpandedPlayer).ToList();
            var partialSession = Create(current with { PlayerGeneralIds = savedRoster }, ExpandedPlayer);
            var partialDefinition = partialSession.Definition;
            var saved = partialSession.CreateCheckpoint();
            var frozenRoster = saved.PlayerGeneralIds ?? throw new InvalidOperationException("New checkpoints must retain their player roster.");
            savedRoster.Clear();
            Require(frozenRoster.Count == LegacyRoster.Length + 1, "Checkpoint publication must detach the definition's source list.");
            RejectMutation(frozenRoster);
            var restored = Restore(saved);
            Require(restored.Definition.PlayerGeneralIds.SequenceEqual(LegacyRoster.Append(ExpandedPlayer)) &&
                restored.ContentHash == saved.CampaignHash && restored.Definition.PlayerGeneralIds.Count < current.PlayerGeneralIds.Count,
                "A saved roster remains exact when the current catalogue offers additional generals.");

            var callerRoster = frozenRoster.ToList();
            var detached = saved with { PlayerGeneralIds = callerRoster };
            callerRoster.Clear();
            Require(detached.PlayerGeneralIds!.Count == frozenRoster.Count, "The checkpoint roster init accessor must freeze caller-owned lists too.");
            RejectMutation(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(saved)).PlayerGeneralIds!);
            MustThrow(() => Restore(saved with { CampaignHash = "invalid" }));
            MustThrow(() => Restore(saved with { PlayerGeneralIds = frozenRoster.Reverse().ToArray() }));
            MustThrow(() => Restore(saved with { PlayerGeneralIds = null }));
            MustThrow(() => Restore(legacy with { CampaignHash = ShanheCampaignContent.GetContentHash(current) }));
            MustThrow(() => Restore(saved with { PlayerGeneralIds = frozenRoster.Append(ExpandedPlayer).ToArray() }));
            MustThrow(() => ShanheCampaignSession.Restore(saved with { PlayerGeneralIds = LegacyRoster }, partialDefinition,
                StandardShanheCampaignCatalog.CreateBattleRegistry));

            var bossRoster = LegacyRoster.Append(StandardShanheBossPackage.ShenguiLvbuId).ToArray();
            var forged = saved with
            {
                PlayerGeneralIds = bossRoster,
                CampaignHash = ShanheCampaignContent.GetContentHash(current with { PlayerGeneralIds = bossRoster })
            };
            MustThrow(() => Restore(forged));
        }
    }

    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition, string player) =>
        ShanheCampaignSession.Create(definition, new(71, player), StandardShanheCampaignCatalog.CreateBattleRegistry);

    private static ShanheCampaignSession Restore(ShanheCampaignCheckpoint checkpoint)
    {
        var cold = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(checkpoint));
        return ShanheCampaignSession.Restore(cold, StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(cold, 1),
            StandardShanheCampaignCatalog.CreateBattleRegistry);
    }

    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var result = session.Submit(command);
        Require(result.Accepted, result.Error ?? "Campaign command rejected.");
    }

    private static void RejectMutation(IReadOnlyList<string> values)
    {
        Require(values is IList<string> { IsReadOnly: true }, "Published player rosters must be read-only.");
        try { ((IList<string>)values)[0] = values[0]; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A published player roster accepted mutation.");
    }

    private static void MustThrow(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid campaign checkpoint was accepted.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
