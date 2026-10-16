using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinGuanqiuJianChecks
{
    public static void IndependentSourceThresholds()
    {
        foreach (var honors in new[] { 0, 1 })
        {
            var (game, registry) = Start("multi"); Collect(game, 4);
            Driver(game, "loss"); Settle(game);
            if (honors == 1)
            {
                Driver(game, "grant-second"); Settle(game); Collect(game, 1);
            }
            var maxHp = game.State.Players[0].MaxHp;
            End(game); Settle(game);
            Require(game.State.Players[0].MaxHp == maxHp && !game.CreateSnapshot(0, false).Players[0].Skills!.Any(s => s.ContentId == "classic:qingce"),
                "Four books with zero or one honor cannot qualify the honor awakening, including a missing source in frozen facts.");
            Replay(game, registry); Conserve(game);
        }
        {
            var (game, registry) = Start("multi"); Collect(game, 4);
            Driver(game, "loss-book"); Settle(game); Driver(game, "grant-book"); Settle(game);
            PrepareArrows(game, 1); PlayArrow(game); Reach(game, p => Action(p, "public-pile-flow")); Choose(game, c => true); Settle(game);
            var sources = game.CreateSnapshot(0, false).Players[0].PublicPersistentPiles!;
            Require(sources.Single(s => s.SourceSkillId == "classic:zhengrong").Count == 5 && sources.Single(s => s.SourceSkillId == "classic:bizhuan").Count == 1,
                "Five honors cannot block a real new book store when its own source has fewer than four cards.");
            Replay(game, registry); Conserve(game);
        }
        Require(!JsonSerializer.Serialize(CardLocation.Hand(0)).Contains("PublicPileId", StringComparison.Ordinal), "Legacy card locations omit the optional pile identity.");
        var invalid = false;
        try { _ = new CardLocation(CardZoneKind.Hand, 0, "illegal"); } catch (ArgumentException) { invalid = true; }
        Require(invalid, "A source identity cannot create an alternate hand zone.");
    }
    private static (GameEngine,ContentRegistry) Start(string mode="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-honor-pile-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:gj-owner",g.Revision,P(g)!.PromptId));Settle(g);return(g,r);
    }
    private static IReadOnlyList<CardSnapshot> Pile(GameEngine g)
    {
        var player = g.CreateSnapshot(0, true).Players[0];
        return player.PublicPersistentPiles?.SingleOrDefault(p => p.SourceSkillId == "classic:zhengrong")?.Cards ??
            (player.PublicPersistentPileSkillId == "classic:zhengrong" ? player.PublicPersistentPileCards : null) ?? [];
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Action(PendingDecision p,string name)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==name);
    private static void Driver(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,"fixture:gj-driver",id,[],[],g.Revision,P(g)!.PromptId));
    private static void PrepareArrows(GameEngine g,int n)
    {
        Driver(g,"supply");Settle(g);var kept=g.CreateSnapshot(0,true).Players[0].Hand.Where(c=>c.Kind==CardKind.ArrowBarrage).Take(n).Select(c=>c.Id).ToHashSet();Require(kept.Count==n,"Fixed deck supplies the representative arrow entities.");
        var remove=g.CreateSnapshot(0,true).Players[0].Hand.Where(c=>!kept.Contains(c.Id)).Select(c=>c.Id).ToArray();Driver(g,"trim");foreach(var id in remove)Choose(g,c=>c.Cards.SequenceEqual([id]));Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");Settle(g);
    }
    private static void PlayArrow(GameEngine g)
    {
        var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.ArrowBarrage);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind));
    }
    private static void Collect(GameEngine g,int n){PrepareArrows(g,n);for(var i=0;i<n;i++){PlayArrow(g);Reach(g,p=>Action(p,"public-pile-flow"));Choose(g,c=>c.Parameters["source-zone"]=="Hand");Settle(g);}Require(Pile(g).Count==n,"Collect real honors once.");}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Choose(GameEngine g,Func<PromptChoice,bool> predicate)=>Answer(g,P(g)!.Choices.First(predicate));
    private static void Answer(GameEngine g,PromptChoice c){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Tick(GameEngine g)
    {
        if(P(g) is {} p&&p.PlayerSeat==0)
        {
            if(p.Kind==DecisionKind.PlayCard){End(g);return;}
            if(p.Kind==DecisionKind.DiscardCards){Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));return;}
            if(p.Choices.Count>0){Answer(g,p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("token")=="finish")??p.Choices.First());return;}
            throw new InvalidOperationException("Unhandled fixed prompt "+JsonSerializer.Serialize(p));
        }
        Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var i=0;i<260;i++){if(P(g) is {}p&&predicate(p))return;Tick(g);}throw new InvalidOperationException("Missing honor boundary "+JsonSerializer.Serialize(P(g)));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0&&g.ResolutionStack.Count==0);
    private static void Replay(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s,false))==SnapshotJson.Serialize(copy.CreateSnapshot(s,false)))&&g.CardMovements.SequenceEqual(copy.CardMovements)&&g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(copy.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"All viewer checkpoint/accepted command JSON reconstruct physical moves and facts.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,"Fixture command rejected: "+result.Error?.Message);}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Count==64&&g.CreateCardZoneDiagnostics().Select(z=>z.CardId).Distinct().Count()==64,"Every real entity belongs to exactly one zone.");
    private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-honor-pile",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var ending=mode=="strip-gain"?",{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}":mode=="death-gain"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}":"";
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"fixture:gj-driver","revision":1,"activations":[
              {"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":10}]},
              {"id":"trim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":0,"maximumCards":null,"zones":["hand"],"resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},
              {"id":"grant-consumer","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:gj-instance-consumer"]}]},
              {"id":"grant-second","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:zhengrong"]}]},
              {"id":"grant-book","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:bizhuan"]}]},
              {"id":"loss-book","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:bizhuan"],"sourceBind":"standard:none"}]},
              {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:zhengrong"],"sourceBind":"standard:none"}]}
             ]},
             {"id":"fixture:gj-consumer-grant","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:gj-instance-consumer"]}]}]},
             {"id":"fixture:gj-instance-consumer","revision":1,"triggers":[{"id":"exchange","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"publicPersistentPile"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":3} },"effects":[{"op":"exchangePublicPileHand","target":"owner","skillIds":["classic:zhengrong"]}]}]},
             {"id":"fixture:gj-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.public-pile.obtain","skill-program.public-pile.exchange-obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{ending}}]}]},
             {"id":"fixture:gj-discard","revision":1,"triggers":[{"id":"discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.classic:qingce.SelectAndMoveOwnedCard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var c=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:gj-consumer-grant":{"name":"独立消费来源","description":"获得另一来源的公开牌堆消费能力"},"fixture:gj-instance-consumer":{"name":"真实多源消费","description":"跨来源显式选择荣堆"},"fixture:gj-driver":{"name":"荣机制驱动","description":"真实补牌清理与失源"},"fixture:gj-gain":{"name":"真实获牌观察","description":"暂停获牌子链","optionLabels":{"continue":"继续"}},"fixture:gj-discard":{"name":"真实弃置观察","description":"付款后暂停","optionLabels":{"continue":"继续"}}}}""");
            foreach(var item in c.Programs)b.AddSkill(new(item.Key,item.Key,item.Key){Program=item.Value});
            b.AddGeneral(new("fixture:gj-owner","荣机制将","supporter",mode=="ai"?"standard:none":"classic:zhengrong","wei",20,["fixture:gj-driver",..(mode=="instances"?new[]{"fixture:gj-consumer-grant"}:Array.Empty<string>()),..(mode=="multi"?new[]{"classic:bizhuan","classic:tongbo"}:Array.Empty<string>()),..(mode=="ai"?Array.Empty<string>():new[]{"classic:hongju","fixture:gj-gain","fixture:gj-discard"})]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:gj-{i}","目标"+i,"supporter",mode=="ai"?"classic:zhengrong":"standard:none","wei",20,mode=="ai"?["classic:hongju"]:[]));
            b.AddDeck(new("fixture:gj-deck","荣机制真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(mode=="slash"?i%4==0?"standard:slash":i%4==1?"standard:fire_slash":i%4==2?"standard:thunder_slash":"standard:draw_two":mode=="field"&&i%8==0?"standard:lightning":i%4==0?"standard:qinggang_sword":"standard:arrow_barrage",mode=="multi"?Suit.Spade:(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-honor-pile-check","荣机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:gj-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:gj-owner","fixture:gj-1","fixture:gj-2","fixture:gj-3"]));
        }
    }
}
