using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FeiYiChecks
{
    private const string Owner="fixture:fy-owner",Driver="fixture:fy-driver",Quiet="fixture:fy-quiet",Peer="fixture:fy-peer",Watch="fixture:fy-watch",Conditional="fixture:fy-conditional",Swap="fixture:fy-swap";
    private const string Mode="identity:classic-fei-yi-fixture",Yanru="ol:yanru",Hezhong="ol:hezhong";
    public static void YanruIndependentParityMinimumAndZeroHandCold()
    {
        var(g,r)=Start();var initial=V(g).Hand.Count;Need(initial%2==0,"The verified seed enters an actual even-hand Play.");
        Begin(g);Need(Current(g) is{Odd:false,Minimum:3},"Even Yanru freezes floor half of the actual six hand cards before drawing.");
        Pick(g,3);g=Cold(g,r);Finish(g);Play(g);
        Need(V(g).Hand.Count==initial&&E<FeiYiMovementIssuedEvent>(g).Where(e=>e.FrameId==E<FeiYiYanruPaidEvent>(g).Single().FrameId).Select(e=>e.Stage).SequenceEqual([FeiYiStage.DiscardChildren,FeiYiStage.DrawChildren]),"Even Yanru pays three genuine hand entities before the three-card native draw.");
        RejectYanru(g);Use(g,"give",[V(g).Hand[0].Id],[1]);Play(g);Begin(g);Reach(g,()=>Current(g)is{Stage:FeiYiStage.DiscardChoice});
        Need(Current(g)is{Odd:true,HandCount:8,Minimum:4},"Odd Yanru first draws three and then freezes the true post-child hand count, rather than the original five.");
        Need(Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"The discard selection remains private to the real owner.");
        Pick(g,7);g=Cold(g,r);Finish(g);Play(g);
        Need(V(g).Hand.Count==1&&E<FeiYiYanruPaidEvent>(g).Count()==2&&E<FeiYiYanruPaidEvent>(g).Select(e=>e.Odd).Order().SequenceEqual([false,true])&&E<FeiYiMovementIssuedEvent>(g).Any(e=>e.Stage==FeiYiStage.DiscardChildren&&e.Count==7&&e.Minimum==4),"At least half permits a larger real discard; both parity budgets are independent and each paid once.");RejectYanru(g);
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        Need(g.GetHumanLegalActions().Any(a=>a.ProgramSkillId==Yanru),"A new real Play has fresh parity budgets.");_=Cold(g,r);
        (g,r)=Start();Use(g,"give",V(g).Hand.Select(c=>c.Id).ToArray(),[1]);Play(g);Need(V(g).Hand.Count==0,"A native transfer empties the real hand.");Begin(g);Need(Current(g)is{Odd:false,Minimum:0},"Zero is a valid even hand with a genuine zero minimum.");g=Cold(g,r);Finish(g);Play(g);Need(V(g).Hand.Count==3&&E<FeiYiYanruPaidEvent>(g).Count()==1,"Zero-card discard does not invent material and still draws three once.");_=Cold(g,r);
    }
    public static void HezhongOverlappingThresholdsPaymentUseAndTurnExpiryCold()
    {
        foreach(var scenario in new[]{"double","fengzi"})
        {
            var(g,r)=Start(scenario);g=Arm(g,"greater",true,r);g=Arm(g,"less",false,r);
            Need(E<FeiYiRankEffectIssuedEvent>(g).Count()==2&&E<FeiYiRankEffectIssuedEvent>(g).First().Rank<E<FeiYiRankEffectIssuedEvent>(g).Last().Rank,"Two genuine singleton transitions arm independent ordered thresholds.");
            Use(g,"lose-hezhong");Play(g);Use(g,"draw");Play(g);
            var low=E<FeiYiRankEffectIssuedEvent>(g).First().Rank;var high=E<FeiYiRankEffectIssuedEvent>(g).Last().Rank;
            var card=V(g).Hand.First(c=>c.Rank>low&&c.Rank<high);var before=V(g).Hand.Count;var physical=card.Id;
            Native(g,g.GetHumanLegalActions().First(a=>a.CardId==physical&&a.Kind==LegalActionKind.DrawTwo));
            if(scenario=="fengzi")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:fengzi"&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Activate(g);
                Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="extra-effects-rank-guess")==true);g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="cost");
            }
            Reach(g,()=>E<FeiYiEffectRoundStartedEvent>(g).Any());g=Cold(g,r);Play(g);
            var binds=E<FeiYiRankEffectBoundEvent>(g).ToArray();var use=binds.First().UseFrameId;
            Need(binds.Length==2&&binds.All(e=>e.UseFrameId==use)&&E<FeiYiEffectRoundStartedEvent>(g).Where(e=>e.UseFrameId==use).Select(e=>e.Round).SequenceEqual([1,2])&&
                V(g).Hand.Count==before-1-(scenario=="fengzi"?1:0)+(scenario=="fengzi"?8:6)&&g.CardMovements.Count(m=>m.CardId==physical&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1&&E<CardUseFinishedEvent>(g).Count(e=>e.CardId==physical)==1,
                "Both thresholds add full native rounds to the same paid ordinary trick after source loss; Fengzi's old round composes before them without a second payment or second Use.");_=Cold(g,r);
        }
        {
            var(g,r)=Start("claims");g=Arm(g,"greater",true,r);g=Arm(g,"less",false,r);Use(g,"draw");Play(g);
            var low=E<FeiYiRankEffectIssuedEvent>(g).First().Rank;var high=E<FeiYiRankEffectIssuedEvent>(g).Last().Rank;
            var card=V(g).Hand.First(c=>c.Rank>low&&c.Rank<high);var hp=Enumerable.Range(1,3).Select(s=>V(g,s).Hp).ToArray();
            Native(g,g.GetHumanLegalActions().First(a=>a.CardId==card.Id&&a.Kind==LegalActionKind.ArrowBarrage));
            Reach(g,()=>E<FeiYiEffectRoundStartedEvent>(g).Any()&&P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification});
            Need(V(g,1).Hand.Any(c=>c.Id==card.Id)&&E<ProgramDamageCardsClaimedEvent>(g).Count(e=>e.CardIds.Contains(card.Id))==1,"The first actual native damage legally gives Jianxiong the original material before either additional round.");
            g=Cold(g,r);Play(g);
            Need(Enumerable.Range(1,3).All(s=>V(g,s).Hp==hp[s-1]-3)&&E<FeiYiEffectRoundStartedEvent>(g).Count()==2&&E<ProgramDamageCardsClaimedEvent>(g).Count(e=>e.CardIds.Contains(card.Id))==1&&
                g.CardMovements.All(m=>m.CardId!=card.Id||m.Reason!=CardMoveReasons.UseFinished)&&E<CardUseFinishedEvent>(g).Count(e=>e.CardId==card.Id)==1,
                "All three native global rounds retain the accepted use and frozen group source while its legitimately claimed entity remains acquired exactly once.");_=Cold(g,r);
        }
        foreach(var scenario in new[]{"assault","assault-fengzi"})
        {
            var(g,r)=Start(scenario);g=Arm(g,"greater",true,r);g=Arm(g,"less",false,r);Use(g,"draw");Play(g);
            var low=E<FeiYiRankEffectIssuedEvent>(g).First().Rank;var high=E<FeiYiRankEffectIssuedEvent>(g).Last().Rank;
            var card=V(g).Hand.First(c=>c.Rank>low&&c.Rank<high);var hand=V(g,1).Hand.Select(c=>c.Id).ToArray();var hp=V(g,1).Hp;
            Native(g,g.GetHumanLegalActions().First(a=>a.CardId==card.Id&&a.Kind==LegalActionKind.UnexpectedAssault&&a.TargetSeats.SequenceEqual([1])));
            if(scenario=="assault-fengzi")
            {Reach(g,()=>Optional(P(g),"ol:fengzi"));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="extra-effects-rank-guess")==true);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="cost");}
            var total=scenario=="assault-fengzi"?4:3;
            for(var round=0;round<total;round++)
            {
                Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectTargetCard});
                Need(P(g)!.Choices.All(c=>c.Cards.Count==0),"Every actual repeated hand reveal publishes only opaque source-side slots.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
            }
            Play(g);var reveals=E<UnexpectedAssaultRevealedEvent>(g).Select(e=>e.Receipt).ToArray();
            Need(reveals.Length==total&&reveals.Select(e=>e.ExtraEffectOrdinal).SequenceEqual(Enumerable.Range(0,total))&&reveals.Select(e=>e.RevealedCardId).Distinct().Count()==1&&
                V(g,1).Hand.Select(c=>c.Id).SequenceEqual(hand)&&V(g,1).Hp==hp&&E<CardUseFinishedEvent>(g).Count(e=>e.CardId==card.Id)==1,
                "The same unchanged target hand entity is legitimately revealed in each native round; exact unique ordinals preserve strict receipt identity for three or four effects.");_=Cold(g,r);
        }
        foreach(var scenario in new[]{"actor","actor-unmatched"})
        {
            var(g,r)=Start(scenario);var peerSeat=Enumerable.Range(1,3).Single(seat=>V(g,seat).Skills!.Any(skill=>skill.Id==Hezhong));var victim=peerSeat==1?2:1;
            g=Arm(g,"greater",scenario=="actor",r,victim);
            for(var i=0;i<7&&E<FeiYiHezhongPaidEvent>(g).Count(e=>e.Source.OwnerSeat==peerSeat)<2;i++){Use(g,"strip-peer",targets:[peerSeat]);Play(g);}
            var peerGrants=E<FeiYiRankEffectIssuedEvent>(g).Where(e=>e.Source.OwnerSeat==peerSeat).ToArray();Need(peerGrants.Length==2,"Two true AI-owned singleton losses each choose their still-unused Hezhong direction through native Advance: "+JsonSerializer.Serialize(new{scenario,peerSeat,victim,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).HandCount,Skills=V(g,s).Skills!.Select(skill=>skill.Id).ToArray()}),Paid=E<FeiYiHezhongPaidEvent>(g).ToArray(),Issued=E<FeiYiRankEffectIssuedEvent>(g).ToArray(),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Hezhong||e.SkillId==Driver).ToArray(),Moves=g.CardMovements.TakeLast(15).ToArray(),Thoughts=g.AiThoughts.TakeLast(10).ToArray()}));
            Use(g,"draw");Play(g);var ownGrant=E<FeiYiRankEffectIssuedEvent>(g).Single(e=>e.Source.OwnerSeat==0);
            var card=V(g).Hand.First(c=>(scenario=="actor"?c.Rank>ownGrant.Rank:c.Rank<=ownGrant.Rank)&&peerGrants.Any(grant=>grant.Greater?c.Rank>grant.Rank:c.Rank<grant.Rank));
            Native(g,g.GetHumanLegalActions().First(a=>a.CardId==card.Id&&a.Kind==LegalActionKind.UnexpectedAssault&&a.TargetSeats.SequenceEqual([victim])));
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Swap&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));Answer(g,c=>c.Targets.SequenceEqual([peerSeat]));
            Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Swap&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
            var changed=E<ProgramCardUseActorReplacedEvent>(g).Single();var live=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==changed.CardUseFrameId);
            Need(changed is{PreviousActorSeat:0,ProviderSeat:0}&&changed.ActorSeat==peerSeat&&live.SourceSeat==peerSeat&&
                (scenario=="actor"?live.FeiYiRounds is{AcceptedActorSeat:0,Grants.Count:1}:live.FeiYiRounds is null)&&
                peerGrants.All(grant=>!E<FeiYiRankEffectBoundEvent>(g).Any(e=>e.GrantId==grant.FrameId)),
                "The real finalized-use actor changes while the immutable original accepted owner anchors the already-bound invoice; the new actor's otherwise-matching pending grants cannot attach to this existing use.");
            g=Cold(g,r);Continue(g);Play(g);
            Need(E<FeiYiEffectRoundStartedEvent>(g).Count()==(scenario=="actor"?1:0)&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==changed.CardUseFrameId)==1&&
                peerGrants.All(grant=>!E<FeiYiRankEffectBoundEvent>(g).Any(e=>e.GrantId==grant.FrameId)),
                "Both a prebound use and a use that had no initial match complete after native actor replacement without refunding old grants or consuming the replacement actor's unrelated thresholds.");_=Cold(g,r);
        }
        {
            var(g,r)=Start();Use(g,"draw");Play(g);var kept=V(g).Hand.OrderByDescending(c=>c.Rank).Take(1).Concat(V(g).Hand.OrderBy(c=>Math.Abs(c.Rank-7)).Take(1)).Select(c=>c.Id).Distinct().ToArray();
            Need(kept.Length==2,"The fixed deck supplies a high singleton and a lower real trick.");Use(g,"give",V(g).Hand.Where(c=>!kept.Contains(c.Id)).Select(c=>c.Id).ToArray(),[1]);Play(g);
            var trick=V(g).Hand.OrderBy(c=>c.Rank).First();var remaining=V(g).Hand.Single(c=>c.Id!=trick.Id);
            Native(g,g.GetHumanLegalActions().First(a=>a.CardId==trick.Id&&a.Kind==LegalActionKind.DrawTwo));Reach(g,()=>Optional(P(g),Hezhong));
            Need(g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.CardId==trick.Id&&u.RecoveryPaidContinuation?.Kind==RecoveryPaidCardUseKind.Trick)&&V(g).Hand.Count==1&&!E<CardUseFinishedEvent>(g).Any(e=>e.CardId==trick.Id),
                "The exact ordinary-trick payment is genuinely suspended on its native continuation before any DrawTwo effect: "+JsonSerializer.Serialize(new{Hand=V(g).Hand,Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()}));
            g=Cold(g,r);Activate(g);Direction(g,"less");Play(g);
            var issued=E<FeiYiRankEffectIssuedEvent>(g).ToArray();var bound=E<FeiYiRankEffectBoundEvent>(g).ToArray();
            Need(issued.Length==1&&bound.Length==1,"The singleton child must issue and then bind one exact current-use invoice: "+JsonSerializer.Serialize(new{trick,remaining,Hand=V(g).Hand,Issued=issued,Bound=bound,Started=E<FeiYiStartedEvent>(g).ToArray(),Paid=E<FeiYiHezhongPaidEvent>(g).ToArray(),Moves=g.CardMovements.TakeLast(12).ToArray(),Use=E<CardUseFinishedEvent>(g).ToArray()}));
            Need(issued.Single().CardId==remaining.Id&&bound.Single().EffectiveRank==trick.Rank&&E<FeiYiEffectRoundStartedEvent>(g).Count()==1&&V(g).Hand.Count==6,
                "The real Hand-to-Processing payment leaves one card and its Hezhong child enhances that very accepted trick before its first effect.");_=Cold(g,r);
        }
        {
            var(g,r)=Start();g=Arm(g,"greater",true,r);var grant=E<FeiYiRankEffectIssuedEvent>(g).Single();var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
            var before=E<FeiYiEffectRoundStartedEvent>(g).Count();Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.DrawTwo));Play(g);Need(!E<FeiYiRankEffectBoundEvent>(g).Any(e=>e.GrantId==grant.FrameId)&&E<FeiYiEffectRoundStartedEvent>(g).Count()==before,"Unspent issued thresholds expire at their real turn boundary.");_=Cold(g,r);
        }
    }
    public static void OffturnSingletonCounterspellDelayedAndVirtualZeroCold()
    {
        foreach(var scenario in new[]{"earlier-gain","earlier-loss"})
        {
            var(child,registry)=Start(scenario);var original=V(child).Hand[0].Id;
            Use(child,"give",V(child).Hand.Where(c=>c.Id!=original).Select(c=>c.Id).ToArray(),[1]);
            if(scenario=="earlier-loss")
            {Reach(child,()=>P(child)?.SkillPrompt?.SkillId==Watch&&P(child)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"));Answer(child,c=>c.Cards.SequenceEqual([original]));}
            Reach(child,()=>P(child)?.SkillPrompt?.SkillId==Watch&&P(child)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
            Need(V(child).Hand.Count==2&&(scenario=="earlier-gain"?V(child).Hand.Any(c=>c.Id==original):child.CardMovements.Any(m=>m.CardId==original&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile)),
                "The earlier same-batch native child has really changed the hand before the frozen singleton candidate resumes.");
            child=Cold(child,registry);Continue(child);
            if(scenario=="earlier-gain")
            {Reach(child,()=>Optional(P(child),Hezhong));Activate(child);Direction(child,"less");Play(child);Need(E<FeiYiRankEffectIssuedEvent>(child).Single().CardId==original&&V(child).Hand.Count==3,"An independently added hand card does not erase the original singleton event; Hezhong still reveals the frozen original entity.");}
            else
            {Play(child);Need(!E<FeiYiHezhongPaidEvent>(child).Any()&&!E<FeiYiRankEffectIssuedEvent>(child).Any(),"If the original singleton left Hand, no replacement is revealed; a subsequent real Draw2 cannot invent a trigger from its first internal single-card movement.");}
            _=Cold(child,registry);
        }
        var(g,r)=Start("offturn");Use(g,"give",V(g).Hand.Select(c=>c.Id).ToArray(),[2]);Play(g);End(g);
        Reach(g,()=>Optional(P(g),Hezhong));Need(g.State.CurrentSeat==1&&V(g).Hand.Count==1,"A real foreign PlayEnding gift changes the empty owner hand to one.");g=Cold(g,r);Activate(g);Direction(g,"less");
        Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.Nullification});
        Need(E<FeiYiRankEffectIssuedEvent>(g).Count()==1&&!E<FeiYiRankEffectBoundEvent>(g).Any(),"A peer's own ordinary trick does not spend this owner's rank effect.");
        g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="nullification"&&c.Cards.Count==1);
        Reach(g,()=>E<FeiYiRankEffectBoundEvent>(g).Any());
        Need(E<CardActionAcceptedEvent>(g).Any(e=>e.Action.ActorSeat==0&&e.Action.EffectiveKind==CardKind.Nullification&&e.Action.Type==CardActionType.Response)&&
            E<FeiYiRankEffectBoundEvent>(g).Single().EffectiveRank==0&&g.State.CurrentSeat==1,"A real counterspell does not consume the pending effect; a subsequent foreign-turn virtual ordinary Use retains its native explicit zero rank and matches less.");
        g=Cold(g,r);Reach(g,()=>E<FeiYiEffectRoundStartedEvent>(g).Count()==1&&!g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.FeiYiRounds is not null));
        Need(E<FeiYiHezhongPaidEvent>(g).Count()==1,"Counterspell payment becoming singleton may offer the other direction, but declining it never fabricates a second paid effect.");_=Cold(g,r);
        (g,r)=Start("delayed");g=Arm(g,"less",false,r);Use(g,"draw");Play(g);var grant=E<FeiYiRankEffectIssuedEvent>(g).Single();
        var delayed=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Indulgence&&a.ConversionSource?.BindingId=="delayed");Native(g,delayed);Play(g);
        Need(!E<FeiYiRankEffectBoundEvent>(g).Any()&&V(g,1).Judgment.Count==1,"A real delayed-trick placement neither receives nor consumes the pending ordinary-trick bonus.");
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.DrawTwo&&V(g).Hand.Single(c=>c.Id==a.CardId).Rank<grant.Rank));Play(g);Need(E<FeiYiRankEffectBoundEvent>(g).Single().GrantId==grant.FrameId&&E<FeiYiEffectRoundStartedEvent>(g).Count()==1,"The following matching ordinary trick still receives its issued effect.");_=Cold(g,r);
    }
    public static void PaidYanruSourceLossDamageDeathAndColdMovementChildren()
    {
        foreach(var scenario in new[]{"loss","death"})
        {
            var(g,r)=Start(scenario);Begin(g);Pick(g,3);Finish(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
            Need(E<FeiYiYanruPaidEvent>(g).Count()==1,"A real discard pays the parity before any nested native damage/loss child.");g=Cold(g,r);Continue(g);
            if(scenario=="loss")
            {Play(g);Need(V(g).Skills!.All(s=>s.Id!=Yanru)&&V(g).Hand.Count==6&&E<FeiYiMovementIssuedEvent>(g).Count(e=>e.Stage==FeiYiStage.DrawChildren)==1,"Paid even Yanru still draws three after a native HP child removes its conditional source.");}
            else
            {Reach(g,()=>g.State.Status==EngineStatus.Completed);Need(!V(g).IsAlive&&E<FeiYiMovementIssuedEvent>(g).All(e=>e.Stage!=FeiYiStage.DrawChildren)&&E<FeiYiCompletedEvent>(g).Count()==1,"True lethal native damage and dying finish; the paid parent is neither repaid nor allowed to draw for a dead owner.");}
            _=Cold(g,r);
        }
    }
    private static FeiYiReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.FeiYi is not null)?.FeiYi;
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Fei Yi command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool>pick){var p=P(g)!;var c=p.Choices.First(pick);if(p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));return;}Accept(g,new AnswerPromptCommand(0,p.PromptId,c.Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Begin(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,Yanru,"parity-draw-discard",[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,IReadOnlyList<int>?cards=null,IReadOnlyList<int>?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Pick(GameEngine g,int n){for(var i=0;i<n;i++)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pick");}
    private static void Finish(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");
    private static void Direction(GameEngine g,string branch){Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Hezhong&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==branch));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static GameEngine Arm(GameEngine g,string direction,bool low,ContentRegistry r,int recipient=1)
    {
        Use(g,"draw");Play(g);var kept=(low?V(g).Hand.OrderBy(c=>c.Rank):V(g).Hand.OrderByDescending(c=>c.Rank)).First();Use(g,"give",V(g).Hand.Where(c=>c.Id!=kept.Id).Select(c=>c.Id).ToArray(),[recipient]);
        Reach(g,()=>Optional(P(g),Hezhong));g=Cold(g,r);
        Activate(g);Direction(g,direction);Play(g);
        return g;
    }
    private static void RejectYanru(GameEngine g){var revision=g.Revision;var count=g.AcceptedCommands.Count;var before=State(g);var result=g.Submit(new UseProgramSkillCommand(0,Yanru,"parity-draw-discard",[],[],g.Revision,P(g)!.PromptId));Need(!result.Accepted&&g.Revision==revision&&g.AcceptedCommands.Count==count&&State(g)==before,"A consumed current-parity activation is rejected atomically.");}
    private static void Reach(GameEngine g,Func<bool>done)
    {
        for(var i=0;i<400;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Fei Yi ended before expected boundary: "+State(g));var p=P(g);
            if(Optional(p,Hezhong)||Optional(p,"ol:fengzi"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Fei Yi prompt: "+JsonSerializer.Serialize(p));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fei Yi did not reach the expected native boundary: "+JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact private choices, genuine movement/HP children, source loss, direction invoices and native effect rounds.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-fei-yi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:fei-yi");if(scenario.Contains("fengzi",StringComparison.Ordinal))CharacterFixtureContent.Register(b,"ol:wang-rong");if(scenario=="claims")CharacterFixtureContent.Register(b,"classic:cao-cao");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"counterspell","inputKinds":{{(scenario=="claims"?"[]":"[\"dodge\"]")}},"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true},{"id":"delayed","inputKinds":["drawTwo"],"inputSuits":[],"outputKind":"indulgence","forPlay":true,"forResponse":false}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"give","minCards":1,"maxCards":20,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
                {"id":"strip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"lose-hezhong","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Hezhong}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-yanru","skillId":"{{Yanru}}","minimumHp":6}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{(scenario.StartsWith("earlier-",StringComparison.Ordinal)?"skill-program.fixture:fy-driver.GiveSelected":"skill-program.fei-yi.yanru.discard")}}"],"optional":false,"priority":100,"usageScope":"game","usageLimit":1,"effects":[{{(scenario=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},":scenario=="earlier-gain"?"{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},":scenario=="earlier-loss"?"{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"amount\":1,\"zones\":[\"hand\"],\"resultBind\":\"original\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"original\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2},":"")}}{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}{{(scenario=="death"?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}":"")}}]}]},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"foreign-native-use","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingLeastHandCount"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"selectedTarget"},"outputKind":"drawTwo"}]}]},
              {"id":"{{Swap}}","revision":1,"triggers":[{"id":"real-role-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["unexpectedAssault"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Conditional,Watch,Peer,Swap};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="费祎真实命令夹具",optionLabels=id is Watch or Swap?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实费祎边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:fy-pick-owner","固定本人","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var own=new List<string>{Driver,Hezhong,scenario=="loss"?Conditional:Yanru};if(scenario is"loss" or"death"||scenario.StartsWith("earlier-",StringComparison.Ordinal))own.Add(Watch);if(scenario.Contains("fengzi",StringComparison.Ordinal))own.Add("ol:fengzi");if(scenario.StartsWith("actor",StringComparison.Ordinal))own.Add(Swap);
            b.AddGeneral(new(Owner,"费祎实际本人","ol-fei-yi","fixture:fy-pick-owner","shu",5,own));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:fy-other-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:fy-pick-"+index;b.AddSkill(new(pick,"固定其他顺序","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他实际角色","supporter",pick,"wei",8,scenario=="offturn"&&index==0?[Peer]:scenario=="claims"?[Quiet,"classic:jianxiong"]:scenario.StartsWith("actor",StringComparison.Ordinal)&&index==1?[Quiet,Hezhong]:[Quiet]));}
            b.AddDeck(new("fixture:fy-deck","真实固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,128).Select(i=>new ContentDeckPhysicalCard(scenario=="offturn"?"standard:dodge":scenario=="claims"?"standard:arrow_barrage":scenario.StartsWith("assault",StringComparison.Ordinal)||scenario.StartsWith("actor",StringComparison.Ordinal)?"standard:unexpected_assault":"standard:draw_two",Suit.Spade,scenario=="offturn"?7:i%13+1)).ToArray()});
            b.AddMode(new(Mode,"费祎真实行为",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:fy-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
