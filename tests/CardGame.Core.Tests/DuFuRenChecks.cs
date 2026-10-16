using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DuFuRenChecks
{
    private const string Yise="ol:yise",Shunshi="ol:shunshi",Kangrui="ol:kangrui";
    private const string Driver="fixture:df-driver",Owner="fixture:df-owner",Quiet="fixture:df-quiet",Peer="fixture:df-peer-damage";
    private const string Watch="fixture:df-gift-watch",Health="fixture:df-health",Conditional="fixture:df-conditional",Freeze="fixture:df-freeze";
    private const string Skip="fixture:df-skip",Arm="fixture:df-arm-skip",Prevent="fixture:df-prevent",Mid="fixture:df-mid-chain",Transfer="fixture:df-transfer",Mode="identity:classic-du-fu-ren-fixture",Legacy="identity:du-fu-ren-legacy-fixture";

    public static void MixedOwnedGainsStackBlackActualSlashSourceLossDodgePreventionAndSnatchCold()
    {
        var (g,r)=Start("colors"); Use(g,"draw"); Play(g); Use(g,"hurt",[1]); Play(g); Use(g,"hurt",[1]); Play(g);
        var red=V(g,0).Hand.First(c=>c.Suit is Suit.Heart or Suit.Diamond).Id;
        var black=V(g,0).Hand.First(c=>c.Suit is Suit.Spade or Suit.Club).Id; var hp=V(g,1).Hp;
        Use(g,"give",[1],[red,black]); Reach(g,()=>Branch(P(g),"recover"));
        var gain=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DuFuRen?.Stage==DuFuRenStage.RedChoice);
        Need(gain.DuFuRen!.RecipientSeat==1 && E<DuFuRenBlackIssuedEvent>(g).Count(e=>e.RecipientSeat==1)==1 &&
            gain.WindowContext!.MovementBatch!.DuFuRenGains!.Count(e=>e.OwnerSeat==0&&e.RecipientSeat==1)==2 && g.CreateSnapshot(1).PendingDecision is null,
            "A real mixed-color two-card gain is one source/recipient batch: one mandatory black mark plus one private optional red recovery.");
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="recover"); Play(g);
        Need(V(g,1).Hp==hp+1 && E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==1)==1,"The red branch really recovers once through native recovery.");
        black=V(g,0).Hand.First(c=>c.Suit is Suit.Spade or Suit.Club).Id;
        Use(g,"give",[1],[black]); Play(g); Need(E<DuFuRenBlackIssuedEvent>(g).Count(e=>e.RecipientSeat==1)==2,"Separate real gain batches stack separate next-Slash increments.");
        Use(g,"lose-yise"); Play(g); Need(V(g,0).Skills!.All(s=>s.Id!=Yise),"The source skill really disappears before its already-issued damage effect.");
        Use(g,"damage",[1]); Play(g); Need(!E<DuFuRenBlackConsumedEvent>(g).Any(),"Cardless native damage cannot spend a received-Slash mark.");
        hp=V(g,1).Hp; g=Cold(g,r); Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]))); Play(g);
        Need(V(g,1).Hp==hp-3 && E<DuFuRenBlackConsumedEvent>(g).Count()==2 && E<DamageAppliedEvent>(g).Last(e=>e.TargetSeat==1).Amount==3,
            "The next actual native Slash adds both issued marks after source loss and consumes each once."); _=Cold(g,r);

        (g,r)=Start("dodge"); Use(g,"give",[1],[V(g,0).Hand[0].Id]); Play(g);
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]))); Play(g);
        Need(E<DuFuRenBlackIssuedEvent>(g).Count()==1&&!E<DuFuRenBlackConsumedEvent>(g).Any()&&!E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==1),
            "A genuine physical Dodge closes the native Slash response without consuming the future received-damage mark."); _=Cold(g,r);

        (g,r)=Start("prevent"); Use(g,"give",[1],[V(g,0).Hand[0].Id]); Play(g);
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));
        Reach(g,()=>Optional(P(g),Prevent)); g=Cold(g,r); Activate(g); Play(g);
        Need(!E<DuFuRenBlackConsumedEvent>(g).Any()&&!E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==1),"A truly prevented Slash damage attempt preserves its mark for a later actual injury."); _=Cold(g,r);

        (g,r)=Start("snatch"); var before=V(g,0).HandCount; var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Snatch&&a.TargetSeats.SequenceEqual([1]));
        Native(g,action); Reach(g,()=>P(g) is { PlayerSeat:0,Kind:DecisionKind.SelectTargetCard });
        Need(P(g)!.Choices.All(c=>c.Cards.Count==0),"Native Snatch publishes opaque foreign hand slots without their entity IDs.");
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="target-card-slot"&&c.Parameters.GetValueOrDefault("slot-index")=="0"); Play(g);
        var snatchBlack=E<DuFuRenBlackIssuedEvent>(g).Where(e=>e.Source.OwnerSeat==1&&e.RecipientSeat==0).ToArray();
        Need(snatchBlack.Length==1,"Native Snatch must issue exactly one original-owner black increment: "+Diagnostic(g));var issued=snatchBlack.Single();
        Need(V(g,0).HandCount==before && E<DuFuRenTransferProducerEvent>(g).Any(e=>e.OwnerSeat==1&&e.RecipientSeat==0) && issued.BatchId>0,
            "Native Snatch attributes the exact HE-to-Processing-to-Hand transfer to the robbed original owner; the used Snatch material itself supplies no false owned-gain trigger."); _=Cold(g,r);

        (g,r)=Start("transfer"); Use(g,"give",[1],[V(g,0).Hand[0].Id]); Play(g); hp=V(g,0).Hp;
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Transfer&&P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target")==true);
        g=Cold(g,r); Answer(g,c=>c.Targets.SequenceEqual([0])); Play(g);
        Need(V(g,0).Hp==hp-2&&E<DuFuRenBlackConsumedEvent>(g).Single().TargetSeat==0&&E<DuFuRenDamageTransferredEvent>(g).Single() is {OriginalTargetSeat:1,TargetSeat:0,Amount:2},
            "The exact native finalized transfer carries A's already-computed increment into actual positive damage at B and consumes that same issued mark once.");
        var turn=g.State.TurnNumber; End(g); Reach(g,()=>g.State.TurnNumber>turn&&g.State.CurrentSeat==0&&P(g)?.Kind==DecisionKind.PlayCard);
        hp=V(g,1).Hp; Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]))); Play(g);
        Need(V(g,1).Hp==hp-1&&E<DuFuRenBlackConsumedEvent>(g).Count()==1,"A later real Slash cannot borrow the already-consumed original transferred increment."); _=Cold(g,r);

        (g,r)=Start("extra"); Use(g,"give",[1],[V(g,0).Hand[0].Id]); Play(g); hp=V(g,1).Hp;
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1])));
        Reach(g,()=>Optional(P(g),"ol:fengzi")); Activate(g); Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="cost")==true);
        g=Cold(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="cost"); Play(g);
        Need(V(g,1).Hp==hp-3&&E<ExtraCardEffectStartedEvent>(g).Count()==1&&E<DuFuRenBlackConsumedEvent>(g).Count()==1&&
            E<DamageAppliedEvent>(g).Where(e=>e.TargetSeat==1).Select(e=>e.Amount).SequenceEqual([2,1]),
            "A real Fengzi repeats the same native Slash and target under one Use: the first actual hit spends its mark and the new effect round cannot reuse the old owning damage receipt."); _=Cold(g,r);
    }

    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T> E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.CurrentSeat,g.State.TurnNumber,g.State.Phase,g.State.Winner,
        Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).IsAlive,Skills=V(g,s).Skills!.Select(x=>x.Id).ToArray()}).ToArray(),
        Origins=E<DuFuRenOwnedOriginEvent>(g).TakeLast(12).ToArray(),Producers=E<DuFuRenTransferProducerEvent>(g).ToArray(),Black=E<DuFuRenBlackIssuedEvent>(g).ToArray(),
        Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Yise||e.SkillId==Shunshi||e.SkillId==Mid).ToArray(),Moves=g.CardMovements.TakeLast(14).ToArray(),
        Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray()});
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static bool Optional(PendingDecision? p,string id)=>p?.SkillPrompt?.SkillId==id&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static bool Branch(PendingDecision? p,string option)=>p?.SkillPrompt?.SkillId==Yise&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option")==option);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Du Fu Ren real command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;_=p.Choices.First(pick);if(p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));return;}Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Decline(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null,IReadOnlyList<int>? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is {PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<350;i++)
        {
            if(done())return;
            if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Du Fu Ren completed before boundary: "+JsonSerializer.Serialize(new{g.State.Winner,Players=g.State.Players.Select(p=>new{p.Seat,p.Hp,p.IsAlive}),Facts=g.Events.TakeLast(25).Select(e=>new{Type=e.Payload.GetType().Name,Payload=JsonSerializer.Serialize(e.Payload,e.Payload.GetType())})}));
            var p=P(g);
            if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(Optional(p,Shunshi))Decline(g);
            else if(Branch(p,"decline"))Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="decline");
            else if(p?.SkillPrompt?.SkillId==Freeze&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-targets")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-targets");
            else if(p?.SkillPrompt?.SkillId==Freeze&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-targets")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-targets");
            else if(p is {PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is {PlayerSeat:0})throw new InvalidOperationException("Unexpected Du Fu Ren prompt: "+JsonSerializer.Serialize(p));
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Du Fu Ren boundary not reached: "+JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact private choices, native owned gains/recovery/dying children and independent issued phase/damage receipts.");return restored;}
    private static (GameEngine,ContentRegistry) Start(string scenario,bool preparation=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death-peer"?Role.Renegade:Role.Lord,ModeId=scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is {PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(preparation)Reach(g,()=>Optional(P(g),Shunshi));else Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-du-fu-ren",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:du-fu-ren");if(scenario.StartsWith("chain-",StringComparison.Ordinal))CharacterFixtureContent.Register(b,"ol:zhang-yi");if(scenario=="extra")CharacterFixtureContent.Register(b,"ol:wang-rong");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"viewAs":[{"id":"dodge-as-native-slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"give","minCards":1,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]},
                {"id":"skip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Arm}}"]}]},
                {"id":"lose-yise","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Yise}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"two-real-offturn-damages","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":100,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherWounded"},{"op":"damage","target":"selectedTarget","amount":1},{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gift","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.du-fu-ren.shunshi.gift"],"optional":false,"effects":[{{(scenario.StartsWith("death",StringComparison.Ordinal)?"": "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},")}} {"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{(scenario.StartsWith("death",StringComparison.Ordinal)?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}":"")}}]}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-heal","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-shunshi","skillId":"{{Shunshi}}","minimumHp":5}]},
              {"id":"{{Freeze}}","revision":1,"triggers":[{"id":"freeze-actual-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"selectTargets","target":"owner","targetKind":"otherLivingHandAtLeastOwner","minimumTargets":1,"maximumTargets":8,"numberExpression":"plannedNormalDrawCount","targetAiOrder":"hostileThenHandCount"},{"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"frozen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Skip}}","revision":1,"triggers":[{"id":"skip-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]},{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"skip-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
              {"id":"{{Arm}}","revision":1,"triggers":[{"id":"arm-after-real-first-discard","window":"turnEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Skip}}"]}]}]},
              {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{Mid}}","revision":1,"triggers":[{"id":"middle-black-gift","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"priority":-100,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true},{"op":"chooseOption","target":"owner","resultBind":"middle","options":[{"id":"continue"}]}]}]},
              {"id":"{{Transfer}}","revision":1,"triggers":[{"id":"real-transfer","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile"},{"op":"redirectCurrentDamage","target":"selectedTarget","sourceBind":"cost","drawLostHpAfterDamage":false}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Peer,Watch,Health,Conditional,Freeze,Skip,Arm,Prevent,Mid,Transfer};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="杜夫人真实命令夹具",optionLabels=id is Watch or Health or Freeze or Mid?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"杜夫人实际原生行为"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            var humanRole=scenario=="death-peer"?Role.Renegade:Role.Lord;
            b.AddSkill(new("fixture:df-pick-owner","固定本人","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==humanRole?100000d:-100000d)});
            var own=new List<string>{Driver,Yise,scenario=="loss"?Conditional:Shunshi};if(scenario=="loss"||scenario.StartsWith("death",StringComparison.Ordinal))own.Add(Watch);if(scenario=="equipment")own.Add(Health);if(scenario=="phase")own.Add(Freeze);if(scenario=="extra")own.Add("ol:fengzi");if(scenario.StartsWith("chain-",StringComparison.Ordinal)&&!scenario.Contains("transfer",StringComparison.Ordinal))own.AddRange([Kangrui,Mid]);
            b.AddGeneral(new(Owner,"杜夫人实际本人","ol-du-fu-ren","fixture:df-pick-owner","wei",4,own));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:df-peer-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {
                var pick="fixture:df-pick-"+index;b.AddSkill(new(pick,"固定其他顺序","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==humanRole?-100000d:100003d-index)});
                var extra=new List<string>{Quiet,Yise};if(scenario=="phase"&&index==0)extra.Add(Peer);if(scenario=="prevent"&&index==0)extra.Add(Prevent);if((scenario.StartsWith("chain-",StringComparison.Ordinal)||scenario=="transfer")&&index==0)extra.Add(Transfer);
                b.AddGeneral(new(id,"真实其他参与者","supporter",pick,"wei",8,extra));
            }
            b.AddCard(new("fixture:df-lion","白银狮子","装备牌","真实装备移除回复",CardKind.SilverLion));b.AddCard(new("fixture:df-fire","火杀","基本牌","真实火杀及链伤",CardKind.FireSlash));
            b.AddDeck(new("fixture:df-deck","固定实体与种子",4,2,[]){PhysicalCards=Enumerable.Range(0,128).Select(i=>new ContentDeckPhysicalCard(scenario=="equipment"?"fixture:df-lion":scenario=="snatch"?"standard:snatch":scenario=="dodge"?"standard:dodge":scenario.Contains("chain",StringComparison.Ordinal)?"fixture:df-fire":"standard:slash",scenario=="colors"&&i%2==0?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            var spares=scenario=="weapon"?Enumerable.Range(0,4).Select(i=>$"fixture:df-unused-{i}").ToArray():[];
            foreach(var spare in spares)b.AddGeneral(new(spare,"未出场原生武器材料","supporter","standard:none","wei",4));
            var roles=scenario=="death-peer"?new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),2},{nameof(Role.Renegade),1}}:new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}};
            b.AddMode(new(scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,"杜夫人真实命令",4,4,roles,"fixture:df-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers,..spares]));
        }
    }
}
