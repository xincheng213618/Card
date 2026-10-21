using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhangHeMobileChecks
{
    private const string Hero="mou:zhang-he-mobile",Quiet="fixture:mzh-quiet",Ally="fixture:mzh-ally",EnemyA="fixture:mzh-enemy-a",EnemyB="fixture:mzh-enemy-b",Deck="fixture:mzh-deck";
    public static void NativeSkippedPhasesAndLiaoshi()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mou-zhang-he",Hero,8,4,0,0,0,[],
            Enumerable.Range(1,6).Select(i=>new ShanheStartingCard(CardKind.Dodge,Suit.Club,i)).ToArray(),
            [new(CardKind.Crossbow,Suit.Club,1),new(CardKind.SilverLion,Suit.Club,2)],
            [new(EnemyA,Hp:8){StartingHand=[new(CardKind.Peach,Suit.Heart,3)]},new(EnemyB,Hp:8){StartingHand=[new(CardKind.Dodge,Suit.Spade,4)]}],
            [new(Ally,Hp:8){StartingHand=[new(CardKind.Dodge,Suit.Spade,5)]}],new string('a',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        MouZhangHeReceipt? R()=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouZhangHe;
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Phase,Prompt=P(),Players=g.CreateSnapshot(0).Players,
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(28).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand c){var result=g.Submit(c);Need(result.Accepted,result.Error?.Message??"Native command rejected.");}
        void Answer(Func<PromptChoice,bool> pred)
        {
            var p=P();Need(p?.PlayerSeat==0,"Only the actual human chooses.");var c=p!.Choices.FirstOrDefault(pred);Need(c is not null,"Required actual choice absent.");
            Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));
        }
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<240;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Game ended before main flow at "+line);
                if(P()is not{PlayerSeat:0}p){Submit(new AdvanceOneStepCommand(g.Revision));continue;}
                if(p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                {Answer(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("response")!="bagua");continue;}
                throw new InvalidOperationException("Unexpected human input at "+line+Diagnostic());
            }
            throw new InvalidOperationException("Bounded Qiaobian main flow not reached at "+line+Diagnostic());
        }
        void Stage(MouZhangHeStage s)=>Reach(()=>P()is{PlayerSeat:0}&&R()?.Stage==s);
        void Branch(string b)=>Answer(c=>c.Parameters.GetValueOrDefault("branch")==b);
        void OpenPlan()=>Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        void Plan(IReadOnlyList<int> cards,IReadOnlyList<SkillProgramTurnPhase> phases)
        {
            OpenPlan();Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Stage(MouZhangHeStage.CostCards);
            foreach(var card in cards)Answer(c=>c.Parameters.GetValueOrDefault("branch")=="cost"&&c.Cards.SequenceEqual([card]));
            Branch("confirm");Stage(MouZhangHeStage.Phases);foreach(var phase in phases)Branch(phase.ToString());Branch("confirm");
        }
        Submit(new StartGameCommand());OpenPlan();
        var initial=g.CreateSnapshot(0).Players[0];var cost=initial.Hand[0].Id;var crossbow=initial.Equipment.Single(c=>c.Kind==CardKind.Crossbow).Id;
        var lion=initial.Equipment.Single(c=>c.Kind==CardKind.SilverLion).Id;
        Plan([cost,crossbow],[SkillProgramTurnPhase.Draw,SkillProgramTurnPhase.Play]);
        Stage(MouZhangHeStage.ObtainTargets);
        Need(E<MouZhangHeCostPaidEvent>().Length==2&&E<MouZhangHeSkipIssuedEvent>().Length==2&&
            E<MouZhangHeSkipObservedEvent>()is[{Entitlement.Phase:SkillProgramTurnPhase.Draw}],
            "Real distinct HE discards complete before issuing two distinct skips; only the actually skipped Draw has opened its reward.");
        Answer(c=>c.Targets.SequenceEqual([2]));Answer(c=>c.Targets.SequenceEqual([3]));Branch("confirm");Stage(MouZhangHeStage.ObtainCard);
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));
        g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0))&&R()is{Stage:MouZhangHeStage.ObtainCard},"Representative Cold retains the exact actual skipped-phase parent and opaque Hand choice.");
        Branch("obtain");Stage(MouZhangHeStage.ObtainCard);Branch("obtain");Stage(MouZhangHeStage.FieldCard);
        Need(E<MouZhangHeObtainedEvent>().Length==2&&E<MouZhangHeSkipReturnedEvent>().Length==1&&E<MouZhangHeSkipObservedEvent>().Length==2,
            "The Draw skip really obtains one Hand entity from each chosen other, returns, then the actual Play skip opens its separate reward.");
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="field"&&c.Cards.SequenceEqual([lion]));Stage(MouZhangHeStage.FieldTarget);
        Answer(c=>c.Targets.SequenceEqual([1]));Stage(MouZhangHeStage.Beneficiary);Branch("draw");
        OpenPlan();
        var moved=E<MouZhangHeFieldMovedEvent>().Single();var firstTurn=E<MouZhangHeSkipIssuedEvent>().First().Entitlement.ActualTurn;
        Need(moved.CardId==lion&&moved.FromSeat==0&&moved.ToSeat==1&&g.CardMovements.Any(m=>m.Sequence==moved.Sequence&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Equipment(1))&&
            E<MouZhangHeDrawReturnedEvent>()is[{RecipientSeat:1,Requested:2,Actual:2}]&&E<MouZhangHeObtainedEvent>().Count(e=>e.OriginalRecipientSeat==1)==2&&g.State.TurnNumber>firstTurn,
            "The corresponding field movement finishes before live HEJ count comparison and the actual beneficiary Draw2; play/discard are not invented at Preparation.");
        var acquired=E<MouZhangHeObtainedEvent>().Where(e=>e.OriginalRecipientSeat==0).Select(e=>g.CardMovements.Single(m=>m.Sequence==e.MovementSequence).CardId).ToArray();
        var nextCost=g.CreateSnapshot(0).Players[0].Hand.First(c=>!acquired.Contains(c.Id)).Id;
        Plan([nextCost],[SkillProgramTurnPhase.Discard]);Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        var currentTurn=g.State.TurnNumber;
        var action=g.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([3])&&a.ConversionSource?.SkillId=="mou-mobile:liaoshi");
        Need(action is not null&&acquired.Contains(action.CardId!.Value),"Prior-turn actual Qiaobian acquisition stays eligible without a fabricated expiry.");
        Submit(new PlayCardCommand(0,action!.CardId!.Value,action.TargetSeats,g.Revision,P()!.PromptId,action.PlayedCardKind,action.TargetCardId){ConversionSource=action.ConversionSource});
        Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        Need(E<MouZhangHeLiaoshiUsedEvent>()is[{ActualTurn:var t}]&&t==currentTurn&&!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId=="mou-mobile:liaoshi"),
            "One genuine converted Slash consumes the common whole-actual-turn Liaoshi Use/Played budget.");
        Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&E<ProgramPhaseScheduledEvent>().Any(e=>e.SkillId=="mou-mobile:qiaobian"&&e.Started));
        Need(g.State.TurnNumber==currentTurn&&E<MouZhangHeDrawReturnedEvent>().Last()is{RecipientSeat:0,Requested:1,Actual:1}&&
            E<MouZhangHeEndingConsumedEvent>().Length==1&&!g.GetHumanLegalActions().Any(a=>a.ConversionSource?.SkillId=="mou-mobile:liaoshi")&&
            g.CreateSnapshot(0).Players[0].HandCount>=g.CreateSnapshot(0).Players.Count(p=>p.IsAlive),
            "A real Discard skip draws1, actual Ending automatically inserts Play at the live threshold, and entering that Play does not reset Liaoshi.");
        Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));Reach(()=>E<ProgramPhaseScheduledEvent>().Any(e=>e.SkillId=="mou-mobile:qiaobian"&&!e.Started));
        Need(E<ProgramPhaseScheduledEvent>().Count(e=>e.SkillId=="mou-mobile:qiaobian")==2,"The earned extra Play returns exactly once to its original Ending producer.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-zhang-he-mobile",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var kind in new[]{CardKind.Dodge,CardKind.Peach,CardKind.Crossbow,CardKind.SilverLion})
            {var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mzh-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,
                JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="跳过实际出牌阶段"}}}));
            b.AddSkill(new(Quiet,"安静同行","跳过实际出牌阶段"){Program=catalog.Programs[Quiet],ProgramPresentation=catalog.Presentations[Quiet]});
            foreach(var peer in new[]{Ally,EnemyA,EnemyB})b.AddGeneral(new(peer,"真实同行","supporter",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定小牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,36).Select(_=>new ContentDeckPhysicalCard("fixture:mzh-Dodge",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋张郃原生主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
