using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouKongRongOlChecks
{
    private const string Hero="mou:kong-rong-ol",Quiet="fixture:mkr-quiet",AllyA="fixture:mkr-ally-a",AllyB="fixture:mkr-ally-b",Enemy="fixture:mkr-enemy",Deck="fixture:mkr-deck";
    public static void ActualTurnEndZeroUseAndSealedTiedHpPaymentsCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mkr-liwen-zhengyi",Hero,4,0,0,0,0,["ol:weijing"],
            [new(CardKind.DrawTwo,Suit.Heart,7),new(CardKind.IronChain,Suit.Club,7),new(CardKind.Slash,Suit.Spade,7)],[],
            [new(Enemy,Hp:8){StartingHand=[]}],[new(AllyA,Hp:4){StartingHand=[]},new(AllyB,Hp:4){StartingHand=[]}],new string('e',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        PlayerSnapshot V(int seat)=>g.CreateSnapshot(0).Players[seat];
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Phase,Prompt=P(),Players=g.CreateSnapshot(0).Players,
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(25).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message+Diagnostic());}
        void Submit(GameCommand command)
        {
            try{var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(r.Accepted,r.Error?.Message??"Actual command rejected.");}
            catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}
        }
        void Answer(Func<PromptChoice,bool> predicate)
        {var p=P()!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"An actual human published choice is required.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<120;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Actual flow ended before "+line);
                if(P()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected actual human boundary at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Bounded Kong Rong flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Native(CardKind kind,int? target=null)
        {
            var a=g.GetHumanLegalActions().FirstOrDefault(a=>a.CardId is>0&&a.ConversionSource is null&&a.Kind!=LegalActionKind.Recast&&
                V(0).Hand.Single(c=>c.Id==a.CardId).Kind==kind&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null,"The controlled physical card has a real canonical action.");Submit(new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P()!.PromptId,a.PlayedCardKind,a.TargetCardId));
        }
        Submit(new StartGameCommand());Play();
        Need(V(0).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.MouKongRongXian)?.Count==3,"The original GameStarting binding grants exactly three public Xian.");
        Native(CardKind.DrawTwo);Play();Native(CardKind.IronChain,3);Play();Native(CardKind.Slash,3);Play();
        Need(E<MouKrXianEarnedEvent>().Count(e=>e.ActionId>0)==1&&V(0).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.MouKongRongXian)?.Count==4,
            "Only the second adjacent own Trick matches; the first action and the later different suit/type Slash do not add a mark.");
        Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="allocate"));
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="allocate"&&c.Targets.SequenceEqual([1]));
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="allocate"&&c.Targets.SequenceEqual([2]));
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="scheduled");
        Need(E<MouKrLiwenScheduledEvent>().Select(e=>(e.Seat,e.Count)).SequenceEqual(new[]{(0,2),(1,1),(2,1)}),
            "The actual turn-end list freezes current Xian descending and seat order for ties after the two distinct allocations.");
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="action"&&c.Cards.Count==0&&c.Targets.SequenceEqual([1])&&c.Description.Contains("卫境",StringComparison.Ordinal));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="agree"));
        Need(E<MouKrLiwenNativeIssuedEvent>()is[{ActorSeat:0,ProviderSeat:0}]&&E<RoundOnceCardlessBasicQuotaPaidEvent>().Length==1&&
            E<CardUseDeclaredEvent>().Any(e=>e.CardId==0&&e.CardKind==CardKind.Slash)&&g.State.TurnNumber==1&&g.State.CurrentSeat==0,
            "The recipient uses an existing canonical zero-material Slash after its ordinary Play Slash; no hand card or invented use substitutes for it.");
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="agree");
        Need(E<MouKrZhengyiVoteEvent>().Length==0&&E<MouKrZhengyiPayerEvent>().Length==0&&E<ProgramSkillHpLostEvent>().Length==0&&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Last().MouKrZhengyi is{Votes.Count:1,Eligible.Count:2},
            "One committed private vote remains solely on its owning frame; no ballot, payer or HP result is revealed before the other eligible role votes.");
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));
        g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"A representative Cold preserves the sealed ballot and exact native Slash without prepayment or replayed choice.");
        Reach(()=>E<MouKrLiwenCompletedEvent>().Length==1);
        var original=E<MouKrZhengyiStartedEvent>().Single();
        Need(E<MouKrZhengyiCommittedEvent>()is[{Prevent:true,PayerCount:2}]&&E<MouKrZhengyiPayerEvent>().Select(e=>(e.Seat,e.Hp)).SequenceEqual(new[]{(0,4),(2,4)})&&
            E<ProgramDamagePreventedEvent>().Count(e=>e.FrameId==original.WindowId&&e.TargetSeat==1&&e.Amount==1)==1&&
            E<ProgramSkillHpLostEvent>().Where(e=>e.FrameId==original.FrameId).Select(e=>(e.TargetSeat,e.Amount)).SequenceEqual(new[]{(0,1),(2,1)})&&V(0).Hp==3&&V(1).Hp==4&&V(2).Hp==3,
            "At least one consent prevents the original normal damage once; all originally tied maximum-HP consenting roles each pay the full real HP loss, without recalculating the second payer after the first loss.");
        Need(E<MouKrLiwenDeclinedEvent>().Select(e=>(e.Seat,e.Removed)).SequenceEqual(new[]{(1,1),(2,1)})&&E<MouKrLiwenDrawReturnedEvent>().Sum(e=>e.Actual)==2&&
            E<MouKrLiwenNativeReturnedEvent>().Length==1&&g.State.TurnNumber==1,
            "Each later scheduled role actually declines its unusable Hand, removes its current Xian and pays its own native draw to Kong Rong before this original turn end returns.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-kong-rong-ol",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"ol:lu-zhi-wei");
            foreach(var kind in new[]{CardKind.Dodge,CardKind.DrawTwo,CardKind.IronChain,CardKind.Slash})
            {var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mkr-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="正常摸牌后跳过出牌阶段"}}}));
            b.AddSkill(new(Quiet,"安静同行","正常摸牌后跳过出牌阶段"){Program=catalog.Programs[Quiet],ProgramPresentation=catalog.Presentations[Quiet]});
            b.AddGeneral(new(AllyA,"实际同伴甲","",Quiet,"qun",4));b.AddGeneral(new(AllyB,"实际同伴乙","",Quiet,"qun",4));b.AddGeneral(new(Enemy,"实际敌方","",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,0,[]){PhysicalCards=Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("fixture:mkr-"+CardKind.Dodge,Suit.Diamond,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋孔融实际主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,AllyA,AllyB,Enemy],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=3,[ShanheBattleSetup.EnemyTeamId]=1}));
        }
    }
}
