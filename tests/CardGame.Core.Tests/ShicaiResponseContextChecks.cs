using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShicaiResponseContextChecks
{
    public static void CompletedDodgeRetainsNativeQualificationAndColdReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:shicai-response", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:shicai-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 0);
        Answer(game, c => c.Cards.Count == 0);
        Reach(game, p => p.Kind == DecisionKind.RespondDodge && p.PlayerSeat == 0);
        var native = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Need(native.HuaManSouyingQualification is { DirectionOrdinal: 2 },
            "The second real Slash carries its own finalized directional qualification.");
        var dodge = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([dodge]));
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:shicai");
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var program = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:shicai");
        var window = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        Need(program.AlternatingSuitTop is { Mode: "completed", Stage: "reward-draw" } &&
            program.WindowContext?.HuaManSouying == native.HuaManSouyingQualification &&
            window.ParentFrameId == native.Id && window.Action.EffectiveKind == CardKind.Dodge &&
            Prompt(game)?.SkillPrompt?.SkillId == "fixture:shicai-draw-observer" &&
            program.WindowContext?.CardUse?.CardActionId == window.Action.ActionId,
            "Shicai automatically top-decks the single Dodge and retains its exact native qualification through the reward's gain child.");
        Cold(game, registry);
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        for (var step = 0; step < 32 && game.ResolutionStack.Any(f => f.Id == window.Id || f.Id == program.Id); step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Need(game.CardMovements.Count(m => m.CardId == dodge && m.From == CardLocation.DiscardPile &&
            m.To == CardLocation.DrawPile && m.Reason.Value == "skill-program.completed-top.place") == 1 &&
            !game.ResolutionStack.Any(f => f.Id == window.Id || f.Id == program.Id),
            "The completed Dodge moves to the deck once and both exact owning frames return.");
        Cold(game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> goal)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Prompt(game) is { } prompt && goal(prompt)) return;
            if (Prompt(game) is { PlayerSeat: 0 } optional && optional.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Shicai did not reach the expected response boundary: " + Prompt(game)?.Kind);
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(game.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) &&
            game.CardMovements.SequenceEqual(copy.CardMovements), "Four views, exact frames and physical movement cold-replay unchanged.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shicai-response-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:xu-you", "ol:hua-man");
            var observer = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:shicai-draw-observer","revision":1,
                "triggers":[{"id":"reward-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],
                "movementOccurrence":"perBatch","movementReasons":["skill-program.completed-top.draw"],"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:shicai-draw-observer":{"name":"摸牌子结算","description":"测试","optionLabels":{"continue":"继续"}}}}""")
                .Programs["fixture:shicai-draw-observer"];
            builder.AddSkill(new("fixture:shicai-draw-observer", "摸牌子结算", "测试") { Program = observer });
            var program = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:shicai-attacker","revision":1,
                "modifiers":[{"id":"two-slashes","query":"slashLimit","operation":"add","value":2,"priority":0}],
                "triggers":[{"id":"two-native-slashes","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true},
                {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:shicai-attacker":{"name":"原生攻击","description":"两次指定同一角色"}}}""")
                .Programs["fixture:shicai-attacker"];
            builder.AddSkill(new("fixture:shicai-attacker", "原生攻击", "两次指定同一角色") { Program = program });
            builder.AddGeneral(new("fixture:shicai-owner", "许攸", "supporter", "classic:shicai", "qun", 20, ["fixture:shicai-draw-observer"]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:shicai-peer-{seat}", "攻击者", "supporter", "fixture:shicai-attacker", "shu", 3, ["ol:souying"]));
            builder.AddDeck(new("fixture:shicai-deck", "真实闪", 4, 0, [new("standard:dodge", 64)]));
            builder.AddMode(new("identity:shicai-response", "响应上下文", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:shicai-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:shicai-owner", "fixture:shicai-peer-1", "fixture:shicai-peer-2", "fixture:shicai-peer-3"]));
        }
    }
}
