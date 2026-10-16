using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NativeLogicalDiscardChecks
{
    private const string Driver="fixture:logical-discard-driver", Watch="fixture:logical-discard-watch",
        Owner="fixture:logical-discard-owner", Mode="identity:logical-discard-fixture";

    public static void WholeLogicalDiscardsKeepCrossPromptAndMixedMaterialInvoicesCold()
    {
        foreach(var shortCost in new[]{false,true})
        {
            var(g,r)=Start();
            if(shortCost)
            {
                Need(V(g).Hand.Count==6,"The fixed initial hand contains four setup cards and two real normal draws.");
                Use(g,"trim");for(var i=0;i<5;i++){Reach(g,()=>Action(g,"select-owned-cards"));Answer(g,c=>c.Cards.Count==1);}Play(g);
                Need(V(g).Hand.Count==1,"Five real selected Hand discards leave exactly one payable material.");
            }
            var baseline=Facts(g).Length;Use(g,"category",[0]);Reach(g,()=>Action(g,"discard-challenge"));
            Answer(g,c=>c.Cards.Count==1);
            if(!shortCost)
            {
                Reach(g,()=>Action(g,"discard-challenge"));
                var draft=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LogicalDiscardInvocation is not null).LogicalDiscardInvocation!;
                Need(draft.Materials.Count==1&&draft.Materials[0].From==CardLocation.Hand(0)&&Facts(g).Length==baseline,
                    "The first non-trick payment stays on its owning frame and does not publish a completed singleton invocation.");
                Need(draft.Materials is IList<NativeLogicalDiscardMaterial>{IsReadOnly:true},"Suspended primary materials are frozen.");
                Need(Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"Other players cannot inspect the second private payment choice.");
                g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);
            }
            Play(g);var paid=Facts(g).Skip(baseline).Single();
            Need(paid.ActualCount==(shortCost?1:2)&&paid.HandCount==paid.ActualCount&&paid.InitiatorSeat==0&&paid.ExecutorSeat==0&&paid.SubjectSeat==0,
                "A whole two-card category cost records exactly once; actual shortage records only its single genuinely paid Hand card.");
            Need(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.LogicalDiscardInvocation is not null),"A completed instruction releases its frame-owned draft.");_=Cold(g,r);
        }
        {
            var(g,r)=Start();var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
            Accept(g,new PlayCardCommand(0,equip.CardId!.Value,equip.TargetSeats,g.Revision,P(g)!.PromptId));Play(g);
            var hand=V(g).Hand[0].Id;var equipment=V(g).Equipment.Single().Id;var baseline=Facts(g).Length;
            Use(g,"mixed");Reach(g,()=>Action(g,"select-owned-cards"));Answer(g,c=>c.Cards.SequenceEqual([hand]));
            Reach(g,()=>Action(g,"select-owned-cards"));g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([equipment]));
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            var batch=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
            var paid=Facts(g).Skip(baseline).Single();
            Need(paid.ActualCount==2&&paid.HandCount==1&&paid.SubjectSeat==0&&paid.ExecutorSeat==0&&
                batch.Movements.Count==2&&batch.Movements.Select(m=>m.From).ToHashSet().SetEquals([CardLocation.Hand(0),CardLocation.Equipment(0)]),
                "Generic MoveBound keeps the original single mixed-zone physical batch and one whole two-primary invocation, rather than a false Hand singleton.");
            g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Play(g);_=Cold(g,r);
        }
        {
            var(g,r)=Start();var baseline=Facts(g).Length;Use(g,"foreign");Reach(g,()=>Action(g,"choose-other-owned-card-discard"));
            Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);var paid=Facts(g).Skip(baseline).Single();
            Need(paid.InitiatorSeat==0&&paid.ExecutorSeat==0&&paid.SubjectSeat==1&&paid.ActualCount==1&&paid.HandCount==1,
                "A real foreign opaque Hand dismantlement belongs to its skill initiator and actual chooser, rather than the victim.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g)=>g.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static NativeLogicalDiscardInvocationRecordedEvent[] Facts(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<NativeLogicalDiscardInvocationRecordedEvent>().ToArray();
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected genuine logical-discard command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var choice=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&choice is not null,"Missing exact published human logical-discard choice.");Accept(g,new AnswerPromptCommand(0,p.PromptId,choice!.Id,g.Revision));}
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<100;i++)
        {
            if(done())return;var p=P(g);
            if(p is{PlayerSeat:0}&&p.SkillPrompt?.SkillId==Watch)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
            else if(p is{PlayerSeat:0})throw new InvalidOperationException($"Unexpected logical-discard prompt {p.Kind}/{p.SkillPrompt?.SkillId}.");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Logical-discard fixture did not reach its bounded native boundary.");
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves the exact pending whole invoice, private prompts, movement batches and once-only completed facts.");return restored;}
    private static(GameEngine,ContentRegistry) Start()
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=3},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-native-logical-discard",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:meng-da");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"category","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"chooseCategoryAlternativeDiscard","target":"selectedTarget","amount":1,"secondaryAmount":2,"cardCategories":["trick"],"zones":["hand","equipment"]}]},
                {"id":"trim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":5,"zones":["hand"],"resultBind":"paid"},{"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"mixed","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand","equipment"],"resultBind":"paid"},{"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"foreign","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-paid-batch","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.{{Driver}}.MoveBoundCards"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
            """;
            var presentation=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="实际弃牌",description="原生整次调用"},[Watch]=new{name="真实移动子窗",description="保持原生批次",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}}});
            var catalog=SkillProgramCatalog.Load(rules,presentation);foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"整次原生弃牌"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:logical-discard-idle","固定选择","无运行能力"));
            b.AddGeneral(new(Owner,"固定原生弃牌者","supporter","fixture:logical-discard-idle","shu",8,[Driver,Watch]));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:logical-discard-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"固定其他角色","supporter","fixture:logical-discard-idle","wei",8,[]));
            b.AddDeck(new("fixture:logical-discard-deck","同质真实连弩",4,2,[]){PhysicalCards=Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"原生整次弃牌边界",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:logical-discard-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
