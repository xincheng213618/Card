using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaXiuMaTieChecks
{
    private const string Ks="ol:kenshang",Owner="fixture:mx-owner",Peer="fixture:mx-peer",Driver="fixture:mx-driver",Health="fixture:mx-health",Watch="fixture:mx-watch",Request="fixture:mx-request",Mode="identity:classic-ma-xiu-ma-tie-fixture";
    public static void KenshangWholeTargetReplacementActualDamageAndCold()
    {
        foreach(var scene in new[]{"keep","rewrite","insufficient","dodge","alcohol","chain"})
        {
            var(g,r)=Start(scene);Draw(g);if(scene=="alcohol"){Native(g,a=>a.Kind==LegalActionKind.Alcohol);Play(g);}if(scene=="chain"){Use(g,"chain",[1]);Play(g);Use(g,"chain",[2]);Play(g);}
            var count=scene=="insufficient"?4:2;var cards=V(g,0).Hand.Take(count).Select(c=>c.Id).Order().ToArray();var before=State(g);
            Need(!g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource?.SkillId==Ks)&&!g.Submit(new UseProgramSkillCommand(0,Ks,"native-variable-slash",cards.Take(1).ToArray(),[1],g.Revision,P(g)!.PromptId)).Accepted&&State(g)==before,"No single-card native converter or undersized activation can bypass the actual >=2 material minimum.");
            Begin(g,cards,1);
            if(scene!="insufficient")
            {
                Reach(g,()=>Action(g,"kenshang-target"));var p=P(g)!;before=State(g);Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"A peer cannot change the original user's complete target frontier.");g=Cold(g,r);
                if(scene=="rewrite")
                {Answer(g,c=>c.Targets.SequenceEqual([2]));Need(P(g)!.Choices.All(c=>c.Parameters.GetValueOrDefault("branch")!="commit"),"A partial replacement cannot be submitted as fewer targets.");g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="commit");}
                else Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="keep");
            }
            Play(g);var issued=E<KenshangUseIssuedEvent>(g).Single();var damage=E<KenshangDamageEvent>(g).Where(e=>e.UseId==issued.UseId).Sum(e=>e.Amount);var draw=E<KenshangDrawIssuedEvent>(g);
            var accepted=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==issued.ActionId).Action;
            Need(accepted.EffectiveSuit==Suit.Spade&&accepted.EffectiveIsRed==false&&accepted.PhysicalCards.Select(c=>c.CardId).SequenceEqual(cards),"The genuine accepted multi-material Use preserves its original same-Spade black appearance for native target restrictions, even in isolated Kenshang content: "+JsonSerializer.Serialize(new{scene,issued.ActionId,accepted.EffectiveSuit,accepted.EffectiveIsRed,ActualMaterials=accepted.PhysicalCards.Select(c=>c.CardId),ExpectedMaterials=cards}));
            Need(issued.MaterialCount==count&&E<KenshangMaterialEvent>(g).Select(e=>e.Cost.CardId).SequenceEqual(cards)&&damage==(scene is"rewrite"or"alcohol"or"chain"?2:scene=="dodge"?0:1)&&draw.Length==(damage<count?1:0),"Only the original materials and total positive applied damage of the whole native Slash determine the single post-use draw: "+scene+" "+Diagnostic(g));
            if(scene=="rewrite")Need(E<SlashTargetsReplacedEvent>(g).Single().Targets.SequenceEqual([2,3])&&E<KenshangDamageEvent>(g).All(e=>e.TargetSeat!=1),"The entire target set can exclude the initially designated target.");
            if(scene=="insufficient")Need(!E<KenshangTargetsCommittedEvent>(g).Any()&&!E<KenshangTargetSelectedEvent>(g).Any(),"An impossible exact four-target replacement is omitted, retaining its legal original Slash.");
            if(scene=="chain")Need(E<KenshangDamageEvent>(g).Any(e=>e.Chain&&e.TargetSeat==2),"Real elemental chain propagation counts toward this card's whole-use damage, despite an undesignated recipient.");
            Need(E<CardUseDebitRecordedEvent>(g).Count(e=>e.Debit.Query==SkillRuleQuery.SlashLimit)==1,"The proactive converted Slash spends exactly its ordinary native Slash allowance.");_=Cold(g,r);
        }
    }
    public static void KenshangEquipmentPaymentsLionOxSourceLossDeathCold()
    {
        foreach(var scene in new[]{"lion","ox","weapon","loss","death","gain-child"})
        {
            var(g,r)=Start(scene);Draw(g);int? equipment=null,grain=null;
            if(scene is"lion"or"ox")
            {Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);equipment=V(g,0).Equipment.Single().Id;if(scene=="lion"){Use(g,"lose");Play(g);}else{grain=V(g,0).Hand[0].Id;Accept(g,new UseEquipmentEffectCommand(0,CardKind.WoodenOx,[grain.Value],[],g.Revision,P(g)!.PromptId));Play(g);}}
            if(scene=="weapon")
            {Use(g,"weapon");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:mx-weapon");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);equipment=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;}
            var pair=(equipment is{}eq?new[]{eq,V(g,0).Hand[0].Id}:V(g,0).Hand.Take(2).Select(c=>c.Id).ToArray()).Order().ToArray();var hp=V(g,0).Hp;
            try{Begin(g,pair,1);}catch(Exception ex){throw new InvalidOperationException("Kenshang actual payment scene="+scene+" materials="+string.Join(',',pair)+" "+Diagnostic(g),ex);}
            if(scene=="lion"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g,0).Hp==hp+1&&!E<KenshangDrawIssuedEvent>(g).Any(),"Removing the genuine equipped Lion heals before target rewriting or post-use draw.");g=Cold(g,r);Continue(g);}
            if(scene is"loss"or"death")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);g=Cold(g,r);Continue(g);if(scene=="death"){Reach(g,()=>!V(g,0).IsAlive);Need(g.State.Winner==Winner.None&&!E<KenshangDrawIssuedEvent>(g).Any(),"Real non-Lord source death drains the paid movement's native dying/death without drawing for a dead owner.");_=Cold(g,r);continue;}Need(V(g,0).Skills!.All(s=>s.ContentId!=Ks),"The real native payment child removes the issuing conditional skill before its still-issued target window.");}
            Reach(g,()=>Action(g,"kenshang-target"));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="keep");
            if(scene=="gain-child"){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var paid=E<KenshangDrawIssuedEvent>(g).Single();Need(paid.ActualCount==1,"The actual draw is paid before its real gained-card child.");g=Cold(g,r);Continue(g);}
            Play(g);Need(E<KenshangDrawIssuedEvent>(g) is[{ActualCount:1}]&&pair.All(id=>E<KenshangMaterialEvent>(g).Count(e=>e.Cost.CardId==id)==1),"Paid equipment and hand costs stay original, with one eventual draw including after true source loss.");
            if(equipment is{}id)Need(g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Equipment(0)&&m.To==(scene=="weapon"?CardLocation.OutsideGame:CardLocation.Processing)&&m.Reason==CardMoveReasons.Use)==1,"Equipment payment preserves native Processing/GeneralWeapon OutsideGame normalization.");
            if(grain is{}gr)Need(g.CardMovements.Any(m=>m.CardId==gr&&m.From==CardLocation.WoodenOxGrain(0)&&m.To==CardLocation.DiscardPile)&&E<KenshangMaterialEvent>(g).All(e=>e.Cost.CardId!=gr),"Native Ox grain cleanup is neither a third material nor a substitute entity.");_=Cold(g,r);
        }
    }
    public static void KenshangBorrowedSwordQinglongUseAndPureResponseCold()
    {
        {
            var(g,r)=Start("borrowed");Draw(g);var actor=PeerSeat(g);
            Native(g,a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([actor]));Play(g);Native(g,a=>a.Kind==LegalActionKind.BarbarianAssault);Play(g);
            Need(!E<KenshangUseIssuedEvent>(g).Any()&&!E<KenshangRequestStartedEvent>(g).Any(),"Pure Duel/AOE played-Slash requests cannot advertise a use-only Kenshang conversion.");
            Use(g,"equip-peer",[actor]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Cards.Count==1);Play(g);
            Native(g,a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats.SequenceEqual([actor,0]));Reach(g,()=>P(g)?.PlayerSeat==actor&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="kenshang-request-open"));
            g=Cold(g,r);Reach(g,()=>P(g)?.PlayerSeat==actor&&Action(g,"kenshang-request-pick"));Need(g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(actor).PendingDecision is{IsPrivate:true},"Only the real requested user sees its private HE material selection.");g=Cold(g,r);Play(g);
            var use=E<KenshangUseIssuedEvent>(g).Single();Need(use.ActorSeat==actor&&use.ProviderSeat==actor&&use.MaterialCount==2&&E<KenshangDrawIssuedEvent>(g).Single().UseId==use.UseId,"Borrowed Sword uses the requested character's own real multi-material Slash, not a pure faction-provided response.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("borrowed-weapon");Draw(g);var actor=PeerSeat(g);var weapon=V(g,actor).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;
            while(V(g,actor).HandCount>1){Use(g,"trim-peer",[actor]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");Play(g);}
            Need(V(g,actor).HandCount==1&&V(g,actor).Equipment.Single().Id==weapon,"The actual AI weapon owner has exactly one hand entity plus its natively equipped sampled GeneralWeapon before the Borrowed Sword request.");
            Native(g,a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats.SequenceEqual([actor,0]));Reach(g,()=>P(g)?.PlayerSeat==actor&&Action(g,"kenshang-request-pick"));
            Need(g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(actor).PendingDecision is{IsPrivate:true},"Borrowed GeneralWeapon material selection remains private to the actual requested AI user.");g=Cold(g,r);
            Reach(g,()=>g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.KenshangRequest?.Selected.Count==1));g=Cold(g,r);
            Reach(g,()=>Action(g,"kenshang-target"));var use=E<KenshangUseIssuedEvent>(g).Single();var materials=E<KenshangMaterialEvent>(g).Where(e=>e.UseId==use.UseId).ToArray();
            Need(use.ActorSeat==actor&&use.ProviderSeat==actor&&use.MaterialCount==2&&materials.Count(e=>e.Cost.CardId==weapon&&e.Cost.From==CardLocation.Equipment(actor))==1&&materials.Count(e=>e.Cost.From==CardLocation.Hand(actor))==1&&g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(actor)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.Use)==1,"The real borrowed Kenshang pays the original equipped GeneralWeapon to OutsideGame exactly once alongside the actual single hand entity.");g=Cold(g,r);
            Play(g);Need(E<BorrowedSwordResolvedEvent>(g) is[{UsedSlash:true,WeaponOwnerSeat:var paidOwner,SlashTargetSeat:0,TransferredWeaponCardId:null}]&&paidOwner==actor&&E<KenshangDrawIssuedEvent>(g).Single().UseId==use.UseId&&E<KenshangMaterialEvent>(g).Count(e=>e.UseId==use.UseId)==2&&g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(actor)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.Use)==1&&!g.ResolutionStack.OfType<CardUseFrame>().Any(),"Cold-restored GeneralWeapon payment drains the complete nested Slash and its original Borrowed Sword return without repaying or transferring that removed weapon.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("qinglong");Draw(g);Native(g,a=>a.Kind==LegalActionKind.Equip&&V(g,0).Hand.Any(c=>c.Id==a.CardId&&c.Kind==CardKind.QinglongCrescentBlade));Play(g);var qinglong=V(g,0).Equipment.Single().Id;
            Use(g,"inherit");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")==qinglong.ToString());Play(g);
            Need(g.CardMovements.Any(m=>m.CardId==qinglong&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason.Value=="equipment.permanent-removal"),"Native weapon inheritance removes the actual equipped Qinglong while retaining its real follow-up ability.");
            Use(g,"weapon");Reach(g,()=>Action(g,"advanced-lifecycle"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:mx-weapon");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);var weapon=V(g,0).Equipment.Single(c=>c.Kind==CardKind.GeneralWeapon).Id;
            Begin(g,V(g,0).Hand.Take(2).Select(c=>c.Id).ToArray(),1);
            Reach(g,()=>Action(g,"kenshang-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));Answer(g,c=>c.Targets.SequenceEqual([2]));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="commit");Reach(g,()=>P(g)is{Kind:DecisionKind.QinglongCrescentBlade});Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="kenshang-request-open");Reach(g,()=>Action(g,"kenshang-request-pick"));
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="card"&&c.Cards.SequenceEqual([weapon]));g=Cold(g,r);PickRequest(g);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");Reach(g,()=>Action(g,"kenshang-target"));
            var outer=E<KenshangUseIssuedEvent>(g).First();var inner=E<KenshangUseIssuedEvent>(g).Last();var original=E<KenshangMaterialEvent>(g).Where(e=>e.UseId==outer.UseId).Select(e=>e.Cost.CardId).ToArray();
            Need(outer.UseId!=inner.UseId&&E<KenshangMaterialEvent>(g).Count(e=>e.UseId==inner.UseId&&e.Cost.CardId==weapon&&e.Cost.From==CardLocation.Equipment(0))==1&&g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.Use)==1&&g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.Id==outer.UseId)&&original.All(id=>g.CardMovements.Last(m=>m.CardId==id).To==CardLocation.Processing),"The inner genuine Qinglong Kenshang removes its own GeneralWeapon while both original outer materials still remain in their native Processing residency: "+JsonSerializer.Serialize(new{OuterUseId=outer.UseId,InnerUseId=inner.UseId,weapon,InnerMaterials=E<KenshangMaterialEvent>(g).Where(e=>e.UseId==inner.UseId).Select(e=>e.Cost),WeaponMoves=g.CardMovements.Where(m=>m.CardId==weapon),OuterLocations=original.Select(id=>new{CardId=id,LastMove=g.CardMovements.Last(m=>m.CardId==id)}),Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name,Use=f is CardUseFrame u?new{u.CardId,u.CardKind,u.SourceSeat,u.PhysicalCardIds,u.TargetSeats}:null})}));g=Cold(g,r);
            Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="keep");Play(g);
            Need(E<KenshangUseIssuedEvent>(g).Length==2&&E<KenshangUseIssuedEvent>(g).All(e=>e.MaterialCount==2&&e.ActorSeat==0&&e.ProviderSeat==0)&&E<QinglongCrescentBladeResolvedEvent>(g).Any(e=>e.Used)&&E<CardUseDebitRecordedEvent>(g).Count(e=>e.Debit.Query==SkillRuleQuery.SlashLimit)==1,"The real Qinglong follow-up independently uses Kenshang and retains its native extra-use quota contract.");_=Cold(g,r);
            Need(g.CardMovements.Count(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.OutsideGame&&m.Reason==CardMoveReasons.Use)==1&&!g.ResolutionStack.OfType<CardUseFrame>().Any(),"Cold native Qinglong returns complete both nested Uses without paying the removed GeneralWeapon again.");
        }
    }
    public static void KenshangActualProgramRequestsCancelPrivacyAndKeNativeCold()
    {
        {
            var(g,r)=Start("request");Draw(g);End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.ProgramTrigger}&&Action(g,"kenshang-request-open"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="kenshang-request-open");Reach(g,()=>Action(g,"kenshang-request-pick"));var p=P(g)!;var before=State(g);
            Need(p.IsPrivate&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null)&&!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==before,"Only the actual requested user can see or answer the private native material selection.");
            PickRequest(g);g=Cold(g,r);var movement=g.CardMovements.Count;Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="cancel");Need(g.CardMovements.Count==movement&&!E<KenshangUseIssuedEvent>(g).Any(),"Cancel restores the original exact native request without paying or exposing a selected hand entity.");
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="kenshang-request-open");PickRequest(g);g=Cold(g,r);PickRequest(g);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");Reach(g,()=>Action(g,"kenshang-target"));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="keep");Reach(g,()=>E<KenshangDrawIssuedEvent>(g).Length==1&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Request));
            Need(E<KenshangRequestStartedEvent>(g).Length==2&&E<KenshangRequestStartedEvent>(g).Select(e=>e.OriginalPromptId).Distinct().Count()==2&&E<KenshangUseIssuedEvent>(g) is[{ActorSeat:0,ProviderSeat:0,MaterialCount:2}]&&E<CardUseDebitRecordedEvent>(g).Length==0,"Cancel/reopen keeps separate exact private invoices; the true out-of-turn skill-request Slash consumes no ordinary Play allowance.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("ke");Draw(g);var actor=PeerSeat(g);Accept(g,new UseProgramSkillCommand(0,"ol:pingduan","ordered-three-items",[],[actor],g.Revision,P(g)!.PromptId));Reach(g,()=>Action(g,"kenshang-request-pick"));g=Cold(g,r);Play(g);
            var use=E<KenshangUseIssuedEvent>(g).Single();Need(use.ActorSeat==actor&&use.ProviderSeat==actor&&E<KenshangKeActionAcceptedEvent>(g).Single().MaterialCount==2&&E<KeBiNengPingduanNativeIssuedEvent>(g).Single().Origin.CardUseFrameId==use.UseId&&E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems==1&&g.State.CurrentSeat==0&&g.State.Phase==TurnPhase.Play,"Ke's genuine recipient may choose any native legal Kenshang use with exact private acceptance, original native return and reward, without faking turn or phase.");_=Cold(g,r);
        }
    }
    private static int PeerSeat(GameEngine g)=>Enumerable.Range(0,4).Single(s=>V(g,s).GeneralId==Peer);
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var a=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(a.Accepted,a.Error?.Message??"Rejected actual Kenshang command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Missing actual human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)?.PlayerSeat==0)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Use(GameEngine g,string id,int[]? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Draw(GameEngine g){Use(g,"draw");Play(g);}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Begin(GameEngine g,int[] cards,int target)=>Accept(g,new UseProgramSkillCommand(0,Ks,"native-variable-slash",cards,[target],g.Revision,P(g)!.PromptId));
    private static void PickRequest(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="card"&&c.Cards.Count==1&&V(g,0).Hand.Any(h=>h.Id==c.Cards[0]));
    private static void Native(GameEngine g,Func<LegalAction,bool> predicate){var a=g.GetHumanLegalActions().FirstOrDefault(predicate);Need(a is not null,"Missing native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard},$"{caller}:{line} actual human Play");
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? boundary=null)
    {
        for(var i=0;i<260;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Kenshang ended before {caller}:{line} {boundary}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("program-action")!="kenshang-request-open");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p.Kind==DecisionKind.QinglongCrescentBlade)Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="qinglong-skip");
            else if(p.SkillPrompt?.SkillId is Health or Watch)Continue(g);
            else if(Action(g,"current-slash-fire"))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="convert");
            else if(Action(g,"kenshang-target"))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="keep");
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected Kenshang {caller}:{line} {boundary}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded Kenshang {caller}:{line} {boundary}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Frames=g.ResolutionStack.TakeLast(4).Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(5).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(copy),"Cold replay retains every private request, original material/target/damage invoice and once-paid native child return.");return copy;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ma-xiu-ma-tie",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ma-xiu-ma-tie","standard:demo-mashu");if(scene=="ke")CharacterFixtureContent.Register(b,"ol:ke-bi-neng");var peerKs=scene is"borrowed"or"borrowed-weapon"or"ke";
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-fixture-materials","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[
             {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true},
             {"id":"aoe","inputKinds":[],"inputSuits":[],"outputKind":"barbarianAssault","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
             {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
             {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"inherit","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"inheritWeapon","target":"owner"}]},
             {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}""","""{"id":"fixture:mx-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(scene=="chain")defs.Add("""{"id":"fixture:mx-fire","revision":1,"triggers":[{"id":"real-fire","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"priority":5,"optional":false,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}""");
            if(scene=="request")defs.Add($$"""{"id":"{{Request}}","revision":1,"triggers":[{"id":"native-request","window":"afterNormalDraw","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]}]}""");
            if(scene=="borrowed-weapon")defs.Add("""{"id":"fixture:mx-peer-weapon","revision":1,"triggers":[{"id":"actual-weapon","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]}]}""");
            if(scene=="loss")defs.Add($$"""{"id":"fixture:mx-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{Ks}}","minimumHp":13}]}""");
            if(scene is"loss"or"death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-paid-use-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.use"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"payment","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scene=="death"?20:1)}}}]}]}""");
            if(scene=="gain-child")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ma-xiu-ma-tie.kenshang.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生垦伤夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"原生夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:mx-pick","实际本人","公开固定选将评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});b.AddSkill(new("fixture:mx-peer-pick","实际请求者","公开固定选将评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>100000d)});b.AddSkill(new("fixture:mx-unused","备用实体武将","公开固定选将评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000d)});
            var own=new List<string>{Driver,Health};if(!peerKs){own.Add("standard:mashu");own.Add(scene=="loss"?"fixture:mx-conditional":Ks);}if(scene is"loss"or"death"or"gain-child")own.Add(Watch);if(scene=="chain")own.Add("fixture:mx-fire");if(scene=="ke")own.Add("ol:pingduan");b.AddGeneral(new(Owner,"实际马休马铁","ol-ma-xiu-ma-tie","fixture:mx-pick","qun",12,own,GeneralGender.Male));
            var peers=new[]{Peer,"fixture:mx-peer-b","fixture:mx-peer-c"};foreach(var peer in peers){var skills=new List<string>{Health};if(peer==Peer)skills.Add("fixture:mx-quiet");if(peer==Peer&&peerKs){skills.Add("standard:mashu");skills.Add(Ks);}if(peer==Peer&&scene=="request")skills.Add(Request);if(peer==Peer&&scene=="borrowed-weapon")skills.Add("fixture:mx-peer-weapon");b.AddGeneral(new(peer,"实际其他角色","supporter",peer==Peer?"fixture:mx-peer-pick":"fixture:mx-quiet","qun",12,skills));}
            b.AddGeneral(new("fixture:mx-weapon","实际备用武器","supporter","fixture:mx-unused","qun",4,[]));
            var kinds=scene switch{"lion"=>new[]{CardKind.SilverLion},"ox"=>[CardKind.WoodenOx],"alcohol"=>[CardKind.Alcohol],"dodge"=>[CardKind.Dodge],"qinglong"=>[CardKind.QinglongCrescentBlade,CardKind.Dodge,CardKind.Dodge,CardKind.Dodge],"borrowed"or"borrowed-weapon"=>[CardKind.BorrowedSword,CardKind.Crossbow],_=>[CardKind.Crossbow]};
            foreach(var kind in kinds.Distinct())b.AddCard(new("fixture:mx-card-"+kind,"真实"+kind,kind is CardKind.SilverLion or CardKind.Crossbow or CardKind.QinglongCrescentBlade or CardKind.WoodenOx?"装备牌":kind==CardKind.BorrowedSword?"锦囊牌":"基本牌","原生实体",kind));
            b.AddDeck(new("fixture:mx-deck","固定有界真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("fixture:mx-card-"+kinds[i%kinds.Length],Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"马休马铁原生边界",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:mx-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:mx-weapon"]));
        }
    }
}
