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
            foreach(var generalId in new[]{"boundary:sun-shangxiang",protectedTarget?"boundary:huang-yueying":null}.OfType<string>())
                try{CharacterFixtureContent.Register(b, generalId);}
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
