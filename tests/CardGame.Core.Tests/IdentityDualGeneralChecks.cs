using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class IdentityDualGeneralChecks
{
    private const string Mode = "identity:classic-dual-fixture-8";
    private const string Primary = "fixture:dual-primary";
    private const string Secondary = "fixture:dual-secondary";

    public static void SelectionHealthPrivacyAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var single = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(false));
        Require(registry.ContentHash != single.ContentHash, "Dual-general rules must affect the content fingerprint.");
        try
        {
            ContentRegistry.Build(new StandardContentPackage(), new FixturePackage(bankCount: 13));
            throw new Exception("A dual-general mode accepted fewer than sixteen distinct generals.");
        }
        catch (InvalidOperationException) { }
        foreach (var role in new[] { Role.Lord, Role.Rebel })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 8, HumanSeat = 0, HumanRole = role,
                ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(game, new StartGameCommand());
            ReachSelection(game);
            Require(game.PendingDecision!.Choices.Count == (role == Role.Lord ? 13 : 8),
                "Both slots retain the existing role-based candidate counts.");
            Select(game, Primary);
            ReachSelection(game);
            var second = game.PendingDecision!;
            Require(second.Choices.Count == (role == Role.Lord ? 13 : 8) &&
                !second.ValidContentIds.Contains(Primary), "The deputy is a fresh private choice without reusing the primary.");
            Require(Enumerable.Range(1, 7).Append(-1).All(viewer => game.CreateSnapshot(viewer).PendingDecision is null),
                "Other viewers must never see the deputy candidates.");
            var restored = Cold(game, registry);
            var revision = game.Revision;
            Require(!game.Submit(new SelectGeneralCommand(0, Primary, revision, second.PromptId)).Accepted && game.Revision == revision,
                "An unavailable primary cannot be selected again or partially mutate the match.");
            Select(game, Secondary);
            Select(restored, Secondary);
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)),
                "A restored deputy prompt must accept the same exact selection deterministically.");
            var privateView = game.CreateSnapshot(1).Players[0];
            Require(role == Role.Lord ? privateView.IsSecondaryGeneralPublic && privateView.SecondaryGeneralId == Secondary
                : privateView.GeneralId.Length == 0 && privateView.SecondaryGeneralId is null,
                "Lord generals are public immediately; other seats keep both choices private until setup finishes.");
            for (var step = 0; step < 32 && !game.Events.Any(e => e.Payload is SetupCompletedEvent); step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            var players = game.CreateSnapshot(0, true).Players;
            Require(game.Events.Any(e => e.Payload is SetupCompletedEvent) && players.Count == 8 &&
                players.All(p => p.IsGeneralPublic && p.IsSecondaryGeneralPublic && p.SecondaryGeneralId is not null) &&
                players.SelectMany(p => new[] { p.GeneralId, p.SecondaryGeneralId }).Distinct().Count() == 16 &&
                game.Events.Count(e => e.Payload is GeneralSelectedEvent) == 8 &&
                game.Events.Count(e => e.Payload is SecondaryGeneralSelectedEvent) == 8,
                "All eight seats must complete two distinct selections and reveal both generals.");
            Require(players.Count(p => p.Role == Role.Lord) == 1 && players.Count(p => p.Role == Role.Loyalist) == 2 &&
                players.Count(p => p.Role == Role.Rebel) == 4 && players.Count(p => p.Role == Role.Renegade) == 1,
                "Dual generals preserve the eight-player identity distribution.");
            var human = players[0];
            Require(players.All(p => p.MaxHp ==
                (registry.Generals[p.GeneralId].BaseHp + registry.Generals[p.SecondaryGeneralId!].BaseHp) / 2 +
                (p.Role == Role.Lord ? 1 : 0)), "Every AI uses the same combination health rule as the human.");
            Require(human.MaxHp == (role == Role.Lord ? 4 : 3) && human.Hp == human.MaxHp,
                "Odd combined health floors the average, with the original +1 Lord bonus.");
            var state = ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!)[0];
            var faction = typeof(GameEngine).GetMethod("GetEffectiveFactionId", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [state]);
            Require(state.Gender == GeneralGender.Male && Equals(faction, "wei"),
                "A female Shu deputy must retain the male Wei primary's gender and effective faction.");
            Require(human.SecondarySkills!.Any(s => s.ContentId == "fixture:dual-deputy-skill") &&
                human.SecondarySkills!.Any(s => s.ContentId == "fixture:dual-lord-skill") == (role == Role.Lord),
                "Deputy skills are visible while printed Lord skills retain identity qualifications.");
            Cold(game, registry);
            if (role == Role.Lord)
            {
                for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    Accept(game, new AdvanceOneStepCommand(game.Revision));
                foreach (var (skill, amount) in new[] { ("fixture:dual-primary-skill", 1), ("fixture:dual-deputy-skill", 2) })
                {
                    Require(game.GetHumanLegalActions().Any(a => a.ProgramSkillId == skill), "Both generals must expose executable active skills.");
                    var before = game.CreateSnapshot(0).Players[0].HandCount;
                    Accept(game, new UseProgramSkillCommand(0, skill, "draw", [], [], game.Revision, game.PendingDecision!.PromptId));
                    for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                        Accept(game, new AdvanceOneStepCommand(game.Revision));
                    Require(game.CreateSnapshot(0).Players[0].HandCount == before + amount,
                        "Each general's skill must actually resolve its own effect.");
                    Cold(game, registry);
                }
            }
        }

        var automatic = GameEngine.CreateStandard(new GameOptions { Seed = 31, ModeId = Mode }, registry);
        Require(automatic.CreateSnapshot(0, true).Players.All(p => p.IsSecondaryGeneralPublic && p.SecondaryGeneralId is not null),
            "Constructor-assigned games must also initialize both generals.");
        var legacy = GameEngine.CreateStandard(new GameOptions { Seed = 31, ModeId = Mode }, single);
        Require(legacy.CreateSnapshot(0, true).Players.All(p => p.SecondaryGeneralId is null),
            "Single-general modes must continue to create one general per seat.");
    }

    private static void ReachSelection(GameEngine game)
    {
        for (var step = 0; step < 24; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 }) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Dual-general fixture did not reach the next human selection.");
    }

    private static void Select(GameEngine game, string id)
    {
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, game.PendingDecision!.PromptId));
        Require(game.PendingDecision!.ValidContentIds.Contains(id), $"The full roster must allow the fixture's exact cross-faction choice '{id}'.");
        Accept(game, new SelectGeneralCommand(0, id, game.Revision, game.PendingDecision.PromptId));
    }

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        foreach (var viewer in new[] { 0, 1, -1 })
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(viewer)) == SnapshotJson.Serialize(game.CreateSnapshot(viewer)),
                "Dual-general checkpoints must replay the exact private and public views.");
        return restored;
    }

    private sealed class FixturePackage(bool dual = true, int bankCount = 24) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("identity-dual-check", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, amount) in new[] { ("fixture:dual-primary-skill", 1), ("fixture:dual-deputy-skill", 2) })
            {
                var program = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,
                    "activations":[{"id":"draw","usesPerTurn":1,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving",
                    "effects":[{"op":"draw","target":"owner","amount":{{amount}}}]}]}]}
                    """, $$"""{"schemaVersion":3,"skills":{"{{id}}":{"name":"摸牌","description":"测试摸牌"} } }""").Programs[id];
                builder.AddSkill(new(id, id, id) { Program = program, ActionForms = SkillActionForm.Active });
            }
            builder.AddSkill(new("fixture:dual-lord-skill", "主公技", "身份限制") { Tags = SkillTag.Lord });
            builder.AddSkill(new("fixture:dual-bank-skill", "候选偏好", "固定 AI 测试候选")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new(Primary, "主将", "supporter", "fixture:dual-primary-skill", "wei", 4));
            builder.AddGeneral(new(Secondary, "副将", "supporter", "fixture:dual-deputy-skill", "shu", 3,
                ["fixture:dual-lord-skill"], GeneralGender.Female));
            var pool = new List<string> { Primary, Secondary };
            for (var index = 0; index < bankCount; index++)
            {
                var id = $"fixture:dual-bank-{index}";
                pool.Add(id);
                builder.AddGeneral(new(id, "对手", "supporter", "fixture:dual-bank-skill", "wu"));
            }
            builder.AddMode(new(Mode, "八人双将测试", 8, 8,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 },
                "standard:basic-demo", GeneralPoolIds: pool)
            {
                DualGenerals = dual,
                RoleGeneralCandidateCounts = new Dictionary<string, int>
                { [nameof(Role.Lord)] = 13, [nameof(Role.Loyalist)] = 8, [nameof(Role.Rebel)] = 8, [nameof(Role.Renegade)] = 13 }
            });
        }
    }
}
