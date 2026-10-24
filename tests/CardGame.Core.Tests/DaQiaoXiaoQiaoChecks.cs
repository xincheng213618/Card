using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DaQiaoXiaoQiaoChecks
{
    private const string Owner="fixture:dqxq-owner",Driver="fixture:dqxq-driver",Quiet="fixture:dqxq-quiet",Watch="fixture:dqxq-watch",Health="fixture:dqxq-health",Source="fixture:dqxq-source",Mode="identity:classic-da-qiao-xiao-qiao",Xingwu="ol:xingwu",Luoyan="ol:luoyan",Tianxiang="boundary:tianxiang",Liuli="classic:liuli";
    public static void PublicPileDerivedSourcesZeroTransitionsAndCold()
    {
        foreach(var scene in new[]{"pile","independent"})
        {
            var(g,r)=Start(scene);Need(Has(g,Tianxiang)==(scene=="independent")&&!Has(g,"boundary:hongyan"),"No empty-pile derivative or unrelated Hongyan is fabricated.");
            var ids=new List<int>();for(var n=0;n<3;n++)
            {
                Begin(g);var p=P(g)!;var state=State(g);Need(p.IsPrivate&&g.CreateSnapshot(1).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"Only the owner answers the true private deposit prompt.");
                var id=P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="deposit").Cards.Single();ids.Add(id);g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([id]));
                if(scene=="pile"&&n==0){Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:dqxq-derived-watch");Need(g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w=>w.Window==SkillProgramTriggerWindow.SkillsChanged&&w.ResumeProgramFrameId==g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DaQiaoXiaoQiao is not null).Id),"The first native derived grant opens a real ownership-change child on the original Xingwu frame.");g=Cold(g,r);Continue(g);}
                Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.Burst));
                Need(Pile(g).Order().SequenceEqual(ids.Order()),"The public native pile projection contains the exact stored entities: "+JsonSerializer.Serialize(new{scene,n,Expected=ids,Actual=Pile(g),V(g).PublicPersistentPileSkillId,V(g).PublicPersistentPileCount}));
                Need(Has(g,Tianxiang)&&Has(g,Liuli)&&!Has(g,"boundary:hongyan"),"Nonzero pile presence qualifies both exact linked skills and no Hongyan: "+JsonSerializer.Serialize(V(g).Skills!.Select(s=>s.ContentId)));
                Need(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To.Zone==CardZoneKind.PublicPersistentPile)==1,"The exact original hand entity is deposited once: "+JsonSerializer.Serialize(g.CardMovements.Where(m=>m.CardId==id)));
                if(n<2){Branch(g,"finish");var turn=g.State.TurnNumber;NextPlay(g,turn);}else{Branch(g,"pile");foreach(var cost in ids)Answer(g,c=>c.Cards.SequenceEqual([cost]));Target(g,1);Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==3);}
            }
            Need(Pile(g).Length==0&&Has(g,Tianxiang)==(scene=="independent")&&Has(g,Liuli)==(scene=="independent")&&E<DaQiaoXiaoQiaoLuoyanChangedEvent>(g).Count(e=>e.Added)==2&&E<DaQiaoXiaoQiaoLuoyanChangedEvent>(g).Count(e=>!e.Added)==2&&E<DaQiaoXiaoQiaoMovementPaidEvent>(g).Count(e=>e.Stage==DaQiaoXiaoQiaoStage.CostChildren&&e.Count==3)==1,"Removing the exact last three cards revokes only Luoyan-derived sources, retaining independent same-name ownership.");
            Need(E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==2),"The actual three-card branch reaches the male target's native two-point damage.");_=Cold(g,r);
        }
    }
    public static void BothCostsNativeTurnoverGenderDamageAndCold()
    {
        foreach(var scene in new[]{"male","female"})
        {
            var(g,r)=Start(scene);var hand=V(g).HandCount;Begin(g);Branch(g,"skip-deposit");Need(Pile(g).Length==0,"Declining deposit does not invent a card or suppress the independent second optional item.");Branch(g,"turnover");Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.HandCards));
            Need(V(g).IsFaceDown&&E<DaQiaoXiaoQiaoTurnedEvent>(g)is[{FaceDown:true}],"The alternate cost is a real native turnover before the two-card private payment.");
            Need(P(g)is{RequiredCardCount:2,MinimumCardCount:2}&&P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("owned-selection-role")=="pick"),"The actual two-Hand discard is one complete owned selection, while the separate public-pile branch stays outside this discard projection.");
            var beforeSelection=g.CardMovements.Count;var cards=P(g)!.Choices.Where(c=>c.Cards.Count==1).Take(2).Select(c=>c.Cards[0]).ToArray();
            Answer(g,c=>c.Cards.SequenceEqual([cards[0]]));
            Need(P(g)is{RequiredCardCount:1,MinimumCardCount:1}&&Current(g)!.Selected.Select(c=>c.Id).SequenceEqual([cards[0]])&&g.CardMovements.Count==beforeSelection&&P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("owned-selection-selected")==cards[0].ToString()),"A native first Hand pick remains unpaid and highlighted on its original Xingwu frame.");
            g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([cards[1]]));
            Need(Stage(g,DaQiaoXiaoQiaoStage.Target)&&g.CardMovements.Count==beforeSelection,"Finishing the two-card native collection keeps both materials unpaid until the original target choice.");
            Target(g,1);Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==1);
            var amount=scene=="female"?1:2;Need(V(g).HandCount==hand-2&&cards.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.da-qiao-xiao-qiao.xingwu.hand-discard")==1)&&E<DamageAppliedEvent>(g)is[{SourceSeat:0,TargetSeat:1,Amount:var actual}]&&actual==amount&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g).Single().Amount==amount,"Two actual hand cards are paid together once; current male and female recipients take native two and one respectively.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("pile");Begin(g);Branch(g,"skip-deposit");Need(P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="pile"),"An empty pile cannot pay the three-card branch.");g=Cold(g,r);Branch(g,"finish");Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==1);Need(E<DamageAppliedEvent>(g).Length==0&&E<DaQiaoXiaoQiaoMovementPaidEvent>(g).Length==0&&!V(g).IsFaceDown,"Declining both optional items pays nothing and causes no damage.");_=Cold(g,r);
        }
    }
    public static void EquipmentStripLionOxAndPaidChildrenCold()
    {
        var(g,r)=Start("equipment");Use(g,"draw");Play(g);var ox=V(g).Hand.First(c=>c.Kind==CardKind.WoodenOx).Id;Use(g,"place",[0],[ox]);Play(g);
        var grain=V(g).Hand.First(c=>c.Kind!=CardKind.SilverLion).Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain],[1],g.Revision,P(g)!.PromptId));Play(g);var lion=V(g).Hand.First(c=>c.Kind==CardKind.SilverLion).Id;Use(g,"place",[1],[lion]);Play(g);Use(g,"wound",[1]);Play(g);var hp=V(g,1).Hp;
        Need(V(g,1).Equipment.Any(c=>c.Id==lion)&&V(g,1).Equipment.Any(c=>c.Id==ox)&&V(g,1).WoodenOxGrainCount==1,"Native equipment placement and the actual Wooden Ox command establish real target equipment and hidden grain.");
        Begin(g);Branch(g,"skip-deposit");Branch(g,"turnover");Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.HandCards));ChooseTwo(g);Target(g,1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);
        Need(Current(g)is{Stage:DaQiaoXiaoQiaoStage.EquipmentChildren}&&V(g,1).Equipment.Count==0&&V(g,1).WoodenOxGrainCount==0&&V(g,1).Hp==hp+1&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g).Length==0&&g.CardMovements.Any(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(1)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard),"All target equipment and its genuine Ox grain leave first; Silver Lion's native recovery child pauses before skill damage.");g=Cold(g,r);Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==1);
        Need(V(g,1).Hp==hp-1&&E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==1)==1&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g)is[{TargetSeat:1,Amount:2}]&&g.CardMovements.Count(m=>m.CardId==lion&&m.Reason.Value=="skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard")==1&&g.CardMovements.Count(m=>m.CardId==ox&&m.Reason.Value=="skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard")==1,"After every native equipment child, male damage is truly two, uncapped by the already removed Lion, with no repeated equipment or grain payment.");_=Cold(g,r);
        Need(E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.InitiatorSeat==0&&e.ExecutorSeat==0&&e.SubjectSeat==1&&e.ActualCount==2&&e.HandCount==0&&e.Reason.Value=="skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard"),"The whole equipment strip is one genuine logical discard by the skill user on the target's equipment, excluding Ox grain spill from its primary cost.");
        {
            (g,r)=Start("equipment");Use(g,"draw");Play(g);lion=V(g).Hand.First(c=>c.Kind==CardKind.SilverLion).Id;Use(g,"place",[0],[lion]);Play(g);Use(g,"lose");Play(g);var before=V(g).Hp;Begin(g);Answer(g,c=>c.Cards.SequenceEqual([lion]));Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g).Hp==before+1&&Pile(g).Contains(lion)&&Current(g)?.Stage==DaQiaoXiaoQiaoStage.DepositChildren,"The first optional item can store a genuine owned equipment entity; its native Lion recovery remains a real child.");g=Cold(g,r);Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.Burst));Branch(g,"finish");Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==1);_=Cold(g,r);
        }
        {
            (g,r)=Start("equipment-death");var recipient=Enumerable.Range(1,3).Single(s=>V(g,s).Skills!.Any(k=>k.ContentId==Health));Use(g,"draw");Play(g);lion=V(g).Hand.First(c=>c.Kind==CardKind.SilverLion).Id;Use(g,"place",[recipient],[lion]);Play(g);Use(g,"wound",[recipient]);Play(g);
            Need(V(g,recipient).Hp==V(g,recipient).MaxHp-2&&V(g,recipient).Equipment.Any(c=>c.Id==lion)&&V(g,recipient).Skills!.Any(s=>s.ContentId==Health),"The publicly identified real recovery observer owns the genuine wounded Lion before payment: "+JsonSerializer.Serialize(new{recipient,V(g,recipient).Hp,V(g,recipient).MaxHp}));
            Begin(g);Branch(g,"skip-deposit");Branch(g,"turnover");Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.HandCards));ChooseTwo(g);Target(g,recipient);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);
            Need(Current(g)is{Stage:DaQiaoXiaoQiaoStage.EquipmentChildren}paid&&paid.TargetSeat==recipient&&E<DaQiaoXiaoQiaoMovementPaidEvent>(g).Any(e=>e.Stage==DaQiaoXiaoQiaoStage.CostChildren&&e.Count==2)&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g).Length==0,"The attack has fully paid its exact two-hand cost and frozen its target before the real equipment recovery child.");g=Cold(g,r);Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Length==1);
            Need(!V(g).IsAlive&&V(g,recipient).IsAlive&&V(g,recipient).Equipment.Count==0&&E<PlayerDyingEvent>(g).Any(e=>e.VictimSeat==0)&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==recipient&&e.Amount==2)&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g)is[{Amount:2}issued]&&issued.TargetSeat==recipient&&g.CardMovements.Count(m=>m.CardId==lion&&m.Reason.Value=="skill-program.da-qiao-xiao-qiao.xingwu.equipment-discard")==1,"A native recovery child really kills the original non-Lord source; its fully paid fixed-target attack still finishes native damage once without repeating equipment payment.");_=Cold(g,r);
        }
    }
    public static void LinkedTianxiangLiuliAndPaidSourceLossDeathCold()
    {
        foreach(var branch in new[]{"damage-draw","lose-hp-gift"})
        {
            var(g,r)=Start("linked");DepositAndFinish(g);var turn=g.State.TurnNumber;NextPlay(g,turn);var hp=V(g).Hp;Use(g,"incoming-damage",[1]);Reach(g,()=>Offer(g,Tianxiang));Activate(g);Reach(g,()=>P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="suit-prevention-benefit"));
            Need(P(g)!.Choices.Where(c=>c.Cards.Count==1).All(c=>V(g).Hand.Single(h=>h.Id==c.Cards[0]).Suit==Suit.Heart),"Luoyan grants current Tianxiang without Hongyan: real spade cards remain ineligible for its heart payment.");var cost=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([cost]));Answer(g,c=>c.Targets.SequenceEqual([2])&&c.Parameters.GetValueOrDefault("branch")==branch);Play(g);
            Need(V(g).Hp==hp&&E<ProgramSuitPreventionPaymentEvent>(g).Single()is{OwnerSeat:0,CostCardId:var paid,EffectiveSuit:Suit.Heart}&&paid==cost&&E<DamageAppliedEvent>(g).All(e=>e.TargetSeat!=0),"The derived current Tianxiang prevents the original genuine incoming damage and pays its exact heart entity once.");
            Need(branch=="damage-draw"?E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==1&&e.TargetSeat==2&&e.Amount==1)&&E<ProgramSuitPreventionDrawEvent>(g)is[{RecipientSeat:2,Requested:1,Actual:1}]:E<ProgramSkillHpLostEvent>(g).Any(e=>e.TargetSeat==2&&e.Amount==1)&&V(g,2).Hand.Any(c=>c.Id==cost)&&E<ProgramSuitPreventionGiftEvent>(g)is[{Unavailable:false}],"Both linked382 branches retain native source/damage/draw or real HP-loss and exact discarded-card obtain.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("liuli");DepositAndFinish(g);Reach(g,()=>Offer(g,Liuli));Activate(g);Reach(g,()=>P(g)!.Choices.Any(c=>c.Targets.Count==1));Need(P(g)!.Choices.All(c=>!c.Targets.Contains(1)),"Derived native Liuli excludes the actual Slash source rather than widening current target legality.");g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));Reach(g,()=>P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand));Reach(g,()=>E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==1&&e.TargetSeat==3));Need(E<DamageAppliedEvent>(g).All(e=>e.TargetSeat!=0)&&g.CardMovements.Any(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value.Contains("liuli",StringComparison.Ordinal)),"A real enemy AI physical Slash is redirected using a genuine owned card and the owner's native attack range.");_=Cold(g,r);
        }
        foreach(var scene in new[]{"loss","death"})
        {
            var(g,r)=Start(scene);Begin(g);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="deposit");Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DaQiaoXiaoQiao is not null).Id;g=Cold(g,r);Continue(g);
            if(scene=="loss"){Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.Burst));Need(!Has(g,Xingwu)&&Pile(g).Length==0,"A native paid deposit child really removes the conditional source and native cleanup removes the old pile.");Branch(g,"turnover");Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.HandCards));ChooseTwo(g);Target(g,1);}
            Reach(g,()=>E<DaQiaoXiaoQiaoCompletedEvent>(g).Any(e=>e.FrameId==frame));Need(E<DaQiaoXiaoQiaoMovementPaidEvent>(g).Count(e=>e.FrameId==frame&&e.Stage==DaQiaoXiaoQiaoStage.DepositChildren)==1&&E<DaQiaoXiaoQiaoCompletedEvent>(g).Count(e=>e.FrameId==frame)==1&&(scene=="loss"?V(g).IsAlive&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g).Length==1:!V(g).IsAlive&&E<DaQiaoXiaoQiaoDamageIssuedEvent>(g).Length==0),"A paid original binding returns once after genuine skill loss or non-Lord death, without depositing twice or damaging after its actor dies.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Has(GameEngine g,string skill)=>V(g).Skills!.Any(s=>s.ContentId==skill);
    private static int[] Pile(GameEngine g)=>V(g).PublicPersistentPiles is{}piles?piles.Where(p=>p.SourceSkillId==Xingwu).SelectMany(p=>p.Cards.Select(c=>c.Id)).ToArray():V(g).PublicPersistentPileSkillId==Xingwu?V(g).PublicPersistentPileCards?.Select(c=>c.Id).ToArray()??[]:[];
    private static DaQiaoXiaoQiaoReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.DaQiaoXiaoQiao is not null)?.DaQiaoXiaoQiao;
    private static bool Stage(GameEngine g,DaQiaoXiaoQiaoStage stage)=>Current(g)?.Stage==stage&&P(g)?.SkillPrompt?.SkillId==Xingwu;
    private static bool Offer(GameEngine g,string skill)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Da Qiao Xiao Qiao command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Need(V(g,p.PlayerSeat).IsHuman,"Actual AI uses Advance, not human answers.");var c=p.Choices.FirstOrDefault(choose);Need(c is not null,"Missing exact Xingwu option: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Branch(GameEngine g,string branch)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);
    private static void Target(GameEngine g,int seat)=>Answer(g,c=>c.Targets.SequenceEqual([seat]));
    private static void ChooseTwo(GameEngine g){for(var i=0;i<2;i++)Answer(g,c=>c.Cards.Count==1);}
    private static void Use(GameEngine g,string id,int[]? targets=null,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Begin(GameEngine g){End(g);Reach(g,()=>Offer(g,Xingwu));Activate(g);Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.Deposit));}
    private static void DepositAndFinish(GameEngine g){Begin(g);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="deposit");Reach(g,()=>Stage(g,DaQiaoXiaoQiaoStage.Burst));Branch(g,"finish");}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void NextPlay(GameEngine g,int after)=>Reach(g,()=>g.State.TurnNumber>after&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static string Diagnostic(GameEngine g)=>$"turn={g.State.TurnNumber};winner={g.State.Winner};prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId};stage={Current(g)?.Stage};hp={V(g).Hp};pile={Pile(g).Length};paid={E<DaQiaoXiaoQiaoMovementPaidEvent>(g).Length}";
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<300;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Missing Da Qiao Xiao Qiao boundary: "+Diagnostic(g));var p=P(g);
            if(p is null||!V(g,p.PlayerSeat).IsHuman)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else throw new InvalidOperationException("Unexpected Da Qiao Xiao Qiao prompt: "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded Da Qiao Xiao Qiao boundary: "+Diagnostic(g));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s)))+JsonSerializer.Serialize(g.ResolutionStack)+JsonSerializer.Serialize(g.CardMovements)+g.Events.Count;
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves real public pile entities, exact derived sources, private costs and once-paid native child returns.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"death"or"equipment-death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-da-qiao-xiao-qiao",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:da-qiao-xiao-qiao");CharacterFixtureContent.Register(b,"boundary:xiao-qiao");CharacterFixtureContent.Register(b,"classic:da-qiao");if(scene.StartsWith("equipment",StringComparison.Ordinal))CharacterFixtureContent.Register(b,"ol:meng-da");
            var defs=new List<string>{$$"""
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
             {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
             {"id":"incoming-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
             {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
             """};
            // Split the JSON sequence into exact skill definitions before extending it.
            var sequence=JsonNode.Parse("["+defs[0]+"]")!.AsArray();defs=sequence.Select(n=>n!.ToJsonString()).ToList();
            if(scene=="equipment-death")
            {
                var health=JsonNode.Parse(defs[2])!;var effects=health["triggers"]![0]!["effects"]!.AsArray();effects.Add(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"}"""));effects.Add(JsonNode.Parse("""{"op":"damage","target":"selectedTarget","amount":20}"""));defs[2]=health.ToJsonString();
            }
            void Add(string id,string body)=>defs.Add("{\"id\":\""+id+"\",\"revision\":1,"+body+"}");var skills=new List<string>{Driver,Luoyan};
            if(scene=="pile"){Add("fixture:dqxq-derived-watch","\"triggers\":[{\"id\":\"first-derived-ownership\",\"window\":\"skillsChanged\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"continue\",\"options\":[{\"id\":\"continue\"}]}]}]");skills.Add("fixture:dqxq-derived-watch");}
            if(scene=="loss"){Add(Source,"\"hpSkillGrants\":[{\"id\":\"healthy\",\"skillId\":\"ol:xingwu\",\"minimumHp\":9}]");skills.Add(Source);}else skills.Add(Xingwu);
            if(scene=="independent"){skills.Add(Tianxiang);skills.Add(Liuli);}if(scene=="equipment")skills.Add(Health);
            if(scene is"loss"or"death"){var tail=scene=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}":"{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}";Add(Watch,"\"triggers\":[{\"id\":\"actual-deposit-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"skill-program.da-qiao-xiao-qiao.xingwu.deposit\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"continue\",\"options\":[{\"id\":\"continue\"}]},"+tail+"]}]");skills.Add(Watch);}
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="原生星舞落雁小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,labels[id].name,"真实原生行为"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:dqxq-pick-owner","本人","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});b.AddGeneral(new(Owner,"真实大乔小乔","ol-da-qiao-xiao-qiao","fixture:dqxq-pick-owner","wu",8,skills,GeneralGender.Female));
            var peers=Enumerable.Range(1,3).Select(i=>"fixture:dqxq-peer-"+i).ToArray();foreach(var(peer,i)in peers.Select((peer,i)=>(peer,i))){var pick="fixture:dqxq-pick-"+i;b.AddSkill(new(pick,"其他角色","公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>1000000d*(3-i))});b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"wei",20,scene.StartsWith("equipment",StringComparison.Ordinal)&&i==0?[Quiet,Health]:scene=="liuli"&&i==0?[]:[Quiet],scene=="female"?GeneralGender.Female:GeneralGender.Male));}
            foreach(var(id,kind)in new[]{("lion",CardKind.SilverLion),("ox",CardKind.WoodenOx)})b.AddCard(new("fixture:dqxq-"+id,id,"装备牌","真实原生装备",kind));var cards=scene.StartsWith("equipment",StringComparison.Ordinal)?new[]{"standard:slash","fixture:dqxq-lion","fixture:dqxq-ox"}:new[]{"standard:slash"};b.AddDeck(new("fixture:dqxq-deck","小固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard(cards[i%cards.Length],i/cards.Length%2==0?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"真实星舞落雁",4,4,scene is"death"or"equipment-death"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:dqxq-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
