using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OldHandDispositionChecks
{
    private const string Owner="fixture:old-hand-owner",Quiet="fixture:old-hand-quiet",Driver="fixture:old-hand-driver",Mode="identity:old-hand-fixture";
    public static void ActualOldHandDiscardThenOpaqueCappedForeignCards()
    {
        var (g,registry)=Start(); var initial=V(g,0).Hand.Select(c=>c.Id).ToArray(); Play(g); End(g); Reach(g,()=>OldStage(g,"mode")); var frame=Root(g).Id;
        Answer(g,c=>Branch(c,"discard")); Reach(g,()=>OldStage(g,"target")); var r=Root(g).OldHandDisposition!;
        Need(r.ActualDiscardCount==4 && initial.All(id=>g.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.Hand(0) && m.To==CardLocation.DiscardPile)),"Cap is actual discarded old hand payment.");
        g=Cold(g,registry); Answer(g,c=>c.Targets.SequenceEqual([1])); Reach(g,()=>OldStage(g,"foreign"));
        Need(P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand").All(c=>c.Cards.Count==0),"Foreign hidden hand slots reveal no identity.");
        Answer(g,c=>Branch(c,"foreign-discard")); Reach(g,()=>OldStage(g,"foreign")); g=Cold(g,registry); Answer(g,c=>Branch(c,"finish"));
        Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==frame));
        Need(E<OldHandDispositionPaymentEvent>(g).Where(e=>e.FrameId==frame && e.ReturnStage=="foreign").Sum(e=>e.ActualCount)==1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==frame)==1,"Up-to permits a strict smaller discard and one parent return."); _=Cold(g,registry);
    }
    public static void ActualParticipantPreventionSourceDrawAndSharedTurnUsage()
    {
        var (g,registry)=Start(); Play(g); var hp=V(g,1).Hp; var hand=V(g,0).HandCount;
        Slash(g,1); Reach(g,()=>Optional(g)); g=Cold(g,registry); Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate"); Play(g);
        Need(V(g,1).Hp==hp && V(g,0).HandCount==hand+1 && E<ProgramDamagePreventedEvent>(g).Count()==1 &&
            g.CardMovements.Count(m=>m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(0) && m.Reason.Value=="skill-program.ol:xiuhao.prevented-damage-draw")==2,
            "Actual source pays Slash1 then draws exactly2, with complete prevention.");
        Slash(g,1); Play(g);
        Need(V(g,1).Hp==hp-1 && E<ProgramDamagePreventedEvent>(g).Count()==1,"Second outgoing damage in the same actual turn cannot activate either named usage branch again.");
        _=Cold(g,registry);
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T> E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.OldHandDisposition is not null);
    private static bool OldStage(GameEngine g,string stage)=>g.ResolutionStack.LastOrDefault() is ProgramSkillFrame{OldHandDisposition:not null} f && f.OldHandDisposition.Stage==stage && P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="old-hand-disposition")==true;
    private static bool Optional(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:xiuhao" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static bool Branch(PromptChoice c,string branch)=>c.Parameters.GetValueOrDefault("branch")==branch;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Need(r.Accepted,(r.Error?.Message??"Rejected actual old-hand command.")+
        (r.Accepted?"":" "+JsonSerializer.Serialize(new{Command=c,g.State.Winner,Prompt=P(g),Turns=E<TurnStartedEvent>(g),Players=Enumerable.Range(0,4).Select(s=>V(g,s)),RecentEvents=g.Events.TakeLast(24).Select(e=>new{Type=e.Payload.GetType().Name,Payload=JsonSerializer.Serialize(e.Payload,e.Payload.GetType())})})));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Slash(GameEngine g,int target){var a=g.GetHumanLegalActions().First(a=>a.CardId is not null && a.TargetSeats.SequenceEqual([target]));Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});}
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<130;i++){if(done())return;var p=P(g);
            if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed old-hand fixture did not reach expected boundary. "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack}));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact private prompt, paid cursor and all safe viewers.");return restored;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-deng-zhi."+suffix)!;using var reader=new StreamReader(s);return reader.ReadToEnd();}
    private static (GameEngine,ContentRegistry) Start(bool sourceLoss=false,bool preventionLoss=false,bool death=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(sourceLoss,preventionLoss,death));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=death?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool sourceLoss,bool preventionLoss,bool death):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("old-hand-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var production=SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json"));
            foreach(var(id,program)in production.Programs)
                b.AddSkill(new(id,production.Presentations[id].Name,production.Presentations[id].Description)
                {Program=program,ProgramPresentation=production.Presentations[id],Tags=id=="ol:sujian"?SkillTag.Locked:SkillTag.None,ExecutionForms=SkillExecutionForm.Trigger,
                 SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>-10000d)});
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"unlimited","query":"slashLimit","operation":"add","value":20,"priority":0}]}]}""";
            var quietRules=death ? rules.Replace("\"modifiers\":", "\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}],\"modifiers\":",StringComparison.Ordinal) : rules;
            var cat=SkillProgramCatalog.Load(quietRules,$$"""{"schemaVersion":{{SkillProgramCatalog.PresentationSchemaVersion}},"skills":{"{{Quiet}}":{"name":"固定真实敌方","description":"额外杀次数"} } }""");
            b.AddSkill(new(Quiet,"固定真实敌方","额外杀次数"){Program=cat.Programs[Quiet],ProgramPresentation=cat.Presentations[Quiet],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>10000d)});
            var driver=SkillProgramCatalog.Load(rules.Replace(Quiet,Driver,StringComparison.Ordinal),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,
                skills=new Dictionary<string,object>{[Driver]=new{name="真实人类杀次数",description="额外杀次数"}}}));
            b.AddSkill(new(Driver,"真实人类杀次数","额外杀次数"){Program=driver.Programs[Driver],ProgramPresentation=driver.Presentations[Driver],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>-10000d)});
            var ownerSkills=new List<string>{"ol:xiuhao","ol:sujian"};
            if(sourceLoss || preventionLoss)
            {
                const string watcher="fixture:old-hand-loss";
                var lossRules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{watcher}}","revision":1,"triggers":[{"id":"child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.old-hand-disposition"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}]}""";
                if(preventionLoss)lossRules=lossRules.Replace("\"window\":\"cardsMoved\"","\"window\":\"cardsGained\"",StringComparison.Ordinal)
                    .Replace("\"sourceZones\":[\"hand\"]","\"destinationZones\":[\"hand\"]",StringComparison.Ordinal)
                    .Replace("program.old-hand-disposition","skill-program.ol:xiuhao.prevented-damage-draw",StringComparison.Ordinal);
                var loss=SkillProgramCatalog.Load(lossRules,$$"""{"schemaVersion":{{SkillProgramCatalog.PresentationSchemaVersion}},"skills":{"{{watcher}}":{"name":"真实弃牌子响应","description":"支付后失去原技能","optionLabels":{"continue":"继续"} } } }""");
                b.AddSkill(new(watcher,"真实弃牌子响应","支付后失去素俭"){Program=loss.Programs[watcher],ProgramPresentation=loss.Presentations[watcher]});ownerSkills.Add(watcher);
                if(!death)
                {
                    const string recovery="fixture:old-hand-dying-loss";
                    var recoveryRules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{recovery}}","revision":1,"triggers":[{"id":"recover-and-lose","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{(preventionLoss?"ol:xiuhao":"ol:sujian")}}"],"sourceBind":"standard:none"}]}]}]}""";
                    var recoveryPresentation=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[recovery]=new{name="真实濒死恢复失源",description="实际恢复后失去原技能"}}});
                    var recoveryCatalog=SkillProgramCatalog.Load(recoveryRules,recoveryPresentation);
                    b.AddSkill(new(recovery,"真实濒死恢复失源","实际恢复后失去原技能"){Program=recoveryCatalog.Programs[recovery],ProgramPresentation=recoveryCatalog.Presentations[recovery]});ownerSkills.Add(recovery);
                }
            }
            b.AddGeneral(new(Owner,"真实素俭拥有者","supporter",Driver,"shu",12,ownerSkills));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:old-hand-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"真实其他角色","supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:old-hand-deck","固定全杀牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"真实旧手牌处置",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:old-hand-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
