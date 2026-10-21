using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouXiaoQiaoOlChecks
{
    private const string Hero="mou:xiao-qiao-ol",Driver="fixture:mouxq-driver",Hold="fixture:mouxq-reveal-draw",Quiet="fixture:mouxq-quiet";
    public static void RealMiluoRetainedUsesAndExternalRevealReturnCold()
    {
        var s=Scene.Start();s.Play();var dodge=s.V().Hand.First(c=>c.Kind==CardKind.Dodge&&c.Suit==Suit.Heart).Id;
        var giftSlash=s.V().Hand.First(c=>c.Kind==CardKind.Slash&&c.Suit==Suit.Club).Id;
        var retained=s.V().Hand.First(c=>c.Kind==CardKind.DrawTwo).Id;var red=s.V().Hand.First(c=>c.Kind==CardKind.Slash&&c.Suit==Suit.Diamond).Id;
        s.Skill("mou-ol:miluo","reveal-and-give",[]);s.Answer(c=>c.Cards.SequenceEqual([dodge]));s.Branch("recipient",1);
        s.Answer(c=>c.Cards.SequenceEqual([giftSlash]));s.Branch("recipient",2);s.Branch("confirm");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Hold);s.Need(s.E<MouXiaoQiaoMiluoGiftEvent>().Length==0&&s.G.ResolutionStack.OfType<PublicHandRevealWindowFrame>().Any(),"The first actual public Hand-suit Draw child resolves before either original gift moves.");
        s.Cold();s.Answer(_=>true);s.Play();
        s.Need(s.E<MouXiaoQiaoMiluoGiftEvent>()is[{CardId:var giftA},{CardId:var giftB}]&&giftA==dodge&&giftB==giftSlash&&s.E<MouXiaoQiaoJueyanSuitClaimedEvent>().Count(e=>e.Source.OwnerSeat==0)==2&&
            s.E<MouXiaoQiaoOlDrawReturnedEvent>().Count(e=>e.Requested==1&&e.Actual==1)==2,"Miluo's two genuine Hand gifts follow two independent first-suit Draw1 returns exactly once after Cold.");
        s.Skill("mou-ol:jueyan","retained-hand-use",[retained]);s.Reach(()=>s.HasBranch("use"));s.Branch("use");s.Play();
        var drawnUse=s.E<CardUseDeclaredEvent>().Last(e=>e.CardKind==CardKind.DrawTwo);
        s.Need(drawnUse.CardId==0&&s.V().Hand.Any(c=>c.Id==retained)&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==drawnUse.ResolutionId)==1&&
            s.E<MouXiaoQiaoJueyanUsedEvent>().Count(e=>e.Source.OwnerSeat==0&&!e.RedBasic)==1,"The ordinary-trick branch retains its shown entity and completes one genuine zero-material native DrawTwo.");
        s.Skill("mou-ol:jueyan","retained-hand-use",[red]);s.Reach(()=>s.HasBranch("use"));s.Answer(c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([3]));s.Play();
        s.Need(s.V().Hand.Any(c=>c.Id==red)&&s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==1)&&s.E<MouXiaoQiaoJueyanUsedEvent>().Count(e=>e.Source.OwnerSeat==0&&e.RedBasic)==1&&
            !s.G.GetHumanLegalActions().Any(a=>a.SkillId=="mou-ol:jueyan"),"The red-basic allowance is separate, uses a neutral native Slash, retains its original entity, and both Round branches are now spent.");
        var fire=s.V().Hand.First(c=>c.Kind==CardKind.FireAttack).Id;s.Native(fire,1);s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.FireAttackDiscard});
        var reveals=s.G.Events.Select(e=>e.Payload).ToList();var shown=reveals.FindLastIndex(e=>e is FireAttackCardRevealedEvent);
        var reward=reveals.FindLastIndex(e=>e is MouXiaoQiaoOlDrawReturnedEvent d&&d.Requested==1);
        s.Need(shown>=0&&reward>shown&&s.E<MouXiaoQiaoJueyanSuitClaimedEvent>().Any(e=>e.Source.OwnerSeat==1)&&!s.E<FireAttackResolvedEvent>().Any(),"An external real FireAttack Hand reveal pays its owner's first-suit Draw1 before returning to the original source's discard prompt.");
        s.Answer(c=>c.Cards.Count==0);s.Play();s.Skill(Driver,"strip",[],[2]);
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Driver);s.Answer(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("source-zone")=="Hand"&&c.Parameters.GetValueOrDefault("slot-index")=="0");
        s.Play();s.End();s.Reach(()=>s.HasBranch("lose"));s.Branch("lose",2);
        s.Reach(()=>s.E<RoundEndedEvent>().Any(e=>e.RoundNumber==1));s.Need(s.E<MouXiaoQiaoMiluoConsequenceEvent>().Any(e=>e.TargetSeat==2&&!e.Recover)&&s.E<ProgramSkillHpLostEvent>().Any(e=>e.SkillId=="mou-ol:miluo"&&e.TargetSeat==2&&e.Amount==1),"At the actual Round end, the original recipient's current Hand determines the one real HP-loss consequence.");
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G=game;public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V()=>G.CreateSnapshot(0).Players[0];public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public bool HasBranch(string branch)=>P is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="mou-xiao-qiao-ol"&&c.Parameters.GetValueOrDefault("branch")==branch);
        public void Branch(string branch,int? target=null)=>Answer(c=>c.Parameters.GetValueOrDefault("branch")==branch&&(target is null||c.Targets.SequenceEqual([target.Value])));
        public void Skill(string skill,string id,IReadOnlyList<int> cards,IReadOnlyList<int>? targets=null)=>Accept(new UseProgramSkillCommand(0,skill,id,cards,targets??[],G.Revision,P!.PromptId));
        public void Native(int id,int target){var a=G.GetHumanLegalActions().First(a=>a.CardId==id&&a.TargetSeats.SequenceEqual([target])&&a.ConversionSource is null);Accept(new PlayCardCommand(0,id,[target],G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId));}
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> match){var p=P!;var c=p.Choices.FirstOrDefault(match);Need(p.PlayerSeat==0&&c is not null,"The real human can answer only an actually published choice.");Accept(new AnswerPromptCommand(0,p.PromptId,c!.Id,G.Revision));}
        public void Accept(GameCommand c){try{var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Native command rejected.");}catch(Exception e){throw new InvalidOperationException(e.Message+Diag(),e);}}
        public void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<180;i++)
            {
                if(done())return;Need(G.State.Winner==Winner.None,"Unexpected native terminal cutoff at "+line);
                if(P is not{PlayerSeat:0}p)Accept(new AdvanceOneStepCommand(G.Revision));
                else if(p.Kind==DecisionKind.PlayCard)End();
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException("Unexpected actual human boundary at "+line+Diag());
            }
            throw new InvalidOperationException("Bounded OL Xiao Qiao native flow not reached at "+line+Diag());
        }
        public void Cold(){var v=SnapshotJson.Serialize(G.CreateSnapshot(0));G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(v==SnapshotJson.Serialize(G.CreateSnapshot(0)),"The real public-reveal child and its original unspent physical gifts survive Cold unchanged.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diag());}
        private string Diag()=>JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.Phase,Prompt=P,Owner=V(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=G.Events.Where(e=>e.Payload.GetType().Name.StartsWith("MouXiaoQiao",StringComparison.Ordinal)||e.Payload.GetType().Name.StartsWith("PublicHandReveal",StringComparison.Ordinal)||e.Payload is CardUseDeclaredEvent or FireAttackCardRevealedEvent or FireAttackResolvedEvent).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        public static Scene Start()
        {
            var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(7,1,"mouxq-real",Hero,3,0,0,0,0,[Driver,Hold],[new(CardKind.DrawTwo,Suit.Heart,4),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.FireAttack,Suit.Spade,5),new(CardKind.Slash,Suit.Diamond,6),new(CardKind.Slash,Suit.Spade,8)],[],
                [new("fixture:mouxq-enemy-a",Hp:12){StartingHand=[]},new("fixture:mouxq-enemy-b",Hp:12){StartingHand=[]}],[new("fixture:mouxq-ally",Hp:12){StartingHand=[new(CardKind.Dodge,Suit.Spade,6)]}],new string('f',64));
            var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);var s=new Scene(g,r);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-xiao-qiao-ol",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var k in new[]{CardKind.Slash,CardKind.Dodge,CardKind.FireAttack,CardKind.DrawTwo}){var c=CardCatalog.Get(k);b.AddCard(new("fixture:mouxq-"+k,c.DisplayName,c.CategoryName,c.Description,k));}
            var defs=new object[]{
                new{id=Quiet,revision=1,triggers=new[]{new{id="quiet",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}}}},
                new{id=Hold,revision=1,triggers=new[]{new{id="hold",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementOccurrence="perBatch",movementReasons=new[]{"skill-program.mou-ol:jueyan.draw"},optional=false,usageScope="game",usageLimit=1,effects=new[]{new{op="chooseOption",target="owner",resultBind="held",options=new[]{new{id="continue"}}}}}}},
                new{id=Driver,revision=1,activations=new[]{new{id="strip",minCards=0,maxCards=0,minTargets=1,maxTargets=1,targetKind="otherLiving",usesPerTurn=(int?)null,effects=new[]{new{op="selectAndMoveOwnedCard",target="owner",chooserRef=new{kind="owner"},cardOwnerRef=new{kind="selectedTarget"},zones=new[]{"hand"},count=1,destination="discardPile",awaitMovementTriggers=true}}}}}
            };
            var labels=new Dictionary<string,object>{[Quiet]=new{name="原生安静同行",description="正常摸牌后跳过出牌阶段。"},[Driver]=new{name="实际弃牌夹具",description="执行原生指定角色手牌弃置。"},[Hold]=new{name="实际展示奖励停点",description="只在第一次真实展示奖励摸牌后暂停。",optionLabels=new Dictionary<string,string>{{"continue","继续真实展示续接"}}}};
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new("fixture:mouxq-ally","实际展示受益者","","standard:none","wu",12,[Quiet,"mou-ol:jueyan"]));
            foreach(var id in new[]{"fixture:mouxq-enemy-a","fixture:mouxq-enemy-b"})b.AddGeneral(new(id,"实际同行","","standard:none","qun",12,[Quiet]));
            b.AddDeck(new("fixture:mouxq-deck","固定真实手牌库存",0,2,[]){PhysicalCards=Enumerable.Range(0,70).Select(i=>new ContentDeckPhysicalCard("fixture:mouxq-"+CardKind.Slash,Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"OL谋小乔真实主流程",4,4,new Dictionary<string,int>(),"fixture:mouxq-deck",GeneralPoolIds:[Hero,"fixture:mouxq-ally","fixture:mouxq-enemy-a","fixture:mouxq-enemy-b"],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
