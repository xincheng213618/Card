using CardGame.Core;
using CardGame.Content.Standard;
using System.Text.Json;

internal static class Fame2017PublicPileChecks
{
    public static void StoreExchangeDistributionAndReplay()
    {
        var registry = Registry(); var game = Start(registry);
        UseDriver(game, "supply"); Settle(game);
        var suppliedHand = game.CreateSnapshot(0, true).Players[0].Hand.ToDictionary(c => c.Id);
        var equipment = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && suppliedHand[a.CardId!.Value].Suit != Suit.Spade);
        Play(game, equipment); Settle(game);
        for (var i = 0; i < 4; i++)
        {
            var action = SpadeDraw(game);
            Play(game, action); Settle(game);
            Require(Pile(game).Count == i + 1, "Each real spade card use must store exactly one physical top card.");
            Replay(game, registry); Public(game);
        }
        var fifth = SpadeDraw(game);
        Play(game, fifth); Settle(game); Require(Pile(game).Count == 4, "Public pile capacity must reject a fifth deposit.");
        var storageMoves = game.CardMovements.Where(m => m.Reason.Value == "skill-program.public-pile.store").ToArray();
        Require(storageMoves.Length == 4 && storageMoves.All(m => game.CardMovements.Any(previous => previous.CardId == m.CardId && previous.To == CardLocation.Processing && previous.From == CardLocation.DrawPile && previous.Sequence < m.Sequence)), "Every stored card must come from the actual draw pile through Processing.");
        var before = Pile(game).Select(c => c.Id).ToArray();
        NextExchange(game);
        var exchangeLocations = game.CreateCardZoneDiagnostics().ToDictionary(z => z.CardId, z => z.Location);
        Require(Prompt(game)!.IsPrivate && Prompt(game)!.Choices.Any(c => c.Cards.Any(id => exchangeLocations.TryGetValue(id, out var location) && location == CardLocation.Equipment(0))), "Exchange must offer actual equipment as well as hand cards.");
        Require(game.CreateSnapshot(1, false).PendingDecision is null, "Owned payment choices must remain private.");
        Atomic(game, new AnswerPromptCommand(0, Prompt(game)!.PromptId, new("unpublished"), game.Revision));
        var exchangeOwner = game.CreateSnapshot(0, true).Players[0];
        var entering = exchangeOwner.Equipment.Concat(exchangeOwner.Hand).GroupBy(c => c.Suit).Select(g => g.First()).Take(4).ToArray();
        Require(entering.Length == 4, "Fixed fixture must retain all four exchange suits.");
        var moveCount = game.CardMovements.Count;
        foreach (var card in entering)
        {
            Answer(game, Prompt(game)!.Choices.Single(c => c.Cards.SequenceEqual([card.Id])));
            Require(game.CardMovements.Count == moveCount, "Partial selection must not move or pay any entity."); Replay(game, registry);
        }
        Require(Prompt(game)!.IsPrivate == false && game.CreateSnapshot(1, false).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(before.Order()), "Taking books must expose their real public clickable faces.");
        foreach (var id in before)
        { Answer(game, Prompt(game)!.Choices.Single(c => c.Cards.SequenceEqual([id]))); if (id != before[^1]) Replay(game, registry); }
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
        Require(Pile(game).Select(c => c.Id).Order().SequenceEqual(entering.Select(c => c.Id).Order()), "Gain observer must see the fully committed equal swap.");
        var withdrawnLocations = game.CreateCardZoneDiagnostics().ToDictionary(z => z.CardId, z => z.Location);
        Require(before.All(id => withdrawnLocations.TryGetValue(id, out var location) && location == CardLocation.Hand(0)), "Every withdrawn book must enter the owner's real hand.");
        Replay(game, registry); Answer(game, Prompt(game)!.Choices.Single());
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "distribute"));
        Require(Prompt(game)!.Choices.All(c => c.Targets.Count == 1 && c.Targets[0] != 0 && c.Cards.Count == 1), "Four suits require all books to other living players without decline.");
        for (var i = 0; i < 4; i++)
        {
            var choice = Prompt(game)!.Choices.First(c => c.Targets.SequenceEqual([1])); Answer(game, choice);
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
            Require(Pile(game).Count == 3 - i, "Recipient gain observer must pause after each real distributed card."); Replay(game, registry); Answer(game, Prompt(game)!.Choices.Single());
            if (i < 3) Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "distribute"));
        }
        Settle(game); Require(Pile(game).Count == 0 && game.CardMovements.Count(m => m.Reason.Value == "skill-program.public-pile.distribute") == 4, "Every book must be distributed once without stranded cards."); Replay(game, registry);
    }

    private static readonly Dictionary<string, ContentRegistry> Registries = [];
    private static ContentRegistry Registry(string mode="")
    {
        if (!Registries.TryGetValue(mode, out var registry))
            Registries[mode] = registry = ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        return registry;
    }
    private static GameEngine Start(ContentRegistry registry)
    {
        var game=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-public-pile-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=20},registry);
        Accept(game.Submit(new StartGameCommand()));Reach(game,p=>p.Kind==DecisionKind.SelectGeneral&&p.PlayerSeat==0);
        Accept(game.Submit(new SelectGeneralCommand(0,"fixture:book-owner",game.Revision,Prompt(game)!.PromptId)));Reach(game,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);return game;
    }
    private static IReadOnlyList<CardSnapshot> Pile(GameEngine game)=>game.CreateSnapshot(0,true).Players[0].PublicPersistentPileCards??[];
    private static void Public(GameEngine game)
    {
        var expectedIds=Pile(game).Select(c=>c.Id).ToArray();
        Require(Enumerable.Range(0,4).All(seat=>game.CreateSnapshot(seat,false).Players[0].PublicPersistentPileCards?.Select(c=>c.Id).SequenceEqual(expectedIds)==true),"Public book faces must match for all observers.");
    }
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

    private static void UseDriver(GameEngine game,string activation)=>Accept(game.Submit(new UseProgramSkillCommand(0,"fixture:book-driver",activation,[],[],game.Revision,Prompt(game)!.PromptId)));
    private static LegalAction SpadeDraw(GameEngine game)
    {
        for(var i=0;i<4;i++)
        {
            var hand=game.CreateSnapshot(0,true).Players[0].Hand.ToDictionary(c=>c.Id);
            var action=game.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.DrawTwo&&hand[a.CardId!.Value].Suit==Suit.Spade);
            if(action is not null)return action;UseDriver(game,"supply");Settle(game);
        }
        throw new InvalidOperationException("Fixed deck did not supply a spade draw entity.");
    }
    private static void Play(GameEngine game,LegalAction action)=>Accept(game.Submit(new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,game.Revision,Prompt(game)!.PromptId,action.PlayedCardKind,action.TargetCardId){ConversionSource=action.ConversionSource}));
    private static void Answer(GameEngine game,PromptChoice choice)=>Accept(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new AnswerPromptCommand(Prompt(game)!.PlayerSeat,Prompt(game)!.PromptId,choice.Id,game.Revision)])).Single()));
    private static void Tick(GameEngine game)
    {
        if(Prompt(game) is {Kind:DecisionKind.DiscardCards} discard)
        {
            var palette=game.CreateSnapshot(discard.PlayerSeat,true).Players[discard.PlayerSeat].Hand.GroupBy(c=>c.Suit).Select(g=>g.First().Id).ToHashSet();
            var ids=discard.ValidCardIds.OrderBy(id=>palette.Contains(id)).Take(discard.RequiredCardCount).ToArray();Accept(game.Submit(new DiscardCardsCommand(discard.PlayerSeat,ids,discard.PromptId,game.Revision)));
        }
        else if (Prompt(game) is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger) Answer(game, trigger.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="activate") ?? trigger.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("token")=="finish") ?? trigger.Choices.First());
        else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    }
    private static void Reach(GameEngine game,Func<PendingDecision,bool> predicate)
    {for(var i=0;i<700;i++){if(Prompt(game) is {}p&&predicate(p))return;Tick(game);}throw new InvalidOperationException("Required book fixture boundary missing: "+Prompt(game)?.Kind+" "+Prompt(game)?.ToString());}
    private static void Settle(GameEngine game)=>Reach(game,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0&&game.ResolutionStack.Count==0);
    private static void NextExchange(GameEngine game)
    {Accept(game.Submit(new EndPlayPhaseCommand(0,game.Revision,Prompt(game)!.PromptId)));Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="owned"));}
    private static void Replay(GameEngine game,ContentRegistry registry)
    {
        var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Require(Enumerable.Range(0,4).All(seat=>SnapshotJson.Serialize(game.CreateSnapshot(seat,false))==SnapshotJson.Serialize(copy.CreateSnapshot(seat,false)))&&game.CardMovements.SequenceEqual(copy.CardMovements)&&game.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(copy.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"Accepted command checkpoint replay must reconstruct all public/private views, physical movements and events.");
    }
    private static void Atomic(GameEngine game,GameCommand command)
    {var before=GameCheckpointJson.Serialize(game.CreateCheckpoint());Require(!game.Submit(command).Accepted&&before==GameCheckpointJson.Serialize(game.CreateCheckpoint()),"Invalid input must reject without changing commands, revision or entities.");}
    private static void Accept(CommandResult result)=>Require(result.Accepted,"Fixture command rejected: "+result.Error?.Message);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-public-pile",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var borrowedViewAs = mode is "borrowed" or "response-freeze"
                ? """[{"id":"borrowed-conversion","inputKinds":["dodge"],"inputSuits":["spade"],"outputKind":"borrowedSword","singleCardTrickUse":true,"forPlay":true,"forResponse":false}]"""
                : "[]";
            var catalog=SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"fixture:book-driver","revision":1,"viewAs":{{{borrowedViewAs}}},"activations":[
               {"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":{{{(mode == "suppression" ? 20 : 8)}}}}]},
               {"id":"next-target","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]},
               {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},
               {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
               {"id":"trim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":0,"maximumCards":null,"zones":["hand"],"resultBind":"trim-payment"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim-payment","destination":"discardPile","awaitMovementTriggers":true}]},
               {"id":"loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:bizhuan"],"sourceBind":"standard:none"}]},
               {"id":"suppress","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"suppressGeneralSkill","target":"owner"}]},
               {"id":"death","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"loseHp","target":"owner","amount":20}]}
             ]},
             {"id":"fixture:book-observer","revision":1,"triggers":[{"id":"observe","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.public-pile.exchange-obtain","skill-program.public-pile.distribute"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]}
            ]}
            ""","""{"schemaVersion":3,"skills":{"fixture:book-driver":{"name":"公开堆驱动","description":"真实实体补牌与边界"},"fixture:book-observer":{"name":"获牌观察","description":"暂停获牌","optionLabels":{"continue":"继续"}}}}""");
            foreach(var p in catalog.Programs)b.AddSkill(new(p.Key,p.Key,p.Key){Program=p.Value});
            if (mode.Length == 0)
            {
                var idle = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:book-idle","revision":1,"triggers":[{"id":"idle-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:book-idle":{"name":"目标等待","description":"保留真实摸牌和得牌观察，省略无关出牌"}}}""");
                b.AddSkill(new("fixture:book-idle", "目标等待", "交换分发检查不需要其他玩家出牌") { Program = idle.Programs["fixture:book-idle"] });
            }
            if(mode is "normal" or "replacement-skip-play" or "skip-draw")
            {
                var triggers=new List<object>();
                if(mode=="skip-draw")triggers.Add(new{id="skip-draw",window="turnStartBeforeNormalFlow",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"draw"}}}});
                if(mode=="replacement-skip-play")
                {
                    triggers.Add(new{id="replace-draw",window="drawPhaseStarting",subject="owner",optional=false,drawPhaseMode="replacement",effects=new[]{new{op="draw",target="owner",amount=1}}});
                    triggers.Add(new{id="skip-play",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}});
                }
                triggers.Add(new{id="ended",window="drawPhaseEnded",subject="owner",optional=false,effects=new[]{new{op="chooseOption",target="owner",resultBind="ended",options=new[]{new{id="continue"}}}}});
                var boundary=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=new[]{new{id="fixture:draw-boundary",revision=1,triggers}}}),"""{"schemaVersion":3,"skills":{"fixture:draw-boundary":{"name":"摸牌结束观察","description":"正常替代与跳阶段边界","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:draw-boundary","摸牌结束观察","正常替代与跳阶段边界"){Program=boundary.Programs["fixture:draw-boundary"]});
            }
            if(mode=="response-freeze")
            {
                var freeze=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:response-freeze","revision":1,"triggers":[{"id":"lose-suit-policy","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],"optional":false,"effects":[{"op":"suppressGeneralSkill","target":"owner"},{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:response-freeze":{"name":"花色冻结观察","description":"响应支付后移除花色策略","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:response-freeze","花色冻结观察","响应支付后移除花色策略"){Program=freeze.Programs["fixture:response-freeze"]});
            }
            b.AddGeneral(new("fixture:book-owner","公开堆测试将","supporter","classic:bizhuan","qun",mode is "dodge-response" or "response-freeze"?3:20,["classic:tongbo", ..(mode=="ai"?Array.Empty<string>():new[]{"fixture:book-driver"}), "fixture:book-observer", ..(mode=="response-freeze"?new[]{"classic:hongyan","fixture:response-freeze"}:Array.Empty<string>()), ..(mode is "normal" or "replacement-skip-play" or "skip-draw" ? new[]{"fixture:draw-boundary"}:Array.Empty<string>())]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:book-{i}",$"目标{i}","supporter","fixture:book-observer","wei",20,mode is "ai" or "response" or "dodge-response" or "response-freeze" or "borrowed" ? ["classic:bizhuan","classic:tongbo"]:mode.Length == 0 ? ["fixture:book-idle"] : []));
            b.AddDeck(new("fixture:book-deck","公开堆牌堆",8,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(mode=="response-freeze"?i%8==0?"classic:borrowed-sword":i%8==2?"standard:qinggang_sword":i%2==0?"standard:slash":"standard:dodge":mode=="borrowed"?i%3==0?"classic:borrowed-sword":i%3==1?"standard:qinggang_sword":"standard:dodge":mode=="null-response"?i%2==0?"standard:draw_two":"standard:nullification":mode is "dodge-response" or "response-freeze"?i%2==0?"standard:slash":"standard:dodge":mode=="response" ? i%8==0?"standard:duel":i%2==0?"standard:slash":"standard:dodge" : i%10==0?"standard:qinggang_sword":i%5==1?"standard:draw_two":"standard:dodge",mode=="response-freeze"?i%8==2?Suit.Club:Suit.Spade:mode=="borrowed"?i%3==1?Suit.Club:Suit.Spade:mode is "response" or "ai" or "dodge-response" or "response-freeze" or "null-response"?Suit.Spade:i%10==0?Suit.Club:i%5==1?Suit.Spade:(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-public-pile-check","公开堆机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:book-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:book-owner","fixture:book-1","fixture:book-2","fixture:book-3"]));
        }
    }
}
