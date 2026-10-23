using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class HappyShenZhenJiChecks
{
    private const string Hero="happy:shen-zhen-ji",Shenfu="happy:shenfu",Qixian="happy:qixian",Feifu="happy:feifu",Peer="fixture:happy-zj-peer",Attacker="fixture:happy-zj-attacker",
        NoDraw="fixture:happy-zj-no-draw",Quiet="fixture:happy-zj-quiet",DrawHold="fixture:happy-zj-draw",MoveHold="fixture:happy-zj-store",GainHold="fixture:happy-zj-gain",UseHold="fixture:happy-zj-completed-use",Deck="fixture:happy-zj-deck";
    public static void ThreeBranchesActualTurnTargetsAndRefundedRewardCold()
    {
        var s=Scene.Start(false);s.Play();s.Use(1);s.Branch("draw");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==DrawHold&&s.Root?.HappyZhenJi?.Stage==HappyZjStage.RewardDraw);
        var first=s.Root!;var draw=s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f=>f.Context.ParentFrameId==first.Id);
        s.Need(s.V(1).HandCount==3&&s.V(1).Hp==3&&first.HappyZhenJi is{Ordinal:1,Reward:2,Refunded:true}&&draw.Context.RequestedCount==2&&s.V(0).HandCount==1,
            "The real one-card target draw resolves before equality refunds the accepted phase debit and opens the literal first reward of two cards.");
        s.Cold();s.Skip();s.Play();
        s.Need(s.V(0).HandCount==3&&!s.Actions().Any(a=>a.SelectableTargetSeats.Contains(1))&&s.Actions().Any(a=>a.SelectableTargetSeats.Contains(2)),
            "Cold draws exactly two and refunds the native phase quota while retaining the first actual-turn target ban.");
        s.Use(2);s.Branch("discard");s.Menu(HappyZjStage.Discard);
        s.Need(s.P!.Choices.Any(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)),"Shenfu's other Hand remains an opaque dynamic payment choice.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand)&&c.Parameters.GetValueOrDefault("slot-index")=="0");s.Play();
        s.Need(s.V(2).HandCount==2&&s.V(0).HandCount==6&&s.E<HappyZjRefundedEvent>().Select(e=>e.Reward).SequenceEqual([2,3]),
            "The actual logical discard produces equality and a separate three-card reward without erasing the first activation from the round's real count.");
        s.Use(3);s.Branch("thunder");s.Play();
        s.Need(s.V(3).Hp==3&&s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==1)&&s.E<HappyZjRefundedEvent>().Length==2&&s.Actions().Count==0&&
            s.E<HappyZjStartedEvent>().Where(e=>e.Op==SkillProgramEffectOp.ResolveHappyZhenJiShenfu).Select(e=>e.Ordinal).SequenceEqual([1,2,3]),
            "The real native thunder branch without equality does not refund: an unused self target exists but the actual Play quota is exhausted.");
        s.Accept(new EndPlayPhaseCommand(0,s.G.Revision,s.P!.PromptId));s.Play();
        s.Need(s.Actions().Any(a=>a.SelectableTargetSeats.Contains(1)),"A new actual turn resets target qualification, unlike a successful same-turn quota refund.");
        s.Use(1);s.Branch("draw");s.Play();
        var latest=s.E<HappyZjStartedEvent>().Last(e=>e.Op==SkillProgramEffectOp.ResolveHappyZhenJiShenfu);
        s.Need(latest.Ordinal==1&&s.E<NativeDrawAttemptDecidedEvent>().Count(e=>e.AttemptFrameId==draw.Id&&e.RequestedCount==2&&!e.Replaced)==1,
            "The next actual turn restarts the separate activation counter while the original paid reward remains executed once.");
    }
    public static void BlackEquipmentResponseStoresSeventhAndTurnEndReturnsAllCold()
    {
        var s=Scene.Start(true);s.Play();var hand=s.V(0).Hand.Select(c=>c.Id).ToArray();var lion=s.V(0).Equipment!.Single(c=>c.Kind==CardKind.SilverLion).Id;
        s.Need(s.V(0).HandLimit==7&&s.V(0).Hp==6&&s.V(0).MaxHp==7,"Qixian's fixed seven limit is independent of the fixture's real HP and printed maximum bonus.");
        s.Accept(new EndPlayPhaseCommand(0,s.G.Revision,s.P!.PromptId));s.Reach(()=>s.P?.SkillPrompt?.SkillId==Qixian&&s.P.Choices.Any(c=>Action(c)=="activate"));s.Answer(c=>Action(c)=="activate");s.Menu(HappyZjStage.SelectStore);
        foreach(var id in hand)s.Answer(c=>c.Cards.SequenceEqual([id]));s.Answer(c=>Option(c)=="confirm");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==MoveHold&&s.Root?.HappyZhenJi?.Stage==HappyZjStage.StoreChildren);
        var store=s.Root!;s.Need(s.Pile().Select(c=>c.Id).Order().SequenceEqual(hand.Order())&&s.V(0).HandCount==0&&store.HappyZhenJi!.Payments is[{Moves.Count:6}],
            "Qixian moves the six selected real hand entities into its public source as one paid batch before its movement child.");
        s.Cold();s.Skip();s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
        s.Need(s.P!.Choices.Any(c=>c.Cards.SequenceEqual([lion])&&c.Parameters.GetValueOrDefault("conversion-skill-id")==Feifu),"Feifu offers the actual black equipped Silver Lion as the one-material native Dodge conversion.");
        s.Answer(c=>c.Cards.SequenceEqual([lion])&&c.Parameters.GetValueOrDefault("conversion-skill-id")==Feifu);
        s.Reach(()=>s.E<HappyZjFeifuClaimedEvent>().Length==1);
        var claimed=s.E<HappyZjFeifuClaimedEvent>().Single();
        s.Need(s.Pile().Count==7&&s.Zone(lion)==claimed.To&&claimed.From==CardLocation.DiscardPile&&s.V(0).Hp==7&&
            s.E<CardRespondedEvent>().Any(e=>e.ResponderSeat==0&&e.CardId==lion&&e.EffectiveCardKind==CardKind.Dodge)&&!s.E<DamageAppliedEvent>().Any(e=>e.TargetSeat==0),
            "The real equipment conversion first finishes its native response and Silver Lion recovery, then stores the same entity as the seventh public card; it is not yet the turn-end return.");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==UseHold);
        var completed=s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        s.Need(completed.CompletedResponseReturn is{Kind:ProgramCompletedResponseKind.Dodge}&&completed.Action.ActionId==claimed.ActionId&&
            completed.Action.Type==CardActionType.Response&&completed.Action.ConversionChain.Any(c=>c.SkillId==Feifu&&c.BindingId=="black-as-dodge")&&
            completed.Action.PhysicalCards is[{CardId:var material}]&&material==lion&&s.Zone(lion)==claimed.To,
            "The same accepted response opens an actual opt-in CardUseCompleted observer after Feifu claims its original entity, while the typed Slash return still owns its cursor.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("result-bind")=="completed-use"&&c.Parameters.GetValueOrDefault("option-id")=="continue");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==GainHold&&s.Root?.HappyZhenJi?.Stage==HappyZjStage.ReturnChildren);
        var returned=s.Root!;s.Need(returned.HappyZhenJi!.Entities.Count==7&&s.Pile().Count==0&&s.V(0).HandCount==7&&s.E<TurnEndedEvent>().Any(e=>e.ActorSeat==3),
            "At the attacker's actual turn end, Qixian collects all seven cards across the two original public sources before the gained-card child.");
        s.Cold();s.Skip();s.Play();
        s.Need(s.V(0).HandCount==7&&s.Pile().Count==0&&s.E<HappyZjFeifuClaimedEvent>().Length==1&&
            s.E<ProgramCardTriggerResolvedEvent>().Count(e=>e.SkillId==UseHold&&e.TriggerId=="completed-use"&&e.OwnerSeat==0&&e.Activated)==1&&
            s.E<HappyZjPaidEvent>().Count(e=>e.FrameId==store.Id)==1&&s.E<HappyZjPaidEvent>().Count(e=>e.FrameId==returned.Id)==1&&
            s.G.CardMovements.Count(m=>m.Reason.Value=="skill.happy-qixian.return"&&m.To==CardLocation.Hand(0))==7,
            "Cold returns the original paid seven-card batch once and never repeats the six-card deposit or the converted response entity.");
    }
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c)=>c.Parameters.GetValueOrDefault("option");
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-zhen-ji",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var lion=CardCatalog.Get(CardKind.SilverLion);
            b.AddCard(new("classic:silver-lion",lion.DisplayName,lion.CategoryName,lion.Description,CardKind.SilverLion));
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{MoveHold}}","revision":1,"triggers":[{"id":"store","isSkillInvocation":false,"window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.happy-qixian.store"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"return","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.happy-qixian.return"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{UseHold}}","revision":1,"triggers":[{"id":"completed-use","isSkillInvocation":false,"window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"actor","cardKinds":["dodge"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-use","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels=new[]{NoDraw,Quiet,DrawHold,MoveHold,GainHold,UseHold}.ToDictionary(id=>id,id=>new Dictionary<string,object>{{"name",id},{"description","欢乐神甄姬真实返次、响应与武将牌"}});
            labels[UseHold]["optionLabels"]=new Dictionary<string,string>{{"continue","继续"}};
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"保留真实回合的角色","",Quiet,"wei",12));b.AddGeneral(new(Attacker,"单张真实杀的角色","",NoDraw,"wei",12));
            b.AddDeck(new(Deck,"固定实体牌",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神甄姬小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer,Attacker],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.HappyZhenJi is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int card)=>G.CreateCardZoneDiagnostics().Single(c=>c.CardId==card).Location;
        public IReadOnlyList<CardSnapshot> Pile()=>V(0).PublicPersistentPiles?.Where(p=>p.SourceSkillId is Qixian or Feifu).SelectMany(p=>p.Cards).ToArray()??V(0).PublicPersistentPileCards??[];
        public IReadOnlyList<LegalAction> Actions()=>G.GetHumanLegalActions().Where(a=>a.ProgramSkillId==Shenfu).ToArray();
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Use(int target)=>Accept(new UseProgramSkillCommand(0,Shenfu,"three-options",[],[target],G.Revision,P!.PromptId));
        public void Menu(HappyZjStage stage)=>Reach(()=>Root?.HappyZhenJi?.Stage==stage&&P?.Choices.Any(c=>Action(c)=="happy-zhen-ji")==true);
        public void Branch(string mode){Menu(HappyZjStage.Branch);Answer(c=>Option(c)==mode);}
        public void Skip()=>Answer(c=>Action(c)=="skip");
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing exact current Happy Zhen Ji choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
        public void Reach(Func<bool> done)
        {
            for(var n=0;n<220;n++)
            {
                if(done())return;
                if(P is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId is DrawHold or MoveHold or GainHold)Skip();
                else if(P is{PlayerSeat:0}offer&&offer.SkillPrompt?.SkillId==Qixian&&offer.Choices.Any(c=>Action(c)=="skip"))Skip();
                else if(P is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Zhen Ji boundary "+JsonSerializer.Serialize(new{P,Root=Root?.HappyZhenJi}));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Zhen Ji missed the bounded actual flow "+SnapshotJson.Serialize(G.CreateSnapshot(0)));
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),"Cold preserves the exact original payment, producer and public-pile frames.");
        }
        public void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zhen Ji command.");}
        public void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Root=Root?.HappyZhenJi,Uses=E<HappyZjStartedEvent>(),Refunds=E<HappyZjRefundedEvent>()}));}
        public static Scene Start(bool storage)
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var cards=storage?Enumerable.Range(0,6).Select(i=>new ShanheStartingCard(CardKind.Slash,i%2==0?Suit.Heart:Suit.Club,i+1)).ToArray():[new ShanheStartingCard(CardKind.Slash,Suit.Spade,9)];
            var ally=new ShanheCombatantDefinition(Peer,Hp:storage?12:3,ExtraSkillIds:[NoDraw]){StartingHand=storage?[]:[new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Slash,Suit.Club,7)]};
            var enemy2=new ShanheCombatantDefinition(Peer,Hp:storage?12:2,ExtraSkillIds:[NoDraw]){StartingHand=storage?[]:[new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Slash,Suit.Club,7),new(CardKind.Slash,Suit.Spade,8)]};
            var enemy3=new ShanheCombatantDefinition(storage?Attacker:Peer,Hp:storage?12:4,ExtraSkillIds:[NoDraw]){StartingHand=storage?[new(CardKind.Slash,Suit.Club,7)]:[]};
            var setup=new ShanheBattleSetup(37,1,"happy-zhen-ji",Hero,storage?6:3,storage?4:0,0,0,0,[NoDraw,DrawHold,MoveHold,GainHold,UseHold],cards,storage?[new(CardKind.SilverLion,Suit.Club,4)]:[],[enemy2,enemy3],[ally],new string('f',64)){DefaultGodFactionId="wei"};
            var g=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
            var s=new Scene(g,registry);s.Accept(new StartGameCommand());return s;
        }
    }
}
