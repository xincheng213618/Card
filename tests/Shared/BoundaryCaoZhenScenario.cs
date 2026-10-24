using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class BoundaryCaoZhenScenario
{
    internal const string Skill = "boundary:sidi-mobile";
    private const string Hero = "boundary:cao-zhen-mobile", Peer = "fixture:boundary-sidi-peer", Quiet = "fixture:boundary-sidi-quiet", Deck = "fixture:boundary-sidi-deck";
    private const string IdentityMode = "identity:classic-boundary-sidi";
    private static readonly string[] IdentityGenerals = Enumerable.Range(0, 4).Select(seat => "fixture:boundary-sidi-" + seat).ToArray();

    // Starts at the real optional forecast after the owner's Crossbow Use completed.
    internal static (ContentRegistry Registry, GameEngine Game) CreateScene(bool singleHuman = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(45, 1, "boundary-sidi", Hero, 4, 0, 0, 0, 0, [Quiet],
            [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3)], [],
            [new(Peer, Hp: 4) { StartingHand = [new(CardKind.Slash, Suit.Spade, 4), new(CardKind.Slash, Suit.Spade, 5), new(CardKind.DrawTwo, Suit.Heart, 6), new(CardKind.Indulgence, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 8)] },
             new(Peer, Hp: 4) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 9)] }],
            [new(Peer, Hp: 4) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 10)] }], new string('d', 64));
        var game = GameEngine.CreateStandard(singleHuman ? new GameOptions
        {
            Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        } : new GameOptions
        {
            Seed = 45, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord,
            ModeId = IdentityMode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 100; step++)
        {
            var prompt = Decision(game);
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            if (prompt is { Kind: DecisionKind.SelectGeneral })
                Accept(game, new SelectGeneralCommand(prompt.PlayerSeat, IdentityGenerals[prompt.PlayerSeat], game.Revision, prompt.PromptId));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Play(game, 0);
        UseCard(game, 0, CardKind.Crossbow, []);
        Reach(game, prompt => prompt?.SkillPrompt?.SkillId == Skill && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        return (registry, game);
    }

    internal static PendingDecision? Decision(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    internal static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Rejected Sidi scene command."); }
    internal static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Decision(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    internal static void Reach(GameEngine game, Func<PendingDecision?, bool> done)
    {
        for (var step = 0; step < 200; step++)
        {
            var prompt = Decision(game); if (done(prompt)) return;
            if (prompt is not null)
            {
                if (prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                { Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                throw new InvalidOperationException("Unexpected Sidi scene decision: " + JsonSerializer.Serialize(prompt));
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded Sidi scene did not reach its next native decision.");
    }
    internal static void Play(GameEngine game, int actor) => Reach(game, prompt => prompt is { Kind: DecisionKind.PlayCard } && prompt.PlayerSeat == actor);
    internal static void EndPlay(GameEngine game, int actor)
    { Play(game, actor); Accept(game, new EndPlayPhaseCommand(actor, game.Revision, Decision(game)!.PromptId)); }
    internal static void UseCard(GameEngine game, int actor, CardKind kind, int[] targets)
    {
        var snapshot = game.CreateSnapshot(actor);
        var card = snapshot.Players[actor].Hand.First(card => card.Kind == kind);
        Accept(game, new PlayCardCommand(actor, card.Id, targets, game.Revision, Decision(game)!.PromptId, kind));
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-sidi", new(1, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var programs = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"keep-originals","query":"handLimit","operation":"add","value":40,"priority":0}],"triggers":[
                  {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "固定牌量", description = "保留原生阶段，不增加普通摸牌。" } } }));
            builder.AddSkill(new(Quiet, programs.Presentations[Quiet].Name, programs.Presentations[Quiet].Description) { Program = programs.Programs[Quiet], ProgramPresentation = programs.Presentations[Quiet] });
            builder.AddGeneral(new(Peer, "受预测角色", "supporter", Quiet, "shu", 4));
            for (var seat = 0; seat < 4; seat++)
                builder.AddGeneral(new(IdentityGenerals[seat], seat == 0 ? "界曹真" : "受预测角色" + seat,
                    seat == 0 ? "boundary-cao-zhen-mobile" : "supporter", seat == 0 ? Skill : Quiet, "wei", 4, seat == 0 ? [Quiet] : null));
            var kinds = new[] { "crossbow", "dodge", "slash", "draw_two", "indulgence", "peach" };
            builder.AddDeck(new(Deck, "固定实体牌堆", 24, 0, [])
            { PhysicalCards = Enumerable.Range(0, 144).Select(index => new ContentDeckPhysicalCard("standard:" + kinds[index % kinds.Length], Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "司敌实际用牌场景", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            builder.AddMode(new(IdentityMode, "司敌固定身份局", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                Deck, GeneralCandidateCount: 4, GeneralPoolIds: IdentityGenerals));
        }
    }
}
