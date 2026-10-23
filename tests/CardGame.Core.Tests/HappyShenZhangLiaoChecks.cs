using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class HappyShenZhangLiaoChecks
{
    private const string Hero="happy:shen-zhang-liao",Duorui="happy:duorui",Peer="fixture:happy-zl-peer",NoDraw="fixture:happy-zl-no-draw",
        Quiet="fixture:happy-zl-quiet",GainHold="fixture:happy-zl-gain",DrawHold="fixture:happy-zl-draw",Driver="fixture:happy-zl-driver",Deck="fixture:happy-zl-deck";
    public static void ViewedColorAbolitionThresholdsAndSourcedRestorationResumeCold()
    {
        var s=Scene.Start();s.Reach(()=>s.P?.SkillPrompt?.SkillId==Duorui&&s.P.Choices.Any(c=>Action(c)=="activate"));
        s.Answer(c=>Action(c)=="activate");s.Menu(HappyZlStage.Target);s.Answer(c=>c.Targets.SequenceEqual([1]));s.Menu(HappyZlStage.Hand);
        var view=s.Root!.HappyZhangLiao!.View.ToArray();var peach=s.V(1).Hand.Single(c=>c.Kind==CardKind.Peach&&c.Suit==Suit.Heart).Id;
        s.Need(view.Order().SequenceEqual(s.V(1).Hand.Select(c=>c.Id).Order())&&
            (s.G.CreateSnapshot(0).PrivateRevealedCards??[]).Select(c=>c.Id).Order().SequenceEqual(view.Order())&&
            !(s.G.CreateSnapshot(2).PrivateRevealedCards??[]).Any(c=>view.Contains(c.Id)),"Duorui really privately views the chosen other hand before choosing one actual card.");
        s.Answer(c=>c.Cards.SequenceEqual([peach]));
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==GainHold&&s.Root?.HappyZhangLiao?.Stage==HappyZlStage.GainChildren);
        var gain=s.Root!;s.Need(s.Zone(peach)==CardLocation.Hand(0)&&gain.HappyZhangLiao!.Payments.Count==1&&s.E<HappyZlColorEvent>().Single() is{Target:1,Color:true},
            "The real obtain commits and signs the actual turn's red target qualification before the gained-card child.");
        s.Cold();s.Skip();s.Play();
        var reds=s.V(0).Hand.Where(c=>c.Kind==CardKind.Slash&&c.Suit==Suit.Heart).Select(c=>c.Id).ToArray();var victimHp=s.V(1).Hp;
        s.Card(reds[0],1);s.Play();
        s.Need(s.V(1).Hp==victimHp-1&&s.E<HappyZlLeaseEvent>().Length==1&&s.G.GetHumanLegalActions().Any(a=>a.CardId==reds[1]&&a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])),
            "The selected target cannot Dodge a real red Slash; its real random abolition plus two wounded living players crosses the strict >2 Zhiti threshold and grants a second Slash.");
        s.Card(reds[1],1);s.Play();var leases=s.E<HappyZlLeaseEvent>();
        s.Need(s.V(1).Hp==victimHp-2&&leases.Length==2&&leases.Select(e=>e.Slot).Distinct().Count()==2&&leases.All(e=>s.Capacity(1,e.Slot)==0)&&
            !s.E<CardRespondedEvent>().Any(e=>e.ResponderSeat==1)&&s.V(1).Hand.Count(c=>c.Kind==CardKind.Dodge)==3,
            "Both actual same-color uses prohibit the target's retained physical Dodges and independently abolish two available slots without repetition.");
        s.Accept(new EndPlayPhaseCommand(0,s.G.Revision,s.P!.PromptId));s.Menu(HappyZlStage.Discard);
        var foreignBefore=s.V(1).HandCount;var discard=s.Root!;
        s.Need(discard.HappyZhangLiao is{Target:1,Count:2,Cursor:0}&&s.P!.Choices.Any(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)),
            "At the other real Discard end the strict >3 threshold freezes two abolished slots while foreign Hand choices remain opaque.");
        for(var n=0;n<2;n++){s.Menu(HappyZlStage.Discard);s.Answer(c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)&&c.Parameters.GetValueOrDefault("slot-index")=="0");}
        var beforeNextDraw=s.V(0).HandCount;s.Play();
        s.Need(s.V(1).HandCount==foreignBefore-2&&s.V(0).HandCount==beforeNextDraw+1&&s.E<HappyZlPaidEvent>().Count(e=>e.FrameId==discard.Id)==2&&leases.All(e=>s.Capacity(1,e.Slot)==0),
            "Zhiti discards two actual other-zone entities once, adds one card to the next normal Draw and does not automatically return Duorui slots at the next Play.");
        var beforeRestore=s.V(0).HandCount;s.Use("incoming",[1]);
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==DrawHold&&s.Root?.HappyZhangLiao?.Stage==HappyZlStage.Drawing);
        var restoration=s.Root!;var draw=s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f=>f.Context.ParentFrameId==restoration.Id);
        s.Need(restoration.HappyZhangLiao is{Target:1,Restored:2,Cursor:2,Restore.Count:2}&&leases.All(e=>s.Capacity(1,e.Slot)==1)&&
            draw.Context.RequestedCount==2&&draw.Return.Kind==NativeDrawAttemptReturnKind.ProgramOperation&&s.V(0).Hp==3&&
            s.E<ChainedDamagePropagatedEvent>().Any(e=>e.SourceSeat==1&&e.FromSeat==1&&e.TargetSeat==0&&e.Amount==1&&e.Nature==DamageNature.Fire),
            "The real fire-chain propagation retains source one and restores exactly its two issued capacities, opening the native draw before the propagated damage changes HP.");
        s.Cold();s.Skip();s.Play();
        s.Need(s.V(0).Hp==2&&s.V(0).HandCount==beforeRestore+2&&s.E<HappyZlRestoredEvent>().Length==2&&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e=>e.AttemptFrameId==draw.Id&&e.RequestedCount==2&&!e.Replaced)==1&&
            s.G.CardMovements.Count(m=>m.CardId==peach&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.happy-duorui.take")==1&&
            s.E<HappyZlPaidEvent>().Count(e=>e.FrameId==gain.Id)==1,
            "Cold native return never restores twice, redraws twice or obtains the already-paid private card again.");
    }
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-zhang-liao",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy-duorui.take"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true},{"op":"setChainedState","target":"selectedTarget","chained":true},{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1,"nature":"fire"}]}]}
            ]}
            """;
            var labels=new[]{NoDraw,Quiet,GainHold,DrawHold,Driver}.ToDictionary(id=>id,id=>new{name=id,description="欢乐神张辽真实获得、废栏、来源恢复"});
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"保留弃牌阶段的真实角色","",Quiet,"wei",12));
            b.AddDeck(new(Deck,"固定实体牌",0,2,[]){PhysicalCards=Enumerable.Range(0,60).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神张辽小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(v=>G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p=>p is not null);
        public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.HappyZhangLiao is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public int Capacity(int seat,EquipmentSlot slot)=>V(seat).EquipmentSlotCapacities?.GetValueOrDefault(slot,1)??1;
        public CardLocation Zone(int card)=>G.CreateCardZoneDiagnostics().Single(c=>c.CardId==card).Location;
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Menu(HappyZlStage stage)=>Reach(()=>Root?.HappyZhangLiao?.Stage==stage&&P?.Choices.Any(c=>Action(c)=="happy-zhang-liao")==true);
        public void Use(string binding,int[] targets)=>Accept(new UseProgramSkillCommand(0,Driver,binding,[],targets,G.Revision,P!.PromptId));
        public void Card(int id,int target)
        {
            var a=G.GetHumanLegalActions().Single(a=>a.CardId==id&&a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0,id,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
        }
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing exact current Happy Zhang Liao choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
        public void Skip()=>Answer(c=>Action(c)=="skip");
        public void Reach(Func<bool> done)
        {
            for(var n=0;n<200;n++)
            {
                if(done())return;
                if(P is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId is GainHold or DrawHold)Skip();
                else if(P is{PlayerSeat:0}offer&&offer.SkillPrompt?.SkillId==Duorui&&offer.Choices.Any(c=>Action(c)=="skip"))Skip();
                else if(P is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Zhang Liao boundary "+JsonSerializer.Serialize(new{P,Root=Root?.HappyZhangLiao}));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Zhang Liao missed bounded real occurrence "+SnapshotJson.Serialize(G.CreateSnapshot(0)));
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),"Cold preserves the exact original private/payment/native draw frames and frozen viewer state.");
        }
        public void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zhang Liao command.");}
        public void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Root=Root?.HappyZhangLiao,Leases=E<HappyZlLeaseEvent>(),Expired=E<HappyZlLeaseExpiredEvent>()}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(37,1,"happy-zhang-liao",Hero,3,0,0,0,0,[NoDraw,GainHold,DrawHold,Driver],
                [new(CardKind.Slash,Suit.Heart,6),new(CardKind.Slash,Suit.Heart,7)],[],
                [new(Peer,ExtraSkillIds:[NoDraw]){StartingHand=[]},new(Peer,ExtraSkillIds:[NoDraw]){StartingHand=[]}],
                [new(Peer,ExtraSkillIds:[NoDraw]){StartingHand=[new(CardKind.Peach,Suit.Heart,3),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Diamond,4),new(CardKind.Slash,Suit.Club,8),new(CardKind.Peach,Suit.Spade,9),new(CardKind.Dodge,Suit.Heart,6)]}],new string('f',64)){DefaultGodFactionId="wei"};
            var g=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
            var s=new Scene(g,registry);s.Accept(new StartGameCommand());return s;
        }
    }
}
