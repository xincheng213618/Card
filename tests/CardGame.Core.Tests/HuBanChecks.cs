using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HuBanChecks
{
    private const string Owner="fixture:hb-owner",Driver="fixture:hb-driver",Quiet="fixture:hb-quiet",Watch="fixture:hb-watch",Huiyun="ol:huiyun",Mode="identity:classic-hu-ban";
    private static string Scene="";
    public static void HuiyunWholeFireAttackRoundOptionsAndRepeatedEffectsCold()
    {
        var(g,r)=Start("round");Use(g,"stock");Play(g);
        foreach(var option in new[]{3,2,1})
        {
            Begin(g,0);Reach(g,()=>Option(g));var origin=Current(g)!.Origin;
            Need(E<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==origin.UseId)&&E<HuiyunEffectRevealedEvent>(g).Count(e=>e.UseId==origin.UseId)==1,"The bonus begins only after the whole genuine FireAttack, with its original shown entity frozen.");
            PickOption(g,option);Reach(g,()=>HandOffer(g));g=Cold(g,r);if(option==3)Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="draw");else Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use");Play(g);
        }
        Need(E<HuiyunOptionSpentEvent>(g).Select(e=>e.Option).SequenceEqual([3,2,1])&&E<HuiyunOptionSpentEvent>(g).Select(e=>e.RoundNumber).Distinct().Count()==1,"Each option has its own once-per-round invoice, independent of the number of real converted FireAttacks.");
        var spent=E<HuiyunOptionSpentEvent>(g).Length;Begin(g,0);Play(g);Need(E<HuiyunUseIssuedEvent>(g).Length==4&&E<HuiyunOptionSpentEvent>(g).Length==spent,"After all three options are spent, another real conversion remains legal and supplies no bonus menu.");
        Use(g,"extra");Play(g);var turn=g.State.TurnNumber;var round=E<RoundStartedEvent>(g).Last().RoundNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);Need(E<RoundStartedEvent>(g).Last().RoundNumber==round,"The actual inserted extra turn remains in the same round.");Begin(g,0);Play(g);Need(E<HuiyunOptionSpentEvent>(g).Length==spent,"An extra actual turn cannot refresh the three round options.");_=Cold(g,r);
        (g,r)=Start("repeated");Use(g,"stock");Play(g);Begin(g,0);Reach(g,()=>Option(g));var original=Current(g)!.Origin;
        Need(E<HuiyunEffectEnteredEvent>(g).Count(e=>e.UseId==original.UseId)==4&&E<HuiyunEffectRevealedEvent>(g).Count(e=>e.UseId==original.UseId)==4&&Current(g)!.Opportunities.Select(o=>(o.EffectRound,o.TargetSeat)).Distinct().Count()==4&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==original.UseId)==1,"A genuine extra target plus Fengzi's extra effect freezes four distinct original effect/recipient/display tickets after one whole Use.");
        foreach(var option in new[]{3,2,1})
        {
            PickOption(g,option);Reach(g,()=>Option(g)||P(g)is{PlayerSeat:0}&&HandOffer(g)||P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
            if(HandOffer(g)&&P(g)!.PlayerSeat==0){g=Cold(g,r);if(option==3)Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="draw");else Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use"&&c.Targets.SequenceEqual([1]));}
            Reach(g,()=>Option(g)||P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        }
        Play(g);Need(E<HuiyunOptionSpentEvent>(g).Length==3&&E<HuiyunOpportunityCompletedEvent>(g).Count(e=>e.FrameId==E<HuiyunStartedEvent>(g).Single().FrameId)==4&&E<HuiyunUseIssuedEvent>(g).Length==1,"All actual opportunities finish once; natural nested FireAttacks never fabricate another Huiyun conversion origin or restart the outer menu.");_=Cold(g,r);
    }
    public static void HuiyunNaturalHandUseQuotaAndNativeKindsCold()
    {
        foreach(var scene in new[]{"slash","equipment","delayed","global","draw-two","chain","nested"})
        {
            var(g,r)=Start(scene);
            if(scene=="slash"){Native(g,a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource is null&&a.TargetSeats.SequenceEqual([1]));Play(g);Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"The ordinary proactive Slash allowance is genuinely paid before Huiyun.");}
            Begin(g,0);Reach(g,()=>Option(g));var shown=Current(g)!.Opportunities[0].ShownCardId!.Value;PickOption(g,1);Reach(g,()=>HandOffer(g));
            Need(P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("choice")=="use").All(c=>c.Cards.SequenceEqual([shown])),"Option1 offers the original natural Hand entity only, never another same-name material.");
            g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use"&&(scene is"slash"or"delayed"or"nested"?c.Targets.SequenceEqual([1]):true));Play(g);
            var issued=E<HuiyunNativeIssuedEvent>(g).Single().Invoice;var accepted=E<CardActionAcceptedEvent>(g).Select(e=>e.Action).Where(a=>a.ActionId==issued.ActionId).ToArray();var declared=E<CardUseDeclaredEvent>(g).Where(e=>e.ResolutionId==issued.UseId).ToArray();var paid=E<HuiyunPaymentRecordedEvent>(g).Where(e=>e.UseId==issued.UseId&&e.Followup).ToArray();
            Need(issued.CardId==shown&&issued.Plan.CardId==shown&&issued.Plan.ConversionSource is null&&(issued.Plan.AdditionalConversionSources?.Count??0)==0&&declared is[{CardId:var declaredCard,CardKind:var declaredKind,SourceSeat:0}]&&declaredCard==shown&&declaredKind==issued.Kind&&paid is[{From.Zone:CardZoneKind.Hand,To.Zone:CardZoneKind.Processing}]&&paid[0].CardId==shown&&paid[0].ActionId==issued.ActionId&&accepted.All(a=>a.Type==CardActionType.Use&&a.PhysicalCards is[{From.Zone:CardZoneKind.Hand}]&&a.ConversionChain.Count==0)&&E<HuiyunNativeReturnedEvent>(g).Length==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1,"Every natural follow-up uses its real Hand cost and returns through its whole native basic/trick/delayed/equipment continuation exactly once. "+Diagnostic(g));
            if(scene=="slash")Need(E<CardUseDebitRecordedEvent>(g).Count(e=>e.Debit.Query==SkillRuleQuery.SlashLimit)==1&&!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"The exact assisted Slash neither needs a new ordinary allowance nor spends another one, while later unrelated Slashes remain limited.");
            if(scene=="delayed")Need(V(g,1).Judgment.Any(c=>c.Id==shown),"The original delayed card is truly placed in its legal target's judgment zone.");
            if(scene=="global")Need(E<TargetsConfirmedEvent>(g).Single(e=>e.ResolutionId==issued.UseId).TargetSeats.Count==4,"A true global native use keeps every implicit living target.");
            if(scene=="equipment")Need(V(g,0).Equipment.Any(c=>c.Id==shown),"A naturally used displayed equipment remains in the actor's actual equipment zone.");
            if(scene=="nested")Need(E<HuiyunUseIssuedEvent>(g).Length==1&&E<CardUseDeclaredEvent>(g).Count(e=>e.CardKind==CardKind.FireAttack)==2,"An inner natural FireAttack has its own genuine native use and never inherits the outer conversion benefit.");
            _=Cold(g,r);
        }
        {
            var(g,r)=Start("nested-last");Use(g,"give",V(g,0).Hand.Take(4).Select(c=>c.Id).ToArray(),[1]);Play(g);Need(V(g,0).HandCount==2,"The actual outer conversion starts with exactly two Hand entities and no draw-pile remainder.");
            Begin(g,0);Reach(g,()=>Option(g));PickOption(g,1);Reach(g,()=>HandOffer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use"&&c.Targets.SequenceEqual([1]));
            Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.FireAttackDiscard});var native=E<HuiyunNativeIssuedEvent>(g).Single().Invoice;Need(V(g,0).HandCount==1,"The natural FireAttack's last-Hand payment has already drained its genuine Lianying draw before requesting a same-suit effect discard.");g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
            var facts=g.Events.Select(e=>e.Payload).ToList();var damage=facts.FindIndex(e=>e is DamageAppliedEvent{SourceSeat:0,TargetSeat:1,Amount:1});var returned=facts.FindIndex(e=>e is HuiyunNativeReturnedEvent e2&&e2.Invoice.UseId==native.UseId);
            Need(damage>=0&&returned>damage&&facts.Take(damage).OfType<ProgramBindingResolvedEvent>().Count(e=>e.SkillId=="classic:lianying")==2&&E<HuiyunEffectPaymentIssuedEvent>(g).Single().Receipt.UseId==native.UseId&&E<HuiyunUseIssuedEvent>(g).Length==1,"Both the natural FireAttack's initial last-Hand loss and its independent effect discard finish real Lianying children before damage and the outer recast, without fabricating another Huiyun origin.");_=Cold(g,r);
        }
    }
    public static void HuiyunOriginalShownEntityConsentAndPrivacyCold()
    {
        foreach(var scene in new[]{"decline","shown-discard","same-card","identity","return","privacy","reused-material"})
        {
            var(g,r)=Start(scene);
            if(scene=="return"){Use(g,"give",V(g,0).Hand.Take(4).Select(c=>c.Id).ToArray(),[1]);Play(g);Need(V(g,0).HandCount==2,"Native transfers leave exactly two real own Hand entities with an exhausted draw pile.");}
            var originalMaterial=scene=="reused-material"?V(g,0).Hand[0].Id:(int?)null;Begin(g,scene=="privacy"?1:0,originalMaterial);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.FireAttackReveal}||Option(g));
            int shown;
            if(P(g)is{Kind:DecisionKind.FireAttackReveal}){shown=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();Answer(g,c=>c.Cards.SequenceEqual([shown]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.FireAttackDiscard});Answer(g,c=>scene is"shown-discard"or"return"or"reused-material"?c.Cards.SequenceEqual([shown]):c.Cards.Count==0);}
            else shown=Current(g)!.Opportunities[0].ShownCardId!.Value;
            Reach(g,()=>Option(g));var option=scene is"same-card"or"reused-material"?2:1;PickOption(g,option);Reach(g,()=>HandOffer(g));
            if(scene=="privacy")
            {
                var p=P(g)!;Need(p.PlayerSeat==1&&g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(2).PendingDecision is null,"The real foreign target alone sees its natural Hand menu; issuer and peers receive no private Hand identities.");var before=State(g);Need(!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&before==State(g),"The issuer cannot answer the foreign target's real private choice.");g=Cold(g,r);Play(g);Need(E<HuiyunNativeIssuedEvent>(g).Single().Invoice.ActorSeat==1,"A real AI target chooses and pays its own natural entity through native advancement.");continue;
            }
            if(scene=="shown-discard"){Need(P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("choice")!="use"),"The old shown card in Discard cannot be fetched or replaced by another same-name Hand card.");Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="decline");}
            else if(scene=="decline"){g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="decline");}
            else
            {
                if(scene=="return")Need(V(g,0).Hand.Any(c=>c.Id==shown)&&g.CardMovements.Any(m=>m.CardId==shown&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle),"Native Lianying really reshuffles the sole discarded original shown card back into its original Hand before Huiyun checks it.");
                if(scene=="identity")Need(P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("choice")=="use").All(c=>!c.Targets.SequenceEqual([1])),"An optional Wusheng conversion cannot turn a naturally unusable displayed red Dodge into an assisted Slash.");
                if(scene=="reused-material")Need(V(g,0).Hand.Any(c=>c.Id==originalMaterial),"Real Jianxiong obtains the original converted FireAttack's printed Slash material before Huiyun naturally uses it again.");
                g=Cold(g,r);if(scene=="identity")Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="decline");else if(scene=="reused-material")Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use"&&c.Cards.SequenceEqual([originalMaterial!.Value])&&c.Targets.SequenceEqual([1]));else Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use"&&c.Cards.SequenceEqual([shown]));
            }
            Play(g);
            if(scene is"decline"or"shown-discard"or"identity")Need(E<HuiyunNativeIssuedEvent>(g).Length==0&&E<CardRecastEvent>(g).Length==0&&E<HuiyunOptionSpentEvent>(g).Length==1,"Decline or unusable original material spends the chosen option but fabricates neither a native Use nor a recast.");
            if(scene=="same-card")Need(E<HuiyunNativeIssuedEvent>(g).Single().Invoice.CardId==shown&&E<CardRecastEvent>(g).Length==0,"Option2 using the shown entity never recasts a replacement card from the remaining Hand.");
            if(scene=="reused-material")Need(E<HuiyunPaymentRecordedEvent>(g).Where(e=>e.CardId==originalMaterial).Select(e=>e.UseId).Distinct().Count()==2&&E<HuiyunPaymentRecordedEvent>(g).Where(e=>e.CardId==originalMaterial).Select(e=>e.Sequence).Distinct().Count()==2&&E<HuiyunNativeIssuedEvent>(g).Single().Invoice.CardId==originalMaterial&&E<HuiyunNativeReturnedEvent>(g).Length==1,"The same physical Slash has two real native Uses and distinct once-paid movement invoices; its later reuse never invalidates or borrows the original FireAttack payment.");
            if(scene=="return"){var facts=g.Events.Select(e=>e.Payload).ToList();var recast=facts.FindIndex(e=>e is HuiyunRecastCardEvent);Need(recast>=0&&facts.Take(recast).OfType<ProgramBindingResolvedEvent>().Count(e=>e.SkillId=="classic:lianying")==2&&E<HuiyunRecastCardEvent>(g).Length==1,"The original effect's last-Hand child returns its shown entity, then the natural equipment's separate last-Hand child finishes before one real remaining Hand entity is recast.");}
            _=Cold(g,r);
        }
    }
    public static void HuiyunEquipmentCostRecastChildrenAndPaidLossDeathCold()
    {
        foreach(var scene in new[]{"lion","ox","weapon","paid-loss","paid-death"})
        {
            var(g,r)=Start(scene);int? grain=null;int? equipment=null;
            if(scene is"lion"or"ox")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);equipment=V(g,0).Equipment.Single().Id;
                if(scene=="lion"){Use(g,"wound");Play(g);}
                else{grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);Need(V(g,0).WoodenOxGrain!.Any(c=>c.Id==grain)&&(g.CreateSnapshot(1).Players[0].WoodenOxGrain?.Count??0)==0,"The real Ox grain is private before paying the equipped Ox as the original FireAttack material.");}
            }
            if(scene=="paid-loss"){Use(g,"wound");Play(g);}
            if(scene=="weapon"){Use(g,"weapon");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.ContainsKey("advanced-value"))==true);Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:hb-reserve");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);equipment=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;}
            Begin(g,scene.StartsWith("paid-",StringComparison.Ordinal)?1:0,equipment);
            if(scene=="weapon"){Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.FireAttackReveal});Answer(g,c=>c.Cards.Count==1);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.FireAttackDiscard});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);}
            if(scene is"lion"or"ox")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(!E<HuiyunStartedEvent>(g).Any(),"The original HE payment drains its actual Lion/Ox child before whole-FireAttack bonus issuance.");g=Cold(g,r);Continue(g);}
            Reach(g,()=>Option(g));
            if(scene=="weapon")
            {
                Need(g.CardMovements.Count(m=>m.CardId==equipment&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.Use)==1&&E<FireAttackResolvedEvent>(g).Any(e=>e.CausedDamage&&e.MatchingDiscardCardId is not null)&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==0&&e.Amount==1)&&E<HuiyunPaymentRecordedEvent>(g).Count(e=>e.CardId==equipment&&!e.Followup)==1,"A genuinely generated equipped GeneralWeapon pays exactly once to OutsideGame and still completes actual FireAttack reveal, matching discard and fire damage.");
                g=Cold(g,r);PickOption(g,3);Reach(g,()=>HandOffer(g));Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="draw");Play(g);Need(E<HuiyunCompletedEvent>(g).Length==1&&E<HuiyunNativeIssuedEvent>(g).Length==0&&E<HuiyunMovementPaidEvent>(g).Any(e=>e.Stage==HuiyunStage.DrawChildren&&e.Count==1),"The real damaging GeneralWeapon conversion returns once and pays a legal option3 draw without inventing a use of its already-discarded shown entity.");_=Cold(g,r);continue;
            }
            PickOption(g,1);Reach(g,()=>HandOffer(g));g=Cold(g,r);
            if(P(g)!.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("choice")=="use");
            if(scene=="paid-death")Reach(g,()=>!V(g,0).IsAlive);
            if(scene.StartsWith("paid-",StringComparison.Ordinal))Reach(g,()=>E<HuiyunCompletedEvent>(g).Length==E<HuiyunStartedEvent>(g).Length);else Play(g);
            var native=E<HuiyunNativeIssuedEvent>(g).Single().Invoice;
            Need(E<HuiyunNativeReturnedEvent>(g).Length==1&&E<HuiyunRecastCardEvent>(g).Length>0&&E<HuiyunRecastCardEvent>(g).All(e=>g.CardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==CardLocation.Hand(e.HolderSeat)&&m.Reason==CardMoveReasons.RecastDiscard))&&E<CardRecastEvent>(g).Length==E<HuiyunRecastCardEvent>(g).Length,"The accepted native card completes before the remaining true Hand is atomically recast; real recast cost/draw events happen once.");
            if(scene=="lion")Need(E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"Converting the real equipped Lion preserves native removal recovery.");
            if(scene=="ox")Need(g.CardMovements.Any(m=>m.CardId==grain&&m.Reason.Value=="equipment.wooden-ox.grain-discard")&&!E<HuiyunRecastCardEvent>(g).Any(e=>e.CardId==grain),"Ox spill is a true native child and never an extra recast primary card.");
            if(scene=="paid-loss")Need(!V(g,0).Skills!.Any(s=>s.Id==Huiyun)&&V(g,1).IsAlive&&native.ActorSeat==1,"The selected option and living target's genuine use/recast survive loss of their issuer's HP-conditional skill source.");
            if(scene=="paid-death")Need(!V(g,0).IsAlive&&V(g,1).IsAlive&&native.ActorSeat==1&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0),"The already-paid option survives actual non-Lord issuer death and returns the living target's native use and recast exactly once.");
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static HuiyunReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.Huiyun is not null)?.Huiyun;
    private static bool Option(GameEngine g)=>Current(g)is{Stage:HuiyunStage.OptionChoice}&&P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.ContainsKey("option"));
    private static bool HandOffer(GameEngine g)=>Current(g)is{Stage:HuiyunStage.HandUseChoice}&&P(g)?.Choices.Any(c=>c.Parameters.ContainsKey("choice"))==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException("scene="+Scene+" "+why);}
    private static void Accept(GameEngine g,GameCommand c){try{var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(x.Accepted,x.Error?.Message??"Rejected actual Hu Ban command.");}catch(Exception e){throw new InvalidOperationException("scene="+Scene+" command="+c.GetType().Name+" cause="+e.Message+" "+Diagnostic(g),e);}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> match){var p=P(g)!;var c=p.Choices.FirstOrDefault(match);Need(p.PlayerSeat==0&&c is not null,"Missing published human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? cards=null,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> match){var a=g.GetHumanLegalActions().FirstOrDefault(match);Need(a is not null,"Missing native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Begin(GameEngine g,int target,int? material=null)=>Native(g,a=>a.ConversionSource is{SkillId:Huiyun,BindingId:"huiyun-fire-attack"}&&a.TargetSeats.SequenceEqual([target])&&(material is null||a.CardId==material));
    private static void PickOption(GameEngine g,int i){Need(Option(g),"The issuer has a true option menu.");Answer(g,c=>c.Parameters.GetValueOrDefault("option")==i.ToString());}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)
    {
        for(var i=0;i<300;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Ended before {caller}:{line}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind==DecisionKind.FireAttackReveal)Answer(g,c=>c.Cards.Count==1);
            else if(p.Kind is DecisionKind.FireAttackDiscard or DecisionKind.RespondDodge or DecisionKind.RespondSlash)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.SkillPrompt?.SkillId==Watch)Continue(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="bingzhong-target"))Answer(g,c=>c.Targets.SequenceEqual([1])||c.Targets.Count==0);
            else if(p.SkillPrompt?.SkillId is"ol:fengzi"or"classic:lianying"or"classic:jianxiong")Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate"||c.Parameters.GetValueOrDefault("branch")=="cost");
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Kind==DecisionKind.SelectHarvestCard)Answer(g,c=>c.Cards.Count==1);
            else throw new InvalidOperationException($"Unexpected {caller}:{line}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded {caller}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.Huiyun?.Stage,Index=(f as ProgramSkillFrame)?.Huiyun?.Index}),Facts=g.Events.TakeLast(3).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves every private native menu, original effect/reveal, round spend and once-paid child cursor.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        Scene=scene;ContentRegistry r;try{r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));}catch(Exception e){throw new InvalidOperationException("scene="+scene+" fixture load cause="+e.Message,e);}var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="paid-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-hu-ban",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:hu-ban");if(scene=="repeated"){CharacterFixtureContent.Register(b,"ol:yang-fu");CharacterFixtureContent.Register(b,"ol:wang-rong");}if(scene is"return"or"nested-last")CharacterFixtureContent.Register(b,"classic:lu-xun");if(scene=="reused-material")CharacterFixtureContent.Register(b,"classic:cao-cao");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0},{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0}],"activations":[
            {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]},
            {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
            {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"give","minCards":1,"maxCards":20,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
            {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(scene=="paid-loss")defs.Add("""{"id":"fixture:hb-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-huiyun","skillId":"ol:huiyun","minimumHp":12}]}""");
            if(scene=="lion")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-lion-recovery","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"recovery"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""");
            if(scene=="ox")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-grain-spill","window":"cardsMoved","subject":"owner","sourceZones":["woodenOxGrain"],"movementOccurrence":"perBatch","movementReasons":["equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grain","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]}""");
            if(scene.StartsWith("paid-",StringComparison.Ordinal))defs.Add($$"""{"id":"fixture:hb-equipment-child","revision":1,"triggers":[{"id":"real-paid-native-equipment","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"loseHp","target":"selectedTarget","amount":{{(scene=="paid-death"?20:1)}}}]}]}""");
            var labels=defs.Select(x=>JsonNode.Parse(x)!).ToDictionary(x=>x["id"]!.GetValue<string>(),x=>new{name=x["id"]!.GetValue<string>(),description="胡班原生小夹具",optionLabels=x["id"]!.GetValue<string>()==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()});var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实晖云边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:hb-pick","实际本人","固定公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000d)});var own=new List<string>{Driver,scene=="paid-loss"?"fixture:hb-conditional":Huiyun};if(scene=="repeated")own.AddRange(["ol:bingzhong","ol:fengzi"]);if(scene=="identity")own.Add("standard:wusheng");if(scene is"return"or"nested-last")own.Add("classic:lianying");if(scene=="reused-material")own.Add("classic:jianxiong");if(scene is"lion"or"ox")own.Add(Watch);b.AddGeneral(new(Owner,"实际胡班","ol-hu-ban","fixture:hb-pick","wei",12,own,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>"fixture:hb-peer-"+i).ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter",Quiet,"wei",12,scene.StartsWith("paid-",StringComparison.Ordinal)?["fixture:hb-equipment-child"]:[]));
            if(scene=="weapon"){b.AddSkill(new("fixture:hb-unused","实际备用","固定公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-200000d)});b.AddGeneral(new("fixture:hb-reserve","实际武将武器","supporter","fixture:hb-unused","wei",4,[]));}
            var kind=scene switch{"slash"or"reused-material"=>CardKind.Slash,"delayed"=>CardKind.SupplyShortage,"global"=>CardKind.FiveGrains,"draw-two"=>CardKind.DrawTwo,"chain"=>CardKind.IronChain,"nested"or"nested-last"or"repeated"=>CardKind.FireAttack,"identity"=>CardKind.Dodge,"lion"=>CardKind.SilverLion,"ox"=>CardKind.WoodenOx,_=>CardKind.Crossbow};b.AddCard(new("fixture:hb-card","原生"+kind,CardCatalog.Get(kind).CategoryName,"真实实体",kind));b.AddDeck(new("fixture:hb-deck","真实同质固定牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene is"return"or"nested-last"?18:128).Select(_=>new ContentDeckPhysicalCard("fixture:hb-card",scene=="identity"?Suit.Heart:Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"胡班真实行为",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:hb-deck",GeneralCandidateCount:scene=="weapon"?5:4,GeneralPoolIds:scene=="weapon"?[Owner,..peers,"fixture:hb-reserve"]:[Owner,..peers]));
        }
    }
}
