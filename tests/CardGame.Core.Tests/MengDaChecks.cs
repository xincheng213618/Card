using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MengDaChecks
{
    private const string Goude="ol:goude",Owner="fixture:md-owner",Driver="fixture:md-driver",Quiet="fixture:md-quiet",
        Watch="fixture:md-watch",Ending="fixture:md-ending",Mode="identity:classic-meng-da-fixture";
    public static void GoudeWholeInvocationHistoryAndOptionMasksCold()
    {
        foreach(var scene in new[]{"none","draw-two","draw-three","discard-three","equipment-only","foreign-draw","physical","converted"})
        {
            var(g,r)=Start(scene);var turn=g.State.TurnNumber;
            if(scene=="draw-two")Use(g,"draw-two");
            if(scene=="draw-three")Use(g,"draw-three");
            if(scene=="discard-three")Discard(g,"discard-three",3);
            if(scene=="equipment-only"){Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Discard(g,"discard-equipment",1);}
            if(scene=="foreign-draw")Use(g,"draw-other",targets:[1]);
            if(scene is"physical"or"converted"){Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]));Play(g);}
            Play(g);End(g);Reach(g,()=>Offer(g)||g.State.TurnNumber>turn);
            Need(!Offer(g)&&!E<GoudeStartedEvent>(g).Any(e=>e.ActualTurn==turn),"An entire Draw2/3, three-card Hand discard, one equipment discard, foreign-faction draw and physical/conversion Slash do not become Goude's four qualifying actions: "+scene);
            if(scene=="discard-three")Need(E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.TurnNumber==turn&&e.InitiatorSeat==0&&e.ActualCount==3&&e.HandCount==3)&&
                !E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.TurnNumber==turn&&e.ActualCount==1&&e.HandCount==1),
                "Three real Hand payments remain one logical discard invocation, rather than three singleton MoveCard facts.");
            if(scene=="equipment-only")Need(E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.TurnNumber==turn&&e.ActualCount==1&&e.HandCount==0),"A genuine equipment discard is retained in the whole invocation ledger but is not a one-Hand discard.");
            Need(!E<GoudeZeroSlashDeclaredEvent>(g).Any(e=>e.ActualTurn==turn),"Printed or one-material converted Slash never becomes zero-entity usage.");
            _=Cold(g,r);
        }
        foreach(var scene in new[]{"draw-one","recast","late"})
        {
            var(g,r)=Start(scene);
            if(scene=="recast")
            {
                var card=V(g,0).Hand[0].Id;Accept(g,new RecastCardCommand(0,card,g.Revision,P(g)!.PromptId));
                Play(g);Need(g.CardMovements.Any(m=>m.CardId==card&&m.Reason==CardMoveReasons.RecastDiscard),"The qualification comes from real Iron Chain recast drawing one, not a synthetic discard.");
            }
            else{Use(g,"draw-one");Play(g);if(scene=="late"){Need(V(g,0).Skills!.All(s=>s.ContentId!=Goude),"The genuine Draw1 occurred while Goude was unowned.");Use(g,"grant");Play(g);}}
            Begin(g);var started=E<GoudeStartedEvent>(g).Single();
            Need(started.ExecutedMask==(int)GoudeOption.Draw&&!Options(g).Contains(GoudeOption.Draw)&&Options(g).Order().SequenceEqual(new[]{GoudeOption.Discard,GoudeOption.Slash,GoudeOption.Faction}.Order()),
                "Whole-call history exists before skill acquisition and excludes the already executed draw option, including recast's actual Draw1.");
            FreezeReceipt(g);g=Cold(g,r);Choose(g,GoudeOption.Discard);Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>Branch(g,"card"));Answer(g,c=>c.Cards.Count==1);
            Reach(g,()=>E<GoudeCompletedEvent>(g).Any(e=>e.FrameId==started.FrameId));
            Need(E<GoudeMovementPaidEvent>(g).Count(e=>e.FrameId==started.FrameId&&e.Option==GoudeOption.Discard&&e.ActualCount==1)==1&&
                E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.ParentFrameId==started.FrameId&&e.ActualCount==1&&e.HandCount==1),
                "A self-targeted genuine Hand discard pays the other branch once and creates its own complete native invocation.");
            _=Cold(g,r);
        }
        StoneAxeWholeGeneralWeaponDiscard();
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Action(GameEngine g,string a)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==a)==true;
    private static bool Branch(GameEngine g,string b)=>Action(g,"meng-da-goude")&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==b);
    private static bool Offer(GameEngine g)=>P(g)?.PlayerSeat==0&&P(g)?.SkillPrompt?.SkillId==Goude&&Action(g,"activate");
    private static GoudeOption[] Options(GameEngine g)=>P(g)!.Choices.Select(c=>Enum.Parse<GoudeOption>(c.Parameters["option"])).ToArray();
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected genuine Meng Da command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(p.PlayerSeat==0&&c is not null,"Missing actual human Meng Da choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().FirstOrDefault(pick);Need(a is not null,"Missing actual Meng Da native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},caller,line,"actual Play");
    private static void Begin(GameEngine g)
    {
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>Offer(g)||g.State.TurnNumber>turn);Need(Offer(g),"No actual Goude offer at this completed turn: "+Diagnostic(g));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Branch(g,"option"));
    }
    private static void Choose(GameEngine g,GoudeOption o)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option")==o.ToString());
    private static void Discard(GameEngine g,string id,int count)
    {Use(g,id);Reach(g,()=>Action(g,"select-owned-cards"));for(var i=0;i<count;i++){Answer(g,c=>c.Cards.Count==1);if(i<count-1)Reach(g,()=>Action(g,"select-owned-cards"));}Play(g);}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? boundary=null)
    {
        for(var i=0;i<220;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Meng Da ended at {caller}:{line} {boundary}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId==Watch)Continue(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected Meng Da {caller}:{line} {boundary}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Meng Da {caller}:{line} {boundary}: "+Diagnostic(g));
    }
    private static void FreezeReceipt(GameEngine g)
    {
        var r=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Goude is not null).Goude!;
        Need(r.FactionSeats is IList<int>{IsReadOnly:true}&&r.Materials is IList<GoudeHandMaterial>{IsReadOnly:true},"Own receipt collections are immutable to observers.");
        try{((IList<int>)r.FactionSeats).Add(3);throw new InvalidOperationException("Mutable Goude faction seats.");}catch(NotSupportedException){}
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive,V(g,s).FactionId}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(4).Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(5).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves all native turn, logical invocation, faction, opaque selection and once-paid child cursors.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"draw-death"or"slash-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(scene=="legacy-zero"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));}
        Play(g);return(g,r);
    }
    private static void StoneAxeWholeGeneralWeaponDiscard()
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new GoudeStoneAxeFixture());
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=4},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);
        Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var axe=V(g,0).Equipment.Single().Id;
        Use(g,"inherit");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")==axe.ToString());Play(g);
        Need(g.CardMovements.Any(m=>m.CardId==axe&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason.Value=="equipment.permanent-removal"),
            "A real native weapon inheritance retains Stone Axe's actual ability after removing the original physical Axe.");
        Use(g,"weapon");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:md-axe-reserve");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);
        var weapon=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;
        Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]));Reach(g,()=>P(g)?.Kind==DecisionKind.StoneAxe);
        var hand=V(g,0).Hand[0].Id;var turn=g.State.TurnNumber;g=Cold(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="stone-axe-use"&&c.Cards.Contains(weapon)&&c.Cards.Contains(hand));Play(g);
        var invocation=E<NativeLogicalDiscardInvocationRecordedEvent>(g).Single(e=>e.Reason==CardMoveReasons.StoneAxeDiscard);
        Need(invocation.InitiatorSeat==0&&invocation.ExecutorSeat==0&&invocation.SubjectSeat==0&&invocation.ActualCount==2&&invocation.HandCount==1&&
            g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.StoneAxeDiscard)==1&&
            g.CardMovements.Count(m=>m.CardId==hand&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.StoneAxeDiscard)==1&&
            g.CardMovements.Count(m=>m.CardId==hand&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.StoneAxeDiscard)==1&&
            E<StoneAxeResolvedEvent>(g).Any(e=>e.Used&&e.DiscardedCardIds.Count==2)&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==1),
            "The actual two-material Stone Axe pays one Hand entity and one equipped GeneralWeapon to their exact native destinations once, retaining whole ActualCount2 even though HandCount1.");
        g=Cold(g,r);End(g);Reach(g,()=>Offer(g)||g.State.TurnNumber>turn);
        Need(!Offer(g)&&!E<GoudeStartedEvent>(g).Any(e=>e.ActualTurn==turn)&&!E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.TurnNumber==turn&&e.ActualCount==1&&e.HandCount==1),
            "Neither native Processing half nor removed GeneralWeapon turns the genuine two-card Axe cost into Goude's single-Hand-discard action.");
        _=Cold(g,r);
    }
    private sealed class GoudeStoneAxeFixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-meng-da-stone-axe",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:meng-da");
            var defs=new[]{
                $$"""{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[{"id":"slash","inputKinds":["stoneAxe"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[{"id":"inherit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"inheritWeapon","target":"owner"}]},{"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"viewAs":[{"id":"dodge","inputKinds":["stoneAxe"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}],"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="整次贯石斧原生付款"});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生付款"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:md-axe-pick","实际孟达","公开固定选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            b.AddSkill(new("fixture:md-axe-unused","实际备用武器","公开固定选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000d)});
            b.AddGeneral(new(Owner,"实际孟达","ol-meng-da","fixture:md-axe-pick","shu",12,[Driver,Goude]));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:md-axe-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"wei",12,[]));
            b.AddGeneral(new("fixture:md-axe-reserve","实际备用武器","supporter","fixture:md-axe-unused","wei",4,[]));
            b.AddCard(new("fixture:md-axe-card","真实贯石斧","装备牌","实际原生实体",CardKind.StoneAxe));
            b.AddDeck(new("fixture:md-axe-deck","同质真实贯石斧牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:md-axe-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"孟达原生贯石斧边界",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:md-axe-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:md-axe-reserve"]));
        }
    }

    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-meng-da",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:meng-da");
            static string A(string id,string effects,int targets=0)=>$$"""{"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"otherLiving","usesPerTurn":null,"effects":[{{effects}}]}""";
            static string DiscardEffect(string zones,int count)=>$$"""{"op":"selectOwnedCards","target":"owner","amount":{{count}},"zones":[{{zones}}],"resultBind":"paid"},{"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}""";
            var activations=new[]{A("draw-one","""{"op":"draw","target":"owner","amount":1}"""),A("draw-two","""{"op":"draw","target":"owner","amount":2}"""),A("draw-three","""{"op":"draw","target":"owner","amount":3}"""),
                A("draw-other","""{"op":"draw","target":"selectedTarget","amount":1}""",1),A("discard-one",DiscardEffect("\"hand\"",1)),A("discard-three",DiscardEffect("\"hand\"",3)),A("discard-equipment",DiscardEffect("\"equipment\"",1)),
                A("grant",$$"""{"op":"grantSkills","target":"owner","skillIds":["{{Goude}}"]}""")};
            var defs=new List<string>{$$"""{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[{"id":"slash","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[{{string.Join(',',activations)}}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var loss=scene is"discard-loss"or"draw-loss"or"slash-loss";var death=scene is"draw-death"or"slash-death";
            if(loss)defs.Add($$"""{"id":"fixture:md-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-goude","skillId":"{{Goude}}","minimumHp":13}]}""");
            if(loss||death)
            {
                var slash=scene.StartsWith("slash-",StringComparison.Ordinal);var discard=scene=="discard-loss";
                var trigger=slash?""" "window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"], """:discard?""" "window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","movementReasons":["skill-program.meng-da.goude.discard"], """:""" "window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.meng-da.goude.draw"], """;
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-paid-child",{{trigger}}"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(death?20:1)}}}]}]}""");
            }
            if(scene=="ending-draw")defs.Add($$"""{"id":"{{Ending}}","revision":1,"triggers":[{"id":"ending-draw","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}""");
            if(scene=="all-four")defs.Add($$"""{"id":"{{Ending}}","revision":1,"triggers":[{"id":"three-real-items","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":1},{{DiscardEffect("\"hand\"",1)}},{"op":"selectTarget","target":"owner","targetKind":"otherLivingInAttackRange"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}""");
            if(scene=="legacy-zero")defs.Add("""{"id":"fixture:md-legacy-zero","revision":1,"triggers":[{"id":"old-zero-use","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"normalSlashTarget","useCardActionWindows":false}]}]}""");
            if(scene=="dead-turn-owner")defs.Add($$"""{"id":"{{Ending}}","revision":1,"triggers":[{"id":"actual-ending-death","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"loseHp","target":"owner","amount":20}]}]}""");
            if(scene=="slash-dodge")defs.Add("""{"id":"fixture:md-dodge","revision":1,"viewAs":[{"id":"real-dodge","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="孟达原生整次行为夹具",
                optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生行为"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:md-pick","实际孟达","公开固定选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var skills=new List<string>{Driver};if(scene!="late")skills.Add(loss?"fixture:md-conditional":Goude);if(loss||death)skills.Add(Watch);if(scene=="ending-draw")skills.Add(Ending);if(scene=="legacy-zero")skills.Add("fixture:md-legacy-zero");
            b.AddGeneral(new(Owner,"实际孟达","ol-meng-da","fixture:md-pick",scene=="all-four"?"wei":"shu",12,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:md-peer-{i}").ToArray();
            for(var i=0;i<peers.Length;i++){var peerSkills=new List<string>();if(scene=="slash-dodge")peerSkills.Add("fixture:md-dodge");if(scene=="all-four"){peerSkills.Add(Goude);peerSkills.Add(Ending);}if(scene=="dead-turn-owner")peerSkills.Add(Ending);
                b.AddGeneral(new(peers[i],"实际其他角色","supporter",Quiet,scene is"all-four"or"dead-turn-owner"?"shu":"wei",12,peerSkills));}
            var kind=scene=="recast"?CardKind.IronChain:scene=="physical"?CardKind.Slash:CardKind.Crossbow;
            b.AddCard(new("fixture:md-card","真实"+kind,kind==CardKind.Crossbow?"装备牌":kind==CardKind.Slash?"基本牌":"锦囊牌","原生实体",kind));
            b.AddDeck(new("fixture:md-deck","固定同质真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:md-card",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"孟达原生小局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:md-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}

