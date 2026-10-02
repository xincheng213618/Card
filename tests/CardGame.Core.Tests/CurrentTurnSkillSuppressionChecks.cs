using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class CurrentTurnSkillSuppressionChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    // Direct host/source lifecycle audit; these mutations are not replayed commands.
    public static void HostLifecycleQualificationAudit()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4,
            HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:suppress-fixture",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Accept(g, new StartGameCommand());
        Accept(g, new SelectGeneralCommand(0, "fixture:suppress-owner", g.Revision, g.PendingDecision!.PromptId));
        for (var i = 0; i < 60 && g.PendingDecision?.Kind != DecisionKind.PlayCard; i++)
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
        var owner = players[0]; var target = players[1];
        var source = owner.SkillGrants.Grants.Single(s => s.SkillId == "fixture:suppress-driver");
        var frame = new ProgramSkillFrame(90001, 0, source.SkillId, "audit", registry.GetSkill(source.SkillId).Program!.GameplayHash,
            1, [], [1]) { SkillInstanceId = source.SkillInstanceId };
        Require(Enabled(g, target).Contains("fixture:ordinary"), "Initial ordinary binding.");
        typeof(GameEngine).GetMethod("PushRuntimeFrame", Flags)!.Invoke(g, [frame]);
        typeof(GameEngine).GetMethod("IssueCurrentTurnNonLockedSkillSuppression", Flags)!.Invoke(g, [frame, 1]);
        Require(!Enabled(g, target).Contains("fixture:ordinary") && Enabled(g, target).Contains("fixture:locked") &&
            !Enabled(g, target).Contains("fixture:limited"), "Tags preserve only Locked, not Limited.");
        Require(g.CreateSnapshot(0).Players[1].Skills!.All(s => s.ContentId != "fixture:ordinary"), "Public skill snapshot shares qualification.");
        target.SkillGrants.Grant(new("later", "fixture:ordinary", "later-instance", "acquired:audit"));
        target.SkillGrants.Grant(new("disabled", "fixture:locked", "disabled-instance", "acquired:audit", false));
        Require(!Enabled(g, target).Contains("fixture:ordinary"), "New dynamic grants remain suppressed.");
        owner.SkillGrants.RemoveGrant(source.GrantId);
        var revision = g.Revision; var count = g.Events.Count; var grants = target.SkillGrants.Revision;
        for (var i = 0; i < 4; i++) { Require(!Enabled(g, target).Contains("fixture:ordinary"), "Issued fact survives exact source loss."); g.CreateSnapshot(i); }
        Require(g.Revision == revision && g.Events.Count == count && target.SkillGrants.Revision == grants, "Qualification and snapshots are observational.");
        var turn = g.CreateSnapshot(0).TurnNumber;
        typeof(GameEngine).GetMethod("ExpireCurrentTurnNonLockedSkillSuppressions", Flags)!.Invoke(g, [turn, 2]);
        Require(!Enabled(g, target).Contains("fixture:ordinary"), "Another seat's turn end cannot expire fact.");
        typeof(GameEngine).GetMethod("ExpireCurrentTurnNonLockedSkillSuppressions", Flags)!.Invoke(g, [turn, 0]);
        Require(Enabled(g, target).Contains("fixture:ordinary") && !target.SkillGrants.Grants.Single(s => s.GrantId == "disabled").IsEnabled,
            "Actual owner expiry restores eligibility without changing local disable.");
        Require(((System.Collections.ICollection)typeof(GameEngine).GetField("_currentTurnSkillSuppressions", Flags)!.GetValue(g)!).Count == 0,
            "Exact actual turn removes its issued scalar facts.");
    }
    private static IReadOnlyList<string> Enabled(GameEngine g, CharacterState p) =>
        (IReadOnlyList<string>)typeof(GameEngine).GetMethod("EnabledContentSkillIds", Flags)!.Invoke(g, [p])!;
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(c); Require(r.Accepted, r.Error?.Message ?? "Rejected"); }
    private static void Require(bool b, string text) { if (!b) throw new InvalidOperationException(text); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:suppress", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:suppress-driver","revision":1,"activations":[{"id":"audit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:suppress-driver":{"name":"driver","description":"driver"}}}""");
            b.AddSkill(new("fixture:suppress-driver", "driver", "driver") { Program = catalog.Programs["fixture:suppress-driver"] });
            b.AddSkill(new("fixture:ordinary", "ordinary", "ordinary"));
            b.AddSkill(new("fixture:locked", "locked", "locked") { Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:limited", "limited", "limited") { Tags = SkillTag.Limited });
            b.AddSkill(new("fixture:pick", "pick", "pick") { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -1000 } });
            b.AddGeneral(new("fixture:suppress-owner", "owner", "supporter", "fixture:suppress-driver", "shu", 4, ["fixture:pick"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:suppress-{i}", "target", "supporter", "fixture:ordinary", "wei", 4, ["fixture:locked", "fixture:limited"]));
            b.AddMode(new("identity:suppress-fixture", "suppress", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "standard:basic-demo", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:suppress-owner", "fixture:suppress-1", "fixture:suppress-2", "fixture:suppress-3"]));
        }
    }
}
