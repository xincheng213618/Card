using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoShuangChecks
{
    private const string Tuogu="ol:tuogu", Shan="ol:shanzhuan", Driver="fixture:csw-driver", Quiet="fixture:csw-quiet";
    private const string A="fixture:csw-original", B="fixture:csw-pile", C="fixture:csw-next", Temp="fixture:csw-temporary";
    private const string Watch="fixture:csw-watch", Cleanup="fixture:csw-cleanup", Hp="fixture:csw-hp", Foreign="fixture:csw-foreign";
    private const string Owner="fixture:csw-owner", Mode="identity:classic-cao-shuang-fixture";
    private static readonly string[] Forbidden=["fixture:csw-limited","fixture:csw-awake","fixture:csw-lord","fixture:csw-hidden"];

    public static void ActualDeceasedChoiceReplacesOnlyInheritedGrantAndCleansOldPileCold()
    {
        var(g,registry)=Start("inherit");
        foreach(var (victim,id) in new[]{(1,B),(2,A),(3,C)})
        {
            Use(g,"kill",[victim]); Reach(g,()=>Optional(P(g),Tuogu)); Activate(g);
            Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="caoshuang-tuogu")==true);
            var prompt=P(g)!;var ids=prompt.Choices.Select(c=>c.Parameters.GetValueOrDefault("chosen-skill")).ToArray();
            Need(prompt.PlayerSeat==victim && !V(g,victim).IsAlive && prompt.IsPrivate && g.CreateSnapshot(0).PendingDecision is null &&
                ids.Contains(id) && Forbidden.All(f=>!ids.Contains(f)) && !ids.Contains(Temp),
                "The real deceased chooses only eligible printed skills; limited, awakening, lord, hidden-text and later acquired skills stay outside its private prompt.");
            var before=State(g);
            Need(!g.Submit(new AnswerPromptCommand(0,prompt.PromptId,prompt.Choices.First().Id,g.Revision)).Accepted && before==State(g),
                "The receiving living owner cannot impersonate the deceased chooser.");
            g=Cold(g,registry); Answer(g,c=>c.Parameters.GetValueOrDefault("chosen-skill")==id);
            if(victim==2)
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Cleanup);
                Need(!V(g,0).Skills!.Any(s=>s.ContentId==B) && V(g,0).PublicPersistentPileCount==0 && V(g,0).PublicPersistentPileSkillId is null && (V(g,0).PublicPersistentPiles?.Count??0)==0 &&
                    g.CardMovements.Count(m=>m.Reason.Value=="skill-program.public-pile.source-lost")==1,
                    "Replacing the last inherited pile source runs native source-loss cleanup before the owning Tuogu instruction finishes.");
                g=Cold(g,registry);Continue(g);
            }
            Play(g);
            var inherited=E<CaoShuangTuoguInheritedEvent>(g).ToArray();
            Need(inherited.Length==victim && inherited.Last().SelectedSkillId==id &&
                (victim==1 || inherited.Last().PreviousGrantId==inherited[^2].GrantId) &&
                V(g,0).Skills!.Any(s=>s.ContentId==A) && V(g,0).Skills!.Any(s=>s.ContentId==id),
                "Each real inheritance owns a distinct grant, replaces the precise previous one and preserves the owner's independently printed same-name skill.");
            if(victim==1)
            {
                Use(g,"self-damage",[]);Reach(g,()=>Optional(P(g),B));Activate(g);
                Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards")==true);
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards");Play(g);
                var storage=E<PublicPileCashOutStartedEvent>(g).Single(e=>e.Operation==SkillProgramEffectOp.StoreBoundCardsInPublicPile && e.Issuer.OwnerSeat==0);
                Need(V(g,0).PublicPersistentPileSkillId==B && V(g,0).PublicPersistentPileCount==1 && storage.Pile.SkillId==B &&
                    storage.Pile.SkillInstanceId==inherited.Single().SkillInstanceId && E<PublicPileCashOutPaidEvent>(g).Single(e=>e.FrameId==storage.FrameId).FrozenCount==1,
                    "The actually borrowed skill executes and stores one actual hand entity under its exact inherited skill instance.");
            }
            _=Cold(g,registry);
        }
        Need(V(g,0).Skills!.Count(s=>s.ContentId==A)==1 && !V(g,0).Skills!.Any(s=>s.ContentId==B),
            "Replacing an inherited duplicate leaves the original printed grant visible and usable, without restoring the older borrowed pile skill.");

        var(dead,deadRegistry)=Start("dead-human");Use(dead,"self-kill",[]);
        Reach(dead,()=>P(dead) is{PlayerSeat:0} p && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="caoshuang-tuogu"));
        var actor=Enumerable.Range(1,5).Single(s=>V(dead,s).GeneralId==Owner);
        Need(!V(dead,0).IsAlive && E<ProgramBindingStartedEvent>(dead).Any(e=>e.SkillId==Tuogu && e.OwnerSeat==actor),
            "A real AI Cao Shuang elects Tuogu with its positive optional policy and gives the choice to the dead human.");
        dead=Cold(dead,deadRegistry);Answer(dead,c=>c.Parameters.GetValueOrDefault("chosen-skill")==A);
        Need(E<CaoShuangTuoguInheritedEvent>(dead).Single().OwnerSeat==actor && V(dead,actor).Skills!.Any(s=>s.ContentId==A),
            "The dead human's accepted actual command grants the chosen printed skill to the distinct living AI owner.");
        _=Cold(dead,deadRegistry);
    }

    public static void ActualPhysicalPlacementColorsOriginalDelayedTransferAndJudgmentCold()
    {
        foreach(var scenario in new[]{"red","black","printed"})
        {
            var(g,registry)=Start(scenario);var physical=V(g,1).Hand.First();var uses=E<CardUseDeclaredEvent>(g).Count();
            Use(g,"damage",[1]);Reach(g,()=>Optional(P(g),Shan));Activate(g);
            Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="caoshuang-shanzhuan")==true);
            Need(P(g) is{PlayerSeat:0,IsPrivate:true} p && p.ValidCardIds.Count==0 && p.Choices.All(c=>c.Cards.Count==0) &&
                p.Choices.All(c=>!c.Parameters.ContainsKey("card-id")) && g.CreateSnapshot(1).PendingDecision is null,
                "Shanzhuan publishes anonymous foreign hand slots, without exposing hidden entity IDs, ranks, suits or names.");
            g=Cold(g,registry);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            var placed=E<CaoShuangShanzhuanPlacedEvent>(g).Single();
            var kind=scenario=="black"?CardKind.SupplyShortage:CardKind.Indulgence;
            Need(placed.CardId==physical.Id && placed.EffectiveKind==kind && V(g,1).Judgment.Single().Id==physical.Id &&
                V(g,1).Judgment.Single().Kind==kind && E<CardUseDeclaredEvent>(g).Count()==uses &&
                !E<CardActionAcceptedEvent>(g).Any(e=>e.Action.PhysicalCards.Any(c=>c.CardId==physical.Id)),
                "One actual hand entity enters Judgment with red/black conversion or its original delayed identity; placement creates no Use/action.");
            g=Cold(g,registry);Continue(g);Play(g);
            Need(g.CardMovements.Count(m=>m.CardId==physical.Id&&m.Reason.Value=="skill-program.caoshuang.shanzhuan.place")==1,
                "Cold movement-child return does not place the entity or issue its invoice twice.");
            if(scenario=="black")
            {
                Use(g,"move-judgment-here",[1]);Reach(g,()=>P(g)?.Choices.Any(c=>c.Cards.Contains(physical.Id))==true);Answer(g,c=>c.Cards.Contains(physical.Id));Play(g);
                Need(V(g,0).Judgment.Single() is{Kind:CardKind.SupplyShortage} moved && moved.Id==physical.Id,
                    "A real Judgment-to-Judgment transfer retains the virtual delayed kind on the same physical entity.");
                g=Cold(g,registry);Use(g,"take-judgment",[]);Reach(g,()=>P(g)?.Choices.Any(c=>c.Cards.Contains(physical.Id))==true);Answer(g,c=>c.Cards.Contains(physical.Id));Play(g);
                Need(V(g,0).Hand.Single(c=>c.Id==physical.Id).Kind==CardKind.Dodge && V(g,0).Judgment.Count==0,
                    "Leaving Judgment clears the effective mapping and exposes the original physical kind in hand.");
            }
            else
            {
                End(g);Reach(g,()=>E<DelayedCardResolvedEvent>(g).Any(e=>e.CardId==physical.Id));
                Need(E<DelayedCardResolvedEvent>(g).Single(e=>e.CardId==physical.Id).CardKind==kind &&
                    g.CardMovements.Any(m=>m.CardId==physical.Id&&m.Reason==CardMoveReasons.DelayedCardFinish),
                    "The native delayed-judgment resolver consumes the exact placed entity using its effective delayed identity.");
            }
            _=Cold(g,registry);
        }
    }

    public static void ActualSilverLionPlacementWaitsForReplacementRecoveryCold()
    {
        var(g,registry)=Start("silver");var lion=V(g,0).Hand.First();Use(g,"equip",[1],[lion.Id]);Play(g);End(g);
        Reach(g,()=>Optional(P(g),Foreign));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Targets.SequenceEqual([1]));
        Reach(g,()=>Optional(P(g),Shan));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Cards.SequenceEqual([lion.Id]))==true);Answer(g,c=>c.Cards.SequenceEqual([lion.Id]));
        Reach(g,()=>P(g)?.Kind==DecisionKind.RecoveryReplacement);
        var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.CaoShuangShanzhuan is not null);
        Need(g.ResolutionStack.OfType<RecoveryReplacementFrame>().Single().Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion &&
            V(g,1).Hp==5 && V(g,1).Judgment.Single().Id==lion.Id && E<CaoShuangShanzhuanPlacedEvent>(g).Count()==1,
            "An actual equipped Silver Lion is already placed and its real removal recovery waits on the exact paid Shanzhuan frame.");
        g=Cold(g,registry);Answer(g,c=>c.Parameters.GetValueOrDefault("recovery-action")=="keep");
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Hp);Need(V(g,1).Hp==6,"The native removal heal occurs once before its actual HP observer returns.");
        g=Cold(g,registry);Continue(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,registry);Continue(g);
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Foreign && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("result-bind")=="tail"));
        Need(!g.ResolutionStack.Any(f=>f.Id==root.Id) && E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==1)==1 &&
            E<CaoShuangShanzhuanPlacedEvent>(g).Count()==1 && g.CardMovements.Count(m=>m.CardId==lion.Id&&m.Reason.Value=="skill-program.caoshuang.shanzhuan.place")==1,
            "Cold recovery-choice, HP and placement children return once without repeating Silver Lion's heal or placement cost.");
        _=Cold(g,registry);
    }

    public static void ActualEndingDrawUsesDealtDamageIncludingSelfAndPrevention()
    {
        foreach(var scenario in new[]{"ending-none","ending-self","ending-prevent"})
        {
            var(g,registry)=Start(scenario);
            if(scenario=="ending-self"){Use(g,"self-damage",[]);Play(g);}
            if(scenario=="ending-prevent"){Use(g,"damage",[1]);Play(g);Need(!E<DamageAppliedEvent>(g).Any(),"The native prevention fixture prevents this actual attempt.");}
            var hand=V(g,0).Hand.Count;End(g);
            if(scenario=="ending-self")
            {
                Reach(g,()=>g.State.CurrentSeat!=0);
                Need(!E<ProgramBindingStartedEvent>(g).Any(e=>e.SkillId==Shan&&e.BindingId=="ending-without-actual-damage"),
                    "Genuine self damage still counts as damage dealt and suppresses the ending draw.");
            }
            else
            {
                Reach(g,()=>Optional(P(g),Shan));Need(g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(),"The draw belongs to the actual ending phase.");
                g=Cold(g,registry);Activate(g);Reach(g,()=>g.State.CurrentSeat!=0);
                Need(E<ProgramBindingStartedEvent>(g).Count(e=>e.SkillId==Shan&&e.BindingId=="ending-without-actual-damage")==1 && V(g,0).Hand.Count==hand+1,
                    "No actual damage, including a fully prevented attempt, earns one real optional ending draw.");
            }
            _=Cold(g,registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,6).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string a,IReadOnlyList<int> targets,IReadOnlyList<int>? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,a,cards??[],targets,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<400;i++)
        {
            if(done())return;
            if(P(g)is{PlayerSeat:0} p)
            {
                if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else if(p.Kind is DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" or "take-damage");
                else throw new InvalidOperationException("Unexpected actual Cao Shuang prompt: "+JsonSerializer.Serialize(p));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The bounded actual Cao Shuang boundary was not reached: "+JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Cao Shuang command.");}
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,6).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Private choices, exact owning frames, grants, physical movement, events and commands cold-restore identically.");return cold;}
    private static string Embedded(string s){using var stream=typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-cao-shuang.{s}")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    private static(GameEngine,ContentRegistry)Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=6,HumanSeat=0,HumanRole=scenario=="dead-human"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=18},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});
        Accept(g,new SelectGeneralCommand(0,scenario=="dead-human"?"fixture:csw-peer-1":Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-cao-shuang",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            string Active(string id,string effects,int targets=0,int cards=0,string extra="")=>$"{{\"id\":\"{id}\",\"minCards\":{cards},\"maxCards\":{cards},\"minTargets\":{targets},\"maxTargets\":{targets},\"targetKind\":\"otherLiving\",\"usesPerTurn\":null{extra},\"effects\":{effects}}}";
            var defs=new List<string>{$$"""
              {"id":"{{Driver}}","revision":1,"activations":[
                {{Active("kill","[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}]",1)}},
                {{Active("self-kill","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}]")}},
                {{Active("damage","[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]",1)}},
                {{Active("self-damage","[{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]")}},
                {{Active("equip","[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}]",1,1,",\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"]")}},
                {{Active("move-judgment-here","[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"selectedTargetCorrespondingZone\",\"targetRef\":{\"kind\":\"owner\"},\"awaitMovementTriggers\":true}]",1)}},
                {{Active("take-judgment","[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}]")}}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{A}}","revision":1,"activations":[{{Active("draw","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]")}}]},
              {"id":"{{C}}","revision":1,"activations":[{{Active("draw","[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]")}}]},
              {"id":"{{Temp}}","revision":1,"modifiers":[{"id":"temporary","query":"handLimit","operation":"add","value":1,"priority":0}]},
              {"id":"fixture:csw-acquire","revision":1,"triggers":[{"id":"later-acquired","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Temp}}"]}]}]},
              {"id":"{{B}}","revision":1,"triggers":[{"id":"borrowed-pile","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","optional":true,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand","equipment"],"resultBind":"borrowed-card"},{"op":"storeBoundCardsInPublicPile","target":"owner","sourceBind":"borrowed-card"}]}]},
              {"id":"{{Cleanup}}","revision":1,"triggers":[{"id":"old-source-cleanup","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.public-pile.source-lost"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cleanup","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-placement","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.caoshuang.shanzhuan.place"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"native-removal-heal","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            """};
            if(scenario=="silver")defs.Add($$"""{"id":"{{Foreign}}","revision":1,"triggers":[{"id":"actual-foreign-damage","window":"judgmentPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1},{"op":"chooseOption","target":"owner","resultBind":"tail","options":[{"id":"continue"}]}]}]}""");
            if(scenario=="silver")defs.Add("""{"id":"fixture:csw-recovery","revision":1,"cardPolicies":[{"id":"real-lord-recovery","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}""");
            if(scenario=="ending-prevent")defs.Add("""{"id":"fixture:csw-prevent","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"preventCurrentDamageAndDrawMultiple","target":"owner","amount":1}]}]}""");
            var ids=new[]{Driver,Quiet,A,B,C,Temp,"fixture:csw-acquire",Watch,Cleanup,Hp}.ToList();if(scenario=="silver")ids.AddRange([Foreign,"fixture:csw-recovery"]);if(scenario=="ending-prevent")ids.Add("fixture:csw-prevent");
            var labels=ids.ToDictionary(id=>id,id=>new{name=id,description="实际曹爽规则驱动",optionLabels=id is Watch or Cleanup or Hp or Foreign?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()});
            Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
            foreach(var(id,tag)in new[]{(Forbidden[0],SkillTag.Limited),(Forbidden[1],SkillTag.Awakening),(Forbidden[2],SkillTag.Lord),(Forbidden[3],SkillTag.None)})b.AddSkill(new(id,id,id==Forbidden[3]?"包含隐匿的技能":"受排除的印刷技能"){Tags=tag});
            b.AddSkill(new("fixture:csw-owner-pick","固定曹爽","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,r=>r==Role.Lord?100000d:-100000d)});
            b.AddSkill(new("fixture:csw-peer-pick","固定其他","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,r=>r==Role.Lord?-100000d:100000d)});
            var own=new List<string>{Tuogu,Shan,A,Cleanup,Driver};if(scenario=="dead-human")own.Add(Quiet);if(scenario=="silver")own.AddRange([Foreign,"fixture:csw-recovery"]);
            b.AddGeneral(new(Owner,"曹爽真实拥有者","ol-cao-shuang","fixture:csw-owner-pick","wei",8,own){InitialHp=scenario=="silver"?1:8});
            var peerSkills=new List<string>{A,B,C,Driver,"fixture:csw-acquire"};peerSkills.AddRange(Forbidden);peerSkills.Add(Quiet);if(scenario!="inherit"&&scenario!="dead-human")peerSkills.Add(Watch);if(scenario=="silver")peerSkills.Add(Hp);if(scenario=="ending-prevent")peerSkills.Add("fixture:csw-prevent");
            // The human peer needs its driver; other copies are deterministic quiet actors.
            var peers=Enumerable.Range(1,5).Select(i=>$"fixture:csw-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"实际印刷技能角色","supporter","fixture:csw-peer-pick","wei",6,scenario=="dead-human"&&id==peers[0]?peerSkills.Where(s=>s!=Quiet).ToArray():peerSkills));
            if(scenario=="silver")b.AddCard(new("fixture:csw-lion","白银狮子","装备牌","实际移除回复",CardKind.SilverLion));
            var card=scenario=="silver"?"fixture:csw-lion":scenario=="printed"?"standard:indulgence":"standard:dodge";
            var suit=scenario=="red"?Suit.Heart:Suit.Club;
            b.AddDeck(new("fixture:csw-deck","固定实际实体",2,0,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(card,suit,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"曹爽实际命令",6,6,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),5}},"fixture:csw-deck",GeneralCandidateCount:6,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
