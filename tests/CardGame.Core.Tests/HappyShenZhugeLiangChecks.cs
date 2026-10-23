using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class HappyShenZhugeLiangChecks
{
    private const string Hero="happy:shen-zhuge-liang",Qixing="happy:qixing",Wind="happy:kuangfeng",Mist="happy:dawu";
    private const string Driver="fixture:happy-zgl-driver",Quiet="fixture:happy-zgl-quiet",Watch="fixture:happy-zgl-watch",Probe="fixture:happy-zgl-probe",Mode="identity:classic-happy-zgl-fixture";
    public static void SevenRealHandStarsWindDamageAndSelfMistActualExpiryCold()
    {
        var(game,registry)=Start();Reach(game,p=>p.SkillPrompt?.SkillId==Qixing&&HasAction(p,"select-owned-cards"));
        Need(game.State.CurrentSeat==0&&View(game).HandCount==10&&View(game).PrivateReserveCount==0&&
            game.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)&&m.Reason==CardMoveReasons.InitialDeal)==3&&
            game.CardMovements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0))==10,
            "The seven bonus cards really enter the Team first player's three-card starting hand before the mandatory private choice.");
        var chosen=View(game).Hand.Take(7).Select(c=>c.Id).ToArray();
        foreach(var id in chosen.Take(3))Answer(game,c=>c.Cards.SequenceEqual([id]));
        game=Cold(game,registry);
        foreach(var id in chosen.Skip(3))Answer(game,c=>c.Cards.SequenceEqual([id]));
        Reach(game,p=>p.SkillPrompt?.SkillId==Watch);
        var setup=game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Qixing);
        Need(setup.PendingMovementContinuation is not null&&View(game).HandCount==3&&View(game).PrivateReserveCards!.Select(c=>c.Id).Order().SequenceEqual(chosen.Order())&&
            game.CreateSnapshot(1).Players.Single(p=>p.Seat==0).PrivateReserveCards is null,
            "The chosen seven are one paid private reserve movement; its original program waits for the native hand-loss child and other viewers see no star faces.");
        game=Cold(game,registry);Answer(game,_=>true);
        Offer(game,Qixing);Answer(game,c=>Action(c)=="activate");Reach(game,p=>HasAction(p,"private-reserve-choice"));
        var hand=View(game).Hand.First().Id;var star=View(game).PrivateReserveCards!.First().Id;var handCount=View(game).HandCount;
        Answer(game,c=>c.Cards.SequenceEqual([hand]));Answer(game,c=>c.Parameters.GetValueOrDefault("finish")=="true");Answer(game,c=>c.Cards.SequenceEqual([star]));Play(game);
        Need(View(game).HandCount==handCount&&View(game).PrivateReserveCount==7&&Location(game,hand)==new CardLocation(CardZoneKind.PrivateReserve,0)&&Location(game,star)==CardLocation.Hand(0)&&
            Events<ProgramBindingStartedEvent>(game).Count(e=>e.FrameId==setup.Id)==1,
            "The shared equal-count exchange really swaps a hand entity and a star after normal Draw, without replaying the initial grant.");

        var hp1=View(game,1).Hp;var hp2=View(game,2).Hp;
        End(game);Offer(game,Wind);Answer(game,c=>Action(c)=="activate");Reach(game,p=>p.SkillPrompt?.SkillId==Wind&&HasAction(p,"select-owned-cards"));
        var windStars=new List<int>();
        for(var n=0;n<2;n++){var id=Pending(game)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();windStars.Add(id);Answer(game,c=>c.Cards.SequenceEqual([id]));}
        Answer(game,c=>Action(c)=="finish-owned-cards");Reach(game,p=>HasAction(p,"select-targets"));Answer(game,c=>c.Targets.SequenceEqual([1,2]));
        Offer(game,Mist);
        var windDamage=Events<DamageAppliedEvent>(game).Where(e=>e.SourceSeat==0&&e.TargetSeat is 1 or 2).ToArray();
        var facts=game.Events.Select(e=>e.Payload).ToArray();
        var lastStar=Array.FindLastIndex(facts,e=>e is CardMovedEvent move&&windStars.Contains(move.CardId)&&move.From==new CardLocation(CardZoneKind.PrivateReserve,0));
        var firstDamage=Array.FindIndex(facts,e=>e is DamageAppliedEvent{SourceSeat:0,TargetSeat:1 or 2});
        Need(View(game).PrivateReserveCount==5&&windStars.All(id=>Location(game,id)==CardLocation.DiscardPile)&&lastStar>=0&&firstDamage>lastStar&&
            windDamage.Length==2&&windDamage.All(e=>e.Amount==1&&e.Nature==DamageNature.Normal)&&View(game,1).Hp==hp1-1&&View(game,2).Hp==hp2-1,
            "Happy Wind is one ordinary damage per chosen participant, rather than a fire-vulnerability mark.");
        Answer(game,c=>Action(c)=="activate");Reach(game,p=>p.SkillPrompt?.SkillId==Mist&&HasAction(p,"select-owned-cards"));
        var hp=View(game).Hp;
        var mistStar=Pending(game)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();Answer(game,c=>c.Cards.SequenceEqual([mistStar]));
        Reach(game,p=>p.SkillPrompt?.SkillId==Watch&&Events<HappyDawuGrantedEvent>(game).Length==1);
        var grant=Events<HappyDawuGrantedEvent>(game).Single();
        Need(Location(game,mistStar)==CardLocation.DiscardPile&&View(game).PrivateReserveCount==4&&
            game.CardMovements.Single(m=>m.Sequence==grant.PaymentSequence) is{From:var paidFrom,CardId:var paidCard}&&paidFrom==new CardLocation(CardZoneKind.PrivateReserve,0)&&paidCard==mistStar,
            "Self Mist grants defense only after its exact physical one-star movement has really paid.");
        var normal=Events<DamageAppliedEvent>(game).Last(e=>e.TargetSeat==0);
        Need(normal.SourceSeat!=0&&normal.Nature==DamageNature.Normal&&normal.Amount==1&&View(game).Hp==hp-1,
            "During another actual turn, the paid self Mist reduces a real two-point normal damage by exactly one.");
        Answer(game,_=>true);Reach(game,p=>p.SkillPrompt?.SkillId==Watch&&Events<DamageAppliedEvent>(game).Any(e=>e.TargetSeat==0&&e.Nature==DamageNature.Fire));
        var fire=Events<DamageAppliedEvent>(game).Last(e=>e.TargetSeat==0);
        Need(fire.Nature==DamageNature.Fire&&fire.Amount==1&&View(game).Hp==hp-2,"Fire remains one full point while the same self Mist lease is still active.");
        Answer(game,_=>true);Play(game);
        Need(Events<TurnStartedEvent>(game).Last() is{ActorSeat:0} actual&&actual.TurnNumber>grant.ActualTurn&&
            Events<ProgramBooleanStateChangedEvent>(game).Last(e=>e.SkillId==Mist&&e.StateId=="mist-active").Value==false,
            "The original public state expires at the owner's next actual turn start before any next-Draw exchange.");
        Skill(game,"heal");Play(game);var afterHeal=View(game).Hp;Skill(game,"normal");Play(game);
        Need(View(game).Hp==afterHeal-1&&Events<HappyDawuGrantedEvent>(game).Length==1,
            "After expiry, a real one-point normal damage is no longer reduced, and no paid star or grant is repeated.");
    }
    private static string? Action(PromptChoice choice)=>choice.Parameters.GetValueOrDefault("program-action");
    private static bool HasAction(PendingDecision prompt,string action)=>prompt.Choices.Any(c=>Action(c)==action);
    private static PlayerSnapshot View(GameEngine game,int seat=0)=>game.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? Pending(GameEngine game)=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static CardLocation Location(GameEngine game,int id)=>game.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
    private static T[] Events<T>(GameEngine game)=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Answer(GameEngine game,Func<PromptChoice,bool> select){var prompt=Pending(game)!;Accept(game,new AnswerPromptCommand(prompt.PlayerSeat,prompt.PromptId,prompt.Choices.First(select).Id,game.Revision));}
    private static void End(GameEngine game)=>Accept(game,new EndPlayPhaseCommand(0,game.Revision,Pending(game)!.PromptId));
    private static void Skill(GameEngine game,string activation)=>Accept(game,new UseProgramSkillCommand(0,Driver,activation,[],[],game.Revision,Pending(game)!.PromptId));
    private static void Offer(GameEngine game,string skill)=>Reach(game,p=>p.SkillPrompt?.SkillId==skill&&HasAction(p,"activate"));
    private static void Play(GameEngine game)=>Reach(game,p=>p is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine game,Func<PendingDecision,bool> ready)
    {
        for(var n=0;n<260;n++)
        {
            if(Pending(game)is{}prompt)
            {
                if(ready(prompt))return;
                if(prompt.Kind==DecisionKind.SelectFaction){Answer(game,_=>true);continue;}
                if(prompt.Kind is DecisionKind.ProgramTrigger or DecisionKind.SelectTargetCard)
                {
                    var choice=prompt.Choices.FirstOrDefault(c=>Action(c)=="skip")??prompt.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")??prompt.Choices.First();
                    Answer(game,c=>c.Id==choice.Id);continue;
                }
                if(prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification){Answer(game,c=>c.Cards.Count==0);continue;}
            }
            Accept(game,new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Happy Zhuge Liang missed native boundary: "+JsonSerializer.Serialize(new{Prompt=Pending(game)?.Kind,Seat=Pending(game)?.PlayerSeat,Frames=game.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name})}));
    }
    private static void Accept(GameEngine game,GameCommand command){var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected native Happy Zhuge Liang command.");}
    private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static string State(GameEngine game)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=game.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),game.CardMovements,Zones=game.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(game.AcceptedCommands)});
    private static GameEngine Cold(GameEngine game,ContentRegistry registry)
    {
        var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Need(State(game)==State(restored),"Private star selection, real native movement child and every original payment/cursor recover identically.");return restored;
    }
    private static (GameEngine,ContentRegistry) Start()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var game=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId="a",ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        Accept(game,new StartGameCommand());Reach(game,p=>p.Kind==DecisionKind.SelectGeneral);Accept(game,new SelectGeneralCommand(0,Hero,game.Revision,Pending(game)!.PromptId));return(game,registry);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-zgl",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(builder),Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":2}]},
                {"id":"normal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"normal"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"priority":100,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Probe}}","revision":1,"triggers":[{"id":"native-damage","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                {"op":"damageParticipants","target":"owner","targetKind":"otherLiving","amount":2,"nature":"normal"},
                {"op":"damageParticipants","target":"owner","targetKind":"otherLiving","amount":1,"nature":"fire"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"initial-paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:qixing.MoveBoundCards"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-initial","options":[{"id":"continue"}]}]},
                {"id":"damaged","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-damage","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Probe,Watch};
            var presentations=ids.ToDictionary(id=>id,id=>
            {
                var value=new Dictionary<string,object>{{"name",id},{"description","Native Happy Zhuge Liang fixture."}};
                if(id==Watch)value["optionLabels"]=new Dictionary<string,string>{{"continue","继续"}};
                return value;
            });
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations}));
            foreach(var(id,program)in catalog.Programs)builder.AddSkill(new(id,id,"Native fixture"){Program=program,ProgramPresentation=catalog.Presentations[id]});
            for(var n=1;n<4;n++)builder.AddGeneral(new($"fixture:happy-zgl-peer-{n}","Quiet participant","supporter",Quiet,"wei",8){AdditionalSkillIds=n==1?[Probe]:[]});
            builder.AddDeck(new("fixture:happy-zgl-deck","Real star entities",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(n=>new ContentDeckPhysicalCard("standard:slash",n%2==0?Suit.Heart:Suit.Spade,n%13+1)).ToArray()});
            builder.AddMode(new(Mode,"Happy Zhuge Liang Team",4,4,new Dictionary<string,int>(),"fixture:happy-zgl-deck",GeneralCandidateCount:4,
                GeneralPoolIds:[Hero,"fixture:happy-zgl-peer-1","fixture:happy-zgl-peer-2","fixture:happy-zgl-peer-3"],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{"a",2},{"b",2}}));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder builder):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition definition)=>builder.AddGeneral(definition.Id==Hero?definition with{AdditionalSkillIds=[..definition.AdditionalSkillIds!,Driver,Watch]}:definition);
        public void AddSkill(ContentSkillDefinition definition)=>builder.AddSkill(definition);public void AddCard(ContentCardDefinition definition)=>builder.AddCard(definition);
        public void AddDeck(ContentDeckRecipe definition)=>builder.AddDeck(definition);public void AddMode(ContentModeDefinition definition)=>builder.AddMode(definition);
    }
}
