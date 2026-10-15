using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class WangYunChecks
{
    private const string Owner="fixture:wang-yun-owner",Quiet="fixture:wang-yun-quiet",Driver="fixture:wang-yun-driver",Watch="fixture:wang-yun-watch",Loss="fixture:wang-yun-loss",Mode="identity:classic-wang-yun-fixture";
    public static void ActualRandomWeaponAndIssuedSlashDamageAwakening()
    {
        Need(!JsonSerializer.Serialize(new ProgramSkillFrame(1,0,"old","old","hash",0,[],[])).Contains("WangYun",StringComparison.Ordinal),"Old canonical frame JSON omits all new null owning receipts.");
        var ids=new List<int>{1};var origin=new WangYunNativeUseOrigin{Targets=ids};ids[0]=9;Need(origin.Targets.SequenceEqual([1]),"Native origin freezes exposed target collections at construction.");
        var(g,r)=Start("weapon-child");Play(g);var before=V(g,0).MaxHp;Plan(g);Reach(g,()=>PlanStage(g,"target"));
        var frame=Root(g);var weapon=E<WangYunNativeUseIssuedEvent>(g).Single().Origin;
        Need(weapon.Kind=="weapon" && weapon.Actor==1 && weapon.Materials.Single().From==CardLocation.DrawPile && E<WangYunNativePaymentCompletedEvent>(g).Single().Origin.PaymentCompleted && E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId==Watch),"Random weapon uses a real draw-pile entity through one actual native payment, including its genuine equipment-use trigger child.");
        Need(P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0])),"The original plan owner is a legal designated Slash target.");
        g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==frame.Id));
        var damage=E<WangYunLianjiDamageEvent>(g).Single();var slash=E<WangYunNativeUseIssuedEvent>(g).Single(e=>e.Origin.Kind=="slash").Origin;
        Need(damage.ActualActor==1 && damage.PlanOwner==0 && damage.Target==0 && damage.Amount==1 && slash.Materials.All(m=>m.From.OwnerSeat==1) &&
            E<WangYunNativeUseReturnedEvent>(g).Count(e=>e.ProgramFrameId==frame.Id)==2 && E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==frame.Id)==1,"Only a truly issued actor-owned Slash damages the designated owner and returns its paid native subtree once.");
        Play(g);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>E<WangYunAwakenedEvent>(g).Any());Play(g);
        Need(V(g,0).MaxHp==before && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:lianji") && V(g,0).Skills!.Any(s=>s.ContentId=="ol:jingong"),"The next actual preparation removes Lianji and grants current random Jingong without an invented max-HP change.");_=Cold(g,r);
    }
    public static void ActualRandomTrickSamplesColdAndNativeLightningJudgment()
    {
        var(g,r)=Start("trick");Play(g);var id=V(g,0).Hand.First(c=>c.Kind==CardKind.Slash).Id;
        var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());for(var i=0;i<3;i++)_=g.GetHumanLegalActions();Need(checkpoint==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Read-only action queries never consume random samples.");
        Accept(g,new UseProgramSkillCommand(0,"ol:jingong","three-random-tricks",[id],[],g.Revision,P(g)!.PromptId));Reach(g,()=>RootOrNull(g)?.WangYunTrick?.Stage=="choice");var f=Root(g);var samples=f.WangYunTrick!.Samples.ToArray();
        Need(samples.Length==3 && samples.Distinct().Count()==3 && samples.Contains(CardKind.Lightning) && samples.Contains(CardKind.DrawTwo) && samples.Contains(CardKind.Duel),"The small real catalog guarantees three distinct current random tricks including delayed Lightning.");
        g=Cold(g,r);Need(Root(g).WangYunTrick!.Samples.SequenceEqual(samples),"Cold replay retains the original sample ordering.");Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="lightning");Play(g);
        Need(V(g,0).Judgment.Any(c=>c.Id==id) && E<WangYunNativePaymentCompletedEvent>(g).Single().Origin.OutputKind==CardKind.Lightning && E<WangYunNativePaymentCompletedEvent>(g).Single().Origin.Materials.Single().CardId==id,"The selected real Slash material pays for native delayed Lightning placement.");
        g=Cold(g,r);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>E<LightningResolvedEvent>(g).Any());
        Need(E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0 && e.Amount==3),"The later real Lightning judgment uses effective delayed kind and applies native three damage.");_=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static ProgramSkillFrame? RootOrNull(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.WangYunPlan is not null || f.WangYunTrick is not null);
    private static ProgramSkillFrame Root(GameEngine g)=>RootOrNull(g)!;
    private static bool PlanStage(GameEngine g,string stage)=>RootOrNull(g)?.WangYunPlan?.Stage==stage && P(g)?.PlayerSeat==0;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,(x.Error?.Message??"Rejected native Wang Yun command")+" "+JsonSerializer.Serialize(P(g)));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Plan(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"ol:lianji","paid-weapon-plan",[V(g,0).Hand.First().Id],[1],g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++){if(done())return;var p=P(g);if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Bounded Wang Yun native boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s))}));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact samples, physical invoices and native children.");return restored;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-wang-yun."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static (GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("wang-yun-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var baseRegistry=ContentRegistry.Build(new StandardContentPackage());foreach(var card in baseRegistry.Cards.Values.Where(c=>scenario!="trick" || c.CategoryName!="锦囊牌" || c.LegacyKind is CardKind.DrawTwo or CardKind.Duel or CardKind.Lightning))b.AddCard(card);foreach(var skill in baseRegistry.Skills.Values)b.AddSkill(skill);
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            if(scenario is "cardless" or "cardless-actor")
            {
                string ReadLu(string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-lu-zhi-wei."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
                var current=SkillProgramCatalog.Load(ReadLu("rules.json"),ReadLu("presentation.json"));var definition=current.Programs["ol:weijing"];var presentation=current.Presentations["ol:weijing"];b.AddSkill(new("ol:weijing",presentation.Name,presentation.Description){Program=definition,ProgramPresentation=presentation});
            }
            var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"empty","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","minimumCards":4,"maximumCards":4,"zones":["hand"],"resultBind":"hand"},{"op":"moveBoundCards","target":"owner","sourceBind":"hand","destination":"discardPile","awaitMovementTriggers":true}]}]}"""};
            var skills=new List<string>(scenario=="trick"?["ol:jingong"]:["ol:lianji","ol:moucheng"]);
            if(scenario is "loss" or "death")
            {
                skills.Add(Watch);defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-cost-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.wang-yun.plan-payment"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
                if(scenario=="loss"){skills.Add(Loss);defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"real-recover-loss","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:lianji"],"sourceBind":"standard:none"}]}]}""");}
            }
            if(scenario=="weapon-child")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-weapon-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["crossbow"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]}""");
            if(scenario is "cardless" or "cardless-actor")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-zero-slash-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}""");
            if(scenario=="cardless-actor")defs.Add("""{"id":"fixture:wang-yun-native-role","revision":1,"triggers":[{"id":"replace","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"role-pause","options":[{"id":"continue"}]}]}]}""");
            var presentations=defs.Select(d=>(Id:JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(d=>d.Id,d=>new{name=d.Id,description="真实王允原生付款边界",optionLabels=d.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations})));
            b.AddGeneral(new(Owner,"真实王允拥有者","supporter",Driver,"qun",12,skills,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:wang-yun-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"安静同桌","supporter",Quiet,"wei",12,scenario=="weapon-child"?[Watch]:scenario is "cardless" or "cardless-actor"?["ol:weijing",Watch,..(scenario=="cardless-actor"?new[]{"fixture:wang-yun-native-role"}:Array.Empty<string>())]:[]));
            b.AddDeck(new("fixture:wang-yun-deck","固定真实杀与武器",4,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Spade,7)).Concat(scenario=="trick"?[]:Enumerable.Range(0,12).Select(_=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Spade,7))).ToArray()});
            b.AddMode(new(Mode,"王允真实机制夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:wang-yun-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
