using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class HappyShenLuSuChecks
{
    private const string Hero="happy:shen-lu-su",Driver="fixture:happy-ls-driver",Quiet="fixture:happy-ls-quiet",Watch="fixture:happy-ls-watch",Mode="team:happy-ls-fixture";
    public static void BothDingzhouBranchesAndUnrestrictedTeamZhimengCold()
    {
        var(game,registry)=Start();Play(game);
        Skill(game,Driver,"draw",[]);Play(game);Skill(game,Driver,"equip",[1]);Play(game);Use(game,CardKind.Indulgence,[1]);Play(game);
        var field=game.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.Equipment(1)||c.Location==CardLocation.Judgment(1)).Select(c=>c.CardId).ToArray();
        Need(field.Length==2,"The real first recipient has both an equipment and a delayed judgment entity.");
        var peerHand=View(game,1).HandCount;
        Skill(game,"happy:dingzhou","exchange",[1]);Reach(game,p=>HappyChoice(p,"field"));Answer(game,c=>c.Parameters.GetValueOrDefault("option")=="field");
        foreach(var _ in field){Reach(game,p=>HappyChoice(p,"give:"));Answer(game,c=>c.Cards.Count==1);}
        Play(game);
        var first=Events<HappyDingzhouStartedEvent>(game).Single();
        Need(View(game,1).HandCount==peerHand+2&&field.All(id=>Location(game,id)==CardLocation.Hand(0))&&
            Events<HappyDingzhouMovementPaidEvent>(game).Where(e=>e.FrameId==first.FrameId).Select(e=>e.Stage).SequenceEqual([HappyDingzhouStage.FieldGiftChildren,HappyDingzhouStage.FieldGainChildren])&&
            !game.GetHumanLegalActions().Any(a=>a.SkillId=="happy:dingzhou"),"The first branch pays the frozen field count, then obtains every real field entity; both branches share the spent Play budget.");

        End(game);Play(game);Skill(game,Driver,"draw",[]);Play(game);Use(game,CardKind.Crossbow,[]);Play(game);
        var originalHand=View(game,2).Hand.Select(c=>c.Id).ToArray();
        var ownBefore=View(game).HandCount;
        var oldEquipment=game.CreateCardZoneDiagnostics().Single(c=>c.Location==CardLocation.Equipment(0)).CardId;
        Need(originalHand.Length>0,"The second branch begins with a real nonempty other hand.");
        Skill(game,"happy:dingzhou","exchange",[2]);Reach(game,p=>HappyChoice(p,"hand"));Answer(game,c=>c.Parameters.GetValueOrDefault("option")=="hand");
        Reach(game,p=>p.SkillPrompt?.SkillId==Watch);
        var frame=game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="happy:dingzhou");var receipt=frame.HappyDingzhou!;
        Need(receipt is{Stage:HappyDingzhouStage.HandGainChildren,Paid:true,TargetSeat:2}&&receipt.Amount==originalHand.Length&&receipt.Obtained.Order().SequenceEqual(originalHand.Order())&&
            View(game,2).HandCount==0&&View(game).HandCount==ownBefore+originalHand.Length&&originalHand.All(id=>Location(game,id)==CardLocation.Hand(0)),
            "The second branch really takes the entire old hand before the native gain observer and before any return choice.");
        game=Cold(game,registry);Answer(game,_=>true);Reach(game,p=>HappyChoice(p,"give:"));
        Answer(game,c=>c.Cards.SequenceEqual([oldEquipment]));
        for(var n=1;n<originalHand.Length;n++){Reach(game,p=>HappyChoice(p,"give:"));Answer(game,c=>c.Cards.Count==1);}
        Play(game);
        var paid=Events<HappyDingzhouMovementPaidEvent>(game).Where(e=>e.FrameId==frame.Id).ToArray();
        Need(View(game,2).HandCount==originalHand.Length&&Location(game,oldEquipment)==CardLocation.Hand(2)&&
            paid.Select(e=>e.Stage).SequenceEqual([HappyDingzhouStage.HandGainChildren,HappyDingzhouStage.HandReturnChildren])&&paid.All(e=>e.Count==originalHand.Length)&&
            Events<HappyDingzhouReturnRequiredEvent>(game).Single(e=>e.FrameId==frame.Id).PayableCount==originalHand.Length&&
            Events<HappyDingzhouStartedEvent>(game).Count(e=>e.FrameId==frame.Id)==1&&!game.GetHumanLegalActions().Any(a=>a.SkillId=="happy:dingzhou"),
            "Cold return keeps the one actual acquisition and then gives the exact count, including a previously equipped own entity, in one native batch.");

        Skill(game,Driver,"draw-peer",[3]);Play(game);End(game);
        Reach(game,p=>p.SkillPrompt?.SkillId=="happy:zhimeng"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var ownerHand=View(game).HandCount;var targetHand=View(game,3).HandCount;
        Need(targetHand>ownerHand+1,"The actual Team target exceeds the mobile-only hand-count limit.");
        Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(game,p=>ShenLsChoice(p,"peer:3"));
        Answer(game,c=>c.Parameters.GetValueOrDefault("option")=="peer:3");Reach(game,p=>p.SkillPrompt?.SkillId==Watch);
        var split=game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="happy:zhimeng").ShenLuSu!;
        Need(split is{Stage:ShenLuSuStage.DivideChildren,Paid:true,Target:3}&&split.Amount==ownerHand+targetHand&&
            View(game).HandCount==(split.Amount+1)/2&&View(game,3).HandCount==split.Amount/2&&
            split.Division.All(c=>Location(game,c.CardId)==CardLocation.Hand(c.ToSeat)),
            "Happy Team Zhimeng accepts that peer and reuses the native random physical partition, retaining the odd-card rule.");
        Answer(game,_=>true);Reach(game,_=>false,()=>!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="happy:zhimeng"));
    }
    private static PlayerSnapshot View(GameEngine game,int seat=0)=>game.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? Pending(GameEngine game)=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] Events<T>(GameEngine game)=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static CardLocation Location(GameEngine game,int id)=>game.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
    private static bool HappyChoice(PendingDecision prompt,string option)=>prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="happy-dingzhou"&&c.Parameters.GetValueOrDefault("option")?.StartsWith(option,StringComparison.Ordinal)==true);
    private static bool ShenLsChoice(PendingDecision prompt,string option)=>prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shen-lu-su"&&c.Parameters.GetValueOrDefault("option")==option);
    private static void Skill(GameEngine game,string id,string activation,int[] targets)=>Accept(game,new UseProgramSkillCommand(0,id,activation,[],targets,game.Revision,Pending(game)!.PromptId));
    private static void End(GameEngine game)=>Accept(game,new EndPlayPhaseCommand(0,game.Revision,Pending(game)!.PromptId));
    private static void Use(GameEngine game,CardKind kind,int[] targets)
    {
        var ids=View(game).Hand.Where(c=>c.Kind==kind).Select(c=>c.Id).ToHashSet();
        var action=game.GetHumanLegalActions().First(a=>a.CardId is{}id&&ids.Contains(id)&&a.TargetSeats.SequenceEqual(targets));
        Accept(game,new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,game.Revision,Pending(game)!.PromptId,action.PlayedCardKind,action.TargetCardId){ConversionSource=action.ConversionSource});
    }
    private static void Answer(GameEngine game,Func<PromptChoice,bool> select)
    {
        var prompt=Pending(game)!;Accept(game,new AnswerPromptCommand(prompt.PlayerSeat,prompt.PromptId,prompt.Choices.First(select).Id,game.Revision));
    }
    private static void Play(GameEngine game)=>Reach(game,p=>p is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine game,Func<PendingDecision,bool> ready,Func<bool>? state=null)
    {
        for(var n=0;n<260;n++)
        {
            if(state?.Invoke()==true)return;
            if(Pending(game)is{}prompt)
            {
                if(ready(prompt))return;
                if(prompt.Kind==DecisionKind.SelectFaction){Answer(game,_=>true);continue;}
                if(prompt.Kind is DecisionKind.ProgramTrigger or DecisionKind.SelectTargetCard)
                {
                    var choice=prompt.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")??
                        prompt.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")??prompt.Choices.First();
                    Answer(game,c=>c.Id==choice.Id);continue;
                }
                if(prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification)
                {Answer(game,c=>c.Cards.Count==0);continue;}
            }
            Accept(game,new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Happy Lu Su missed native boundary: "+JsonSerializer.Serialize(new{Prompt=Pending(game)?.Kind,Seat=Pending(game)?.PlayerSeat,Frames=game.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name})}));
    }
    private static void Accept(GameEngine game,GameCommand command)
    {
        var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Happy Lu Su command.");
    }
    private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static string State(GameEngine game)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=game.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),game.CardMovements,Zones=game.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(game.AcceptedCommands)});
    private static GameEngine Cold(GameEngine game,ContentRegistry registry)
    {
        var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Need(State(game)==State(restored),"Original private receipt, native child, views, exact physical payments and replay inputs cold-restore identically.");return restored;
    }
    private static (GameEngine,ContentRegistry) Start()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var game=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId="a",ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        Accept(game,new StartGameCommand());Reach(game,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(game,new SelectGeneralCommand(0,Hero,game.Revision,Pending(game)!.PromptId));return(game,registry);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-ls",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(builder),Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"draw-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":7}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy-shen-lu-su.FieldGainChildren","skill-program.happy-shen-lu-su.HandGainChildren","skill-program.mobile-shen-lu-su.DivideChildren"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"watch","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,
                skills=ids.ToDictionary(id=>id,id=>new{name=id,description="Native Happy Lu Su fixture.",optionLabels=id==Watch?new Dictionary<string,string>{{"continue","继续"}}:[]})}));
            foreach(var(id,program)in catalog.Programs)builder.AddSkill(new(id,id,"Native fixture"){Program=program,ProgramPresentation=catalog.Presentations[id]});
            for(var n=1;n<4;n++)builder.AddGeneral(new($"fixture:happy-ls-peer-{n}","Quiet participant","supporter",Quiet,"wei",8));
            var kinds=new[]{"standard:slash","standard:crossbow","standard:indulgence","standard:peach"};
            builder.AddDeck(new("fixture:happy-ls-deck","Real mixed physical deck",3,0,[]){PhysicalCards=Enumerable.Range(0,96).Select(n=>new ContentDeckPhysicalCard(kinds[n%4],n%2==0?Suit.Heart:Suit.Spade,n%13+1)).ToArray()});
            builder.AddMode(new(Mode,"Happy Lu Su Team",4,4,new Dictionary<string,int>(),"fixture:happy-ls-deck",GeneralCandidateCount:4,GeneralPoolIds:[Hero,"fixture:happy-ls-peer-1","fixture:happy-ls-peer-2","fixture:happy-ls-peer-3"],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{"a",2},{"b",2}}));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder builder):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition definition)=>builder.AddGeneral(definition.Id==Hero?definition with{AdditionalSkillIds=[..definition.AdditionalSkillIds!,Driver,Watch]}:definition);
        public void AddSkill(ContentSkillDefinition definition)=>builder.AddSkill(definition);public void AddCard(ContentCardDefinition definition)=>builder.AddCard(definition);
        public void AddDeck(ContentDeckRecipe definition)=>builder.AddDeck(definition);public void AddMode(ContentModeDefinition definition)=>builder.AddMode(definition);
    }
}
