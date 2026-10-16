using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HanFuChecks
{
    private const string Shuzi="ol:shuzi",Kuangshou="ol:kuangshou",Owner="fixture:hf-owner",Driver="fixture:hf-driver",Gain="fixture:hf-gain",Peer="fixture:hf-peer-gain",DiscardChild="fixture:hf-discard-child",Health="fixture:hf-health",Mode="identity:classic-han-fu-fixture";
    private const string Gift="skill-program.han-fu.shuzi.gift",Returned="skill-program.han-fu.shuzi.return",Board="skill-program.han-fu.shuzi.board",Draw="skill-program.han-fu.kuangshou.draw",Discard="skill-program.han-fu.kuangshou.discard";
    public static void ShuziHandExchangePrivateChooserAndNamesCold()
    {
        foreach(var scenario in new[]{"plain","identity","view-as"})
        {
            var(g,r)=Start(scenario);var original=V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray();var hand=V(g,0).HandCount;var other=V(g,1).HandCount;var hp=V(g,1).Hp;
            Begin(g,1,original);Reach(g,()=>Branch(g,"return"));var root=Root(g,SkillProgramEffectOp.ResolveShuziExchange);var p=P(g)!;
            Need(p is{PlayerSeat:1,IsPrivate:true}&&p.Choices.Count==other+2&&p.Choices.All(c=>c.Cards.Count==1&&V(g,1).Hand.Any(h=>h.Id==c.Cards[0]))&&
                Enumerable.Range(0,4).Where(s=>s!=1).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&V(g,0).HandCount==hand-2&&root.HanFu!.Gifts!.Select(m=>m.CardId).SequenceEqual(original),
                "Two real original hand entities are given before only the actual recipient privately chooses one current hand entity to return.");
            var before=State(g);Need(!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"The original giver cannot forge the recipient's concealed return choice.");
            g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));
            if(scenario=="identity")
            {
                Play(g);var given=E<HanFuMaterialPaidEvent>(g).Where(e=>e.FrameId==root.Id&&e.Stage==HanFuStage.GiftChildren).ToArray();var returned=E<HanFuMaterialPaidEvent>(g).Single(e=>e.FrameId==root.Id&&e.Stage==HanFuStage.ReturnChildren);
                Need(given.All(e=>e.Material is{Name:CardKind.Slash,PrintedKind:CardKind.Crossbow})&&returned.Material.Name==CardKind.Crossbow&&E<ShuziDamageIssuedEvent>(g).All(e=>e.FrameId!=root.Id)&&V(g,1).Hp==hp,
                    "Gift names freeze the giver's real hand identity before transfer; the receiver returns an actual Crossbow, and its later identity under the giver cannot create a false name match.");
            }
            else
            {
                Reach(g,()=>Branch(g,"damage"));Need(!Branch(g,"board")&&V(g,0).HandCount==hand-1,"A matching return offers the real one-damage bonus, but an empty public field cannot fabricate a board move.");
                if(scenario=="view-as")Need(E<HanFuMaterialPaidEvent>(g).Where(e=>e.FrameId==root.Id).All(e=>e.Material.Name==CardKind.Crossbow),"Optional Crossbow-to-Slash view-as choices do not change actual entity names.");
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="damage");Play(g);
                Need(V(g,1).Hp==hp-1&&E<ShuziDamageIssuedEvent>(g).Count(e=>e.FrameId==root.Id)==1&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==1),"The accepted exact name match performs one genuine native sourced damage and returns to the same paid exchange.");
            }
            Need(E<HanFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1&&g.CardMovements.Count(m=>original.Contains(m.CardId)&&m.Reason.Value==Gift)==2,"Cold continuation never pays a gifted entity twice.");
            before=State(g);Need(!g.Submit(new UseProgramSkillCommand(0,Shuzi,"gift-two-return-one",V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray(),[1],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==before,"Shuzi cannot activate twice in one real Play phase.");_=Cold(g,r);
        }
    }
    public static void ShuziEquipmentSelfAndPublicCorrespondingZonesCold()
    {
        {
            var(g,r)=Start();var cards=V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray();var count=g.CardMovements.Count;var hp=V(g,0).Hp;
            Begin(g,0,cards);Reach(g,()=>Branch(g,"return"));var root=Root(g,SkillProgramEffectOp.ResolveShuziExchange);
            Need(P(g)!.PlayerSeat==0&&g.CardMovements.Count==count&&root.HanFu!.Movements is[{ActualCount:0}],"The official any-person target includes self; hand-to-the-same-hand satisfies the gift without inventing a loss or gain.");
            g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([cards[0]]));Reach(g,()=>Branch(g,"damage"));Need(g.CardMovements.Count==count,"A genuine same-hand return also pays zero physical moves.");
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="damage");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);g=Cold(g,r);Continue(g);Reach(g,()=>Branch(g,"discard"));Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(V(g,0).Hp==hp-1&&E<ShuziDamageIssuedEvent>(g).Count(e=>e.FrameId==root.Id)==1&&E<HanFuStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard)==1&&E<HanFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1,
                "Self-targeted Shuzi uses real self-damage and drains the nested genuine Kuangshou draw/discard before its original paid activation completes.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("lion");Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Use(g,"lose",[0]);Play(g);var hp=V(g,0).Hp;var lion=V(g,0).Equipment.Single().Id;var hand=V(g,0).Hand[0].Id;
            Begin(g,0,[lion,hand]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);var root=Root(g,SkillProgramEffectOp.ResolveShuziExchange);
            Need(V(g,0).Hp==hp+1&&root.HanFu!.Movements is[{ActualCount:1}]&&g.CardMovements.Count(m=>m.CardId==lion&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Hand(0)&&m.Reason.Value==Gift)==1&&
                !g.CardMovements.Any(m=>m.CardId==hand&&m.Reason.Value==Gift),"Self-targeting may really give equipped Silver Lion to hand and resolve its native removal recovery while the accompanying own hand card remains a no-op.");
            g=Cold(g,r);Continue(g);Reach(g,()=>Branch(g,"return"));Answer(g,c=>c.Cards.SequenceEqual([lion]));Reach(g,()=>Branch(g,"skip"));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");Play(g);_=Cold(g,r);
        }
        foreach(var scenario in new[]{"judgment","own-board"})
        {
            var(g,r)=Start(scenario);var source=scenario=="judgment"?2:1;Native(g,a=>a.Kind==LegalActionKind.Indulgence&&a.TargetSeat==source);Play(g);var card=V(g,source).Judgment.Single().Id;
            Begin(g,1,V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray());Reach(g,()=>Branch(g,"return"));g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));Reach(g,()=>Branch(g,"skip"));
            if(source==1){Need(!Branch(g,"board"),"A public delayed trick already in the original recipient's judgment area cannot be moved out and back as a Shuzi board payment.");Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");}
            else
            {
                Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="board");Need(P(g)!.Choices.Where(c=>c.Cards.Count>0).All(c=>c.Cards.SequenceEqual([card])),"Only the actual foreign public judgment entity is eligible; private hands are never board materials.");
                g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([card]));Play(g);Need(V(g,1).Judgment.Any(c=>c.Id==card&&c.Kind==CardKind.Indulgence)&&V(g,2).Judgment.Count==0&&g.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Judgment(2)&&m.To==CardLocation.Judgment(1)&&m.Reason.Value==Board)==1,
                    "The native delayed trick keeps its effective judgment identity when moved exactly once into the original recipient's corresponding area.");
            }
            Play(g);_=Cold(g,r);
        }
    }
    public static void KuangshouActualDamageDistinctCutoffAndDiscardCold()
    {
        foreach(var scenario in new[]{"quota","late-quota"})
        {
            var(g,r)=Start(scenario);Use(g,"one",[1]);Play(g);Use(g,"one",[1]);Play(g);Use(g,"lose",[3]);Play(g);
            if(scenario=="quota"){Use(g,"kill",[2]);Play(g);Need(!V(g,2).IsAlive,"The dead-person count scenario uses a real lethal damage and native death cleanup.");}
            Use(g,"draw");Play(g);var before=V(g,0).HandCount;Use(g,"incoming",[1]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);var root=Root(g,SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard);
            Need(root.HanFu is{DamageAmount:2,HistoryCutoff:null,RequiredDiscard:null}&&V(g,0).HandCount==before+3,"One actual two-point damage instance really draws three and leaves X unfrozen until its genuine gain child finishes.");
            g=Cold(g,r);Continue(g);Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([scenario=="quota"?3:2]));Reach(g,()=>Branch(g,"discard"));var receipt=Root(g,SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard).HanFu!;
            var expected=scenario=="quota"?new[]{0,1,2,3}:new[]{0,1,2};
            Need(receipt.DamagedSeats!.SequenceEqual(expected)&&receipt.RequiredDiscard==expected.Length&&receipt.HistoryCutoff is not null&&
                Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&P(g)!.Choices.All(c=>c.Cards.Count==1),
                "X counts actual positive-damage people once, includes this owner and a dead victim, excludes mere HP loss, and includes new real damage from Draw3's child before the quota freezes.");
            g=Cold(g,r);for(var i=0;i<expected.Length;i++){Reach(g,()=>Branch(g,"discard"));Answer(g,c=>c.Cards.Count==1);}
            if(scenario=="late-quota")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==DiscardChild);Need(Root(g,SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard).HanFu!.RequiredDiscard==3,"The exact original quota is already paid before its real discard observer begins.");
                g=Cold(g,r);Continue(g);Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([3]));
            }
            Play(g);var quota=E<KuangshouQuotaFrozenEvent>(g).Single(e=>e.FrameId==root.Id);
            Need(quota.DamagedSeatCount==expected.Length&&E<HanFuMovementIssuedEvent>(g).Single(e=>e.FrameId==root.Id&&e.Movement.Reason==Discard).Movement.ActualCount==expected.Length&&
                E<HanFuStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard)==1&&E<HanFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1,
                "Later native discard children cannot increase the already frozen quota, repeat Draw3, count damage points or strand the original actual-damage parent.");_=Cold(g,r);
        }
    }
    public static void HanFuPaidLossDeathLionOxAndNativeChildrenCold()
    {
        foreach(var scenario in new[]{"loss","death"})
        {
            var(g,r)=Start(scenario);var cards=V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray();Begin(g,1,cards);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Peer);var root=Root(g,SkillProgramEffectOp.ResolveShuziExchange);g=Cold(g,r);Continue(g);
            if(scenario=="loss")
            {
                Reach(g,()=>V(g,0).Skills!.All(s=>s.ContentId!=Shuzi)&&P(g)?.SkillPrompt?.SkillId==Peer);g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));Reach(g,()=>Branch(g,"return"));g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));Reach(g,()=>Branch(g,"skip"));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");Play(g);
            }
            else
            {
                Reach(g,()=>g.ResolutionStack.OfType<DyingFrame>().Any()||!V(g,0).IsAlive);g=Cold(g,r);Reach(g,()=>!V(g,0).IsAlive&&E<HanFuCompletedEvent>(g).Any(e=>e.FrameId==root.Id));
                Need(g.State.Winner==Winner.None&&E<PlayerDyingEvent>(g).Any()&&!E<HanFuMaterialPaidEvent>(g).Any(e=>e.FrameId==root.Id&&e.Stage==HanFuStage.ReturnChildren),"Actual non-Lord owner death drains the paid gift's dying subtree and never transfers a fresh hand card into a dead recipient.");
            }
            Need(cards.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.Reason.Value==Gift)==1)&&E<HanFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1,"Paid Shuzi costs survive source loss or death and cold replay without a second payment.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("kuang-loss");Use(g,"incoming",[1]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);var root=Root(g,SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard);g=Cold(g,r);Continue(g);
            Reach(g,()=>Option(g,"after-loss"));Need(V(g,0).Skills!.All(s=>s.ContentId!=Kuangshou),"A real Draw3 child removes the exact HP-conditional Kuangshou source.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="after-loss");Reach(g,()=>Branch(g,"discard"));g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            Need(E<HanFuMovementIssuedEvent>(g).Count(e=>e.FrameId==root.Id&&e.Movement.Reason==Draw)==1&&E<HanFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1,"The issued draw still pays its exact one-person discard after source loss, once.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("ox");Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var ox=V(g,0).Equipment.Single().Id;var grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[],g.Revision,P(g)!.PromptId));Play(g);
            Begin(g,1,V(g,0).Hand.Select(c=>c.Id).Take(2).ToArray());Reach(g,()=>Branch(g,"return"));Accept(g,new AdvanceOneStepCommand(g.Revision));Reach(g,()=>Branch(g,"board"));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="board");g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([ox]));Play(g);
            Need(V(g,1).Equipment.Any(c=>c.Id==ox)&&V(g,1).WoodenOxGrain!.Any(c=>c.Id==grain)&&(V(g,0).WoodenOxGrain?.Count??0)==0&&g.CardMovements.Any(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.WoodenOxGrain(1)),"A genuine field-to-field Wooden Ox movement carries its actual grain through the native nested movement batch.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("weapon");Use(g,"weapon");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:hf-weapon-source");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);var weapon=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;
            Need(g.CardMovements.Any(m=>m.CardId==weapon&&m.From==CardLocation.OutsideGame&&m.To==CardLocation.Equipment(0)),"The GeneralWeapon is genuinely generated by the native sampled-general command.");
            Use(g,"incoming",[1]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);Continue(g);Reach(g,()=>Branch(g,"discard"));Need(P(g)!.Choices.Any(c=>c.Cards.Contains(weapon)),"A real self-discardable GeneralWeapon remains a legal Kuangshou equipment material.");g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([weapon]));Play(g);
            Need(g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason.Value==Discard)==1&&!g.CardMovements.Any(m=>m.CardId==weapon&&m.To==CardLocation.DiscardPile),"The actual paid GeneralWeapon self-discard normalizes to OutsideGame without pretending to enter the discard pile.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g,SkillProgramEffectOp op)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.HanFu?.Op==op);
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="han-fu"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool Option(GameEngine g,string option)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")==option)==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual HanFu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var choices=p.Choices.Where(pick).ToArray();Need(V(g,p.PlayerSeat).IsHuman&&choices.Length>0,"Missing exact native human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,choices[0].Id,g.Revision));}
    private static void Continue(GameEngine g){if(V(g,P(g)!.PlayerSeat).IsHuman)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Begin(GameEngine g,int target,IReadOnlyList<int> cards)=>Accept(g,new UseProgramSkillCommand(0,Shuzi,"gift-two-return-one",cards,[target],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> pick){var a=g.GetHumanLegalActions().First(pick);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<250;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"HanFu fixture ended before native boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&V(g,p.PlayerSeat).IsHuman)
            {
                if(p.Kind==DecisionKind.PlayCard)Accept(g,new EndPlayPhaseCommand(p.PlayerSeat,g.Revision,p.PromptId));
                else if(p.SkillPrompt?.SkillId is Gain or Peer or DiscardChild or Health&&Option(g,"continue"))Continue(g);
                else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die");
                else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException("Unexpected native HanFu choice: "+Diagnostic(g));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing native HanFu boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Winner,Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Id,c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(5).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact private names, paid batches, native child cursors, quota cutoff and all four player views.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="plain")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-han-fu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:han-fu");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"activations":[
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
             {"id":"one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
             {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":6}]},
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
             {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""","""{"id":"fixture:hf-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var gainTail=scenario is"quota"or"late-quota"?",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}":scenario=="kuang-loss"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"loss\",\"options\":[{\"id\":\"after-loss\"}]}":"";
            defs.Add($$"""{"id":"{{Gain}}","revision":1,"triggers":[{"id":"real-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Draw}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{gainTail}}]}]}""");
            if(scenario=="late-quota")defs.Add($$"""{"id":"{{DiscardChild}}","revision":1,"triggers":[{"id":"late-real-damage","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","movementReasons":["{{Discard}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]}]}""");
            if(scenario is"loss"or"death")defs.Add($$"""{"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Gift}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"peer","options":[{"id":"continue"}]},{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"loseHp","target":"selectedTarget","amount":{{(scenario=="death"?20:1)}}}{{(scenario=="loss"?",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"lost\",\"options\":[{\"id\":\"after-loss\"}]}":"")}}]}]}""");
            if(scenario is"loss"or"kuang-loss")defs.Add($$"""{"id":"fixture:hf-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{(scenario=="loss"?Shuzi:Kuangshou)}}","minimumHp":{{(scenario=="loss"?17:15)}}}]}""");
            if(scenario=="identity")defs.Add("""{"id":"fixture:hf-identity","revision":1,"cardIdentities":[{"id":"real-hand-name","zones":["hand"],"inputKinds":["crossbow"],"inputSuits":[],"outputKind":"fireSlash"}]}""");
            if(scenario=="view-as")defs.Add("""{"id":"fixture:hf-view-as","revision":1,"viewAs":[{"id":"optional-name","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生韩馥夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"原生规则夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:hf-pick","固定本人选将","公开角色评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});b.AddSkill(new("fixture:hf-unused-pick","备用武将牌","不参与实际选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000d)});
            var own=new List<string>{Driver,Gain,Health,scenario=="loss"?"fixture:hf-conditional":Shuzi,scenario=="kuang-loss"?"fixture:hf-conditional":Kuangshou};if(scenario=="late-quota")own.Add(DiscardChild);if(scenario=="identity")own.Add("fixture:hf-identity");if(scenario=="view-as")own.Add("fixture:hf-view-as");
            b.AddGeneral(new(Owner,"实际韩馥","ol-han-fu","fixture:hf-pick","qun",16,own,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:hf-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter","fixture:hf-quiet","qun",12,scenario is"loss"or"death"?[Health,Peer]:[Health]));
            b.AddGeneral(new("fixture:hf-weapon-source","实际备用武将武器","supporter","fixture:hf-unused-pick","qun",4,[]));
            var kind=scenario switch{"lion"=>CardKind.SilverLion,"ox"=>CardKind.WoodenOx,"judgment"or"own-board"=>CardKind.Indulgence,"identity"or"view-as"=>CardKind.Crossbow,_=>CardKind.Slash};b.AddCard(new("fixture:hf-card","真实实体材料",kind is CardKind.SilverLion or CardKind.WoodenOx or CardKind.Crossbow?"装备牌":kind==CardKind.Indulgence?"锦囊牌":"基本牌","原生实体",kind));
            b.AddDeck(new("fixture:hf-deck","固定同质真实实体",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:hf-card",Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"韩馥真实行为边界",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Loyalist)]=1},"fixture:hf-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:hf-weapon-source"]));
        }
    }
}
