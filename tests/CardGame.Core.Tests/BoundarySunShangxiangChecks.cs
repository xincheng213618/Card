using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class BoundarySunShangxiangChecks
{
    private const string Skill = "boundary:jieyin-current";
    private static readonly Lazy<ContentRegistry> Classic = new(() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()));

    public static void DefinitionsAndHandPayment()
    {
        PlacementDefinitions();
        var (g,r)=Start();
        Require(r.Generals["boundary:sun-shangxiang"].SkillIds.SequenceEqual([Skill,"classic:xiaoji"]),"The real module reuses classic Xiaoji.");
        Driver(g,"hurt-target",[1]);Settle(g);
        var initial=g.State.Players[0].Hand.Count;var cost=g.State.Players[0].Hand[0].Id;
        var rev=g.Revision;
        var invalid=g.Submit(new UseProgramSkillCommand(0,Skill,"discard-hand",[cost],[0],g.Revision,P(g)!.PromptId));
        Require(!invalid.Accepted&&g.Revision==rev,"A female target is rejected atomically before cost/quota.");
        Use(g,"discard-hand",cost,1);Settle(g);
        Require(Zones(g).GetLocation(cost)==CardLocation.DiscardPile&&g.State.Players[0].Hand.Count==initial&&g.State.Players[1].Hp==3,
            "The higher owner draws once after discarding one hand entity; the lower target recovers once.");
        Require(!Actions(g).Any(),"Both active bindings share the actual phase usage.");ReplayFourViews(g,r);
        var json=JsonSerializer.Serialize(new ProgramSelectedCardPayment(1,SkillProgramEffectOp.DiscardSelected,[1],0));
        Require(!json.Contains("PlacementSourceLocation"),"Legacy selected payments omit null placement provenance.");
        var unsupportedTail=false;
        try{Load("fixture:bad-tail","\"activations\":["+Activation("bad","[{\"op\":\"freezeSelectedHpPair\",\"target\":\"owner\"},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1,\"resultBind\":\"later\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"later\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]",1)+"]");}
        catch(InvalidOperationException ex){unsupportedTail=ex.Message.Contains("only a Draw/Recover tail");Console.WriteLine("New HP pair tail negative: "+ex.Message);}
        Require(unsupportedTail,"A loaded frozen pair cannot subsequently start an unsupported owning movement.");
        foreach(var effects in new[]{"[{\"op\":\"draw\",\"target\":\"hpPairHigher\",\"amount\":1}]",
            "[{\"op\":\"freezeSelectedHpPair\",\"target\":\"owner\"},{\"op\":\"loseHp\",\"target\":\"hpPairLower\",\"amount\":1}]",
            "[{\"op\":\"freezeSelectedHpPair\",\"target\":\"owner\"},{\"op\":\"freezeSelectedHpPair\",\"target\":\"owner\"}]",
            "[{\"op\":\"freezeSelectedHpPair\",\"target\":\"owner\"},{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"draw\",\"target\":\"hpPairHigher\",\"amount\":1}]"})
        {var rejected=false;try{Load("fixture:bad-pair","\"activations\":["+Activation("bad",effects,1)+"]");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Frozen pair consumers require their unconditional producer and are Draw/Recover only.");}
    }

    private static void PlacementDefinitions()
    {
        foreach(var effects in new[]{
            "[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"minimumCards\":1,\"maximumCards\":1,\"resultBind\":\"cost\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"cost\"}]",
            "[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"cost\"},{\"op\":\"filterBoundCards\",\"target\":\"owner\",\"sourceBind\":\"cost\",\"resultBind\":\"derived\",\"categories\":[\"equipment\"]},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"cost\"}]",
            "[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"cost\"},{\"op\":\"filterBoundCards\",\"target\":\"owner\",\"sourceBind\":\"cost\",\"resultBind\":\"derived\",\"categories\":[\"equipment\"]},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"derived\"}]"})
        {
            var rejected=false;try{Load("fixture:bad-placement","\"activations\":[{\"id\":\"bad\",\"usesPerTurn\":null,\"minCards\":1,\"maxCards\":1,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"anyLivingMale\",\"sourceZones\":[\"hand\",\"equipment\"],\"cardCategories\":[\"equipment\"],\"effects\":"+effects+"}]");}
            catch(InvalidOperationException ex){rejected=ex.Message.Contains("original unconditional single captured activation binding");Console.WriteLine("New placement parser negative: "+ex.Message);}
            Require(rejected,"Placement rejects other producers and derived captured bindings at the new resource gate.");
        }
    }

    public static void EquipmentAndReplacement()
    {
        var (g,r)=Start(equipment:true);
        Driver(g,"equip",[1]);Settle(g);var old=g.CreateSnapshot(0).Players[1].Equipment.Single().Id;
        Driver(g,"hurt-owner");Settle(g);var cost=g.State.Players[0].Hand.First(c=>EquipmentCatalog.IsEquipment(c.Kind)).Id;
        var before=g.State.Players[1].HandCount;Use(g,"place-equipment",cost,1);Settle(g);
        Require(Zones(g).GetLocation(cost)==CardLocation.Equipment(1)&&Zones(g).GetLocation(old)==CardLocation.DiscardPile,
            "Actual placement replaces the occupied slot instead of using an equipment card.");
        Require(g.State.Players[1].HandCount==before+1&&g.State.Players[0].Hp==3,"Higher target Draw then lower owner Recover resolve once: hp="+g.State.Players[0].Hp+" target="+g.State.Players[1].Hp+" hand="+g.State.Players[1].HandCount+" before="+before);
        Require(!g.Events.Select(e=>e.Payload).OfType<CardUsedEvent>().Any(e=>e.CardId==cost),"Placement never becomes a card Use.");ReplayFourViews(g,r);

        var (transfer,tr)=Start(equipment:true,protectedTarget:true);
        Driver(transfer,"equip",[0]);Settle(transfer);var equipped=transfer.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        Use(transfer,"place-equipment",equipped,1);Reach(transfer,p=>p.SkillPrompt?.SkillId=="classic:xiaoji");ReplayFourViews(transfer,tr);Evidence(transfer,"paid-equipment-xiaoji");
        var parent=transfer.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Skill);
        Require(parent.SelectedCardPayment is {MovementCommitted:true,PlacementSourceLocation.Zone:CardZoneKind.Equipment}&&parent.HpPairSnapshot is null,
            "The owning paid physical equipment cost waits for real Xiaoji before freezing HP.");
        Activate(transfer);Settle(transfer);
        Require(Zones(transfer).GetLocation(equipped)==CardLocation.Equipment(1),"Transfer of armor is not prevented by current Qicai.");ReplayFourViews(transfer,tr);
    }

    public static void PairFreezesAcrossChildren()
    {
        var (g,r)=Start(equipment:true,movementChild:true);
        Driver(g,"hurt-target",[1]);Settle(g);Driver(g,"equip",[0]);Settle(g);
        var card=g.CreateSnapshot(0).Players[0].Equipment.Single().Id;Use(g,"place-equipment",card,1);
        while(P(g)?.SkillPrompt?.SkillId=="classic:xiaoji")Skip(g);
        Settle(g);
        Require(g.State.Players[0].Hp==2&&g.State.Players[1].Hp==2,
            "Actual equipment-loss HP child finishes before comparison: owner 3 to 1 then recovers to 2.");ReplayFourViews(g,r);

        var (d,dr)=Start(drawChild:true);Driver(d,"hurt-owner");Settle(d);
        Use(d,"discard-hand",d.State.Players[0].Hand[0].Id,1);Settle(d);
        Require(d.State.Players[0].Hp==3&&d.State.Players[1].Hp==1,
            "Higher target Draw child frozen recovery: hp="+d.State.Players[0].Hp+" target="+d.State.Players[1].Hp);ReplayFourViews(d,dr);
    }

    public static void SlotSelfAndEqualBoundaries()
    {
        var(blocked,br)=Start(equipment:true,slotCapacity:0);var initial=blocked.State.Players[0].Hand[0].Id;
        Require(!Actions(blocked).Any(a=>a.ProgramActivationId=="place-equipment"),"Only male recipients with a real payable slot appear on the placement menu.");
        var unchanged=blocked.Revision;var noSlot=blocked.Submit(new UseProgramSkillCommand(0,Skill,"place-equipment",[initial],[1],blocked.Revision,P(blocked)!.PromptId));
        Require(!noSlot.Accepted&&blocked.Revision==unchanged&&Zones(blocked).GetLocation(initial)==CardLocation.Hand(0),"Zero slot rejects before physical cost and shared quota.");
        Use(blocked,"discard-hand",initial,1);Settle(blocked);ReplayFourViews(blocked,br);

        var(multi,mr)=Start(equipment:true,slotCapacity:2);Driver(multi,"equip",[1]);Settle(multi);Driver(multi,"equip",[1]);Settle(multi);
        var oldIds=multi.CreateSnapshot(0).Players[1].Equipment.Select(c=>c.Id).ToArray();Require(oldIds.Length==2,"The actual recipient has two armor slots.");
        var replacement=multi.State.Players[0].Hand[0].Id;Use(multi,"place-equipment",replacement,1);Settle(multi);
        Require(multi.CreateSnapshot(0).Players[1].Equipment.Count==2&&Zones(multi).GetLocation(oldIds[0])==CardLocation.DiscardPile&&Zones(multi).GetLocation(replacement)==CardLocation.Equipment(1),"Full multi-capacity slot replaces only the mature first equipment.");ReplayFourViews(multi,mr);
        var (g,r)=Start(equipment:true,maleOwner:true);
        Driver(g,"equip",[0]);Settle(g);var equipped=g.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var revision=g.Revision;var rejected=g.Submit(new UseProgramSkillCommand(0,Skill,"place-equipment",[equipped],[0],g.Revision,P(g)!.PromptId));
        Require(!rejected.Accepted&&g.Revision==revision&&Zones(g).GetLocation(equipped)==CardLocation.Equipment(0),"Same-equipment-area self placement is no physical payment.");
        var cost=g.State.Players[0].Hand.First(c=>EquipmentCatalog.IsEquipment(c.Kind)).Id;var hand=g.State.Players[0].Hand.Count;
        Use(g,"place-equipment",cost,0);Settle(g);
        Require(Zones(g).GetLocation(cost)==CardLocation.Equipment(0)&&g.State.Players[0].Hand.Count==hand-1,
            "A real Male self hand placement pays once, with self-pair no reward.");ReplayFourViews(g,r);

        var(equal,er)=Start();Driver(equal,"hurt-target-one",[1]);Settle(equal);
        var count=equal.State.Players[0].Hand.Count;Use(equal,"discard-hand",equal.State.Players[0].Hand[0].Id,1);Settle(equal);
        Require(equal.State.Players[0].Hand.Count==count-1&&equal.State.Players[0].Hp==3&&equal.State.Players[1].Hp==3,"Equal branch: hand="+equal.State.Players[0].Hand.Count+" before="+count+" hp="+equal.State.Players[0].Hp+","+equal.State.Players[1].Hp);ReplayFourViews(equal,er);
    }

    public static void NativeAndLifetime()
    {
        foreach(var equipment in new[]{false,true})
        {
            var(g,r)=Start(equipment:equipment,native:true);
            for(var step=0;step<60&&!g.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Any(e=>e.SkillId==Skill);step++)Accept(g,new AdvanceOneStepCommand(g.Revision));
            Require(g.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Any(e=>e.SkillId==Skill&&e.ActivationId==(equipment?"place-equipment":"discard-hand")),
                "Native real "+(equipment?"placement":"hand discard")+"; turn="+g.State.CurrentSeat+" HP="+string.Join(",",g.State.Players.Select(p=>p.Hp))+" programs="+string.Join(";",g.Events.Select(e=>e.Payload).OfType<ProgramSkillStartedEvent>().Select(e=>e.SkillId+"/"+e.ActivationId)));
            for(var step=0;step<30&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill);step++)Accept(g,new AdvanceOneStepCommand(g.Revision));
            Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill),"Native pair settlement is finite.");ReplayFourViews(g,r);
            Evidence(g,"native-"+(equipment?"placement":"discard"));
            Console.WriteLine("Native formal Jieyin "+(equipment?"placement":"hand discard")+" actually started and completed; accepted revision="+g.Revision);
        }
        var(extra,er)=Start(extraPlay:true);var turn=extra.CreateSnapshot(0).TurnNumber;
        Use(extra,"discard-hand",extra.State.Players[0].Hand[0].Id,1);Settle(extra);Require(!Actions(extra).Any(),"One real scheduled Play consumes the shared quota.");
        Accept(extra,new EndPlayPhaseCommand(0,extra.Revision,P(extra)!.PromptId));Settle(extra);
        Require(extra.CreateSnapshot(0).TurnNumber==turn&&Actions(extra).Any(),"The subsequent normal Play has its own actual phase quota within the same turn.");
        Use(extra,"discard-hand",extra.State.Players[0].Hand[0].Id,1);Settle(extra);ReplayFourViews(extra,er);

        foreach(var activate in new[]{false,true})
        {
            var(silver,sr)=Start(equipment:true,silver:true,observer:true);Driver(silver,"hurt-owner");Settle(silver);Driver(silver,"hurt-target",[1]);Settle(silver);Driver(silver,"equip",[0]);Settle(silver);
            var paid=silver.CreateSnapshot(0).Players[0].Equipment.Single().Id;Use(silver,"place-equipment",paid,1);
            Reach(silver,p=>p.SkillPrompt?.SkillId=="fixture:ssx-hp");ReplayFourViews(silver,sr);Evidence(silver,"silver-hp-observer-"+activate);
            Require(silver.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Skill).HpPairSnapshot is null,"The actual Silver Lion HP observer precedes pair freezing.");
            if(activate)Activate(silver);else Skip(silver);Settle(silver);
            Require(silver.State.Players[0].Hp==3&&silver.State.Players[1].Hp==3&&Zones(silver).GetLocation(paid)==CardLocation.Equipment(1),"Silver Lion issued recovery and pair reward each finish after one actual placement cost.");ReplayFourViews(silver,sr);
        }
        foreach(var save in new[]{false,true})
        {
            var(dying,rr)=Start(equipment:true,rescue:save,movementChild:true,lossAmount:3,observer:true);
            Driver(dying,"equip",[0]);Settle(dying);var paid=dying.CreateSnapshot(0).Players[0].Equipment.Single().Id;
            var peach=save?dying.State.Players[0].Hand.First(c=>c.Kind==CardKind.Peach).Id:0;
            Use(dying,"place-equipment",paid,1);
            for(var step=0;step<60&&dying.State.Status!=EngineStatus.Completed&&!(P(dying)?.Kind==DecisionKind.RescueDying&&P(dying)?.PlayerSeat==0);step++)
            {if(P(dying)?.SkillPrompt?.SkillId=="classic:xiaoji")Skip(dying);else Accept(dying,new AdvanceOneStepCommand(dying.Revision));}
            ReplayFourViews(dying,rr);
            if(save)
            {
                Require(P(dying) is{Kind:DecisionKind.RescueDying,PlayerSeat:0}&&dying.State.Players[0].Hp==0,"The real movement child reaches exactly zero HP rescue after Xiaoji.");
                Evidence(dying,"physical-peach-rescue");Answer(dying,c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([peach]));
            }
            for(var step=0;step<80&&dying.State.Status!=EngineStatus.Completed&&dying.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill);step++)
            {
                if(P(dying)?.Kind==DecisionKind.RescueDying)Answer(dying,c=>c.Cards.Count==0);
                else if(P(dying)?.Kind==DecisionKind.ProgramTrigger&&P(dying)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")){ReplayFourViews(dying,rr);if(save&&P(dying)!.SkillPrompt?.SkillId=="fixture:ssx-hp")Activate(dying);else Skip(dying);}
                else Accept(dying,new AdvanceOneStepCommand(dying.Revision));
            }
            Require(!dying.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Skill)&&Zones(dying).GetLocation(paid)==CardLocation.Equipment(1),"Death or actual entity Peach save returns without repeating the issued placement.");
            Require(save?dying.State.Players[0].IsAlive&&Zones(dying).GetLocation(peach)==CardLocation.DiscardPile:!dying.State.Players[0].IsAlive,"Save pays one original-hand Peach; death finitely cancels the source remainder.");
            Require(dying.Events.Select(e=>e.Payload).OfType<CardMovedEvent>().Count(e=>e.CardId==paid&&e.From==CardLocation.Equipment(0)&&e.To==CardLocation.Equipment(1))==1,"The physical issued equipment cost moves exactly once across death/rescue children.");
            Evidence(dying,"movement-"+(save?"peach-saved":"owner-dead"));ReplayFourViews(dying,rr);Console.WriteLine("Actual movement child "+(save?"physical Peach save with HP observer":"owner death/game completion")+"; cost="+paid+"; revision="+dying.Revision);
        }
    }

    private static void Evidence(GameEngine g,string label)
    {
        if(Environment.GetEnvironmentVariable("CARD_SSX_EVIDENCE") is not{Length:>0} root)return;
        var path=Path.Combine(root,label);Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"checkpoint.json"),GameCheckpointJson.Serialize(g.CreateCheckpoint()));
        File.WriteAllText(Path.Combine(path,"trusted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));
        for(var viewer=0;viewer<4;viewer++)File.WriteAllText(Path.Combine(path,"view-"+viewer+".json"),JsonSerializer.Serialize(g.CreateSnapshot(viewer)));
    }
    private static int EvidenceSequence;
    private static void Accept(GameEngine g,GameCommand command)
    {
        var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());
        try{BoundaryLiDianChecks.Accept(g,command);}
        catch(Exception ex)
        {
            if(Environment.GetEnvironmentVariable("CARD_SSX_EVIDENCE") is{Length:>0} root)
            {
                var path=Path.Combine(root,"actual-submit-"+Interlocked.Increment(ref EvidenceSequence).ToString("D3"));Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path,"checkpoint-before.json"),before);
                File.WriteAllText(Path.Combine(path,"attempt.json"),JsonSerializer.Serialize(command,command.GetType()));
                File.WriteAllText(Path.Combine(path,"faulted-stack.json"),JsonSerializer.Serialize(g.ResolutionStack));
                File.WriteAllText(Path.Combine(path,"exception.txt"),ex.ToString());
            }
            throw;
        }
    }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static IEnumerable<LegalAction> Actions(GameEngine g)=>g.GetHumanLegalActions().Where(a=>a.ProgramSkillId==Skill);
    private static CardZoneStore Zones(GameEngine g)=>(CardZoneStore)typeof(GameEngine).GetField("_cardZones",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
    private static void Use(GameEngine g,string id,int card,int target)=>Accept(g,new UseProgramSkillCommand(0,Skill,id,[card],[target],g.Revision,P(g)!.PromptId));
    private static void Driver(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:ssx-driver",id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Settle(GameEngine g)
    {
        for(var step=0;step<100;step++)
        {
            if(P(g) is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return;
            if(P(g)?.Kind==DecisionKind.ProgramTrigger&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Skip(g);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Pair fixture failed to settle: "+P(g)?.Prompt);
    }
    private static (GameEngine,ContentRegistry) Start(bool equipment=false,bool protectedTarget=false,bool movementChild=false,bool drawChild=false,bool maleOwner=false,bool native=false,int? slotCapacity=null,bool silver=false,bool observer=false,bool rescue=false,int lossAmount=2,bool extraPlay=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(equipment,protectedTarget,movementChild,drawChild,maleOwner,native,slotCapacity,silver,observer,rescue,lossAmount,extraPlay));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=native?3:0,HumanRole=native?Role.Rebel:Role.Lord,
            ModeId="identity:classic-ssx",UseInteractiveSetup=!native,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());
        if(!native){Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:ssx",g.Revision,P(g)!.PromptId));Settle(g);}
        return(g,r);
    }
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";
    private sealed class Fixture(bool equipment,bool protectedTarget,bool movementChild,bool drawChild,bool maleOwner,bool native,int? slotCapacity,bool silver,bool observer,bool rescue,int lossAmount,bool extraPlay):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ssx",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var module in new[]{"BoundarySunShangxiangContent",protectedTarget?"BoundaryHuangYueyingContent":null}.OfType<string>())
                try{typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard."+module)!.GetMethod("Register",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[b]);}
                catch(TargetInvocationException ex){throw new InvalidOperationException(ex.InnerException?.ToString(),ex.InnerException);}
            b.AddSkill(Classic.Value.GetSkill("classic:xiaoji"));
            if(silver)b.AddCard(Classic.Value.GetCard("classic:silver-lion"));
            if(extraPlay)b.AddSkill(Classic.Value.GetSkill("classic:dangxian"));
            if(slotCapacity is { } capacity)b.AddSkill(new("fixture:ssx-slot","实际装备槽","实际装备槽"){Program=Load("fixture:ssx-slot","\"triggers\":[{\"id\":\"slot\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"alterEquipmentSlots\",\"target\":\"owner\",\"equipmentSlots\":[\"armor\"],\"amount\":"+capacity+"}]}]")});
            if(observer)b.AddSkill(new("fixture:ssx-hp","真实回复子窗","真实回复子窗"){Program=Load("fixture:ssx-hp","\"triggers\":[{\"id\":\"hp\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]")});
            var acts=Activation("hurt-owner","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]")+","+
                Activation("hurt-target","[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":2}]",1)+","+
                Activation("hurt-target-one","[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}]",1)+","+
                Activation("equip","[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipped\"}]",1);
            b.AddSkill(new("fixture:ssx-driver","实际准备","实际准备"){Program=Load("fixture:ssx-driver","\"activations\":["+acts+"]")});
            if(movementChild)b.AddSkill(new("fixture:ssx-loss","装备失去HP子窗","装备失去HP子窗"){Program=Load("fixture:ssx-loss","\"triggers\":[{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementOccurrence\":\"perCard\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+lossAmount+"}]}]")});
            if(drawChild)b.AddSkill(new("fixture:ssx-gain","摸牌HP子窗","摸牌HP子窗"){Program=Load("fixture:ssx-gain","\"triggers\":[{\"id\":\"gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":3}]}]")});
            b.AddGeneral(new("fixture:ssx","孙尚香","supporter",Skill,"wu",3,
                new[]{"classic:xiaoji"}.Concat(!native?["fixture:ssx-driver"]:Array.Empty<string>()).Concat(movementChild?["fixture:ssx-loss"]:Array.Empty<string>()).Concat(observer?["fixture:ssx-hp"]:Array.Empty<string>()).Concat(extraPlay?["classic:dangxian"]:Array.Empty<string>()).ToArray(),maleOwner?GeneralGender.Male:GeneralGender.Female){InitialHp=2});
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:ssx-{i}","男方"+i,"supporter",native?Skill:protectedTarget?"boundary:qicai-current":"fixture:ssx-driver","wu",4,
                (drawChild?new[]{"fixture:ssx-gain"}:Array.Empty<string>()).Concat(slotCapacity is not null?["fixture:ssx-slot"]:Array.Empty<string>()).ToArray(),GeneralGender.Male){InitialHp=native?2:null});
            b.AddDeck(new("fixture:ssx-deck","固定",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(rescue&&i%2==0?"standard:peach":equipment?(silver?"classic:silver-lion":"standard:renwang_shield"):"standard:dodge",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-ssx","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:ssx-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:ssx","fixture:ssx-1","fixture:ssx-2","fixture:ssx-3"]));
        }
    }
}
