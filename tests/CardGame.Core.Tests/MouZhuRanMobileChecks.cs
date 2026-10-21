using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhuRanMobileChecks
{
    private const string Hero="mou:zhu-ran-mobile",Zhenwei="mou-mobile:zhenwei",Heyuan="mou-mobile:heyuan",
        Quiet="fixture:mzr-quiet",Ally="fixture:mzr-ally",EnemyA="fixture:mzr-enemy-a",EnemyB="fixture:mzr-enemy-b",Deck="fixture:mzr-deck";
    public static void NativeSealedPaymentsAndHeyuan()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mou-zhu-ran",Hero,8,8,0,0,0,[],
            Enumerable.Range(1,6).Select(i=>new ShanheStartingCard(CardKind.Dodge,Suit.Club,i)).ToArray(),[],
            [new(EnemyA,Hp:8),new(EnemyB,Hp:8)],
            [new(Ally,Hp:7){StartingHand=[new(CardKind.Dodge,Suit.Spade,5)]}],new string('a',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        MouZhuRanReceipt? R()=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouZhuRan;
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Phase,Prompt=P(),Receipt=R(),
            Frames=g.ResolutionStack.Select(f=>f.GetType().Name+"/"+f.Id),Facts=g.Events.TakeLast(20).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason+Diagnostic());}
        void Submit(GameCommand command){var r=g.Submit(command);Need(r.Accepted,r.Error?.Message??"Native command rejected.");}
        void Answer(Func<PromptChoice,bool> predicate)
        {
            var p=P();Need(p?.PlayerSeat==0,"Only actual Human choices are submitted.");var c=p!.Choices.FirstOrDefault(predicate);
            Need(c is not null,"Required native choice missing.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));
        }
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<180;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Native game ended before main flow at "+line);
                if(P()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Human input at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Bounded Zhenwei/Heyuan main flow not reached at "+line+Diagnostic());
        }
        void Stage(MouZhuRanStage stage)=>Reach(()=>P()is{PlayerSeat:0}&&R()?.Stage==stage);
        void Branch(string branch)=>Answer(c=>c.Parameters.GetValueOrDefault("branch")==branch);
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Zhen(int target)
        {
            Submit(new UseProgramSkillCommand(0,Zhenwei,"sealed-discard",[],[target],g.Revision,P()!.PromptId));Stage(MouZhuRanStage.Commit);
        }
        void Ending()
        {
            Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));
            Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
            Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Stage(MouZhuRanStage.HeyuanTarget);
        }
        Submit(new StartGameCommand());Play();Zhen(2);
        Branch("card");var selected=R()!.Selected.Single();var before=SnapshotJson.Serialize(g.CreateSnapshot(0));
        Need(E<MouZhuRanCostPaidEvent>().Length==0&&g.CardMovements.All(m=>m.CardId!=selected.CardId||m.To!=CardLocation.DiscardPile),
            "A genuine private commitment does not prepay or reveal a selected discard.");
        g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0))&&R()is{Stage:MouZhuRanStage.Commit,Selected.Count:1},
            "One representative Cold preserves the owning sealed choice without a second payment.");
        Branch("confirm");Stage(MouZhuRanStage.Effects);
        var first=E<MouZhuRanStartedEvent>().Single();var comparison=E<MouZhuRanComparisonEvent>().Single();
        Need(comparison is{OwnerCount:1,OwnerSuits:1,OpponentCount:0,OpponentSuits:0,X:2}&&
            E<MouZhuRanCommittedEvent>().Single().Participants==2&&E<MouZhuRanCostPaidEvent>()is[{CardId:var cost,Payer:0}]&&cost==selected.CardId&&
            E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e=>e.ParentFrameId==first.FrameId&&e.ActualCount==1&&e.ExecutorSeat==0),
            "Both genuine private choices close before one native HE logical discard and the actual count/suit comparison.");
        Branch("damage");Stage(MouZhuRanStage.Effects);Branch("draw");Stage(MouZhuRanStage.Effects);Branch("confirm");Play();
        Need(E<MouZhuRanResolvedEvent>()is[{DiscardCount:1,LastEffect:MouZhuRanEffect.Draw}]&&
            E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1)&&
            E<MouZhuRanDrawReturnedEvent>()is[{RecipientSeat:0,Requested:3,Actual:3}],
            "The accepted distinct items execute in selected order through real Damage1 then Draw3; the actual last item is recorded.");
        Ending();Answer(c=>c.Targets.SequenceEqual([1]));Stage(MouZhuRanStage.HeyuanCost);Branch("card");Branch("confirm");Play();
        Need(E<MouZhuRanHeyuanAcceptedEvent>()is[{TargetSeat:1,RequiredCards:1,Effect:MouZhuRanEffect.Draw}]&&
            E<MouZhuRanDrawReturnedEvent>().Last()is{RecipientSeat:1,Requested:3,Actual:3},
            "Actual Ending pays precisely the previous one-card count, repeats Draw3 for the wounded beneficiary and creates its permanent support link.");
        Zhen(3);Branch("confirm");Stage(MouZhuRanStage.Effects);
        var second=E<MouZhuRanStartedEvent>().Last(e=>e.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei);
        Need(second.ActualTurn>first.ActualTurn&&E<MouZhuRanComparisonEvent>().Last()is{OwnerCount:1,OwnerSuits:1,OpponentCount:0,X:2}&&
            E<MouZhuRanCostPaidEvent>().Where(e=>e.FrameId==second.FrameId).ToArray()is[var support]&&support.Payer==1&&
            g.CardMovements.Any(m=>m.Sequence==support.Sequence&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile),
            "On a later actual Play the previously helped NPC really commits and pays its own Hand card, counted on Zhu Ran's side even with zero own cards.");
        Branch("damage");Stage(MouZhuRanStage.Effects);Branch("confirm");Play();Ending();
        Need(P()!.Choices.All(c=>!c.Targets.Contains(1)),"The earlier beneficiary's lifetime per-named-skill debit is retained.");
        Answer(c=>c.Targets.SequenceEqual([2]));Stage(MouZhuRanStage.HeyuanCost);Branch("card");Branch("confirm");
        Reach(()=>E<MouZhuRanCompletedEvent>().Length==4);
        var repeated=E<MouZhuRanEffectIssuedEvent>().Last();
        Need(repeated is{Effect:MouZhuRanEffect.Damage,SourceSeat:0,RecipientSeat:2}&&
            E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1)==2&&
            E<MouZhuRanHeyuanAcceptedEvent>().Last()is{TargetSeat:2,RequiredCards:1,Effect:MouZhuRanEffect.Damage},
            "Heyuan repeats Damage1 from the original Zhu Ran to the current wounded beneficiary, rather than attacking the old Zhenwei target.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-zhu-ran-mobile",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var c=CardCatalog.Get(CardKind.Dodge);b.AddCard(new("fixture:mzr-dodge",c.DisplayName,c.CategoryName,c.Description,CardKind.Dodge));
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var programs=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,
                skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="跳过实际出牌阶段"}}}));
            b.AddSkill(new(Quiet,"安静同行","跳过实际出牌阶段"){Program=programs.Programs[Quiet],ProgramPresentation=programs.Presentations[Quiet]});
            foreach(var peer in new[]{Ally,EnemyA,EnemyB})b.AddGeneral(new(peer,"真实同行","supporter",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定真实小牌堆",0,0,[]){PhysicalCards=Enumerable.Range(0,36).Select(_=>new ContentDeckPhysicalCard("fixture:mzr-dodge",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋朱然真实主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],
                ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
