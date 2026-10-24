using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryManChongScenario
{
    internal const string Skill = "boundary:junxing-mobile", Driver = "fixture:boundary-man-chong-driver", Watch = "fixture:boundary-man-chong-watch";
    private const string Mode = "identity:classic-boundary-man-chong", Deck = "fixture:boundary-man-chong-deck";
    private static readonly string[] Generals = ["fixture:boundary-man-chong", "fixture:boundary-man-chong-1", "fixture:boundary-man-chong-2", "fixture:boundary-man-chong-3"];

    internal static (ContentRegistry Registry, GameEngine Game) CreateScene()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 100; step++)
        {
            var prompt = Decision(game);
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return (registry, game);
            if (prompt is { Kind: DecisionKind.SelectGeneral })
                Accept(game, new SelectGeneralCommand(prompt.PlayerSeat, Generals[prompt.PlayerSeat], game.Revision, prompt.PromptId));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed Man Chong scene did not reach its original own Play.");
    }

    private static PendingDecision? Decision(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "The fixed Man Chong scene rejected a command.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-man-chong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:man-chong-mobile", "classic:man-chong");
            var programs = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"equip-target","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[
                    {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"cardCategories":["equipment"],"destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"},"resultBind":"equipment","awaitMovementTriggers":true}]},
                  {"id":"damage-owner","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","effects":[
                    {"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
                {"id":"{{Watch}}","revision":1,"triggers":[
                  {"id":"paid-discard-child","window":"cardsMoved","subject":"owner","movementOccurrence":"perOwnerBatch","sourceZones":["hand","equipment"],"optional":false,"effects":[
                    {"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "场景驱动", description = "真实装备移动和伤害。" },
                    [Watch] = new { name = "付款子窗", description = "保留真实弃牌续接。", optionLabels = new Dictionary<string, string> { ["continue"] = "返回已付款的峻刑" } } } }));
            foreach (var (id, program) in programs.Programs)
                builder.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description) { Program = program, ProgramPresentation = programs.Presentations[id] });
            builder.AddGeneral(new(Generals[0], "界满宠", "boundary-man-chong-mobile", Skill, "wei", 3, ["classic:yuce", Driver]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new(Generals[seat], "同桌" + seat, "supporter", seat == 1 ? Watch : "standard:none", "shu", 4));
            builder.AddDeck(new(Deck, "固定实体牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 1)).ToArray()
            });
            builder.AddMode(new(Mode, "界满宠固定身份局", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                Deck, GeneralCandidateCount: 4, GeneralPoolIds: Generals));
        }
    }
}
