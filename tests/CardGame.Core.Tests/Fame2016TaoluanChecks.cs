using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class Fame2016TaoluanChecks
{
    public static void NamesGiftPrivacyReplay()
    {
        var (game, registry) = Create();
        var options = game.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == "classic:taoluan").ToArray();
        Require(options.Any(a => a.PlayedCardKind == CardKind.DrawTwo), "Self DrawTwo is a real permitted ordinary trick.");
        var action = options.First(a => a.PlayedCardKind == CardKind.DrawTwo);
        var id = action.CardId!.Value;
        Play(game, action);
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Replay(game, registry); RejectUnknown(game);
        Answer(game, c => c.Targets.Contains(1));
        var gift = Prompt(game)!;
        Require(gift.IsPrivate && gift.PlayerSeat == 1 && gift.Choices.Any(c => c.Cards.Count == 1), "The provider privately selects a real different-category equipment card.");
        foreach(var seat in new[]{0,2,3}) Require(game.CreateSnapshot(seat).PendingDecision is null || game.CreateSnapshot(seat).PendingDecision!.Choices.Count == 0, "Other seats cannot inspect the provider's hand choices.");
        Replay(game, registry); RejectUnknown(game);
        var card = gift.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(game, c => c.Cards.Contains(card));
        Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:taoluan-observer"); Replay(game,registry); RejectUnknown(game); Answer(game,_=>true);
        Reach(game,p => p.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 1, "The gift moves exactly one real entity into the owner hand.");
        Require(game.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile), "The converted trick pays its real physical cost.");
        Require(!game.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == "classic:taoluan" && a.PlayedCardKind == CardKind.DrawTwo), "An output name is unavailable after one use this game.");
        Replay(game, registry);
        Play(game, game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "classic:taoluan" && a.PlayedCardKind == CardKind.FireSlash && a.TargetSeat == 1));
        Reach(game,p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(game,c => c.Targets.Contains(1));
        Answer(game,c => c.Cards.Count == 1); Reach(game,p => p.Kind == DecisionKind.PlayCard);
        Require(!game.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == "classic:taoluan" && a.PlayedCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash), "All Slash variants share the canonical once-game name."); Replay(game,registry);
    }
    public static void DeclineTurnResetAndLedger()
    {
        var (game, registry) = Create();
        Play(game, game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "classic:taoluan" && a.PlayedCardKind == CardKind.DrawTwo));
        Reach(game,p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(game,c => c.Targets.Contains(1));
        var hp=game.CreateSnapshot(0).Players[0].Hp;
        Answer(game,c => c.Parameters.GetValueOrDefault("program-action") == "different-action-category-decline");
        Reach(game,p => p.Kind == DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[0].Hp == hp-1 && !game.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == "classic:taoluan"), "Declining loses HP and disables the entire conversion for the current turn.");
        Replay(game,registry);
        Accept(game,new EndPlayPhaseCommand(0,game.Revision,Prompt(game)!.PromptId));
        Reach(game,p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(game.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == "classic:taoluan") && !game.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == "classic:taoluan" && a.PlayedCardKind == CardKind.DrawTwo), "Turn reset restores the skill while retaining the game name ledger.");
        Replay(game,registry);
    }
    public static void ResponseUseCompletion()
    {
        var (game,registry)=Create(true);
        var target=Enumerable.Range(1,3).First(seat=>game.GetCombatDistance(0,seat)==1 && game.CreateSnapshot(seat,true).Players[seat].Hand.Any(c=>c.Kind==CardKind.Slash));
        Accept(game,new UseProgramSkillCommand(0,"classic:tiaoxin","taunt",[],[target],game.Revision,Prompt(game)!.PromptId));
        Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="request-slash");
        Reach(game,p=>p.Kind==DecisionKind.RespondDodge && p.PlayerSeat==0);
        var choice=Prompt(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="classic:taoluan");
        var card=choice.Cards.Single(); RejectUnknown(game); Replay(game,registry);
        Answer(game,c=>c.Id==choice.Id);
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(game.CardMovements.Any(m=>m.CardId==card && m.To==CardLocation.DiscardPile),"The real defensive Dodge is paid before completion asks for a gift.");
        Replay(game,registry); Answer(game,c=>c.Targets.Contains(target)); Replay(game,registry);
        Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="different-action-category-gift");
        Reach(game,p=>p.Kind==DecisionKind.PlayCard); Replay(game,registry);
    }
    public static void SameKindAndDyingGate()
    {
        var (game,registry)=Create(sameKind:true);
        var action=game.GetHumanLegalActions().First(a=>a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.DrawTwo);
        Require(game.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==action.CardId).Kind==CardKind.DrawTwo,"The physical cost already has the requested native name.");
        Play(game,action); Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(game.CardMovements.Any(m=>m.CardId==action.CardId && m.To==CardLocation.DiscardPile),"Same-kind explicit conversion pays its actual cost before triggering.");
        Answer(game,c=>c.Targets.Contains(1));
        Require(Prompt(game)!.Choices.All(c=>c.Cards.Count==0),"A provider holding only ordinary tricks cannot gift the same output category, even when decline is the sole choice.");
        Answer(game,_=>true); Reach(game,p=>p.Kind==DecisionKind.PlayCard); Replay(game,registry);
        var (dying,dyingRegistry)=Create(dyingFixture:true);
        Accept(dying,new UseProgramSkillCommand(0,"fixture:taoluan-damage","damage",[],[1],dying.Revision,Prompt(dying)!.PromptId));
        Reach(dying,p=>p.Kind==DecisionKind.RescueDying);
        Require(Prompt(dying)!.Choices.All(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")!="classic:taoluan"),"No Taoluan Peach or Alcohol rescue is available while another character is dying.");
        Replay(dying,dyingRegistry); RejectUnknown(dying);
    }
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
    public static void WoodenOxEntityCost()
    {
        var (game,registry)=Create(wooden:true);
        var equip=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip && a.ConversionSource is null);
        Play(game,equip);Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        var storage=game.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.UseEquipmentEffect && a.EquipmentKind==CardKind.WoodenOx);
        var id=storage.SelectableCardIds.First();
        Accept(game,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[id],[],game.Revision,Prompt(game)!.PromptId));Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        Play(game,game.GetHumanLegalActions().First(a=>a.CardId==id && a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.DrawTwo));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(game.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.WoodenOxGrain(0) && m.To==CardLocation.Processing),"The stored entity pays from its real private Wooden Ox zone.");Replay(game,registry);
        Answer(game,c=>c.Targets.Contains(1));Answer(game,c=>c.Cards.Count==1);Reach(game,p=>p.Kind==DecisionKind.PlayCard);Replay(game,registry);
    }
    public static void AiProviderAdvanceGiftAndDecline()
    {
        var (game,registry)=Create();
        Play(game,game.GetHumanLegalActions().First(a=>a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.DrawTwo));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Answer(game,c=>c.Targets.Contains(1));
        var prompt=Prompt(game)!;
        Require(prompt.PlayerSeat==1 && prompt.Choices.Any(c=>c.Cards.Count==1),"The AI provider has a real legal different-category gift.");
        Require(prompt.Choices.Where(c=>c.Cards.Count==1).All(c=>!new[]{"Spade","Heart","Club","Diamond"}.Any(name=>c.Description.Contains(name,StringComparison.Ordinal))),"Gift labels use readable card suit and rank text.");
        var giftIds=prompt.Choices.SelectMany(c=>c.Cards).ToArray();
        var before=game.AcceptedCommands.Count; Replay(game,registry);
        Accept(game,new AdvanceOneStepCommand(game.Revision));
        Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:taoluan-observer");
        Require(game.CardMovements.Count(m=>giftIds.Contains(m.CardId) && m.From==CardLocation.Hand(1) && m.To==CardLocation.Hand(0))==1 &&
            game.Events.Select(e=>e.Payload).OfType<ProgramActionCategoryGiftResolvedEvent>().Single().GiftCardId is not null,"Advancing the actual AI prompt resolves one real gift and reaches its movement observer child.");
        Require(!game.AcceptedCommands.Skip(before).OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==1),"No manual answer impersonates the AI provider.");
        Replay(game,registry);RejectUnknown(game);Answer(game,_=>true);Reach(game,p=>p.Kind==DecisionKind.PlayCard);Replay(game,registry);
        var (decline,declineRegistry)=Create(sameKind:true);
        Play(decline,decline.GetHumanLegalActions().First(a=>a.ConversionSource?.SkillId=="classic:taoluan" && a.PlayedCardKind==CardKind.DrawTwo));
        Reach(decline,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));Answer(decline,c=>c.Targets.Contains(1));
        Require(Prompt(decline) is {PlayerSeat:1} && Prompt(decline)!.Choices.Count==1 && Prompt(decline)!.Choices[0].Cards.Count==0,"The AI provider has only the decline branch when all cards match the effective category.");
        var hp=decline.CreateSnapshot(0).Players[0].Hp;Replay(decline,declineRegistry);before=decline.AcceptedCommands.Count;
        Accept(decline,new AdvanceOneStepCommand(decline.Revision));Reach(decline,p=>p.Kind==DecisionKind.PlayCard);
        Require(decline.CreateSnapshot(0).Players[0].Hp==hp-1 && !decline.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId=="classic:taoluan"),"Actual AI decline loses one HP and blocks all conversions for the turn.");
        Require(!decline.AcceptedCommands.Skip(before).OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==1),"The decline branch also uses the native AI advancement path.");Replay(decline,declineRegistry);
    }
    public static void ResourceContracts()
    {
        var assembly=typeof(StandardClassicGeneralPackage).Assembly;
        string Read(string suffix) { using var stream=assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-zhang-rang."+suffix+".json")!; using var reader=new StreamReader(stream);return reader.ReadToEnd(); }
        var rules=Read("rules");var presentation=Read("presentation"); SkillProgramCatalog.Load(rules,presentation);
        void Reject(Action<System.Text.Json.Nodes.JsonNode> mutate) { var node=System.Text.Json.Nodes.JsonNode.Parse(rules)!;mutate(node);try {SkillProgramCatalog.Load(node.ToJsonString(),presentation);}catch(InvalidOperationException){return;}throw new InvalidOperationException("Invalid category gift/name policy was accepted."); }
        Reject(n=>n["skills"]![0]!["viewAs"]![0]!["inputCount"]=2);
        Reject(n=>n["skills"]![0]!["viewAs"]![0]!["useOnly"]=false);
        Reject(n=>n["skills"]![0]!["viewAs"]![0]!["outputKind"]="indulgence");
        Reject(n=>n["skills"]![0]!["viewAs"]![0]!.AsObject().Remove("nameLedgerId"));
        Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]![1]!["zones"]=new System.Text.Json.Nodes.JsonArray("judgment"));
        Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]![1]!["target"]="owner");
        Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray().RemoveAt(0));
        Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]![0]!["targetKind"]="anyLiving");
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
    private static void RejectUnknown(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; var result = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("fixture:illegal"), game.Revision)); Require(!result.Accepted && before == State(game), "An invalid answer preserves every view, event, real movement, cursor and accepted command."); }
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
