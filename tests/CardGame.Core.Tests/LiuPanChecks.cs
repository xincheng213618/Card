using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuPanChecks
{
    private const string Owner="fixture:lp-owner",Driver="fixture:lp-driver",Quiet="fixture:lp-quiet",Issuer="fixture:lp-issuer",Watch="fixture:lp-watch",Skill="ol:pijing",Mode="identity:classic-liu-pan",GiftReason="skill-program.liu-pan.pijing.random-gift";
    private static string Scene="";
    public static void PijingNativeAddRemoveZeroAndRandomGiftCold()
    {
        foreach(var scene in new[]{"add","zero","multi","prepared","fire","zero-virtual","defense-add","defense-remove"})
        {
            var(g,r)=Start(scene);Use(g,"stock");Play(g);
            if(scene is"multi"or"prepared"or"fire"){Use(g,"wound-two");Play(g);}
            if(scene=="prepared")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Use(g,"give",V(g,0).Hand.Take(V(g,0).HandCount-1).Select(c=>c.Id).ToArray(),[1]);Play(g);
                Need(V(g,0).HandCount==1&&V(g,0).Equipment.Any(c=>c.Kind==CardKind.FangtianHalberd),"The genuine equipped Fangtian is tested with the real last Hand entity, rather than a fabricated prepared attack.");
            }
            var hp=Enumerable.Range(0,4).Select(s=>V(g,s).Hp).ToArray();
            if(scene=="zero-virtual")
            {Use(g,"zero");Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));}
            else Native(g,a=>Kind(g,a)==(scene=="multi"?CardKind.ArrowBarrage:CardKind.Slash)&&(scene=="multi"||a.TargetSeats.SequenceEqual(scene=="prepared"?[1,2]:[1])));
            if(scene=="fire"){Reach(g,()=>Action(g,"current-slash-fire"));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="convert"&&c.Targets.Count==0);}
            Activate(g);var start=Current(g)!;Need(start.X==(scene is"multi"or"prepared"or"fire"?2:1),"X freezes the true lost HP, with the zero-lost-HP lower bound of one.");
            Need(P(g)!.Choices.All(c=>c.Cards.Count==0),"The public target menu never exposes any foreign random Hand entity.");
            var before=State(g);Need(!g.Submit(new AnswerPromptCommand(1,P(g)!.PromptId,P(g)!.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"Another seat cannot alter the real owner's designation frontier.");
            if(scene is"zero"or"zero-virtual"or"multi"or"prepared"or"fire"or"defense-remove")Pick(g,"remove",[1]);
            if(scene is"add"or"fire"or"defense-add")Pick(g,"add",[2]);
            if(scene=="prepared")Pick(g,"add",[3]);
            g=Cold(g,r);Pick(g,"finish");if(scene=="defense-add"){Reach(g,()=>P(g)is{PlayerSeat:2,SkillPrompt.SkillId:"classic:shouxi"});g=Cold(g,r);}Play(g);
            var fact=E<PijingTargetsAppliedEvent>(g).Single();var expected=scene is"zero"or"zero-virtual"or"defense-remove"?Array.Empty<int>():scene is"add"or"defense-add"?new[]{1,2}:scene=="multi"?new[]{2,3}:scene=="prepared"?new[]{2,3}:new[]{2};
            Need(E<PijingTargetSlotEvent>(g).Where(e=>e.FrameId==fact.FrameId&&e.Final).OrderBy(e=>e.Index).Select(e=>e.Seat).SequenceEqual(expected)&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==fact.UseId)==1,"Every addition/removal preserves one original Use and its exact ordered final targets, including a genuine empty target set.");
            Need(E<PijingGrantIssuedEvent>(g).Length==E<PijingToggleSelectedEvent>(g).Length&&E<PijingGiftPaidEvent>(g).All(e=>g.CardMovements.Count(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==CardLocation.Hand(0)&&m.Reason.Value==GiftReason)==1),"Each selected person yields one long-lived grant and pays at most one truly random transferable HE entity exactly once.");
            if(scene is"zero"or"zero-virtual")Need(E<DamageAppliedEvent>(g).Length==0&&V(g,1).Hp==hp[1],"Removing the only target settles the real material or zero-material use without manufacturing damage.");
            if(scene=="prepared")Need(V(g,1).Hp==hp[1]&&V(g,2).Hp==hp[2]-1&&V(g,3).Hp==hp[3]-1,"A real prepared Fangtian use removes its first recipient, retains the second target's state and resolves the fresh replacement target through the same native cursor.");
            if(scene=="fire")Need(E<ProgramCurrentSlashFireChangedEvent>(g).Length==1&&E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==2&&e.Nature==DamageNature.Fire)&&V(g,1).Hp==hp[1],"A committed native fire conversion retains its exact original source, while Pijing rebases both the fire target proof and the ordinary attack cursor.");
            if(scene is"add"or"fire")Need(E<CompletedUndamagedUseDamageRecordedEvent>(g).Count(e=>e.CardUseFrameId==fact.UseId&&e.Amount>0)==expected.Length,"The isolated existing whole-use tracker validates the same genuinely accepted/rewritten action for every final recipient, without another tracker skill being owned.");
            if(scene=="defense-remove")Need(!E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId=="classic:shouxi"),"A removed original target cannot activate its stale frozen target-defense candidate.");
            if(scene=="zero-virtual")Need(E<CardUseDeclaredEvent>(g).Single(e=>e.ResolutionId==fact.UseId).CardId==0,"The empty-target case includes a genuine zero-entity native Slash parent return.");
            var spent=E<PijingStartedEvent>(g).Length;Native(g,a=>Kind(g,a)==CardKind.DrawTwo);Play(g);Need(E<PijingStartedEvent>(g).Length==spent,"A later eligible native use in the same actual turn does not refresh the initial Pijing allowance.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("kuangfu-zero");Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var gear=V(g,0).Equipment.Single().Id;
            Accept(g,new UseProgramSkillCommand(0,"ol:kuangfu","equipment-slash",[],[],g.Revision,P(g)!.PromptId));Reach(g,()=>Action(g,"kuangfu"));Answer(g,c=>c.Cards.SequenceEqual([gear])&&c.Targets.SequenceEqual([0]));Reach(g,()=>Action(g,"kuangfu"));Answer(g,c=>c.Cards.Count==0&&c.Targets.SequenceEqual([1]));
            Activate(g);Pick(g,"remove",[1]);g=Cold(g,r);Pick(g,"finish");Play(g);var issued=E<KuangfuSlashIssuedEvent>(g).Single().Return;
            Need(E<PijingTargetSlotEvent>(g).Count(e=>e.Final)==0&&E<CardUseDeclaredEvent>(g).Single(e=>e.ResolutionId==issued.CardUseFrameId).CardId==0&&E<KuangfuSlashResolvedEvent>(g).Single().CardUseFrameId==issued.CardUseFrameId&&E<KuangfuActualDamageObservedEvent>(g).Length==0&&E<KuangfuFinishedEvent>(g).Length==1&&g.CardMovements.Count(m=>m.CardId==gear&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.kuangfu.equipment-cost")==1,"A genuine Kuangfu equipment payment issues one zero-entity Slash; deleting its sole target returns through the exact Kuangfu invoice and completes the no-damage outcome without repaying equipment.");_=Cold(g,r);
        }
    }
    public static void PijingBorrowedPairsAndReciprocalSupportTargetsCold()
    {
        {
            var(g,r)=Start("borrowed");Use(g,"stock");Play(g);Use(g,"wound-two");Play(g);
            foreach(var target in new[]{1,2}){Use(g,"place",[V(g,0).Hand.First().Id],[target]);Play(g);}
            Native(g,a=>Kind(g,a)==CardKind.BorrowedSword&&a.TargetSeats.SequenceEqual([1,0]));Activate(g);Pick(g,"remove",[1,0]);Pick(g,"add",[2,3]);g=Cold(g,r);Pick(g,"finish");Play(g);
            var applied=E<PijingTargetsAppliedEvent>(g).Single();Need(E<PijingTargetSlotEvent>(g).Where(e=>e.FrameId==applied.FrameId&&e.Final).Select(e=>e.Seat).SequenceEqual([2,3])&&E<PijingGrantIssuedEvent>(g).Select(e=>e.HolderSeat).SequenceEqual([1,2])&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==applied.UseId)==1,"Borrowed Sword toggles whole native holder/victim pairs, gifts only from the selected main holders, and returns through the original trick once.");_=Cold(g,r);
        }
        foreach(var scene in new[]{"holder-peach","holder-draw","holder-alcohol"})
        {
            var(g,r)=Start(scene);var issuer=IssuerSeat(g);AcquireLease(ref g,r);Use(g,"wound");Play(g);Use(g,"hurt-peer",[],[issuer]);Play(g);
            var ownHp=V(g,0).Hp;var issuerHp=V(g,issuer).Hp;var ownHand=V(g,0).HandCount;var issuerHand=V(g,issuer).HandCount;var kind=scene=="holder-peach"?CardKind.Peach:scene=="holder-draw"?CardKind.DrawTwo:CardKind.Alcohol;
            Native(g,a=>Kind(g,a)==kind);Reach(g,()=>Current(g)is{Reciprocal:true});var receipt=Current(g)!;
            Need(receipt.ActorSeat==0&&receipt.Source.OwnerSeat==issuer&&receipt.Original.SequenceEqual([0])&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Targets.SequenceEqual([issuer])),"The holder's real unique self designation offers the original issuer as a distance-free additional basic/trick recipient.");
            g=Cold(g,r);Pick(g,"add",[issuer]);Play(g);
            Need(E<PijingGrantConsumedEvent>(g).Length==1&&E<PijingTargetsAppliedEvent>(g).Any(e=>e.Reciprocal)&&E<PijingNextChoiceEvent>(g).Single().Choice=="add","The next-use grant is consumed once by the holder's original native Use, rather than by a fabricated extra card use.");
            if(kind==CardKind.Peach)Need(V(g,0).Hp==ownHp+1&&V(g,issuer).Hp==issuerHp+1,"The true Peach recovers both legal wounded recipients with their native health children.");
            if(kind==CardKind.DrawTwo)Need(V(g,0).HandCount==ownHand+1&&V(g,issuer).HandCount==issuerHand+2,"The real implicit-self DrawTwo consumes its one physical material and draws for both actual recipients.");
            if(kind==CardKind.Alcohol)Need(E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==receipt.UseId)==1,"A true self Alcohol retains its native basic-card continuation when the issuer is added.");_=Cold(g,r);
        }
    }
    public static void PijingLongLeaseSourceLossAndTrueDesignationCold()
    {
        foreach(var scene in new[]{"holder-loss","holder-overlap","holder-dodge","holder-rescue"})
        {
            var(g,r)=Start(scene);var issuer=IssuerSeat(g);AcquireLease(ref g,r);var grant=E<PijingGrantIssuedEvent>(g).First(e=>e.HolderSeat==0);
            if(scene=="holder-overlap")
            {var turn=g.State.TurnNumber;End(g);Reach(g,()=>E<PijingGrantIssuedEvent>(g).Count(e=>e.HolderSeat==0)==2);Play(g);Need(g.State.TurnNumber>turn,"Two real actual turns issue independent long-lived grants before the holder's next eligible use.");}
            if(scene=="holder-loss")
            {Use(g,"hurt-peer",[],[issuer]);Play(g);Need(!V(g,issuer).Skills!.Any(s=>s.Id==Skill),"The old issuer actually loses its HP-conditional Pijing source before the holder consumes the issued right.");Use(g,"heal-peer",[],[issuer]);Play(g);Need(V(g,issuer).Skills!.Any(s=>s.Id==Skill),"A genuine health recovery regrants a new skill instance without replacing the old entitlement invoice.");}
            if(scene=="holder-dodge")
            {
                var incoming=Enumerable.Range(1,3).Single(s=>V(g,s).GeneralId=="fixture:lp-peer-2");Use(g,"hurt-peer",[],[incoming]);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
                Need(E<CardUseDeclaredEvent>(g).Any(e=>e.CardId==0&&e.CardKind==CardKind.Slash&&e.SourceSeat==incoming)&&E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==0&&e.EffectiveCardKind==CardKind.Dodge)&&E<PijingGrantConsumedEvent>(g).Length==0,"An independent AI's mandatory native zero Slash genuinely elicits defensive Dodge; the Dodge has no actual player designation and cannot consume the holder's pending unique-target right.");
            }
            if(scene=="holder-rescue")
            {Use(g,"die");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RescueDying});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1&&c.Parameters.GetValueOrDefault("response")=="peach");}
            else Native(g,a=>Kind(g,a)==CardKind.DrawTwo);
            Reach(g,()=>Current(g)is{Reciprocal:true});var receipt=Current(g)!;var originalGroup=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==receipt.UseId).PijingNextUses!.Single(group=>group.GroupId==receipt.GroupId);Need(receipt.Source==(grant.Source with{BindingId="next-sole-use"})&&originalGroup.Source==grant.Source&&receipt.Grants.Contains(grant.GrantId)&&receipt.ActorSeat==0,"The original issuer's full old activation source remains frozen on its issued native-use group; the resolver uses the exact next-sole-use binding of that same old skill instance after actual turns, source replacement and genuine response children.");
            if(scene=="holder-rescue")Need(g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==receipt.UseId).DyingResponse is not null,"A real dying Peach Use with a true unique recipient is eligible; it is not conflated with an un-targeted response or provided card.");
            g=Cold(g,r);Pick(g,"draw");Play(g);Need(E<PijingGrantConsumedEvent>(g).Select(e=>e.GrantId).Distinct().Count()==(scene=="holder-overlap"?2:1)&&E<PijingNextChoiceEvent>(g).Length==1,"Overlapping rights from the same issuer skill merge into one next-use choice and consume all original grants without multiplying the reward.");_=Cold(g,r);
        }
    }
    public static void PijingEquipmentGiftsChildrenPrivacyAndPaidDeathCold()
    {
        foreach(var scene in new[]{"holder-lion","holder-ox","holder-paid-loss","holder-paid-death","holder-weapon"})
        {
            var(g,r)=Start(scene);var issuer=IssuerSeat(g);int gear;int? grain=null;
            if(scene=="holder-weapon")
            {Use(g,"weapon");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.ContainsKey("advanced-value"))==true);Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:lp-reserve");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);gear=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;}
            else {Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);gear=V(g,0).Equipment.Single().Id;}
            if(scene=="holder-ox")
            {grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);Need(V(g,0).WoodenOxGrain!.Any(c=>c.Id==grain)&&(g.CreateSnapshot(issuer).Players[0].WoodenOxGrain?.Count??0)==0,"The original stored Ox entity is visible only to its real holder before any random gift.");}
            else if(scene!="holder-weapon"){Use(g,"wound");Play(g);}
            Use(g,"empty");Play(g);Need(V(g,0).HandCount==0,"The selected donor's actual Hand is empty, so the sole transferable equipment is chosen by native randomness without a manufactured selection.");End(g);
            if(scene!="holder-weapon")
            {
                Reach(g,()=>P(g)is{PlayerSeat:0,SkillPrompt.SkillId:Watch});var receipt=Current(g)!;
                Need(receipt.Stage==PijingStage.GiftChildren&&receipt.Gift?.CardId==gear&&E<PijingGiftPaidEvent>(g).Count(e=>e.CardId==gear)==1&&!E<PijingGiftCompletedEvent>(g).Any(e=>e.FrameId==g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.Pijing is not null).Id),"The real random HE payment pauses in its owning Lion/Ox child before completing the donor's lease.");
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
                if(scene is"holder-paid-loss"or"holder-paid-death"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([issuer]));}
            }
            Reach(g,()=>E<PijingGrantIssuedEvent>(g).Any(e=>e.HolderSeat==0));
            Need(E<PijingCompletedEvent>(g).Length==1&&E<PijingGiftPaidEvent>(g).Count(e=>e.CardId==gear)==(scene=="holder-weapon"?0:1),"Real native children finish once, while an untransferable GeneralWeapon is skipped and still grants the selected person's next-use right.");
            if(scene=="holder-ox")Need(g.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)==1,"Actual Ox grain spills as a native child and is never another primary random gift.");
            if(scene=="holder-lion")Need(E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"Transferring the real equipped Lion recovers its actual wounded donor.");
            if(scene=="holder-paid-loss")Need(!V(g,issuer).Skills!.Any(s=>s.Id==Skill),"The paid gift and lease complete after the true conditional issuer skill is lost.");
            if(scene=="holder-paid-death")Need(!V(g,issuer).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==issuer),"The paid native recovery child truly kills the non-Lord issuer before the original gift resolution returns.");
            g=Cold(g,r);Play(g);Native(g,a=>Kind(g,a)==CardKind.DrawTwo);Reach(g,()=>Current(g)is{Reciprocal:true});
            if(scene=="holder-paid-death")Need(!P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="add")&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="draw"),"A dead issuer cannot be added as a target, while its previously issued living holder still receives the draw choice before the new frame has paid anything.");
            g=Cold(g,r);Pick(g,"draw");Play(g);Need(E<PijingNextChoiceEvent>(g).Length==1&&E<PijingGrantConsumedEvent>(g).Length==1,"Issuer loss or death does not cancel an issued independent right or duplicate its native draw payment.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PijingReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.Pijing is not null)?.Pijing;
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool Optional(GameEngine g)=>P(g)is{PlayerSeat:0,SkillPrompt.SkillId:Skill}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static CardKind? Kind(GameEngine g,LegalAction a)=>a.PlayedCardKind??V(g,0).Hand.FirstOrDefault(c=>c.Id==a.CardId)?.Kind;
    private static int IssuerSeat(GameEngine g)=>Enumerable.Range(1,3).Single(s=>V(g,s).GeneralId==Issuer);
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException("scene="+Scene+" "+why);}
    private static void Accept(GameEngine g,GameCommand c){try{var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(x.Accepted,x.Error?.Message??"Rejected native Liu Pan command.");}catch(Exception e){throw new InvalidOperationException("scene="+Scene+" command="+c.GetType().Name+" cause="+e.Message+" "+Diagnostic(g),e);}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> match){var p=P(g)!;var c=p.Choices.FirstOrDefault(match);Need(V(g,p.PlayerSeat).IsHuman&&c is not null,"Missing actual human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Pick(GameEngine g,string branch,int[]? target=null)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="pijing"&&c.Parameters.GetValueOrDefault("branch")==branch&&(target is null||c.Targets.SequenceEqual(target)));
    private static void Activate(GameEngine g){Reach(g,()=>Optional(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Current(g)is{Stage:PijingStage.Toggles});}
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> match){var a=g.GetHumanLegalActions().FirstOrDefault(match);Need(a is not null,"Missing native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void AcquireLease(ref GameEngine g,ContentRegistry r){var current=g;End(current);Reach(current,()=>E<PijingGrantIssuedEvent>(current).Any(e=>e.HolderSeat==0));g=Cold(current,r);Play(g);Need(E<PijingGrantConsumedEvent>(g).Length==0,"The actual issuer's completed support use grants a lease without consuming it on an unrelated response or a phase transition.");}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<350;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Ended before {caller}:{line}: "+Diagnostic(g));var p=P(g);
            if(p is null||!V(g,p.PlayerSeat).IsHuman){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(Action(g,"named-defense-decline"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="named-defense-decline");
            else if(p.Kind==DecisionKind.SelectHarvestCard)Answer(g,c=>c.Cards.Count==1);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
            else throw new InvalidOperationException($"Unexpected {caller}:{line}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded {caller}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.Pijing?.Stage}),Facts=g.Events.TakeLast(3).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold restore preserves every private view, original action, frozen target/pair sequence, random payment, long-lived grant and once-paid native child cursor.");return cold;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        Scene=scene;ContentRegistry r;try{r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));}catch(Exception e){throw new InvalidOperationException("scene="+scene+" fixture cause="+e.Message,e);}var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene.StartsWith("holder-",StringComparison.Ordinal)?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-liu-pan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:liu-pan");if(scene=="kuangfu-zero")CharacterFixtureContent.Register(b,"ol:pan-feng");if(scene is"add"or"fire")CharacterFixtureContent.Register(b,"ol:pei-yuan-shao");if(scene.StartsWith("defense-",StringComparison.Ordinal))CharacterFixtureContent.Register(b,"classic:cao-jie");var holder=scene.StartsWith("holder-",StringComparison.Ordinal);var kind=scene=="prepared"?CardKind.FangtianHalberd:scene is"holder-lion"or"holder-paid-loss"or"holder-paid-death"?CardKind.SilverLion:scene=="holder-ox"?CardKind.WoodenOx:!holder?CardKind.Crossbow:CardKind.Dodge;var text=char.ToLowerInvariant(kind.ToString()[0])+kind.ToString()[1..];
            var defs=new List<JsonNode>();void SkillDef(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
            string A(string id,string effects,int cards=0,int targets=0,string more="")=>"{\"id\":\""+id+"\",\"minCards\":"+cards+",\"maxCards\":"+cards+",\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,"+more+"\"effects\":["+effects+"]}";
            var acts=new[]{A("stock","{\"op\":\"draw\",\"target\":\"owner\",\"amount\":8}"),A("wound","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),A("wound-two","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"),A("die","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":12}"),A("hurt-peer","{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}",0,1),A("heal-peer","{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1}",0,1),A("give","{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}",1,1),A("place","{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}",1,1,"\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"),A("empty","{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"empty\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"empty\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}"),A("incoming","{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",0,1),A("zero","{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingVirtualSlashTarget\"},{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}"),A("weapon","{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":5}")};
            var driver=JsonNode.Parse("{\"id\":\""+Driver+"\",\"revision\":1,\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0},{\"id\":\"range\",\"query\":\"attackRange\",\"operation\":\"set\",\"value\":4,\"priority\":0},{\"id\":\"slashes\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":3,\"priority\":0}],\"viewAs\":[],\"activations\":["+string.Join(',',acts)+"]}")!;
            driver["activations"]!.AsArray().Single(a=>a!["id"]!.GetValue<string>()=="give")!["maxCards"]=20;
            foreach(var output in new[]{CardKind.Slash,CardKind.DrawTwo,CardKind.Peach,CardKind.Alcohol,CardKind.ArrowBarrage,CardKind.BorrowedSword,CardKind.IronChain})
            {var node=JsonSerializer.SerializeToNode(new{id="native-"+output,inputKinds=new[]{text},inputSuits=Array.Empty<string>(),outputKind=char.ToLowerInvariant(output.ToString()[0])+output.ToString()[1..],forPlay=true,forResponse=output==CardKind.Slash,allowSameKind=true});if(output is CardKind.Peach or CardKind.Alcohol)node!["useOnly"]=true;if(output is CardKind.DrawTwo or CardKind.ArrowBarrage or CardKind.BorrowedSword or CardKind.IronChain)node!["singleCardTrickUse"]=true;driver["viewAs"]!.AsArray().Add(node);}
            driver["viewAs"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{id="response-peach",inputKinds=new[]{text},inputSuits=Array.Empty<string>(),outputKind="peach",forPlay=false,forResponse=true,allowSameKind=true}));
            driver["viewAs"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{id="dodge",inputKinds=new[]{text},inputSuits=Array.Empty<string>(),outputKind="dodge",forPlay=false,forResponse=true,allowSameKind=true}));defs.Add(driver);
            SkillDef(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            if(holder)SkillDef("fixture:lp-end-draw","\"modifiers\":[{\"id\":\"real-range\",\"query\":\"attackRange\",\"operation\":\"set\",\"value\":4,\"priority\":0}],\"triggers\":[{\"id\":\"true-support-use\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":false,"+(scene=="holder-overlap"?"":"\"usageScope\":\"game\",\"usageLimit\":1,")+"\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"drawTwo\"}]}]");
            if(scene is"holder-loss"or"holder-paid-loss")SkillDef("fixture:lp-conditional","\"hpSkillGrants\":[{\"id\":\"healthy-pijing\",\"skillId\":\"ol:pijing\",\"minimumHp\":12}]");
            if(scene=="fire")SkillDef("fixture:lp-fire","\"triggers\":[{\"id\":\"real-fire\",\"window\":\"cardUseCommitted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"effects\":[{\"op\":\"offerCurrentSlashFireAndExtraTarget\",\"target\":\"owner\"}]}]");
            if(scene=="holder-dodge")
            {
                SkillDef("fixture:lp-no-ai-target","\"cardPolicies\":[{\"id\":\"real-sole-target-domain\",\"kind\":\"prohibitTarget\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"]}]");
                SkillDef("fixture:lp-forced-incoming","\"modifiers\":[{\"id\":\"normal-range\",\"query\":\"attackRange\",\"operation\":\"set\",\"value\":4,\"priority\":0}],\"triggers\":[{\"id\":\"real-health-counter-use\",\"window\":\"afterHealthChanged\",\"subject\":\"owner\",\"condition\":{\"kind\":\"healthChangeKindIs\",\"hpChangeKind\":\"loss\"},\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingVirtualSlashTarget\"},{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}]}]");
            }
            if(scene is"holder-lion"or"holder-ox"or"holder-paid-loss"or"holder-paid-death")
            {var effect="{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"native-child\",\"options\":[{\"id\":\"continue\"}]}";if(scene is"holder-paid-loss"or"holder-paid-death")effect+=",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":"+(scene=="holder-paid-death"?20:1)+"}";SkillDef(Watch,"\"triggers\":[{\"id\":\"paid-equipment-child\",\"window\":\""+(scene=="holder-ox"?"cardsMoved":"afterHealthChanged")+"\",\"subject\":\"owner\","+(scene=="holder-ox"?"\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],":"\"condition\":{\"kind\":\"healthChangeKindIs\",\"hpChangeKind\":\"recovery\"},")+"\"optional\":false,\"effects\":["+effect+"]}]");}
            var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="刘磐原生目标、随机交牌及长时义务小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>n[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实刘磐边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:lp-own-pick","本人","公开本人评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});var own=new List<string>{Driver};if(!holder)own.Add(Skill);if(scene=="kuangfu-zero")own.Add("ol:kuangfu");if(scene=="fire")own.Add("fixture:lp-fire");if(scene is"holder-lion"or"holder-ox"or"holder-paid-loss"or"holder-paid-death")own.Add(Watch);b.AddGeneral(new(Owner,"实际刘磐夹具本人","ol-liu-pan","fixture:lp-own-pick","qun",12,own,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(i=>i==1&&holder?Issuer:"fixture:lp-peer-"+i).ToArray();foreach(var peer in peers)
            {var skills=new List<string>();if(scene.StartsWith("defense-",StringComparison.Ordinal))skills.Add("classic:shouxi");if(scene=="holder-dodge"){skills.Add("fixture:lp-no-ai-target");if(peer=="fixture:lp-peer-2")skills.Add("fixture:lp-forced-incoming");}var pick=Quiet;if(peer==Issuer){pick="fixture:lp-issuer-pick";b.AddSkill(new(pick,"真实发起者","公开身份评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Rebel?1000000d:-1000000d)});skills.AddRange([scene is"holder-loss"or"holder-paid-loss"?"fixture:lp-conditional":Skill,"fixture:lp-end-draw"]);}b.AddGeneral(new(peer,"真实其他角色","supporter",pick,"qun",12,skills));}
            if(scene=="holder-weapon"){b.AddSkill(new("fixture:lp-reserve-pick","未选武将","公开备用评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000000d)});b.AddGeneral(new("fixture:lp-reserve","真实武将武器","supporter","fixture:lp-reserve-pick","qun",4,[]));}
            b.AddCard(new("fixture:lp-card","真实"+kind,CardCatalog.Get(kind).CategoryName,"同质真实实体",kind));b.AddDeck(new("fixture:lp-deck","刘磐固定真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(_=>new ContentDeckPhysicalCard("fixture:lp-card",Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"刘磐小局面",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:lp-deck",GeneralCandidateCount:scene=="holder-weapon"?5:4,GeneralPoolIds:scene=="holder-weapon"?[Owner,..peers,"fixture:lp-reserve"]:[Owner,..peers]));
        }
    }
}
