using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class Fame2017WuXianChecks
{
    public static void BorrowedSwordPairsActualUseAndReplay()
    {
        foreach(var converted in new[]{false,true}) foreach(var count in new[]{4,6})
        {
            var (game,registry)=Create(borrowed:true);Choose(game,"draw");Reach(game,p=>p.Kind==DecisionKind.PlayCard);NextPreparation(game);Choose(game,"targets");Reach(game,p=>p.Kind==DecisionKind.PlayCard);
            foreach(var seat in new[]{1,2,3})
            {
                if(game.CreateSnapshot(0).Players[seat].Equipment.Any(c=>c.Kind==CardKind.Crossbow)) continue;
                Accept(game,new UseProgramSkillCommand(0,"fixture:wuxian-trick","equip-target",[],[seat],game.Revision,Prompt(game)!.PromptId));
                Reach(game,p=>p.Kind==DecisionKind.PlayCard || p.Choices.Any(c=>c.Cards.Count==1));
                if(Prompt(game)!.Kind!=DecisionKind.PlayCard) Answer(game,c=>c.Cards.Count==1);
                Reach(game,p=>p.Kind==DecisionKind.PlayCard);
            }
            var actions=game.GetHumanLegalActions().Where(a=>a.Kind==LegalActionKind.BorrowedSword && a.ProgramActivationId=="red-additional-targets").ToArray();
            Require(actions.All(a=>a.TargetSeats.Count is 4 or 6),"Every extra Borrowed Sword action retains complete ordered holder/victim pairs.");
            var action=actions.First(a=>a.TargetSeats.Count==count && (a.ConversionSource is not null)==converted);
            Replay(game,registry);Play(game,action);
            Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
            var use=game.ResolutionStack.OfType<CardUseFrame>().Single(f=>f.CardId==action.CardId && f.CardKind==CardKind.BorrowedSword);
            Require(use.TargetSeats.SequenceEqual(action.TargetSeats) && use.Action!.DesignatedTargetSeats!.SequenceEqual(action.TargetSeats.Where((_,i)=>i%2==0)),"The real parent use stores all pairs but only weapon holders as trick targets.");Replay(game,registry);RejectUnknown(game);Answer(game,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
            Reach(game,p=>p.Kind==DecisionKind.PlayCard);
            var results=game.Events.Select(e=>e.Payload).OfType<BorrowedSwordResolvedEvent>().Where(e=>e.ResolutionId==use.Id).ToArray();
            Require(results.Length==count/2 && results.Select(e=>e.WeaponOwnerSeat).SequenceEqual(action.TargetSeats.Where((_,i)=>i%2==0)) && results.Select(e=>e.SlashTargetSeat).SequenceEqual(action.TargetSeats.Where((_,i)=>i%2==1)),"Native AI resolves every actual weapon-holder response in the original ordered pairs.");
            Require(game.CardMovements.Count(m=>m.CardId==action.CardId && m.To==CardLocation.Processing)==1 && !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"All pairs share one real payment and normal AI advancement.");Replay(game,registry);
        }
    }
    private static void Choose(GameEngine game,string option)
    { Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");RejectUnknown(game);Answer(game,c=>c.Parameters.GetValueOrDefault("option-id")==option); }
    private static void ReachPreparation(GameEngine game)=>Reach(game,p=>p.SkillPrompt?.SkillId=="classic:fumian" && p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
    private static void FinishSkippingGift(GameEngine game)
    {Accept(game,new EndPlayPhaseCommand(0,game.Revision,Prompt(game)!.PromptId));Reach(game,p=>p.SkillPrompt?.SkillId=="classic:daiyan" && p.PlayerSeat==0);Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");}
    private static void NextPreparation(GameEngine game){FinishSkippingGift(game);ReachPreparation(game);}
    private sealed class PromptCache
    {
        public long Revision = -1;
        public PendingDecision? Decision;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameEngine, PromptCache> PromptCaches = new();
    private static PendingDecision? Prompt(GameEngine game)
    {
        var cached = PromptCaches.GetValue(game, _ => new PromptCache());
        if (cached.Revision != game.Revision)
        {
            cached.Decision = Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision)
                .FirstOrDefault(prompt => prompt is not null);
            cached.Revision = game.Revision;
        }
        return cached.Decision;
    }

    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 400; step++)
        {
            if (Prompt(game) is { } prompt && predicate(prompt)) return;
            if (Prompt(game) is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 }) Answer(game,c=>c.Cards.Count==0);
            else if (Prompt(game) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (Prompt(game) is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 } draft) Answer(game, _ => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The conversion fixture did not reach its prompt: " + Prompt(game)?.Kind.ToString() + " Frames:" + JsonSerializer.Serialize(game.ResolutionStack));
    }
    private sealed class Observation : IEquatable<Observation>
    {
        public required string[] Views { get; init; }
        public required string Frames { get; init; }
        public required string[] Events { get; init; }
        public required string Movements { get; init; }
        public required string Commands { get; init; }

        public bool Equals(Observation? other) => other is not null &&
            Views.SequenceEqual(other.Views, StringComparer.Ordinal) &&
            string.Equals(Frames, other.Frames, StringComparison.Ordinal) &&
            Events.SequenceEqual(other.Events, StringComparer.Ordinal) &&
            string.Equals(Movements, other.Movements, StringComparison.Ordinal) &&
            string.Equals(Commands, other.Commands, StringComparison.Ordinal);
        public override bool Equals(object? other) => other is Observation observation && Equals(observation);
        public override int GetHashCode() => HashCode.Combine(Frames, Movements, Commands);
    }

    private static Observation State(GameEngine game) => new()
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Movements = JsonSerializer.Serialize(game.CardMovements),
        Commands = CommandJson.Serialize(game.AcceptedCommands)
    };
    private static void RejectUnknown(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; var result = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("fixture:illegal"), game.Revision)); Require(!result.Accepted && before.Equals(State(game)), "An invalid answer preserves every view, event, real movement, cursor and accepted command."); }
    private static void Replay(GameEngine game, ContentRegistry registry)
    { var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(game).Equals(State(replay)), "Checkpoint and command JSON replay all public/private states and physical moves."); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static readonly Dictionary<(bool AiOwner, bool Hongyan, bool Borrowed), ContentRegistry> Registries = [];
    private static (GameEngine Game,ContentRegistry Registry) Create(bool aiOwner=false, bool hongyan=false, bool borrowed=false)
    {
        var key = (aiOwner, hongyan, borrowed);
        if (!Registries.TryGetValue(key, out var registry))
            Registries[key] = registry = ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Scenario(aiOwner,hongyan,borrowed));
        var game=GameEngine.CreateStandard(new GameOptions{Seed=17,HumanSeat=0,HumanRole=Role.Lord,PlayerCount=4,ModeId="identity:classic-wuxian",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=40},registry);
        Accept(game,new StartGameCommand());Accept(game,new SelectGeneralCommand(0,aiOwner?"fixture:wuxian-1":"fixture:wuxian-owner",game.Revision,Prompt(game)!.PromptId));if(aiOwner) Reach(game,p=>p.Kind==DecisionKind.PlayCard && p.PlayerSeat==0);else ReachPreparation(game);return(game,registry);
    }
    private sealed class Scenario(bool aiOwner,bool hongyan,bool borrowed):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wuxian",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var observer=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wuxian-observer","revision":1,"triggers":[{"id":"observe","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.consecutive-target.deck-gift"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:wuxian-observer":{"name":"获得观察","description":"实际获得后暂停","optionLabels":{"continue":"继续"}}}}""");
            b.AddSkill(new("fixture:wuxian-observer","获得观察","实际获得后暂停"){Program=observer.Programs["fixture:wuxian-observer"]});
            if (!aiOwner)
            {
                var idle = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wuxian-idle","revision":1,"triggers":[{"id":"idle-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:wuxian-idle":{"name":"目标等待","description":"略过目标的无关出牌"}}}""");
                b.AddSkill(new("fixture:wuxian-idle", "目标等待", "只省略与本检查无关的目标出牌阶段") { Program = idle.Programs["fixture:wuxian-idle"] });
            }
            string SelectNeededConversions(string rules)
            {
                if (!borrowed) return rules;
                var json = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                var viewAs = json["skills"]![0]!["viewAs"]!.AsArray();
                foreach (var rule in viewAs.Where(rule => rule!["id"]!.GetValue<string>() != "borrowed").ToArray())
                    viewAs.Remove(rule);
                return json.ToJsonString();
            }
            var conversion=SkillProgramCatalog.Load(SelectNeededConversions($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wuxian-trick","revision":1,"viewAs":[{"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"draw-two","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true},{"id":"alcohol","inputKinds":[],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false,"useOnly":true},{"id":"iron-chain","inputKinds":[],"inputSuits":[],"outputKind":"ironChain","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}],"activations":[{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"face-down","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]}],"triggers":[{"id":"pause-borrowed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["borrowedSword"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"done","options":[{"id":"continue"}]}]}]}]}"""),"""{"schemaVersion":3,"skills":{"fixture:wuxian-trick":{"name":"转换驱动","description":"真实单牌锦囊","optionLabels":{"continue":"继续"}}}}""");
            b.AddSkill(new("fixture:wuxian-trick","转换驱动","真实单牌锦囊"){Program=conversion.Programs["fixture:wuxian-trick"]});
            b.AddGeneral(new("fixture:wuxian-owner","吴苋测试","supporter","classic:fumian","shu",20,aiOwner?["classic:daiyan"]:hongyan?["classic:daiyan","fixture:wuxian-trick","classic:hongyan"]:["classic:daiyan","fixture:wuxian-trick"]));
            for(var i=1;i<4;i++) b.AddGeneral(new($"fixture:wuxian-{i}","目标","supporter","standard:none","wei",20,hongyan && i==1?["fixture:wuxian-observer","classic:weimu", "fixture:wuxian-idle"]:aiOwner?["fixture:wuxian-observer"]:["fixture:wuxian-observer", "fixture:wuxian-idle"]));
            b.AddDeck(new("fixture:wuxian-deck","实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,240).Select(i=>new ContentDeckPhysicalCard(borrowed?(i%4<2?"standard:crossbow":i%4==2?"classic:borrowed-sword":"standard:slash"):i%3==0?"standard:slash":i%3==1?"standard:crossbow":"standard:peach",borrowed?Suit.Heart:i%3==1?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-wuxian","机制测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:wuxian-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:wuxian-owner","fixture:wuxian-1","fixture:wuxian-2","fixture:wuxian-3"]));
        }
    }
}
