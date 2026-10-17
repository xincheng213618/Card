using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunHaoChecks
{
    private const string Can="ol:sun-hao-canshi",Chou="ol:chouhai",Gui="ol:guiming",Driver="fixture:sh-driver",Owner="fixture:sh-owner",Quiet="fixture:sh-quiet",Watch="fixture:sh-watch",Health="fixture:sh-health",Before="fixture:sh-before",Counter="fixture:sh-counter";
    private const string Mode="identity:classic-sun-hao-fixture",Legacy="identity:sun-hao-legacy-fixture";
    public static void CanshiNormalDrawAdditiveIndependentGrantsAndActualUsesCold()
    {
        foreach(var scene in new[]{"zero","decline","skip","replacement","additive","stack","slash","trick","nullification","played","legacy"})
        {
            var(g,r)=Start(scene);
            if(scene is"decline"or"skip"or"replacement")
            {
                if(scene=="decline"){Reach(g,()=>Optional(g,Can));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");}
                if(scene=="replacement"){Reach(g,()=>Optional(g,"classic:zaiqi"));g=Cold(g,r);Activate(g);}
                Play(g);Need(E<SunHaoCanshiGrantedEvent>(g).Length==0&&V(g,0).HandCount==(scene=="decline"?6:scene=="skip"?4:5),"A real decline, skipped normal Draw, or completed higher-priority replacement produces no Canshi grant and preserves its exact native draw count.");_=Cold(g,r);continue;
            }
            if(scene=="legacy")
            {
                Reach(g,()=>Optional(g,Can));var beforeDraw=V(g,0).HandCount;Activate(g);
                Reach(g,()=>Optional(g,Counter));Need(V(g,0).HandCount==beforeDraw+3&&E<SunHaoCanshiGrantedEvent>(g).Single().X==1,"The genuine PlayStarting legacy producer runs after the accepted normal Draw and its frozen Canshi grant.");
                Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);
                Reach(g,()=>E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId==Counter&&e.Completed));Play(g);
                Need(E<SunHaoCanshiStartedEvent>(g).Length==1&&E<SunHaoCanshiDiscardedEvent>(g).Length==1&&E<CardUseDeclaredEvent>(g).Any(e=>e.CardId==0&&e.SourceSeat==0),"The real legacy zero-entity Use enters and returns from its original committed-use cost before ordinary Play starts.");_=Cold(g,r);continue;
            }
            if(scene=="stack")
            {
                Reach(g,()=>Optional(g,"ol:hongji"));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="draw")==true);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="draw");
            }
            if(scene!="zero")
            {
                Reach(g,()=>Optional(g,Can));var original=V(g,0).HandCount;g=Cold(g,r);Activate(g);
                if(scene=="stack"){Reach(g,()=>Optional(g,Can)&&E<SunHaoCanshiGrantedEvent>(g).Length==1);g=Cold(g,r);Activate(g);}
                Play(g);
                var grants=E<SunHaoCanshiGrantedEvent>(g);Need(grants.Length==(scene=="stack"?2:1)&&grants.All(e=>e.X==1),scene+": each real normal Draw freezes the living wounded count once.");
                Need(V(g,0).HandCount==original+(scene=="stack"?6:scene=="additive"?4:3),scene+": Canshi adds X to the actual normal two-card draw and other additive modifiers.");
            }
            else{Play(g);Need(E<SunHaoCanshiGrantedEvent>(g).Length==0,"Zero wounded characters do not publish a useless Canshi offer or obligation.");continue;}
            var hand=V(g,0).HandCount;
            if(scene is"slash"or"additive"or"stack")
            {
                if(scene=="stack")Use(g,"zero",[1]);else Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([1]));
                Reach(g,()=>Cost(g));Need(g.CreateSnapshot(1).PendingDecision is null&&P(g)!.Choices.All(c=>c.Cards.Count==1),"Only the real owner sees the private HE cost entities.");
                var state=State(g);Need(!g.Submit(new AnswerPromptCommand(1,P(g)!.PromptId,P(g)!.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"A peer cannot answer or advance the owner's private Canshi choice.");
                g=Cold(g,r);Pay(g);
                if(scene=="stack"){Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);}
                Play(g);var started=E<SunHaoCanshiStartedEvent>(g).Single();var paid=E<SunHaoCanshiDiscardedEvent>(g).Where(e=>e.FrameId==started.FrameId).ToArray();
                Need(paid.Length==(scene=="stack"?2:1)&&paid.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,paid.Length))&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==started.UseId)==1,"Each independent original grant pays once before one genuine whole native Slash, including a zero-material Use.");
            }
            else if(scene=="trick")
            {Native(g,a=>a.Kind==LegalActionKind.DrawTwo);Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);Play(g);Need(E<SunHaoCanshiDiscardedEvent>(g).Length==1&&V(g,0).HandCount==hand,"An actual ordinary trick pays one separate HE card before drawing its native two cards.");}
            else if(scene=="nullification")
            {
                End(g);Reach(g,()=>Optional(g,Counter));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Targets.SequenceEqual([1]));
                Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);
                Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);Reach(g,()=>E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId==Counter&&e.Completed));
                Need(E<SunHaoCanshiStartedEvent>(g).Length==1,"A true native Nullification Use owes its independent committed-use cost.");
            }
            else if(scene=="played")
            {
                Use(g,"duel",[1]);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Play(g);
                Need(E<DuelResponseEvent>(g).Any(e=>e.ResponderSeat==0&&e.UsedSlash&&e.SlashCardId is >0)&&E<SunHaoCanshiStartedEvent>(g).Length==0,
                    "A genuine physical-material Slash played to answer the selected actor's native Duel does not issue a Canshi Use cost.");
            }
            else
            {
                End(g);Reach(g,()=>Optional(g,Counter));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.SequenceEqual([1]))==true);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);
                Reach(g,()=>E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId==Counter&&e.Completed));Need(E<SunHaoCanshiStartedEvent>(g).Length==1&&E<CardUseDeclaredEvent>(g).Any(e=>e.CardId==0&&e.SourceSeat==0),"The actual legacy zero-entity Use enters its original committed cost window without fake physical material.");
            }
            _=Cold(g,r);
        }
    }
    public static void CanshiNativeEquipmentChildrenDistanceAndPaidLossDeathCold()
    {
        foreach(var scene in new[]{"lion","ox","weapon","loss","death","no-card"})
        {
            var(g,r)=Start(scene);Reach(g,()=>Optional(g,Can));Activate(g);Play(g);int? equipment=null,grain=null;
            if(scene is"lion"or"ox"or"weapon")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);equipment=V(g,0).Equipment.Single().Id;
                if(scene=="ox"){grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);Need((g.CreateSnapshot(1).Players[0].WoodenOxGrain?.Count??0)==0&&V(g,0).WoodenOxGrain?.Any(c=>c.Id==grain)==true,"The actual native Ox grain stays private before a real discard.");}
            }
            if(scene=="no-card")Use(g,"zero-empty",[1]);else Native(g,a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([scene=="weapon"?2:1]));
            if(scene=="no-card")
            {Play(g);Need(E<SunHaoCanshiCompletedEvent>(g).Single().Paid==false&&E<SunHaoCanshiSkippedEvent>(g).Length==1&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0),"An issued obligation with no legal HE does not cancel or manufacture payment for the genuine Use.");_=Cold(g,r);continue;}
            Reach(g,()=>Cost(g));g=Cold(g,r);if(equipment is{} id)Answer(g,c=>c.Cards.SequenceEqual([id]));else Pay(g);
            if(scene=="lion")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(!E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0)&&E<SunHaoCanshiDiscardedEvent>(g).Length==1,"The genuinely discarded Lion completes native recovery/health children before the original Slash effect.");g=Cold(g,r);Continue(g);
            }
            if(scene=="loss")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(V(g,0).Skills!.All(s=>s.ContentId!=Can),"A real discard child crosses the conditional source's HP boundary.");g=Cold(g,r);Continue(g);}
            if(scene=="death")
            {Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SunHaoCanshi is not null));Need(E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&E<SunHaoCanshiCompletedEvent>(g).Length==1&&E<SunHaoCanshiDiscardedEvent>(g).Length==1,"Real non-Lord death drains the once-paid native discard subtree without an orphan Canshi frame.");}
            else Play(g);
            if(scene=="ox")Need(g.CardMovements.Count(m=>m.CardId==grain&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile)==1,"Discarding the actual Ox spills its original private grain exactly once through native cleanup.");
            if(scene=="weapon")Need(V(g,0).Equipment.All(c=>c.Id!=equipment)&&g.GetAttackRange(0)==1&&E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==2),"The already-confirmed distance-two target remains entitled after the actual weapon cost; no new target receives this exception.");
            if(scene=="loss")
            {Use(g,"zero",[1]);Reach(g,()=>Cost(g));g=Cold(g,r);Pay(g);Play(g);Need(E<SunHaoCanshiStartedEvent>(g).Length==2&&E<SunHaoCanshiDiscardedEvent>(g).Length==2,"The original actual-turn obligation still creates and pays the next real Use cost after skill-instance loss.");}
            _=Cold(g,r);
        }
    }
    public static void ChouhaiRecipientFinalizationChainLightningAndArmorCold()
    {
        foreach(var scene in new[]{"empty","filled","before-fill","before-empty","damage-lion","lightning","chain","chain-legacy","cangxin"})
        {
            var(g,r)=Start(scene);Play(g);
            if(scene=="cangxin")
            {
                Use(g,"empty");Play(g);foreach(var seat in new[]{3,0,1}){Use(g,"chain",[seat]);Play(g);}
                Use(g,"fire-other",[3]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:cangxin"&&P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option")=="pick")==true);
                var native=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LuYuSheng?.Stage==LuYuShengStage.BottomChoice);
                Need(native.LuYuSheng!.OriginalDamage==2&&V(g,0).HandCount==0,"The genuine middle chained recipient freezes empty-hand +1 before its later Cangxin payment.");
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="pick"&&c.Cards.Count==1);Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="finish");Play(g);
                var hits=E<DamageAppliedEvent>(g).Where(e=>e.Nature==DamageNature.Fire).ToArray();
                Need(hits.Select(e=>e.TargetSeat).SequenceEqual([3,0,1])&&hits.Select(e=>e.Amount).SequenceEqual([1,1,1])&&
                    E<LuYuShengDamageAdjustedEvent>(g).Single().Hearts==1&&!E<RecipientScopedDamageBaseEvent>(g).Any(),
                    "Without SignedDamagePayments, native legacy propagation unwinds the later one-Heart reduction before Chouhai, so the third role inherits the original one damage.");_=Cold(g,r);continue;
            }
            if(scene=="lightning")
            {
                Native(g,a=>a.Kind==LegalActionKind.Lightning);Play(g);Use(g,"order");Reach(g,()=>P(g)?.Kind==DecisionKind.ProgramTopReorder);
                var viewed=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.TopReorder is not null).TopReorder!.ViewedCardIds.ToArray();
                var ownerView=g.CreateSnapshot(0);var revealed=ownerView.PrivateRevealedCards;Need(viewed.Length==8&&revealed is not null&&viewed.All(id=>revealed.Any(c=>c.Id==id&&c.Suit==Suit.Spade&&c.Rank is>=2 and<=9))&&Enumerable.Range(1,3).All(seat=>g.CreateSnapshot(seat).PendingDecision is null&&(g.CreateSnapshot(seat).PrivateRevealedCards?.All(c=>!viewed.Contains(c.Id))??true)),"The actual private topdeck operation fixes original Spade2..9 judgment entities without searching seeds or manufacturing damage.");
                g=Cold(g,r);foreach(var id in viewed)Answer(g,c=>c.Cards.SequenceEqual([id])&&c.Parameters.GetValueOrDefault("action")=="top");Play(g);Use(g,"empty");Play(g);End(g);
                Reach(g,()=>E<LightningResolvedEvent>(g).Any(e=>e.JudgmentTargetSeat==0));
                var lightning=E<LightningResolvedEvent>(g).Last(e=>e.JudgmentTargetSeat==0);Need(lightning.Hit,"The original Spade judgment really hits: "+JsonSerializer.Serialize(new{lightning,Judgments=E<JudgmentResolvedEvent>(g).TakeLast(2),Damage=E<DamageAppliedEvent>(g).TakeLast(3)}));
                Reach(g,()=>E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Nature==DamageNature.Thunder));
                var damage=E<DamageAppliedEvent>(g).Last(e=>e.TargetSeat==0);Need(damage.Amount==4&&damage.SourceLess,"A genuine source-less delayed Lightning receives the empty-hand increment before native judgment returns: "+JsonSerializer.Serialize(new{lightning,damage,Modifiers=E<SunHaoChouhaiFrozenEvent>(g)}));_=Cold(g,r);continue;
            }
            if(scene.StartsWith("chain",StringComparison.Ordinal))
            {
                var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&g.State.CurrentSeat==0&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
                Need(Enumerable.Range(1,3).All(s=>V(g,s).HandCount==0),"The real AI draw/discard instructions prepare empty hands without direct state mutation.");
                Use(g,"give",[2],[V(g,0).Hand[0].Id]);Play(g);Need(E<DuFuRenBlackIssuedEvent>(g).Count(e=>e.RecipientSeat==2)==1&&V(g,2).HandCount==0,"The received real black entity issues Yise, then its real gain child discards it before the fire Slash.");
                foreach(var seat in new[]{1,2,3}){Use(g,"chain",[seat]);Play(g);}
                Native(g,a=>a.Kind==LegalActionKind.Slash&&a.PlayedCardKind==CardKind.FireSlash&&a.TargetSeats.SequenceEqual([1]));g=Cold(g,r);Play(g);
                var damage=E<DamageAppliedEvent>(g).Where(e=>e.Nature==DamageNature.Fire&&e.TargetSeat is 1 or 2 or 3).ToArray();
                Need(damage.Select(e=>e.Amount).SequenceEqual([2,4,3])&&E<DuFuRenBlackConsumedEvent>(g).Length==1,"The first finalized +1 propagates as the native base; later independent Yise/Chouhai increments are removed before the third chained recipient in formal and legacy paths.");_=Cold(g,r);continue;
            }
            if(scene=="damage-lion"){Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);}
            if(scene is"empty"or"before-fill"or"damage-lion") {Use(g,"empty");Play(g);}
            var hp=V(g,0).Hp;Use(g,"damage");
            if(scene is"before-fill"or"before-empty")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Before);var frozen=E<SunHaoChouhaiFrozenEvent>(g).Length;g=Cold(g,r);Continue(g);Play(g);Need(E<SunHaoChouhaiFrozenEvent>(g).Length==frozen,"The same native damage does not reissue or recalculate its empty-hand modifier after a BeforeDamage child or cold resume.");}
            else Play(g);
            var expected=scene=="damage-lion"?1:scene is"empty"or"before-fill"?2:1;
            Need(hp-V(g,0).Hp==expected&&E<DamageAppliedEvent>(g).Last(e=>e.TargetSeat==0).Amount==expected,"Chouhai freezes first-finalizer hand state and the real Lion clamps the combined damage after +1.");
            Need(scene!="before-fill"||V(g,0).HandCount==1,"The real BeforeDamage child gains a card after the issued empty-hand increment.");Need(scene!="before-empty"||V(g,0).HandCount==0,"The real BeforeDamage child empties Hand after a nonempty finalization without adding a late bonus.");_=Cold(g,r);
        }
    }
    public static void GuimingRuleWoundedQueriesRolesFactionAndActualTurnCold()
    {
        foreach(var scene in new[]{"guiming","guiming-faction","guiming-role","guiming-peach","guiming-tail","guiming-loss"})
        {
            var(g,r)=Start(scene);Play(g);var treated=scene is not("guiming-faction"or"guiming-role");
            Need(Enumerable.Range(1,3).All(s=>RuleWounded(g,s)==treated)&&!RuleWounded(g,0),"Only other current Wu roles under the actually qualified Lord skill are treated wounded during its owner's actual turn.");
            Need(Enumerable.Range(1,3).All(s=>V(g,s).Hp==V(g,s).MaxHp)&&Enumerable.Range(1,3).All(s=>V(g,s).MaxHp-V(g,s).Hp==0),"The treated predicate never changes actual HP, maximum HP or recovery space.");
            var effectCount=E<ProgramBindingResolvedEvent>(g).Count(e=>e.SkillId=="fixture:sh-predicates"&&e.BindingId=="legacy-wounded-trigger"&&e.Completed);
            Need(effectCount==(treated?3:0)&&(!treated||Enumerable.Range(1,3).All(seat=>V(g,seat).HandCount>=6)),"The generic wounded effect and the legacy CurrentHp<MaxHp trigger both use the same frozen rule predicate for foreign Wu owners: "+JsonSerializer.Serialize(new{scene,effectCount,Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId=="fixture:sh-predicates"),Players=Enumerable.Range(0,4).Select(seat=>new{seat,V(g,seat).Hp,V(g,seat).MaxHp,V(g,seat).HandCount,RuleWounded=RuleWounded(g,seat)})}));
            if(treated)Need(V(g,0).Skills!.Any(s=>s.ContentId=="boundary:xunxun"),"Tang Zi's real wounded-population grant and Guiming coexist on the current owner without recursive shard evaluation.");
            if(treated){Use(g,"wounded-target");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target")==true);Need(P(g)!.Choices.Where(c=>c.Targets.Count>0).SelectMany(c=>c.Targets).Distinct().Order().SequenceEqual([1,2,3]),"OtherWounded uses the shared predicate while preserving normal target identity/privacy.");g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);}
            else {var old=State(g);Need(!g.Submit(new UseProgramSkillCommand(0,Driver,"wounded-target",[],[],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==old,"The real OtherWounded activation is unavailable when no actual or treated wounded target exists.");}
            if(scene=="guiming-peach")
            {
                Use(g,"hurt");Play(g);var recovered=E<RecoveryAppliedEvent>(g).Length;var hand=V(g,0).HandCount;Native(g,a=>a.Kind==LegalActionKind.Peach);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="bingzhong-target")==true);
                g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="finish")==true||P(g)?.Kind==DecisionKind.PlayCard);if(P(g)?.Kind!=DecisionKind.PlayCard)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");Play(g);
                Need(V(g,0).HandCount==hand-1&&E<RecoveryAppliedEvent>(g).Skip(recovered).All(e=>e.TargetSeat==0)&&V(g,1).Hp==V(g,1).MaxHp,"A genuine Peach Use may target a treated-wounded full-HP role, consumes its original material, and emits no phantom RecoveryApplied for that target.");
            }
            if(scene=="guiming-loss")
            {
                Use(g,"lose-guiming");Play(g);Need(V(g,0).Skills!.All(x=>x.ContentId!=Gui)&&Enumerable.Range(1,3).All(seat=>!RuleWounded(g,seat))&&V(g,0).Skills!.All(x=>x.ContentId!="boundary:xunxun"),
                    "Real native source removal immediately invalidates treated-wounded queries and the dependent population grant without altering anyone's HP.");g=Cold(g,r);
            }
            var turn=g.State.TurnNumber;End(g);
            if(scene=="guiming-tail")
            {Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&E<TurnEndedEvent>(g).Any());Need(g.State.TurnNumber==turn&&Enumerable.Range(1,3).All(s=>RuleWounded(g,s)),"The true post-TurnEnded Dianjun Play remains inside the same actual cursor and retains Guiming.");g=Cold(g,r);End(g);}
            Reach(g,()=>E<ActualTurnCursorReturnedEvent>(g).Any(e=>e.ActualTurn==turn&&e.OwnerSeat==0));Need(Enumerable.Range(1,3).All(s=>!RuleWounded(g,s)),"The actual cursor return expires Guiming before another real turn; raw TurnEnded alone does not.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("guiming-songsang");
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:sh-song-kill"&&P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target")==true);
            var lord=Enumerable.Range(0,4).Single(seat=>V(g,seat).Role==Role.Lord);var hp=V(g,0).Hp;var maximum=V(g,0).MaxHp;
            Need(lord!=0&&g.State.CurrentSeat==lord&&g.State.Phase==TurnPhase.Finished&&hp==maximum&&RuleWounded(g,0),
                "The real foreign Lord's post-TurnEnded cursor treats a full-HP Wu human as wounded before the native death producer.");
            var victim=P(g)!.Choices.Where(c=>c.Targets.Count==1&&c.Targets[0]!=lord&&c.Targets[0]!=0).Select(c=>c.Targets[0]).First();
            g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([victim]));Reach(g,()=>Optional(g,"ol:songsang"));
            Need(!V(g,victim).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==victim)&&V(g,0).Hp==maximum,"A genuine non-Lord other death offers the real human's unused Songsang while its full HP remains physical.");
            g=Cold(g,r);Activate(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="fixture:sh-song-watch");
            var song=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="ol:songsang"&&f.SpPangTong is not null);var receipt=song.SpPangTong!;
            Need(receipt.Stage=="grant-children"&&receipt.RuleWounded==true&&receipt.Wounded&&receipt.HpBefore==receipt.MaximumBefore&&receipt.HpBefore==hp&&
                E<SongsangHealthIssuedEvent>(g).Count(e=>e.FrameId==song.Id&&e.RuleWounded==true&&e.Wounded&&e.HpBefore==e.MaximumBefore)==1&&V(g,0).Skills!.Any(x=>x.ContentId=="ol:zhanji"),
                "The real SkillsChanged child pauses after Zhanji acquisition with Songsang's frozen treated-wounded receipt, rather than its physical full-HP maximum increase branch.");
            g=Cold(g,r);Continue(g);Reach(g,()=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="ol:songsang")&&E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId=="ol:songsang"&&e.Completed));
            Need(V(g,0).Hp==hp&&V(g,0).MaxHp==maximum&&!E<RecoveryAppliedEvent>(g).Any(e=>e.TargetSeat==0)&&V(g,0).Skills!.Any(x=>x.ContentId=="ol:zhanji")&&
                E<SongsangHealthIssuedEvent>(g).Single().RuleWounded==true,
                "Cold return preserves real full HP/max HP, emits no zero-space recovery, and retains the actual once-issued Zhanji grant from the treated-wounded branch.");_=Cold(g,r);
        }
    }
    private static bool RuleWounded(GameEngine g,int seat)
    {
        var players=(System.Collections.IList)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!;
        return (bool)typeof(GameEngine).GetMethod("IsRuleWounded",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,[players[seat]])!;
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Optional(GameEngine g,string id)=>P(g)?.SkillPrompt?.SkillId==id&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static bool Cost(GameEngine g)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="sun-hao-canshi")==true;
    private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Sun Hao native command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g);var c=p?.Choices.FirstOrDefault(pick);Need(p?.PlayerSeat==0&&c is not null,"Missing genuine human Sun Hao choice: "+Diag(g));Accept(g,new AnswerPromptCommand(0,p!.PromptId,c!.Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Pay(GameEngine g)=>Answer(g,c=>c.Cards.Count==1);
    private static void Use(GameEngine g,string id,int[]? targets=null,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> predicate){var a=g.GetHumanLegalActions().FirstOrDefault(predicate);Need(a is not null,"Missing native Sun Hao action: "+Diag(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},line);
    private static void Reach(GameEngine g,Func<bool> done,[CallerLineNumber]int line=0)
    {
        for(var i=0;i<220;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"Sun Hao fixture ended at "+line+": "+Diag(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(Cost(g))Pay(g);
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Continue(g);
            else if(p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);
            else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p.Kind==DecisionKind.SelectTargetCard)Answer(g,c=>true);
            else throw new InvalidOperationException("Unexpected Sun Hao boundary "+line+": "+Diag(g));
        }
        throw new InvalidOperationException("Missing bounded Sun Hao boundary "+line+": "+Diag(g));
    }
    private static string Diag(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Kind=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.SunHaoCanshi?.Stage}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay preserves each private HE menu, frozen draw grant and damage recipient, real native children and exact actual-turn expiry.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"death"or"guiming-role"or"guiming-songsang"?Role.Rebel:Role.Lord,ModeId=scene is"chain-legacy"or"cangxin"?Legacy:Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-sun-hao",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:sun-hao");var songsang=scene=="guiming-songsang";var guiming=scene.StartsWith("guiming",StringComparison.Ordinal)&&!songsang;var chain=scene.StartsWith("chain",StringComparison.Ordinal);
            var canshi=!guiming&&!songsang&&scene is not("empty"or"filled"or"before-fill"or"before-empty"or"damage-lion"or"lightning"or"chain"or"chain-legacy"or"cangxin");
            if(scene=="replacement")CharacterFixtureContent.Register(b,"classic:meng-huo");if(scene=="stack")CharacterFixtureContent.Register(b,"ol:zhang-shi-ping");if(guiming){CharacterFixtureContent.Register(b,"ol:tang-zi","boundary:li-dian");}if(scene=="guiming-tail")CharacterFixtureContent.Register(b,"ol:zhang-yi");if(scene=="guiming-peach")CharacterFixtureContent.Register(b,"ol:yang-fu");if(chain)CharacterFixtureContent.Register(b,"ol:du-fu-ren");if(scene=="cangxin")CharacterFixtureContent.Register(b,"ol:lu-yu-sheng");if(songsang)CharacterFixtureContent.Register(b,"ol:pang-tong-sp");
            var kind=scene=="trick"?CardKind.DrawTwo:scene is"lion"or"damage-lion"?CardKind.SilverLion:scene=="ox"?CardKind.WoodenOx:scene=="weapon"?CardKind.QinglongCrescentBlade:scene=="lightning"?CardKind.Lightning:scene=="guiming-peach"?CardKind.Peach:chain?CardKind.Slash:CardKind.Crossbow;
            static string A(string id,string effects,int targets=0,int cards=0,string targetKind="anyLiving")=>$$"""{"id":"{{id}}","minCards":{{cards}},"maxCards":{{cards}},{{(cards>0?"\"sourceZones\":[\"hand\"],":"")}}"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"{{targetKind}}","usesPerTurn":null,"effects":[{{effects}}]}""";
            var activations=new[]{A("lose-guiming","""{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:guiming"],"sourceBind":"standard:none"}"""),A("empty","""{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}"""),A("hurt","""{"op":"loseHp","target":"owner","amount":1}"""),A("damage","""{"op":"damage","target":"owner","amount":1}"""),
                A("zero","""{"op":"useVirtualSlash","target":"selectedTarget"}""",1,0,"otherLiving"),A("zero-empty","""{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"useVirtualSlash","target":"selectedTarget"}""",1,0,"otherLiving"),
                A("give","""{"op":"giveSelected","target":"selectedTarget"}""",1,1,"otherLiving"),A("chain","""{"op":"setChainedState","target":"selectedTarget","chained":true}""",1),A("wounded-target","""{"op":"selectTarget","target":"owner","targetKind":"otherWounded"},{"op":"draw","target":"selectedTarget","amount":1}"""),
                A("fire-other","""{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}""",1),A("order","""{"op":"reorderTopCards","target":"owner","amount":8,"exactTopCount":8}"""),A("incoming","""{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""",1,0,"otherLiving")};
            var view=new List<object>{new{id="slash",inputKinds=Array.Empty<string>(),inputSuits=Array.Empty<string>(),outputKind="slash",forPlay=true,forResponse=true,allowSameKind=true},new{id="null",inputKinds=Array.Empty<string>(),inputSuits=Array.Empty<string>(),outputKind="nullification",forPlay=false,forResponse=true}};
            if(chain)view.Add(new{id="fire",inputKinds=new[]{"slash"},inputSuits=Array.Empty<string>(),outputKind="fireSlash",forPlay=true,forResponse=false});
            var defs=new List<string>{$$"""{"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0},{"id":"slash-room","query":"slashLimit","operation":"add","value":4,"priority":0}],"viewAs":{{JsonSerializer.Serialize(view)}},"activations":[{{string.Join(',',activations)}}, {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]}""",
                $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(canshi&&scene!="zero")defs.Add("""{"id":"fixture:sh-hurt-start","revision":1,"triggers":[{"id":"real-injury","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]}""");
            if(scene=="skip")defs.Add("""{"id":"fixture:sh-skip","revision":1,"triggers":[{"id":"real-skip","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]}]}]}""");
            if(scene=="additive")defs.Add("""{"id":"fixture:sh-additive","revision":1,"triggers":[{"id":"actual-draw-plus-one","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":100,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":1}]}]}""");
            if(scene is"nullification"or"legacy")
            {
                var effects=scene=="legacy"?"""{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false}""":$$"""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"dismantlement"}""";
                defs.Add($$"""{"id":"{{Counter}}","revision":1,"triggers":[{"id":"real-play-ending-use","window":"{{(scene=="legacy"?"playPhaseStarting":"playEnding")}}","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{{effects}}]}]}""");
            }
            if(scene is"loss"or"death")
            {
                if(scene=="loss")defs.Add($$"""{"id":"fixture:sh-conditional","revision":1,"hpSkillGrants":[{"id":"paid-source","skillId":"{{Can}}","minimumHp":12}]}""");
                defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-cost-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementDiscardOnly":true,"movementReasons":["skill-program.sun-hao.canshi.discard"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":{{(scene=="death"?20:1)}}},{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}""");
            }
            if(scene=="lion")defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-lion-health","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"recovery"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""");
            if(scene is"before-fill"or"before-empty"||chain)
            {
                var extra=scene=="before-fill"?""",{"op":"draw","target":"owner","amount":1}""":scene=="before-empty"?""",{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}""":"";
                defs.Add($$"""{"id":"{{Before}}","revision":1,"triggers":[{"id":"real-finalized-boundary","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]}{{extra}}]}]}""");
            }
            if(chain)defs.Add("""{"id":"fixture:sh-peer-empty","revision":1,"triggers":[{"id":"real-gain-discard","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}""");
            if(guiming)defs.Add("""{"id":"fixture:sh-predicates","revision":1,"triggers":[{"id":"generic-wounded-effect","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"draw","target":"owner","amount":1,"condition":{"kind":"wounded"}}]},{"id":"legacy-wounded-trigger","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"lessThan","right":{"kind":"currentMaxHp"}},"effects":[{"op":"draw","target":"owner","amount":1}]}]}""");
            if(songsang)
            {
                defs.Add("""{"id":"fixture:sh-song-kill","revision":1,"triggers":[{"id":"real-foreign-wounded-death","window":"afterTurnEnded","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"lessThan","right":{"kind":"currentMaxHp"}},"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingNonLord"},{"op":"damage","target":"selectedTarget","amount":20}]}]}""");
                defs.Add("""{"id":"fixture:sh-song-watch","revision":1,"triggers":[{"id":"real-zhanji-skill-child","window":"skillsChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"song","options":[{"id":"continue"}]}]}]}""");
            }
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="孙皓真实原生边界小夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"孙皓真实用牌、受伤与原生子窗"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:sh-pick","孙皓真实局面","固定公开选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});
            var skills=new List<string>{Driver};if(canshi)skills.Add(scene=="loss"?"fixture:sh-conditional":Can);if(canshi&&scene!="zero")skills.Add("fixture:sh-hurt-start");
            if(scene=="skip")skills.Add("fixture:sh-skip");if(scene=="replacement")skills.Add("classic:zaiqi");if(scene=="stack")skills.Add("ol:hongji");if(scene=="additive")skills.Add("fixture:sh-additive");if(scene is"nullification"or"legacy")skills.Add(Counter);if(scene is"loss"or"death")skills.Add(Watch);if(scene=="lion")skills.Add(Health);
            if(!canshi&&!guiming&&!songsang)skills.Add(Chou);if(scene is"before-fill"or"before-empty")skills.Add(Before);if(guiming)skills.AddRange([Gui,"ol:xingzhao"]);if(scene=="guiming-tail")skills.Add("ol:dianjun");if(scene=="guiming-peach")skills.Add("ol:bingzhong");if(chain)skills.Add("ol:yise");if(scene=="cangxin")skills.Add("ol:cangxin");if(songsang)skills.AddRange(["ol:songsang","fixture:sh-song-kill","fixture:sh-song-watch"]);
            b.AddGeneral(new(Owner,"真实孙皓","ol-sun-hao","fixture:sh-pick","wu",12,skills,GeneralGender.Male));
            var peers=Enumerable.Range(1,3).Select(s=>$"fixture:sh-peer-{s}").ToArray();foreach(var peer in peers)
            {
                var extra=new List<string>();if(guiming)extra.Add("fixture:sh-predicates");if(songsang)extra.Add(Gui);if(chain)extra.AddRange([Chou,"fixture:sh-peer-empty"]);
                b.AddGeneral(new(peer,"真实其他角色","supporter",Quiet,scene=="guiming-faction"?"qun":"wu",12,extra));
            }
            b.AddCard(new("fixture:sh-card","真实"+kind,CardCatalog.Get(kind).CategoryName,"固定真实实体",kind));
            b.AddDeck(new("fixture:sh-deck","孙皓固定同质实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(_=>new ContentDeckPhysicalCard("fixture:sh-card",scene=="cangxin"?Suit.Heart:Suit.Spade,5)).ToArray()});
            b.AddMode(new(scene is"chain-legacy"or"cangxin"?Legacy:Mode,"孙皓原生真实局面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:sh-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
