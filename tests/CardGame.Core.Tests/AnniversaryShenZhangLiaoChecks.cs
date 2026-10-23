using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryShenZhangLiaoChecks
{
    private const string Hero="anniversary:shen-zhang-liao",Driver="fixture:ann-zl-driver",Watch="fixture:ann-zl-watch",Quiet="fixture:ann-zl-quiet";
    public static void CuixiPaidColdAndTemporarySlotsUseActualPhases()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var g=GameEngine.CreateStandard(Options(),registry);Accept(g,new StartGameCommand());Play(g);
        var weapon=V(g).Hand.First(c=>c.Kind==CardKind.Crossbow).Id;
        Use(g,"equip",[1]);Reach(g,p=>p.Choices.Any(c=>c.Cards.SequenceEqual([weapon])));Answer(g,c=>c.Cards.SequenceEqual([weapon]));Play(g);
        var hp=V(g,1).Hp;Use(g,"damage",[1]);Menu(g,AnniversaryZhangLiaoStage.SlotChoice);Answer(g,c=>Option(c)=="slot:Weapon");Play(g);
        Need(Zone(g,weapon)==CardLocation.DiscardPile&&Capacity(g,1,EquipmentSlot.Weapon)==0&&V(g,1).Hp==hp-1&&E<AnniversaryDuoruiLeaseEvent>(g).Length==1,
            "Duorui truly abolishes the chosen other weapon slot, discards its actual equipped card, then returns the original source damage.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        Offer(g,1);var hand=V(g).HandCount;Answer(g,c=>Action(c)=="activate");Menu(g,AnniversaryZhangLiaoStage.Payment);
        var paidCard=P(g)!.Choices.First().Cards.Single();Answer(g,c=>c.Cards.SequenceEqual([paidCard]));
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch);var paidRoot=Root(g)!;
        Need(paidRoot.AnniversaryZhangLiao is {Stage:AnniversaryZhangLiaoStage.PaymentChildren,PaidCount:1,Paid.Count:1}&&paidRoot.PendingMovementContinuation is not null&&
            Zone(g,paidCard)==CardLocation.DiscardPile&&E<AnniversaryCuixiBranchEvent>(g).Length==0,
            "Cuixi pays one real hand card and pauses its movement child before the other actual turn owner makes a choice.");
        g=Cold(g,registry);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        Menu(g,AnniversaryZhangLiaoStage.VictimChoice);
        Need(P(g)!.PlayerSeat==1&&P(g)!.Choices.Select(Option).Order().SequenceEqual(new[]{"damage","draw"}),"The actual turn owner receives both formal branches after payment.");
        Answer(g,c=>Option(c)=="damage");Menu(g,AnniversaryZhangLiaoStage.SlotChoice);Answer(g,c=>Option(c)=="slot:Armor");
        Menu(g,AnniversaryZhangLiaoStage.DiscardChoice);
        Need(V(g,1).Hp==hp-2&&Capacity(g,1,EquipmentSlot.Armor)==0&&Root(g)!.AnniversaryZhangLiao!.Count==2,
            "Cuixi's damage is native source damage and triggers a second independent Duorui lease; Zhiti freezes the actual two abolished slots at Discard end.");
        var foreignBefore=V(g,1).HandCount;
        for(var n=0;n<2;n++)
        {
            Menu(g,AnniversaryZhangLiaoStage.DiscardChoice);
            Need(P(g)!.Choices.Any(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)),"Zhiti selects opaque foreign Hand slots.");
            Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)&&c.Parameters.GetValueOrDefault("slot-index")=="0");
        }
        Offer(g,2);
        Need(V(g,1).HandCount==foreignBefore-2&&Capacity(g,1,EquipmentSlot.Weapon)==0&&Capacity(g,1,EquipmentSlot.Armor)==0,
            "Zhiti discards two real foreign entities once each while both temporary slots remain abolished through the other character's completed turn.");
        Answer(g,c=>Action(c)=="activate");Menu(g,AnniversaryZhangLiaoStage.Payment);Answer(g,_=>true);
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        Menu(g,AnniversaryZhangLiaoStage.VictimChoice);var drawnBefore=V(g).HandCount;var victimBefore=V(g,2).HandCount;
        Answer(g,c=>Option(c)=="draw");Offer(g,3);
        Need(V(g).HandCount==drawnBefore+2&&V(g,2).HandCount==victimBefore&&
            E<AnniversaryCuixiBranchEvent>(g).Select(e=>e.Branch).SequenceEqual([2,1]),
            "The other branch draws exactly two through the native producer and really skips only that actual turn owner's normal draw.");
        Answer(g,c=>Action(c)=="skip");Play(g);
        Need(V(g).HandCount==hand+4&&Capacity(g,1,EquipmentSlot.Weapon)==1&&Capacity(g,1,EquipmentSlot.Armor)==1&&
            E<AnniversaryDuoruiExpiredEvent>(g).Count(e=>e.Reason=="next-play")==2&&E<AnniversaryZhangLiaoPaidEvent>(g).Count(e=>e.FrameId==paidRoot.Id)==1&&
            g.CardMovements.Count(m=>m.CardId==paidCard&&m.Reason.Value=="skill-program.anniversary-zhang-liao.discard")==1,
            "The next own Draw really gains two Zhiti cards before the next actual Play restores both original capacities; cold return never repeats the paid card. Actual="+
            JsonSerializer.Serialize(new{HandBeforeEnd=hand,HandNow=V(g).HandCount,Weapon=Capacity(g,1,EquipmentSlot.Weapon),Armor=Capacity(g,1,EquipmentSlot.Armor),
                Leases=E<AnniversaryDuoruiLeaseEvent>(g),Expired=E<AnniversaryDuoruiExpiredEvent>(g),Slots=E<EquipmentSlotCapacityChangedEvent>(g),
                DrawAdjustments=E<ProgramNormalDrawAdjustedEvent>(g),PaidInvoices=E<AnniversaryZhangLiaoPaidEvent>(g).Count(e=>e.FrameId==paidRoot.Id),
                PaidCardMoves=g.CardMovements.Count(m=>m.CardId==paidCard&&m.Reason.Value=="skill-program.anniversary-zhang-liao.discard")}));
        Need(State(g)==State(GameReplay.Replay(Options(),g.AcceptedCommands,registry)),"The actual foreign choices, paid child, temporary slots, native skip and Zhiti discard replay from commands.");
    }
    private static GameOptions Options()
    {
        var setup=new ShanheBattleSetup(7,1,"anniversary-zhang-liao",Hero,5,0,0,0,0,[],
            [new(CardKind.Crossbow,Suit.Club,1),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Slash,Suit.Spade,7),new(CardKind.Dodge,Suit.Diamond,6)],[],
            [new("fixture:ann-zl-peer-2"){StartingHand=[new(CardKind.Dodge,Suit.Heart,9),new(CardKind.Slash,Suit.Club,7)]},new("fixture:ann-zl-peer-3"){StartingHand=[new(CardKind.Dodge,Suit.Diamond,9),new(CardKind.Slash,Suit.Spade,8)]}],
            [new("fixture:ann-zl-peer-1"){StartingHand=[new(CardKind.Dodge,Suit.Heart,9),new(CardKind.Slash,Suit.Club,7),new(CardKind.Dodge,Suit.Diamond,8),new(CardKind.Slash,Suit.Spade,6)]}],new string('f',64)){DefaultGodFactionId="wei"};
        return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6};
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static int Capacity(GameEngine g,int seat,EquipmentSlot slot)=>V(g,seat).EquipmentSlotCapacities?.GetValueOrDefault(slot,1)??1;
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.AnniversaryZhangLiao is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g,int id)=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c)=>c.Parameters.GetValueOrDefault("option");
    private static void Need(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Zhang Liao command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing published Zhang Liao choice: "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Use(GameEngine g,string id,int[] targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,p=>p is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Menu(GameEngine g,AnniversaryZhangLiaoStage stage)=>Reach(g,p=>Root(g)?.AnniversaryZhangLiao?.Stage==stage&&p.Choices.Any(c=>Action(c)=="anniversary-zhang-liao"));
    private static void Offer(GameEngine g,int target)=>Reach(g,p=>p.SkillPrompt?.SkillId=="anniversary:cuixi"&&p.Choices.Any(c=>Action(c)=="activate")&&g.ResolutionStack.OfType<ForeignActualTurnStartWindowFrame>().Last().OwnerSeat==target);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> ready)
    {
        for(var n=0;n<240;n++)
        {
            if(P(g) is { } p)
            {
                if(ready(p))return;
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.SkillPrompt?.SkillId==Watch){Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");continue;}
                if(p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
                throw new InvalidOperationException("Unexpected Zhang Liao boundary: "+JsonSerializer.Serialize(new{Prompt=p,Root=Root(g)?.AnniversaryZhangLiao}));
            }
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Zhang Liao missed the bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g,ContentRegistry registry){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(restored),"Cold restore keeps every original frame, invoice, entity movement and viewer snapshot.");return restored;}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("fixture-anniversary-zhang-liao",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b),Hero);
            var json=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"equip","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary-zhang-liao.discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实张辽驱动",description="实际伤害、装备转交。"},[Watch]=new{name="实际支付子窗",description="原生弃置后暂停。",optionLabels=new Dictionary<string,string>{{"continue","继续"}}},[Quiet]=new{name="安静角色",description="跳过出牌。"}};
            var catalog=SkillProgramCatalog.Load(json,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"Native Zhang Liao fixture"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            for(var n=1;n<4;n++)b.AddGeneral(new($"fixture:ann-zl-peer-{n}","真实目标","supporter",Quiet,"wei",8));
            b.AddDeck(new("fixture:ann-zl-deck","小型固定牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(i%2==0?"standard:slash":"standard:dodge",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"固定配牌张辽结算",4,4,new Dictionary<string,int>(),"fixture:ann-zl-deck",GeneralPoolIds:[Hero,"fixture:ann-zl-peer-1","fixture:ann-zl-peer-2","fixture:ann-zl-peer-3"],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{AdditionalSkillIds=[..d.AdditionalSkillIds!,Driver,Watch]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);
        public void AddCard(ContentCardDefinition d)=>b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);
        public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
