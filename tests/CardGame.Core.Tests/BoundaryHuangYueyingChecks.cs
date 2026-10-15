using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class BoundaryHuangYueyingChecks
{
    private static readonly Lazy<ContentRegistry> Classic = new(() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()));

    public static void ActualUseQualification()
    {
        foreach (var bad in new[] { "false", "null", "0", "\"true\"" })
        {
            var rejected=false;
            try { Load("fixture:bad", "\"triggers\":[{\"id\":\"use\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"duel\"],\"requireNoCardConversion\":"+bad+",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]"); }
            catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Unconverted qualification is a true-only opt-in: "+bad);
        }
        var old=Load("fixture:old","\"triggers\":[{\"id\":\"use\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"duel\"],\"optional\":false,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]");
        Require(old.Triggers.Single().RequireNoCardConversion is null&&!JsonSerializer.Serialize(old.Triggers.Single()).Contains("RequireNoCardConversion"),"Old null serialized trigger shape stays absent.");
        Require(!JsonSerializer.Serialize(new ProgramSkillCardSetBinding("old",[],SkillProgramCardSetVisibility.Private,[])).Contains("SelectionActorSeat"),"Old null chooser provenance stays absent.");
        foreach(var kind in new[]{CardKind.DrawTwo,CardKind.IronChain,CardKind.Indulgence,CardKind.SupplyShortage,CardKind.Lightning})
        {
            var(g,r)=Start(kind);var action=g.GetHumanLegalActions().First(a=>a.Kind!=LegalActionKind.Recast&&a.PlayedCardKind is null&&a.CardId is{}id&&g.State.Players[0].Hand.Single(c=>c.Id==id).Kind==kind);
            Play(g,action);Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jizhi-current");
            Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action is{Type:CardActionType.Use,ConversionChain.Count:0},"Every true ordinary/delayed Trick uses its frozen action.");ReplayFourViews(g,r);Skip(g);Settle(g);ReplayFourViews(g,r);
        }
        var(converted,cr)=Start(CardKind.Dodge,conversion:true);var qixi=converted.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Dismantlement&&a.ConversionSource?.SkillId=="classic:qixi");
        Play(converted,qixi);Settle(converted);Require(!converted.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.SkillId=="boundary:jizhi-current"),"A real Qixi conversion does not trigger current Jizhi.");ReplayFourViews(converted,cr);
        foreach(var convert in new[]{false,true})
        {
            var(g,r)=Start(CardKind.Duel,conversion:convert,nullification:true);Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel));Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jizhi-current");Skip(g);
            Reach(g,p=>p.Kind==DecisionKind.Nullification&&p.PlayerSeat==0);ReplayFourViews(g,r);
            Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="nullification"&&(convert?c.Cards.All(id=>g.State.Players[0].Hand.Single(h=>h.Id==id).Kind==CardKind.Dodge):c.Cards.All(id=>g.State.Players[0].Hand.Single(h=>h.Id==id).Kind==CardKind.Nullification)));
            if(!convert){Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jizhi-current");ReplayFourViews(g,r);Activate(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="keep"));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="discard");}
            Settle(g);Require(g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Count(e=>e.SkillId=="boundary:jizhi-current")==(!convert?1:0),"Only the native Nullification response-use activates; converted response stays excluded.");ReplayFourViews(g,r);
        }
    }

    public static void ForeignProgramSelections()
    {
        var(g,r)=Start(CardKind.Dodge,equipment:"standard:renwang_shield");Driver(g,"equip",[1]);Settle(g);var armor=g.CreateSnapshot(0).Players[1].Equipment.Single();
        Driver(g,"foreign-payment",[1]);Settle(g);Require(Zones(g).GetLocation(armor.Id)==CardLocation.Equipment(1),"Protected-only generic equipment discard finishes without a payment.");ReplayFourViews(g,r);
        Driver(g,"other-discard");Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="choose-other-owned-card-discard"));Require(P(g)!.Choices.All(c=>!c.Cards.Contains(armor.Id)),"Other-owned choices keep opaque hand slots and omit protected equipment.");ReplayFourViews(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Settle(g);Require(Zones(g).GetLocation(armor.Id)==CardLocation.Equipment(1),"An actual foreign hand discard leaves protected armor untouched.");ReplayFourViews(g,r);
        Driver(g,"sweep",[1]);Settle(g);Require(Zones(g).GetLocation(armor.Id)==CardLocation.Equipment(1),"Equipment-only participant sweep does not fabricate a discard receipt.");ReplayFourViews(g,r);
        Driver(g,"self-payment",[1]);Reach(g,p=>p.PlayerSeat==1&&p.Choices.Any(c=>c.Cards.Contains(armor.Id)));ReplayFourViews(g,r);Answer(g,c=>c.Cards.SequenceEqual([armor.Id]));Settle(g);Require(Zones(g).GetLocation(armor.Id)==CardLocation.DiscardPile,"The participant's own explicit discard cost is legal.");ReplayFourViews(g,r);
        foreach(var self in new[]{true,false})
        {
            var(b,br)=Start(CardKind.Dodge,equipment:"standard:renwang_shield");Driver(b,"equip",[1]);Settle(b);var entity=b.CreateSnapshot(0).Players[1].Equipment.Single().Id;
            Driver(b,self?"self-bound":"incoming",[1]);if(!self){Reach(b,p=>p.SkillPrompt?.SkillId=="fixture:hy-source");Activate(b);}
            Reach(b,p=>p.Choices.Any(c=>c.Cards.SequenceEqual([entity])));ReplayFourViews(b,br);Answer(b,c=>c.Cards.SequenceEqual([entity]));
            Reach(b,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option"));
            Require(Frame(b).CardSetBindings.Single(x=>x.Name=="bound").SelectionActorSeat==(self?1:0),"Actual participant/source chooser is frozen on the owning binding.");ReplayFourViews(b,br);Answer(b,c=>c.Parameters.GetValueOrDefault("option-id")=="done");Settle(b);
            Require(Zones(b).GetLocation(entity)==(self?CardLocation.DiscardPile:CardLocation.Equipment(1))&&b.CardMovements.Count(m=>m.CardId==entity&&m.From==CardLocation.Equipment(1)&&m.To==CardLocation.DiscardPile)==(self?1:0),"A bound participant cost pays once; a bound foreign discard is prevented before movement.");ReplayFourViews(b,br);
        }
    }

    private static (GameEngine,ContentRegistry) Start(CardKind use,bool conversion=false,bool nullification=false,CardKind drawKind=CardKind.Dodge,int remaining=40,bool child=false,string? equipment=null)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(use,conversion,nullification,drawKind,remaining,child,equipment));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-hy",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:hy",g.Revision,P(g)!.PromptId));Settle(g);return(g,r);
    }
    private static void Play(GameEngine g,LegalAction a)
    {
        var command=new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource};
        var evidence=Environment.GetEnvironmentVariable("CARD_HY_EVIDENCE");
        if(evidence is not null&&a.Kind==LegalActionKind.Slash)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence,"accepted-prefix.checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));
            File.WriteAllText(Path.Combine(evidence,"attempted-command.json"),CommandJson.Serialize([command]));
        }
        try{Accept(g,command);}
        catch(Exception ex)
        {
            if(evidence is not null&&a.Kind==LegalActionKind.Slash)
            {
                File.WriteAllText(Path.Combine(evidence,"exception.txt"),ex.ToString());
                File.WriteAllText(Path.Combine(evidence,"typed-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));
            }
            throw;
        }
    }
    private static void Driver(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:hy-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Settle(GameEngine g)
    {
        for(var step=0;step<100;step++)
        {
            if(P(g) is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return;
            if(P(g) is{Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(P(g) is{Kind:DecisionKind.SelectTargetCard})Answer(g,c=>true);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixture failed to settle: "+P(g)?.Prompt);
    }
    private static CardZoneStore Zones(GameEngine g)=>(CardZoneStore)typeof(GameEngine).GetField("_cardZones",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\""+(members.Contains("options",StringComparison.Ordinal)?",\"optionLabels\":{\"done\":\"继续\"}":"")+"}}}").Programs[id];
    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";
    private sealed class Fixture(CardKind use,bool conversion,bool nullification,CardKind drawKind,int remaining,bool child,string? equipment):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-hy",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            try { CharacterFixtureContent.Register(b, "boundary:huang-yueying"); }
            catch(TargetInvocationException ex){throw new InvalidOperationException(ex.InnerException?.ToString(),ex.InnerException);}
            if(conversion){b.AddSkill(Classic.Value.GetSkill("classic:qixi"));b.AddSkill(Classic.Value.GetSkill("classic:kanpo"));}
            if(equipment?.StartsWith("classic:",StringComparison.Ordinal)==true)b.AddCard(Classic.Value.GetCard(equipment));
            if(equipment=="ice")b.AddCard(Classic.Value.GetCard("classic:ice-sword"));
            var own="{\"kind\":\"owner\"}";var target="{\"kind\":\"selectedTarget\"}";
            var activations=Activation("equip","[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipped\"}]",1)+","+
                Activation("obtain","[{\"op\":\"takeSelectedTargetCards\",\"target\":\"selectedTarget\",\"amount\":1}]",1)+","+
                Activation("foreign-payment","[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":"+own+",\"cardOwnerRef\":"+target+",\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"discardPile\",\"skipIfNoCards\":true}]",1)+","+
                Activation("self-payment","[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":"+target+",\"cardOwnerRef\":"+target+",\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"discardPile\"}]",1)+","+
                Activation("other-discard","[{\"op\":\"chooseOtherOwnedCardDiscard\",\"target\":\"owner\",\"chooserRef\":"+own+",\"zones\":[\"hand\",\"equipment\"]}]")+","+
                Activation("sweep","[{\"op\":\"discardParticipantCards\",\"target\":\"owner\",\"zones\":[\"equipment\"],\"amount\":1}]",1)+","+
                Activation("self-bound","[{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"zones\":[\"equipment\"],\"amount\":1,\"resultBind\":\"bound\"},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"confirm\",\"options\":[{\"id\":\"done\",\"condition\":{\"kind\":\"always\"}}]},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"bound\",\"destination\":\"discardPile\"}]",1)+","+
                Activation("trim","[{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"zones\":[\"hand\"],\"amount\":3,\"resultBind\":\"bound\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"bound\",\"destination\":\"discardPile\"}]",1)+","+
                Activation("empty-hand","[{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"zones\":[\"hand\"],\"amount\":4,\"resultBind\":\"bound\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"bound\",\"destination\":\"discardPile\"}]",1)+","+
                Activation("incoming","[{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1}]",1)+","+
                Activation("kill","[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}]",1);
            b.AddSkill(new("fixture:hy-driver","实际链","实际链"){Program=Load("fixture:hy-driver","\"activations\":["+activations+"]")});
            b.AddSkill(new("fixture:hy-source","来源选择","来源选择"){Program=Load("fixture:hy-source","\"triggers\":[{\"id\":\"source\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamagePoint\",\"optional\":true,\"effects\":[{\"op\":\"selectSourceCard\",\"target\":\"owner\",\"cardSource\":\"damageSource\",\"zones\":[\"equipment\"],\"resultBind\":\"bound\",\"skipIfNoCards\":true},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"confirm\",\"options\":[{\"id\":\"done\",\"condition\":{\"kind\":\"always\"}}]},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"bound\",\"destination\":\"discardPile\"}]}]")});
            if(child)b.AddSkill(new("fixture:hy-child","移动摸牌收据","移动摸牌收据"){Program=Load("fixture:hy-child","\"states\":[{\"id\":\"once\",\"initialValue\":false,\"visibility\":\"private\",\"resetScope\":\"game\",\"reacquirePolicy\":\"preserveUntilGameEnd\"}],\"triggers\":[{\"id\":\"gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"condition\":{\"kind\":\"booleanState\",\"stateId\":\"once\",\"expectedValue\":false},\"effects\":[{\"op\":\"setBooleanState\",\"target\":\"owner\",\"stateId\":\"once\",\"value\":true},{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"amount\":1,\"resultBind\":\"removed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"removed\",\"destination\":\"discardPile\"}]}]")});
            b.AddGeneral(new("fixture:hy","黄月英","supporter","boundary:jizhi-current","shu",3,new[]{"boundary:qicai-current","fixture:hy-driver","fixture:hy-source"}.Concat(conversion?["classic:qixi","classic:kanpo"]:Array.Empty<string>()).Concat(child?["fixture:hy-child"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:hy-{i}","目标"+i,"supporter","boundary:qicai-current","wei",5));
            var ids=new Dictionary<CardKind,string>{{CardKind.Slash,"standard:slash"},{CardKind.Peach,"standard:peach"},{CardKind.Dodge,"standard:dodge"},{CardKind.Duel,"standard:duel"},{CardKind.DrawTwo,"standard:draw_two"},{CardKind.IronChain,"standard:iron_chain"},{CardKind.Indulgence,"standard:indulgence"},{CardKind.SupplyShortage,"standard:supply_shortage"},{CardKind.Lightning,"standard:lightning"},{CardKind.Dismantlement,"standard:dismantlement"}};
            b.AddDeck(new("fixture:hy-deck","固定",4,0,[]){PhysicalCards=Enumerable.Range(0,16+remaining).Select(i=>new ContentDeckPhysicalCard(remaining<2?ids[use]:i==28?ids[use]:nullification&&i==7&&!conversion?"standard:nullification":equipment=="ice"?i==20?"classic:ice-sword":i==21?"standard:renwang_shield":ids[drawKind]:equipment is not null&&i>=20?equipment:ids[drawKind],conversion?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-hy","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:hy-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:hy","fixture:hy-1","fixture:hy-2","fixture:hy-3"]));
        }
    }
}
