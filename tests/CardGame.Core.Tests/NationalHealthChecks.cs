using CardGame.Content.Standard;
using CardGame.Core;

internal static class NationalHealthChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void SetupAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithNationalWarLite();
        foreach (var pair in new[] { ("guo-jia", "xun-yu", 3), ("cao-cao", "guo-jia", 3), ("guo-jia", "cao-cao", 3), ("cao-cao", "ganglie", 4) })
        {
            GameEngine? fixture = null;
            for (var seed = 721000; seed < 721200 && fixture is null; seed++)
            {
                var game = GameEngine.CreateStandard(new GameOptions
                {
                    Seed = seed,
                    HumanSeat = 0,
                    HumanRole = null,
                    PlayerCount = 4,
                    ModeId = "national:lite-4",
                    UseInteractiveSetup = true,
                    AdvanceAfterHumanCommands = false
                }, registry);
                game.Submit(new StartGameCommand());
                var selected = 0;
                var usable = true;
                for (var step = 0; step < 100 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
                {
                    if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
                    {
                        Require(game.CreateSnapshot(1).PendingDecision is null, "General health previews leaked to another viewer.");
                        var id = "national:wei-" + (selected == 0 ? pair.Item1 : pair.Item2);
                        if (!prompt.ValidContentIds.Contains(id)) { usable = false; break; }
                        var choice = prompt.Choices.Single(choice => choice.ContentIds.Contains(id));
                        Require(choice.Parameters["base-hp"] == registry.Generals[id].BaseHp.ToString(), "Primary base HP preview differs from registered content.");
                        if (selected == 0) Require(!choice.Parameters.ContainsKey("combined-max-hp"), "First general pretends the pair is already selected.");
                        else Require(choice.Parameters["combined-max-hp"] == pair.Item3.ToString(), "Secondary preview differs from the expected pair health.");
                        Require(game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted, "Health selection failed.");
                        selected++;
                        var checkpoint = game.CreateCheckpoint();
                        var replay = GameReplay.Restore(checkpoint, registry);
                        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)), "Partial dual-general selection did not replay.");
                    }
                    else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Health setup stalled.");
                }
                if (usable && selected == 2 && game.State.Status == EngineStatus.AwaitingHumanPlay) fixture = game;
            }
            Require(fixture is not null, "Bounded fixtures could not select the requested health pair.");
            var current = fixture!.CreateSnapshot(0, true);
            Require(current.Players[0].MaxHp == pair.Item3 && current.Players[0].Hp == pair.Item3, "Initial health does not equal the confirmed pair maximum.");
            foreach (var player in current.Players)
                Require(player.Hp == player.MaxHp && player.MaxHp == (registry.Generals[player.GeneralId].BaseHp + registry.Generals[player.SecondaryGeneralId!].BaseHp) / 2,
                    "AI and human dual-general health initialization differ.");
            Require(fixture.Submit(new RevealGeneralCommand(0, GeneralSelectionSlot.Primary, fixture.Revision, fixture.PendingDecision!.PromptId)).Accepted, "Reveal failed after health initialization.");
            Require(fixture.CreateSnapshot(0).Players[0].Hp == pair.Item3 && fixture.CreateSnapshot(0).Players[0].MaxHp == pair.Item3, "Reveal silently healed or changed maximum HP.");
        }
        var immediate = GameEngine.CreateStandard(new GameOptions { Seed = 721022, HumanSeat = -1, HumanRole = null, PlayerCount = 4, ModeId = "national:lite-4", UseInteractiveSetup = false }, registry);
        Require(immediate.CreateSnapshot(-1, true).Players.All(player => player.Hp == player.MaxHp && player.MaxHp ==
            (registry.Generals[player.GeneralId].BaseHp + registry.Generals[player.SecondaryGeneralId!].BaseHp) / 2), "Non-interactive setup bypassed dual-general health.");
    }

    public static void ContentIntegrity()
    {
        ContentRegistry Registry(int hp) => ContentRegistry.Build(new StandardContentPackage(), new VitalsPackage(hp));
        Require(Registry(3).ContentHash != Registry(4).ContentHash, "General health changes are missing from content drift detection.");
        foreach (var invalid in new[] { 0, -1, 21, int.MaxValue })
        {
            var rejected = false;
            try { Registry(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid base HP entered the registry.");
        }
        var current = StandardContentRegistry.CreateWithRescueSkillsAndTeamModesAndNationalWarLite();
        Require(current.Generals["national:wei-guo-jia"].BaseHp == 3 &&
                current.Generals["national:wei-xun-yu"].BaseHp == 3,
            "Current national generals must preserve their declared health.");
    }

    private sealed class VitalsPackage(int hp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("test-vitals", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddGeneral(new("test:general", "体力测试", "", "standard:none", BaseHp: hp));
    }
}
