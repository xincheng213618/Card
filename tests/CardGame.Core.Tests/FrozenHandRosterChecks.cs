using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class FrozenHandRosterChecks
{
    private const string Skill="ol:lanjiang",Owner="fixture:frozen-hand-owner",Quiet="fixture:frozen-hand-quiet",Driver="fixture:frozen-hand-driver",Watch="fixture:frozen-hand-watch",Loss="fixture:frozen-hand-loss",Mode="identity:classic-frozen-hand-fixture";
    public static void ActualFrozenRosterChooserOrderPrivacyAndMandatoryReward()
    {
        var(g,registry)=Start();Begin(g);var id=Root(g).Id;var r=Root(g).FrozenHandRoster!;
        Need(r.Seats.SequenceEqual([0,1,2,3]) && r.InitialOwnerHandCount==4 && P(g)!.PlayerSeat==0,"All initial equal-hand participants include self in native clockwise order.");
        var p=P(g)!;var before=State(g);Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted && before==State(g),"A different seat cannot choose on behalf of the actual participant.");
        g=Cold(g,registry);Answer(g,c=>Branch(c,"draw"));
        Reach(g,()=>Stage(g,"participant") && Root(g).FrozenHandRoster!.Cursor==1);
        Need(V(g,1).HandCount<V(g,0).HandCount && P(g)!.PlayerSeat==1 && P(g)!.Choices.Count==2,"A frozen participant remains entitled to choose after owner's previous draw makes its hand smaller.");
        Need(g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(2).PendingDecision is null,"Only the real other chooser sees its private draw decision.");
        var other=P(g)!;before=State(g);Need(!g.Submit(new AnswerPromptCommand(0,other.PromptId,other.Choices[0].Id,g.Revision)).Accepted && before==State(g),"Owner cannot answer another original participant's choice.");
        g=Cold(g,registry);Reach(g,()=>Stage(g,"damage"));
        Need(E<FrozenHandRosterParticipantAnsweredEvent>(g).Where(e=>e.FrameId==id).Select(e=>e.ChooserSeat).SequenceEqual([0,1,2,3]) &&
            E<FrozenHandRosterParticipantAnsweredEvent>(g).Count(e=>e.FrameId==id && e.Accepted)==1,"Each actual participant chooses once in original order, with only self accepting this fixed enemy fixture.");
        Need(P(g)!.Choices.Where(c=>c.Targets.Count==1).All(c=>c.Targets.SequenceEqual([0])),"Damage equality is re-evaluated from current hand counts within the frozen roster.");
        Answer(g,c=>Branch(c,"skip-damage"));Reach(g,()=>Stage(g,"reward"));
        Need(P(g)!.Choices.Count==3 && P(g)!.Choices.All(c=>Branch(c,"reward") && c.Targets.Count==1 && c.Targets[0]!=0),"The mandatory draw tail has only currently smaller original participants and no skip.");
        g=Cold(g,registry);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==id));
        Need(V(g,0).HandCount==5 && V(g,1).HandCount==5 && E<FrozenHandRosterDrawPaidEvent>(g).Count(e=>e.FrameId==id)==2 && E<FrozenHandRosterDamageIssuedEvent>(g).Count(e=>e.FrameId==id)==0 &&
            E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==id)==1,"Skipping optional damage still issues one selected mandatory reward and one final parent return.");_=Cold(g,registry);
    }
    public static void ActualNativeDamageDeathThenReevaluatedOriginalReward()
    {
        var(g,registry)=Start("target-dying");Begin(g);var id=Root(g).Id;Answer(g,c=>Branch(c,"decline"));Reach(g,()=>Stage(g,"damage"));
        Need(P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([1])) && V(g,1).Hp==1,"The fixed original participant is a real one-HP damage target.");
        g=Cold(g,registry);Answer(g,c=>c.Targets.SequenceEqual([1]));
        Reach(g,()=>Stage(g,"reward"));
        Need(!V(g,1).IsAlive && E<FrozenHandRosterDamageIssuedEvent>(g).Count(e=>e.FrameId==id)==1 && P(g)!.Choices.All(c=>c.Targets.Count==1 && c.Targets[0]!=1),
            "One native sourced damage drains real Dying/death/reward children and removes the dead original participant from later choices.");
        Need(P(g)!.Choices.All(c=>Root(g).FrozenHandRoster!.Seats.Contains(c.Targets[0]) && V(g,c.Targets[0]).HandCount<V(g,0).HandCount),"Post-death hand changes re-evaluate reward only inside original roster.");
        g=Cold(g,registry);Answer(g,_=>true);Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==id));
        Need(E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==id)==1 && E<FrozenHandRosterDrawPaidEvent>(g).Count(e=>e.FrameId==id && e.ReturnStage=="complete")==1,"Paid damage and reward return their exact native parent once.");_=Cold(g,registry);
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.FrozenHandRoster is not null);
    private static bool Stage(GameEngine g,string s)=>g.ResolutionStack.LastOrDefault() is ProgramSkillFrame{FrozenHandRoster:not null}f && f.FrozenHandRoster.Stage==s && P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="frozen-hand-roster")==true;
    private static bool Branch(PromptChoice c,string b)=>c.Parameters.GetValueOrDefault("branch")==b;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Need(r.Accepted,(r.Error?.Message??"Rejected real roster command.")+(r.Accepted?"":" "+Diagnostic(g)));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Begin(GameEngine g){Play(g);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Skill && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Stage(g,"participant"));}
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<160;i++){if(done())return;var p=P(g);
            if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Bounded fixed roster fixture did not reach expected boundary. "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.Winner,Prompt=P(g),Frames=g.ResolutionStack,Turns=E<TurnStartedEvent>(g),Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(16).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves every private actual-chooser prompt, frozen roster and once-paid native child cursor.");return restored;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-wu-yan."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static (GameEngine,ContentRegistry)Start(string scenario="normal")
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario.EndsWith("death",StringComparison.Ordinal)?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6},registry);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,registry);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("frozen-hand-roster-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog cat){foreach(var(id,program)in cat.Programs)b.AddSkill(new(id,cat.Presentations[id].Name,cat.Presentations[id].Description){Program=program,ProgramPresentation=cat.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"distance","query":"outgoingDistance","operation":"add","value":-1,"priority":0}]}"""};
            var skills=new List<string>{Skill};
            if(scenario.StartsWith("source-",StringComparison.Ordinal))
            {
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.frozen-hand-roster.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");skills.Add(Watch);
                if(scenario=="source-loss")
                {
                    defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"recover-and-lose","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Skill}}"],"sourceBind":"standard:none"}]}]}""");skills.Add(Loss);
                }
            }
            var presentations=defs.Select(d=>(Id:JsonSerializer.Deserialize<JsonElement>(d).GetProperty("id").GetString()!,HasChoice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(d=>d.Id,d=>new{name=d.Id,description="真实冻结名单子窗夹具",optionLabels=d.HasChoice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations})));
            b.AddGeneral(new(Owner,"真实澜疆拥有者","supporter",Driver,"wu",scenario=="damage-death"?1:12,skills));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:frozen-hand-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"真实原始参与者","supporter",Quiet,"wei",scenario=="target-dying"?1:12));
            b.AddDeck(new("fixture:frozen-hand-deck","固定初始四牌",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard(scenario=="outsiders"?"standard:draw_two":"standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"冻结原始手牌名单",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:frozen-hand-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
