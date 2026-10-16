using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryLiDianChecks
{
    public static void DefinitionAndLegacy()
    {
        foreach (var bad in new[] { "0", "-1", "5", "null", "1.2", "\"2\"" })
        {
            var failed = false;
            try { Load("fixture:bad", "\"activations\":[" + Activation("order", "[{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":4,\"exactTopCount\":" + bad + "}]") + "]"); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "exactTopCount rejects non-positive, oversized, null and non-integer inputs: " + bad);
        }
        var receiptEffects="[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2,\"resultBind\":\"drawn\"},{\"op\":\"selectCardSubset\",\"target\":\"owner\",\"sourceBind\":\"drawn\",\"resultBind\":\"gift\",\"minimumCards\":1,\"maximumCards\":1,\"maximumRankSum\":13,\"aiOrder\":\"mostCardsThenRankSum\"{FLAG}}]";
        foreach(var bad in new[]{"false","null","0","\"true\""})
        {var rejected=false;try{Load("fixture:bad-subset","\"activations\":["+Activation("subset",receiptEffects.Replace("{FLAG}",",\"availableAtSourceOnly\":"+bad))+"]");}catch(InvalidOperationException){rejected=true;}Require(rejected,"The available-source opt-in accepts only true: "+bad);}
        var old=Load("fixture:old-subset","\"activations\":["+Activation("subset",receiptEffects.Replace("{FLAG}",""))+"]");
        Require(old.Activations.Single().Effects[1].AvailableAtSourceOnly==null && !JsonSerializer.Serialize(old.Activations.Single().Effects[1]).Contains("AvailableAtSourceOnly"),"Missing available-source metadata retains old null serialization.");
        var (g,r) = Start(normalDraw:0); Skip(g); Settle(g); Driver(g,"legacy");
        Require(P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="finish-top"), "Legacy null still allows an immediate finish-top.");
        var frame=Frame(g);
        Require(!JsonSerializer.Serialize(frame.TopReorder).Contains("RequiredTopCount") &&
            !JsonSerializer.Serialize(Load("fixture:old", "\"activations\":[" + Activation("order", "[{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":4}]") + "]").Activations.Single().Effects.Single()).Contains("ExactTopCount"), "Old null metadata stays absent.");
        var before=Pile(g); Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="finish-top");
        while(P(g)?.Kind==DecisionKind.ProgramTopReorder) { Replay(g,r); Answer(g,c=>c.Cards.Count==1); }
        Settle(g); Require(!before.SequenceEqual(Pile(g)), "Legacy all-bottom path remains selectable."); Replay(g,r);
        var(strict,sr)=Start(normalDraw:0,child:"move");Skip(strict);Settle(strict);Driver(strict,"old-receipt");
        var receipt=strict.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="fixture:li-driver").CardSetBindings.Single(b=>b.Name=="drawn").CardIds;
        Answer(strict,c=>c.Cards.Contains(receipt[0]));Reach(strict,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"));Replay(strict,sr);
        var originalFailure=false;try{Answer(strict,c=>c.Cards.Count==1&&!receipt.Contains(c.Cards[0]));}catch(InvalidOperationException ex){originalFailure=ex.Message.Contains("bound card left its frozen source",StringComparison.OrdinalIgnoreCase);}
        Require(originalFailure,"A missing-flag receipt consumer preserves its original strict failure after a real child moved its card.");
        var(suits,ur)=Start(normalDraw:0,child:"move");Skip(suits);Settle(suits);Driver(suits,"available-suits");
        var suitReceipt=suits.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="fixture:li-driver").CardSetBindings.Single(b=>b.Name=="drawn").CardIds;
        Answer(suits,c=>c.Cards.Contains(suitReceipt[0]));Reach(suits,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"));Replay(suits,ur);
        Answer(suits,c=>c.Cards.Count==1&&!suitReceipt.Contains(c.Cards[0]));Reach(suits,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-subset"));
        Require(P(suits)!.Choices.Single().Cards.SequenceEqual([suitReceipt[1]]),"The same new available-source subset respects onePerSuit after actual receipt movement.");Replay(suits,ur);Answer(suits,c=>c.Cards.SequenceEqual([suitReceipt[1]]));Settle(suits);Replay(suits,ur);
    }
    internal static void SaveReceiptAfterPhysicalPeach(string child)
    {
        var (game,registry)=Start(normalDraw:0,child:child);Skip(game);Settle(game);
        var converted=child.StartsWith("save-jijiu",StringComparison.Ordinal);
        var peach=game.State.Players[0].Hand.Single(card=>converted?card.Id==13:card.Kind==CardKind.Peach);
        if(converted)
        {
            Accept(game,new EndPlayPhaseCommand(0,game.Revision,P(game)!.PromptId));
            Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:li-offturn");Activate(game);
            Answer(game,c=>c.Targets.SequenceEqual([1]));
            Reach(game,p=>p.SkillPrompt?.SkillId=="boundary:wangxi");Activate(game);
        }
        else {Driver(game,"outgoing",[1]);Activate(game);}
        var receipt=game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:wangxi")
            .CardSetBindings.Single(b=>b.Name=="drawn").CardIds.ToArray();
        Require(!receipt.Contains(peach.Id),"Physical rescue Peach was in the original hand, not the Draw receipt.");
        Answer(game,c=>c.Targets.SequenceEqual([1]));
        Reach(game,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);
        Require(game.State.Players[1].Hp==0,"The real child wound reaches exactly zero HP before rescue.");ReplayFourViews(game,registry);
        Answer(game,c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([peach.Id]));
        if(converted)
            Require(game.ResolutionStack.OfType<CardUseFrame>().Any(use=>use.CardId==peach.Id&&use.CardKind==CardKind.Peach&&
                use.DyingResponse?.UsedPeachPhysicalCardKind==CardKind.Dodge&&use.Action is{}action&&
                action.PhysicalCards.Single().CardKind==CardKind.Dodge&&action.ConversionChain.Single() is{}source&&
                source.SkillId=="boundary:jijiu"&&source.OwnerSeat==0&&source.BindingId=="red-owned-as-rescue-peach"),
                "The issued rescue uses the real Jijiu conversion while retaining its unique physical Dodge cost.");
        int? recoveredHand=null;
        if(child.Contains("observer",StringComparison.Ordinal))
        {
            Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:li-recovery");
            Require(game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),"A real optional HP recovery observer pauses the rescue subtree.");
            ReplayFourViews(game,registry);
            recoveredHand=Zones(game).CardsAt(CardLocation.Hand(1)).Count;
            if(child.EndsWith("-activate",StringComparison.Ordinal))Activate(game);else Skip(game);
            ReplayFourViews(game,registry);
        }
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-subset"));
        Require(recoveredHand is null || Zones(game).CardsAt(CardLocation.Hand(1)).Count==recoveredHand+(child.EndsWith("-activate",StringComparison.Ordinal)?1:0),
            "The actual optional recovery observer completes its selected effect once or skips it.");
        Require(game.State.Players[1].IsAlive&&game.State.Players[1].Hp==1&&P(game)!.ValidCardIds.SequenceEqual(receipt)&&
            P(game)!.Choices.All(c=>c.Cards.Count==1&&receipt.Contains(c.Cards[0])),"The saved participant receives only a complete real Draw receipt choice.");
        ReplayFourViews(game,registry);Answer(game,c=>c.Cards.SequenceEqual([receipt[0]]));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Answer(game,c=>c.Targets.SequenceEqual([1]));
        Reach(game,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"));Skip(game);if(converted)Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:li-offturn");else Settle(game);ReplayFourViews(game,registry);
        Require(game.CardMovements.Count(m=>m.CardId==peach.Id&&m.From==CardLocation.Hand(0))==1&&
            game.CardMovements.Count(m=>m.CardId==receipt[0]&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1))==1&&
            Zones(game).GetLocation(receipt[1])==CardLocation.Hand(0)&&
            game.Events.Select(e=>e.Payload).OfType<DyingResolvedEvent>().Any(e=>e.Survived),
            "One original-hand Peach saves the recipient once, then one receipt entity transfers once.");
        Console.WriteLine($"{(converted?"Converted Peach/Jijiu rescue":"Physical Peach rescue")} ({child}) accepted revision={game.Revision}; originalCost={peach.Id}/{peach.Kind}; receipt=[{string.Join(',',receipt)}]; four-view cold prefixes match.");
    }
    internal static void ReplayFourViews(GameEngine game,ContentRegistry registry)
    {
        var revision=game.Revision;
        var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Require(JsonSerializer.Serialize(game.CreateCheckpoint().Commands)==JsonSerializer.Serialize(restored.CreateCheckpoint().Commands),"Cold restoration preserves the exact accepted command prefix.");
        for(var viewer=0;viewer<4;viewer++)
            Require(JsonSerializer.Serialize(game.CreateSnapshot(viewer))==JsonSerializer.Serialize(restored.CreateSnapshot(viewer)),"All four actual viewer projections match the cold command prefix.");
        Require(game.Revision==revision,"Four pure viewer projections do not advance.");
    }
    internal static (GameEngine,ContentRegistry) Start(int remaining=32,int normalDraw=2,string child="")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(remaining,normalDraw,child));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:li",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:li",g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")=="boundary:xunxun"));return(g,r);
    }
    internal static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    internal static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    internal static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    internal static void Reach(GameEngine g,Func<PendingDecision,bool> goal){for(var i=0;i<100;i++){if(P(g)is{}p&&goal(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Small fixture did not reach target prompt: "+P(g)?.Prompt);}
    internal static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    internal static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p!=null);
    private static void Driver(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:li-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    internal static void Accept(GameEngine g,GameCommand command){var r=g.Submit(command);Require(r.Accepted,r.Error?.Message??"Command rejected.");}
    internal static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    internal static int[] Pile(GameEngine g)=>Zones(g).CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray();
    private static CardZoneStore Zones(GameEngine g)=>(CardZoneStore)typeof(GameEngine).GetField("_cardZones",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static CharacterState[] Players(GameEngine g)=>((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!).ToArray();
    internal static ProgramSkillFrame Frame(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
    internal static void Private(GameEngine g){var rev=g.Revision;var p=P(g)!;var ids=Frame(g).TopReorder!.ViewedCardIds;for(var i=0;i<4;i++){var view=g.CreateSnapshot(i);Require((view.PendingDecision!=null)==(i==p.PlayerSeat) && (i==p.PlayerSeat ? view.PrivateRevealedCards!.Select(c=>c.Id).SequenceEqual(ids) : view.PrivateRevealedCards==null),"Four viewers see only their own private prompt and exact private faces.");}Require(g.Revision==rev,"Pure views do not advance.");}
    internal static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(restored.Revision==g.Revision&&Pile(restored).SequenceEqual(Pile(g))&&JsonSerializer.Serialize(restored.CreateSnapshot(0))==JsonSerializer.Serialize(g.CreateSnapshot(0)),"Cold accepted-command prefix reconstructs state and private prompt.");}
    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private sealed class Fixture(int remaining,int normalDraw,string child):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-li", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if(child.StartsWith("save-jijiu",StringComparison.Ordinal))b.AddSkill((ContentSkillDefinition)typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.EmbeddedSkillProgramCatalog")!.GetMethod("Definition",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,["batch9-support","boundary:jijiu"])!);
            CharacterFixtureContent.Register(b, "boundary:li-dian");
            var activations=Activation("legacy","[{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":4}]")+","+
                Activation("discard-order","[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"amount\":1,\"resultBind\":\"removed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"removed\",\"destination\":\"discardPile\"},{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":4,\"exactTopCount\":2}]")+","+
                Activation("old-receipt","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2,\"resultBind\":\"drawn\"},{\"op\":\"selectCardSubset\",\"target\":\"owner\",\"sourceBind\":\"drawn\",\"resultBind\":\"gift\",\"minimumCards\":1,\"maximumCards\":1,\"maximumRankSum\":13,\"aiOrder\":\"mostCardsThenRankSum\"}]")+","+
                Activation("available-suits","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2,\"resultBind\":\"drawn\"},{\"op\":\"selectCardSubset\",\"target\":\"owner\",\"sourceBind\":\"drawn\",\"resultBind\":\"gift\",\"minimumCards\":1,\"maximumCards\":1,\"maximumRankSum\":13,\"aiOrder\":\"mostCardsThenRankSum\",\"availableAtSourceOnly\":true,\"onePerSuit\":true}]")+","+
                Activation("outgoing","[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}]",1)+","+
                Activation("incoming","[{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":2}]",1);
            b.AddSkill(new("fixture:li-driver","fixture","fixture"){Program=Load("fixture:li-driver","\"activations\":["+activations+"]")});
            if(child!="")
            {
                var moveEffects="[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"amount\":1,\"resultBind\":\"removed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"removed\",\"destination\":\"discardPile\"}]";
                var once=child=="kill"||child.StartsWith("save",StringComparison.Ordinal);
                var states=once ? "\"states\":[{\"id\":\"once\",\"initialValue\":false,\"visibility\":\"private\",\"resetScope\":\"game\",\"reacquirePolicy\":\"preserveUntilGameEnd\"}]," : "";
                var condition=once ? "\"condition\":{\"kind\":\"booleanState\",\"stateId\":\"once\",\"expectedValue\":false}," : "";
                var effects=once ? "[{\"op\":\"setBooleanState\",\"target\":\"owner\",\"stateId\":\"once\",\"value\":true},{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":"+(child=="kill"?20:2)+"}]" : moveEffects;
                b.AddSkill(new("fixture:li-gain","gain child","gain child"){Program=Load("fixture:li-gain",states+"\"triggers\":[{\"id\":\"gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,"+condition+"\"effects\":"+effects+"}]")});
            }
            if(child.Contains("observer",StringComparison.Ordinal))b.AddSkill(new("fixture:li-recovery","回复观察","回复观察"){Program=Load("fixture:li-recovery","\"triggers\":[{\"id\":\"pause\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]")});
            if(child.StartsWith("save-jijiu",StringComparison.Ordinal))b.AddSkill(new("fixture:li-offturn","回合外伤害","回合外伤害"){Program=Load("fixture:li-offturn","\"triggers\":[{\"id\":\"offturn\",\"window\":\"judgmentPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":true,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}]}]")});
            if(normalDraw==3)b.AddSkill(new("fixture:li-draw-modifier","Draw+2","Draw+2"){Program=Load("fixture:li-draw-modifier","\"modifiers\":[{\"id\":\"plus-two\",\"query\":\"drawCount\",\"operation\":\"add\",\"value\":2,\"priority\":0}]")});
            b.AddGeneral(new("fixture:li","李典","supporter","boundary:xunxun","wei",3,new[]{"boundary:wangxi","fixture:li-driver"}.Concat(child!=""?["fixture:li-gain"]:Array.Empty<string>()).Concat(normalDraw==3?["fixture:li-draw-modifier"]:Array.Empty<string>()).Concat(child.StartsWith("save-jijiu",StringComparison.Ordinal)?["boundary:jijiu","fixture:li-offturn"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:li-{i}","其他"+i,"supporter","standard:none","wei",5,child.Contains("observer",StringComparison.Ordinal)?["fixture:li-recovery"]:null));
            b.AddDeck(new("fixture:li-deck","固定",4,normalDraw==3?1:normalDraw,[]){PhysicalCards=Enumerable.Range(0,16+remaining).Select(i=>new ContentDeckPhysicalCard(child.StartsWith("save",StringComparison.Ordinal)&&!child.StartsWith("save-jijiu",StringComparison.Ordinal)&&i==12?"standard:peach":"standard:dodge",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:li","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:li-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:li","fixture:li-1","fixture:li-2","fixture:li-3"]));
        }
    }
}
