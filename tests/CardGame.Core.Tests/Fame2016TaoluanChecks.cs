using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class Fame2016TaoluanChecks
{
    public static void EquipmentGlobalAndNullification()
    {
        var (game,registry)=Create();
        var equip=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip && a.ConversionSource is null);
        Play(game,equip); Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        var card=equip.CardId!.Value;
        Play(game,game.GetHumanLegalActions().First(a=>a.CardId==card && a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.FiveGrains));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(game.CardMovements.Any(m=>m.CardId==card && m.From==CardLocation.Equipment(0) && m.To==CardLocation.Processing),"Equipment is paid through the real shared native trick pipeline.");
        Answer(game,c=>c.Targets.Contains(1)); Answer(game,c=>c.Cards.Count==1); Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        Require(game.Events.Select(e=>e.Payload).OfType<ProgramActionCategoryGiftResolvedEvent>().Count()==1,"A global trick has one Taoluan settlement, not one per target."); Replay(game,registry);
        Play(game,game.GetHumanLegalActions().First(a=>a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.DrawTwo));
        Reach(game,p=>p.Kind==DecisionKind.Nullification && p.PlayerSeat==0);
        var nullification=Prompt(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="classic:taoluan"); var cost=nullification.Cards.Single();
        Answer(game,c=>c.Id==nullification.Id);
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(game.CardMovements.Any(m=>m.CardId==cost && m.To==CardLocation.DiscardPile),"The actual Nullification cost is finished before its own settlement.");Replay(game,registry);
        Answer(game,c=>c.Targets.Contains(1));Answer(game,c=>c.Cards.Count==1);
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Answer(game,c=>c.Targets.Contains(1));Answer(game,c=>c.Cards.Count==1);Reach(game,p=>p.Kind==DecisionKind.PlayCard);Replay(game,registry);
        Require(game.Events.Select(e=>e.Payload).OfType<ProgramActionCategoryGiftResolvedEvent>().Count()==3,"Nested Nullification and the nullified parent each settle exactly once.");
    }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 400; step++)
        {
            if (Prompt(game) is { } prompt && predicate(prompt)) return;
            if (Prompt(game)?.SkillPrompt?.SkillId == "fixture:taoluan-observer") Answer(game,_=>true);
            else if (Prompt(game) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (Prompt(game) is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 } draft) Answer(game, _ => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The conversion fixture did not reach its prompt: " + Prompt(game)?.Kind.ToString() + " Frames:" + JsonSerializer.Serialize(game.ResolutionStack));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), Movements = game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static void Replay(GameEngine game, ContentRegistry registry)
    { var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(game) == State(replay), "Checkpoint and command JSON replay all public/private states and physical moves."); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool slashCards = false, bool sameKind = false, bool dyingFixture = false, bool wooden = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(slashCards, sameKind, dyingFixture, wooden));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-taoluan", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand()); Accept(game, new SelectGeneralCommand(0, "fixture:taoluan-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard); return (game, registry);
    }
    private sealed class Scenario(bool slashCards, bool sameKind, bool dyingFixture, bool wooden) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-conversion", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:taoluan-damage","revision":1,"viewAs":[{"id":"fixture-peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true}],"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":{{(dyingFixture?20:1)}}}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:taoluan-damage":{"name":"伤害","description":"测试真实伤害"}}}""");
            builder.AddSkill(new("fixture:taoluan-damage", "伤害", "测试真实伤害") { Program = catalog.Programs["fixture:taoluan-damage"] });
            var observer=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:taoluan-observer","revision":1,"triggers":[{"id":"observe","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:taoluan.ChooseDifferentActionCategoryGift"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observation","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:taoluan-observer":{"name":"获得观察","description":"实际赠牌后暂停","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new("fixture:taoluan-observer","获得观察","实际赠牌后暂停") { Program=observer.Programs["fixture:taoluan-observer"] });
            builder.AddGeneral(new("fixture:taoluan-owner", "张让测试", "supporter", "classic:taoluan", "wei", BaseHp: 9, AdditionalSkillIds: ["classic:tiaoxin", "fixture:taoluan-damage", "fixture:taoluan-observer"]));
            foreach (var seat in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:taoluan-{seat}", "目标", "supporter", "standard:none", "wei", BaseHp: 9));
            builder.AddDeck(new("fixture:taoluan-deck", "真实装备牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(wooden ? "classic:wooden-ox" : sameKind ? "standard:draw_two" : slashCards && index % 2 == 0 ? "standard:slash" : "standard:crossbow", index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-taoluan", "转换测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:taoluan-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:taoluan-owner", "fixture:taoluan-1", "fixture:taoluan-2", "fixture:taoluan-3"]));
        }
    }
}
