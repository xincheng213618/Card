using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangGuanChecks
{
    private const string Miu="ol:miuyan",Shilu="ol:shilu",Owner="fixture:wg-owner",Driver="fixture:wg-driver",Quiet="fixture:wg-quiet",
        Watch="fixture:wg-watch",Health="fixture:wg-health",Inspect="fixture:wg-inspect",Mode="identity:classic-wang-guan-fixture";
    public static void MiuyanNativeBlackMaterialsPolarityAndWholeDamageCold()
    {
        foreach(var scene in new[]{"hand","equipment","lion","grain","ox-equipment"})
        {
            var(g,r)=Start(scene);int material;
            if(scene=="hand")material=V(g,0).Hand[0].Id;
            else
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);
                material=V(g,0).Equipment.Single().Id;
                if(scene=="lion"){Use(g,"lose");Play(g);}
                if(scene is"grain"or"ox-equipment")
                {
                    var grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);
                    if(scene=="grain")material=grain;
                }
            }
            Fire(g,1,material);Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackDiscard);
            var issued=E<MiuyanUseIssuedEvent>(g).Single().Invoice;
            Need(issued.PreviousState==SkillPolarity.Yang&&issued.CurrentState==SkillPolarity.Yin&&issued.Material.CardId==material&&
                issued.Material.From.Zone==(scene=="grain"?CardZoneKind.WoodenOxGrain:scene=="hand"?CardZoneKind.Hand:CardZoneKind.Equipment)&&
                !E<CompletedUndamagedUseDamageRecordedEvent>(g).Any(e=>e.CardUseFrameId==issued.UseId),
                "The actual accepted native black conversion switches once before effect damage and retains its real Hand/Equipment/Ox-grain cost.");
            g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(E<CompletedUndamagedUseDamageRecordedEvent>(g).Where(e=>e.CardUseFrameId==issued.UseId).Sum(e=>e.Amount)==1&&
                E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1&&
                E<WangGuanStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse&&e.NativeId==issued.UseId)==1&&
                E<MiuyanEntityObtainedEvent>(g).Any()&&g.CardMovements.Count(m=>m.CardId==material&&m.From==issued.Material.From&&m.Reason==CardMoveReasons.Use)==1,
                "Only real whole-Use positive damage yields Yang's exact shown-hand gain; every native cost is paid once.");
            if(scene=="lion")Need(E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"The converted equipped Lion runs its actual recovery before the native Fire Attack.");
            if(scene=="ox-equipment")Need(g.CardMovements.Any(m=>m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile),"Using the equipped Ox runs its actual stored-grain cleanup.");
            if(scene=="hand")
            {
                var count=E<MiuyanUseIssuedEvent>(g).Length;Fire(g,1,source:Driver);PayFire(g);Play(g);
                Need(E<MiuyanUseIssuedEvent>(g).Length==count,"Another real Fire Attack conversion does not issue or toggle Miuyan.");
                Fire(g,1);PayFire(g);Play(g);
                Need(E<MiuyanUseIssuedEvent>(g).Last().Invoice.PreviousState==SkillPolarity.Yin&&!E<MiuyanRoundDisabledEvent>(g).Any(),"A damaging Yin Use returns to Yang and is not suppressed.");
            }
            _=Cold(g,r);
        }
    }

    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="wang-guan"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Wang Guan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Only the real human chooser may answer.");Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(select).Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> select)
    {
        var a=g.GetHumanLegalActions().FirstOrDefault(select)??throw new InvalidOperationException("Missing actual Wang Guan action: "+Diagnostic(g));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    }
    private static void Fire(GameEngine g,int target,int? material=null,string source=Miu)=>Native(g,a=>a.Kind==LegalActionKind.FireAttack&&a.ConversionSource?.SkillId==source&&a.TargetSeats.SequenceEqual([target])&&(material is null||a.CardId==material));
    private static void PayFire(GameEngine g,bool damage=true){Reach(g,()=>P(g)?.Kind==DecisionKind.FireAttackDiscard);Answer(g,c=>damage?c.Cards.Count==1:c.Cards.Count==0);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<320;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Actual Wang Guan fixture ended before boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&V(g,p.PlayerSeat).IsHuman)
            {
                if(p.Kind==DecisionKind.PlayCard)End(g);
                else if(p.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard)Answer(g,c=>c.Cards.Count==1);
                else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
                else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
                else if(p.Kind==DecisionKind.RespondDodge)Answer(g,c=>c.Cards.Count==0);
                else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else if(p.SkillPrompt?.SkillId is Watch or Health)Continue(g);
                else if(Branch(g,"target"))Answer(g,c=>c.Targets.SequenceEqual([1]));
                else if(Branch(g,"card"))Answer(g,_=>true);
                else throw new InvalidOperationException("Unexpected actual Wang Guan prompt: "+Diagnostic(g));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Wang Guan boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).IsAlive,V(g,s).HandCount}),Frames=g.ResolutionStack.Select(f=>new{f.Id,Kind=f.GetType().Name}),Facts=g.Events.TakeLast(8).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold command replay preserves all private viewers, exact native Use/phase/round invoices, physical cards, permanent identities and once-paid child returns.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"draw-death"or"issuer-death"?Role.Rebel:Role.Lord,
            ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(scene=="private"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Action(g,"private-hand-take"));}else Play(g);
        return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-wang-guan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:wang-guan");
            if(scene=="shandao")CharacterFixtureContent.Register(b,"ol:tian-chou");
            if(scene=="draft")CharacterFixtureContent.Register(b,"classic:cao-jie");
            if(scene=="qingliang")CharacterFixtureContent.Register(b,"ol:rui-ji");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[{"id":"control-fire","inputKinds":[],"inputSuits":[],"outputKind":"fireAttack","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
              {"id":"show","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"shown"},{"op":"revealBoundCards","target":"owner","sourceBind":"shown"}]},
              {"id":"gift","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
              {"id":"top","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"top","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"ownerHand","awaitMovementTriggers":true}]},
              {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
              {"id":"kill-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
              {"id":"lose-miuyan","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:miuyan"],"sourceBind":"standard:none"}]},
              {"id":"regrant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:miuyan"]}]},
              {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},
              {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"requested"}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
            $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}"""};
            if(scene is"draw-loss"or"draw-death")
            {
                var tail=scene=="draw-loss"?"""{"op":"loseHp","target":"owner","amount":1}""":"""{"op":"loseHp","target":"owner","amount":20}""";
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-native-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.wang-guan.shilu.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{{tail}}]}]}""");
            }
            if(scene=="draw-loss")defs.Add("""{"id":"fixture:wg-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-shilu","skillId":"ol:shilu","minimumHp":3}]}""");
            if(scene=="private")defs.Add($$"""{"id":"{{Inspect}}","revision":1,"triggers":[{"id":"inspection","window":"drawPhaseEnded","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"viewAndTakeSelectedTargetHand","target":"selectedTarget","amount":1}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实王瓘原生夹具",
                optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生规则"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:wg-pick","实际本人","公开固定选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var skills=new List<string>{Miu,scene=="draw-loss"?"fixture:wg-conditional":Shilu,Driver,Health};if(scene is"draw-loss"or"draw-death")skills.Add(Watch);if(scene=="private")skills.Add(Inspect);if(scene=="draft")skills.Add("classic:huimin");if(scene=="shandao")skills.Add("ol:shandao");
            b.AddGeneral(new(Owner,"实际王瓘","ol-wang-guan","fixture:wg-pick","wei",4,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:wg-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"wei",12,scene=="qingliang"?["ol:qingliang"]:[]));
            var kind=scene=="lion"?CardKind.SilverLion:scene is"grain"or"ox-equipment"?CardKind.WoodenOx:CardKind.Crossbow;
            b.AddCard(new("fixture:wg-card","真实"+kind,"装备牌","真实原生实体",kind));
            b.AddDeck(new("fixture:wg-deck","固定同花真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:wg-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"王瓘原生小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:wg-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
