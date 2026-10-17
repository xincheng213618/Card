using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaYuanYiChecks
{
    private static string ActiveScene="";
    private const string Owner="fixture:my-owner",Driver="fixture:my-driver",Quiet="fixture:my-quiet",Watch="fixture:my-watch",Health="fixture:my-health",Mode="identity:classic-ma-yuan-yi",Jibing="ol:jibing",Wangjing="ol:wangjing",Moucuan="ol:moucuan",Binghuo="ol:binghuo";
    public static void JibingActualDrawThresholdNativePileAndPrivacyCold()
    {
        foreach(var scene in new[]{"slash","dodge","fire","decline","skip","reshuffle","shortage","empty"})
        {
            var(g,r)=Start(scene);
            if(scene=="skip")
            {Play(g);Need(Pile(g).Length==0&&E<MaYuanYiStoredEvent>(g).Length==0,"A truly skipped Draw phase cannot replace or fabricate Bing.");_=Cold(g,r);continue;}
            Reach(g,()=>Optional(g,Jibing));var before=V(g,0).HandCount;var old=State(g);var p=P(g)!;
            Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==old,"A foreign seat cannot answer the owner's draw replacement.");
            g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")==(scene=="decline"?"skip":"activate"));Play(g);
            if(scene=="decline")
            {Need(Pile(g).Length==0&&V(g,0).HandCount==before+2&&!E<MaYuanYiStartedEvent>(g).Any(),"Declining leaves the actual normal Draw of two intact.");_=Cold(g,r);continue;}
            var stored=E<MaYuanYiStoredEvent>(g);
            if(scene is"shortage"or"empty")
            {var count=scene=="empty"?0:1;Need(stored.Length==count&&Pile(g).Length==count&&V(g,0).HandCount==before&&E<MaYuanYiMovementEvent>(g)is[{Count:var actual}]&&actual==count,
                "A genuinely exhausted deck supplies only the available zero or one top entity; it neither invents a second Bing nor resumes replaced normal Draw.");_=Cold(g,r);continue;}
            if(scene=="reshuffle")
            {
                var reshuffled=g.CardMovements.Where(m=>m.Reason==CardMoveReasons.Reshuffle&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile).ToArray();
                Need(stored.Length==2&&reshuffled.Length==4&&stored[0].Sequence<reshuffled[0].Sequence&&stored[1].Sequence>reshuffled[^1].Sequence&&reshuffled.Any(m=>m.CardId==stored[1].CardId)&&V(g,0).HandCount==0,
                    "With one real top card left, native Jibing takes it first, genuinely reshuffles the four earlier discarded Hand entities, and then stores the next actual top; shuffle cards never inflate the two primary costs.");_=Cold(g,r);continue;
            }
            Need(stored.Length==2&&Pile(g).Order().SequenceEqual(stored.Select(e=>e.CardId).Order())&&V(g,0).HandCount==before,
                "Living-faction X=1 is an entry threshold, not a pile capacity: both true top entities replace normal Draw without becoming Hand cards.");
            var ids=Pile(g);Need(Enumerable.Range(0,4).All(s=>ids.All(id=>g.CreateSnapshot(s).Players[0].PublicPersistentPileCards?.Any(c=>c.Id==id)==true||g.CreateSnapshot(s).Players[0].PublicPersistentPiles?.Any(pile=>pile.SourceSkillId==Jibing&&pile.Cards.Any(c=>c.Id==id))==true)),"The exact Bing pile is public in every seat view.");
            Need(g.GetHumanLegalActions().Where(a=>a.CardId is{}id&&ids.Contains(id)).All(a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource is{SkillId:Jibing,BindingId:"bing-slash"}),"Even original Slash or Dodge entities have only the registered pile conversion, never printed native actions.");
            var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.CardId==ids[0]&&a.TargetSeats.SequenceEqual([1]));var state=State(g);
            Need(!g.Submit(new PlayCardCommand(0,ids[0],[1],g.Revision,P(g)!.PromptId,CardKind.Slash)).Accepted&&State(g)==state,"Removing conversionSource cannot turn a public Bing into a native Hand Slash.");
            var wrong=action.ConversionSource! with{SkillInstanceId=action.ConversionSource!.SkillInstanceId+"-foreign"};
            Need(!g.Submit(new PlayCardCommand(0,ids[0],[1],g.Revision,P(g)!.PromptId,CardKind.Slash){ConversionSource=wrong}).Accepted&&State(g)==state,"A different or regranted source instance cannot spend the old source pile.");
            Use(g,"hand-cost");Reach(g,()=>Action(g,"select-owned-cards"));Need(P(g)!.Choices.SelectMany(c=>c.Cards).All(id=>!ids.Contains(id)),"Bing never enters generic Hand cost or two-Hand weapon material selection.");Answer(g,c=>c.Cards.Count==1);Play(g);
            Native(g,a=>a.Kind==LegalActionKind.Slash&&a.CardId==ids[0]&&a.TargetSeats.SequenceEqual([1]));Play(g);
            var accepted=E<CardActionAcceptedEvent>(g).Select(e=>e.Action).First(a=>a.ActorSeat==0&&a.PhysicalCards.Any(c=>c.CardId==ids[0]));
            Need(accepted.EffectiveKind==CardKind.Slash&&accepted.PhysicalCards.Single().From.Zone==CardZoneKind.PublicPersistentPile&&accepted.ConversionChain.Single().SkillId==Jibing&&g.CardMovements.Count(m=>m.CardId==ids[0]&&m.From.Zone==CardZoneKind.PublicPersistentPile&&m.To==CardLocation.Processing)==1,
                "The original printed property, including FireSlash, never leaks through a one-Bing ordinary Slash; native payment occurs once from the exact pile.");
            Need(!g.CardMovements.Any(m=>m.CardId==ids[0]&&m.To==CardLocation.Hand(0)),"Direct pile conversion never first obtains its material into Hand.");_=Cold(g,r);
        }
    }
    public static void WangjingActualUsePlayedDodgeDuelAndForcedSlashCold()
    {
        foreach(var scene in new[]{"use","response","duel","borrowed","fan","multi","provider"})
        {
            var(g,r)=Start(scene);Store(g);var ids=Pile(g);
            if(scene=="provider")
            {
                var turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);
                var provider=Enumerable.Range(1,3).Single(seat=>V(g,seat).GeneralId=="fixture:my-peer-1");var target=Enumerable.Range(1,3).First(seat=>seat!=provider);
                Need(r.Generals[V(g,provider).GeneralId!].FactionId=="shu"&&PeerPile(g,provider).Length==2,"The actual Loyalist provider is selected by its public fixture identity and really owns its earlier two Bing entities: "+JsonSerializer.Serialize(new{Provider=provider,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).FactionId,V(g,s).Hp,V(g,s).HandCount}),Bing=PeerPile(g,provider),Stored=E<MaYuanYiStoredEvent>(g),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Jibing)}));
                var count=V(g,provider).HandCount;for(var i=0;i<count;i++){Use(g,"trim-peer",[provider]);Reach(g,()=>Action(g,"select-and-move-owned-card"));Answer(g,c=>c.Parameters.ContainsKey("slot-index"));Play(g);}
                var supplied=PeerPile(g,provider);var hand=V(g,provider).HandCount;Need(hand==0&&V(g,0).Hp>V(g,provider).Hp,"The true requester alone has highest HP and the provider has no Hand Slash alternative.");
                var request=g.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.UseProgramSkill&&a.ProgramSkillId=="classic:jijiang");
                Accept(g,new UseProgramSkillCommand(0,"classic:jijiang",request.ProgramActivationId!,[],[target],g.Revision,P(g)!.PromptId));
                Reach(g,()=>P(g)is{Kind:DecisionKind.RespondSlash}p&&p.PlayerSeat==provider);var prompt=P(g)!;var stable=State(g);
                Need(g.CreateSnapshot(0).PendingDecision is null&&g.CreateSnapshot(target).PendingDecision is null&&!g.Submit(new AnswerPromptCommand(0,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==stable,
                    "The real allied provider privately decides its own original pile cost; neither requester nor target may answer or inspect that menu.");
                g=Cold(g,r);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
                var use=g.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.CardKind==CardKind.Slash);var a=use.Action!;
                Need(a.ActorSeat==0&&a.ProviderSeat==provider&&a.RequesterSeat==0&&a.PhysicalCards is[var cost]&&supplied.Contains(cost.CardId)&&cost.From.Zone==CardZoneKind.PublicPersistentPile&&a.ConversionChain.Any(x=>x.SkillId==Jibing&&x.OwnerSeat==provider)&&E<CardSupplyCompletedEvent>(g).Any(e=>e.ActionId==a.ActionId&&e.ProviderSeat==provider&&e.RequesterSeat==0),
                    "A genuine active Jijiang Use carries separate requester/actor and provider, the exact original Bing pile cost, and a real completed-supply observer boundary.");
                g=Cold(g,r);Continue(g);Play(g);
                Need(E<MaYuanYiBingActionEvent>(g).Count(e=>e.ProviderSeat==provider)==1&&E<WangjingDrawIssuedEvent>(g).Count(e=>e.OwnerSeat==provider&&e.OpponentSeat==0&&e.ActualCount==1)==1&&V(g,provider).HandCount==hand+1&&g.CardMovements.Count(m=>m.CardId==a.PhysicalCards.Single().CardId&&m.From.Zone==CardZoneKind.PublicPersistentPile&&m.To==CardLocation.Processing)==1,
                    "The native supplied Slash records one Bing action and one independent Wangjing Draw1 for the actual Requester, never a second response/use invoice or a second pile payment.");
            }
            else if(scene=="multi")
            {
                var hand=V(g,0).HandCount;Native(g,a=>a.Kind==LegalActionKind.Slash&&a.CardId is{}id&&ids.Contains(id)&&a.TargetSeats.SequenceEqual([1]));
                Reach(g,()=>Action(g,"bingzhong-target"));g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([2]));if(Action(g,"bingzhong-target"))Answer(g,c=>c.Targets.Count==0);Play(g);
                var draws=E<WangjingDrawIssuedEvent>(g);Need(draws.Length==2&&draws.Select(e=>e.ActionId).Distinct().Count()==1&&draws.Select(e=>e.OpponentSeat).SequenceEqual([1,2])&&draws.All(e=>e.ActualCount==1)&&E<MaYuanYiBingActionEvent>(g).Length==1&&V(g,0).HandCount==hand+2,
                    "One real Bing Slash with a native extra target issues two independent highest-HP opponent Draw1 calls while paying its original pile entity only once: "+JsonSerializer.Serialize(new{Draws=draws,BingActions=E<MaYuanYiBingActionEvent>(g),HandBefore=hand,HandAfter=V(g,0).HandCount}));
            }
            else if(scene=="fan")
            {
                Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);var hand=V(g,0).HandCount;
                Native(g,a=>a.Kind==LegalActionKind.Slash&&a.CardId is{}id&&ids.Contains(id)&&a.PlayedCardKind==CardKind.FireSlash&&a.TargetSeats.SequenceEqual([1]));Play(g);
                Need(E<MaYuanYiBingActionEvent>(g)is[{Kind:CardKind.FireSlash}]&&E<WangjingDrawIssuedEvent>(g)is[{OpponentSeat:1,ActualCount:1}]&&V(g,0).HandCount==hand+1,
                    "A genuine Zhuque Fan second conversion changes a real Bing Slash to FireSlash without losing Wangjing or its original pile provenance.");
                Use(g,"gain-binghuo");Play(g);End(g);Reach(g,()=>Optional(g,Binghuo));Need(E<MaYuanYiBingActionEvent>(g).Single().Kind==CardKind.FireSlash,"The same true FireSlash Bing also qualifies the later actual-turn Binghuo ledger.");
            }
            else if(scene=="use")
            {
                foreach(var target in new[]{1,2}){var hand=V(g,0).HandCount;Native(g,a=>a.Kind==LegalActionKind.Slash&&a.CardId is{}id&&ids.Contains(id)&&a.TargetSeats.SequenceEqual([target]));Play(g);Need(V(g,0).HandCount==hand+1,"Each distinct native Bing Slash against a highest-HP tie issues its own true Draw1 with no once limit.");}
                Need(E<WangjingDrawIssuedEvent>(g).Select(e=>e.OpponentSeat).SequenceEqual([1,2])&&E<WangjingDrawIssuedEvent>(g).All(e=>e.ActualCount==1),"Wangjing preserves separate logical Draw1 invocations.");
            }
            else if(scene=="response")
            {
                Incoming(g,1);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});var p=P(g)!;
                Need(p.Choices.Any(c=>c.Cards.Any(ids.Contains))&&g.CreateSnapshot(2).Players[0].Hand.Count==0,"A genuine Slash-defense can directly use a Bing Dodge without exposing private Hand entities.");g=Cold(g,r);Answer(g,c=>c.Cards.Any(ids.Contains));Play(g);
                Need(E<MaYuanYiBingActionEvent>(g)is[{Type:CardActionType.Response,Kind:CardKind.Dodge,ActorSeat:0,ProviderSeat:0,OpponentSeat:1}]&&E<WangjingDrawIssuedEvent>(g)is[{OpponentSeat:1,ActualCount:1}],"True defensive Dodge has its exact attack opponent and native Wangjing draw.");
            }
            else if(scene=="duel")
            {
                foreach(var seat in new[]{1,2,3}){Use(g,"wound-peer",[seat]);Play(g);}
                Native(g,a=>a.Kind==LegalActionKind.Duel&&a.TargetSeats.SequenceEqual([1]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});g=Cold(g,r);Answer(g,c=>c.Cards.Any(ids.Contains));Play(g);
                Need(E<MaYuanYiBingActionEvent>(g).Any(e=>e.Type==CardActionType.Response&&e.Kind==CardKind.Slash&&e.OpponentSeat==1)&&E<WangjingDrawIssuedEvent>(g).Any(e=>e.OpponentSeat==1),"A pure-played Duel Slash is eligible for Wangjing even though it is not a new Slash Use.");
            }
            else
            {
                var turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);
                Need(V(g,1).Equipment.Any(c=>c.Kind==CardKind.GeneralWeapon)&&PeerPile(g,1).Length==2,"The forced-use source has a native weapon and its own real two-entity Bing pile: "+JsonSerializer.Serialize(new{Gear=V(g,1).Equipment,Stored=E<MaYuanYiStoredEvent>(g),Bing=PeerPile(g,1),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Jibing)}));
                var count=V(g,1).HandCount;for(var i=0;i<count;i++){Use(g,"trim-peer",[1]);Reach(g,()=>Action(g,"select-and-move-owned-card"));g=Cold(g,r);Answer(g,c=>c.Parameters.ContainsKey("slot-index"));Play(g);}
                Need(V(g,1).HandCount==0,"The native forced user has no printed Hand Slash alternative.");
                Native(g,a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats.SequenceEqual([1,0]));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
                var use=g.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.CardKind==CardKind.Slash);var a=use.Action!;
                Need(a.ActorSeat==1&&a.ProviderSeat==1&&a.EffectiveKind==CardKind.Slash&&a.PhysicalCards is[{CardKind:CardKind.FireSlash,From.Zone:CardZoneKind.PublicPersistentPile}]&&a.ConversionChain.Any(s=>s.SkillId==Jibing),"Borrowed Sword truly uses the original FireSlash Bing as ordinary Slash, with the correct provider and original pile cost.");
                g=Cold(g,r);Answer(g,c=>c.Cards.Count==0);Play(g);Need(E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==use.Id)==1,"The forced direct-pile native Slash completes and returns to its exact Borrowed Sword parent once.");
            }
            _=Cold(g,r);
        }
    }
    public static void MoucuanAllFactionsThresholdMaximumLossGrantAndDeathCold()
    {
        foreach(var scene in new[]{"awake","awake-loss","awake-death"})
        {
            var(g,r)=Start(scene);Store(g);var first=E<MaYuanYiStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing);Need(first.FactionCount==3&&Pile(g).Length==2,"The true first Draw uses three living effective factions.");
            var turn=g.State.TurnNumber;End(g);Reach(g,()=>Optional(g,Jibing)&&g.State.TurnNumber>turn);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Play(g);
            Need(Pile(g).Length==4&&E<MaYuanYiStartedEvent>(g).Last(e=>e.Op==SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing)is{BingBefore:2,FactionCount:3},"X-1 Bing can store two more; no hard-four or X-sized capacity is introduced.");
            turn=g.State.TurnNumber;End(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId is Health or Watch);
            if(P(g)?.SkillPrompt?.SkillId==Watch){Need(!Has(g,0,Binghuo)&&!Has(g,0,Moucuan)&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.MaYuanYi is{Stage:MaYuanYiStage.MaximumChildren}),"The real paid HP-conditional source-loss window drains on its index1 owning awakening before any new HP child or grant.");g=Cold(g,r);Continue(g);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);}
            var paid=E<MoucuanMaximumPaidEvent>(g).Single();Need(paid.MaximumBefore==5&&paid.MaximumAfter==4&&paid.HpBefore==5&&paid.HpAfter==4&&!Has(g,0,Binghuo),"The actual maximum-HP payment and clamp finish their native health child before granting Binghuo.");
            g=Cold(g,r);Continue(g);
            if(scene=="awake-death")
            {Reach(g,()=>!V(g,0).IsAlive);Need(E<PlayerDiedEvent>(g).Count(e=>e.VictimSeat==0)==1&&!E<MoucuanSkillGrantedEvent>(g).Any(),"A real paid maximum-HP child may kill its non-Lord issuer; a dead owner receives no new skill.");}
            else
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch||Has(g,0,Binghuo));if(P(g)?.SkillPrompt?.SkillId==Watch){g=Cold(g,r);Continue(g);}Play(g);Need(E<MoucuanSkillGrantedEvent>(g).Length==1&&Has(g,0,Binghuo),"The real grant follows the maximum-HP and real SkillsChanged children exactly once.");
                if(scene=="awake-loss")Need(!Has(g,0,Moucuan)&&Has(g,0,Binghuo),"Losing the original HP-conditional awakening during its already-paid clamp does not revoke the independent Binghuo grant.");
                Use(g,"regrant-awakening");Play(g);End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&g.State.TurnNumber>turn);
                Need(E<MoucuanMaximumPaidEvent>(g).Length==1,"Awakening remains spent across a genuinely new skill-source acquisition.");
            }
            _=Cold(g,r);
        }
    }
    public static void BinghuoHistoricalBingJudgmentReplacementThunderChildrenCold()
    {
        foreach(var scene in new[]{"judgment","red-judgment","judgment-loss","judgment-death","foreign-judgment","judgment-handoff","judgment-claim"})
        {
            var(g,r)=Start(scene);Store(g);var id=Pile(g)[0];Native(g,a=>a.Kind==LegalActionKind.Slash&&a.CardId==id&&a.TargetSeats.SequenceEqual([1]));Play(g);
            Need(!Has(g,0,Binghuo)&&E<MaYuanYiBingActionEvent>(g).Length==1,"The real turn-wide Bing history exists before Binghuo is acquired.");Use(g,"gain-binghuo");Play(g);
            if(scene=="judgment"){Use(g,"chain",[0]);Play(g);Use(g,"chain",[1]);Play(g);}
            End(g);
            if(scene=="foreign-judgment")
            {
                Reach(g,()=>g.State.CurrentSeat!=0&&P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});var foreignTurn=g.State.TurnNumber;var foreignOwner=g.State.CurrentSeat;var material=Pile(g).Single();g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([material]));
                Reach(g,()=>Optional(g,Binghuo));Need(g.State.TurnNumber==foreignTurn&&g.State.CurrentSeat==foreignOwner&&E<MaYuanYiBingActionEvent>(g).Any(e=>e.ActualTurn==foreignTurn&&e.TurnOwner==foreignOwner&&e.ProviderSeat==0&&e.Kind==CardKind.Dodge),"A real foreign-turn defensive Bing Dodge, rather than the earlier own-turn Slash, qualifies the exact foreign Ending obligation.");
            }
            Reach(g,()=>Optional(g,Binghuo));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Action(g,"ma-yuan-yi"));g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([scene is"judgment"or"judgment-death"?1:0]));
            int? replacement=null;
            if(scene=="judgment")
            {
                Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.ProgramJudgmentReplacement}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-replace"&&c.Cards.Count==1));
                replacement=P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-replace"&&c.Cards.Count==1).Cards.Single();g=Cold(g,r);
                Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-replace"&&c.Cards.SequenceEqual([replacement.Value]));
            }
            if(scene is"judgment-loss"or"judgment-death")
            {Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);Need(E<BinghuoTargetIssuedEvent>(g).Length==1&&!E<BinghuoJudgmentReturnedEvent>(g).Any(),"A real judgment-finalized child pauses before the whole judgment returns to Binghuo.");g=Cold(g,r);Continue(g);}
            if(scene=="judgment-handoff")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);var returned=E<BinghuoJudgmentReturnedEvent>(g).Single();
                Need(returned.HandoffSequence is{}sequence&&g.CardMovements.Any(m=>m.Sequence==sequence&&m.CardId==returned.CardId&&m.From==CardLocation.Judgment(0)&&m.To==CardLocation.Processing&&m.Reason.Value=="skill-program.judgment.result")&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.MaYuanYi is{Stage:MaYuanYiStage.JudgmentChildren,ActualCount:1}&&f.PendingMovementContinuation is not null)&&!E<BinghuoDamageIssuedEvent>(g).Any(),
                    "The exact native judgment-to-Processing handoff opens its real owner-loss child before Binghuo can damage, while the separate one-card cleanup remains once-paid on its owning frame.");
                g=Cold(g,r);Continue(g);
            }
            if(scene=="judgment-claim")
            {
                Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.ProgramJudgmentTrigger}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-activate")&&g.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().LastOrDefault()is{}window&&window.CandidateIndex>=0&&window.CandidateIndex<window.Candidates.Count&&window.Candidates[window.CandidateIndex].SkillId=="classic:tiandu");
                g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-activate");
            }
            Reach(g,()=>E<MaYuanYiCompletedEvent>(g).Count()==E<MaYuanYiStartedEvent>(g).Count());
            var result=E<BinghuoJudgmentReturnedEvent>(g).Single();
            if(replacement is{}paid)Need(result.CardId==paid&&E<ProgramJudgmentReplacementResolvedEvent>(g).Any(e=>e.JudgmentFrameId==result.JudgmentId&&e.SkillId=="fixture:my-rejudge"&&e.Activated&&e.ReplacementCardId==paid),"The real native replacement menu pays the chosen original Hand entity, and Binghuo receives that finalized replacement through the same judgment exactly once.");
            if(scene=="judgment-claim")Need(result.CardId is{}claimed&&result.HandoffSequence is null&&E<ProgramJudgmentCardClaimedEvent>(g).Any(e=>e.JudgmentFrameId==result.JudgmentId&&e.SkillId=="classic:tiandu"&&e.OwnerSeat==0&&e.CardId==claimed)&&V(g,0).Hand.Any(c=>c.Id==claimed)&&E<MaYuanYiMovementEvent>(g).Single(e=>e.FrameId==result.FrameId)is{Stage:MaYuanYiStage.JudgmentChildren,Count:0}&&E<BinghuoDamageIssuedEvent>(g).Length==1,
                "A real finalized Tiandu claim keeps its original judgment entity in the claimant's Hand: Binghuo neither reclaims nor disposes it, records zero cleanup, and still uses the finalized black suit once.");
            else Need(result.CardId is{}final&&E<MaYuanYiMovementEvent>(g).Single(e=>e.FrameId==result.FrameId)is{Stage:MaYuanYiStage.JudgmentChildren,Count:1}cleanup&&g.CardMovements.Count(m=>m.Sequence>cleanup.Before&&m.Sequence<=cleanup.After&&m.CardId==final&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.JudgmentFinish)==1,
                "The unclaimed finalized judgment entity leaves native Processing through one genuine JudgmentFinish movement before Binghuo's damage and complete return.");
            Need(scene=="red-judgment"?result.Suit==Suit.Heart&&!E<BinghuoDamageIssuedEvent>(g).Any():result.Suit==Suit.Spade&&E<BinghuoDamageIssuedEvent>(g).Length==1,"Binghuo reads the finalized native judgment suit, not a preliminary or reclaimed card.");
            if(scene=="judgment")Need(E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==1&&e.Amount==1)&&E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==0&&e.Amount==1),"A black judgment issues genuine thunder damage and its real linked recipient propagation.");
            if(scene=="judgment-loss")Need(!Has(g,0,Binghuo)&&Has(g,0,"fixture:my-loss-conditional")&&V(g,0).Hp==3&&E<ProgramSkillHpLostEvent>(g).Count(e=>e.SkillId==Watch&&e.TargetSeat==0&&e.Amount==1)==1&&E<BinghuoDamageIssuedEvent>(g).Length==1,"The already-issued judgment obligation survives the real HP-conditional source's loss after its finalized-judgment child crosses the HP threshold, then applies the original one thunder damage exactly once.");
            if(scene=="judgment-death")Need(!V(g,0).IsAlive&&E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0),"The actual judgment child completes issuer death without repeating its paid judgment.");
            _=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static bool Has(GameEngine g,int s,string skill)=>V(g,s).Skills!.Any(x=>x.Id==skill);
    private static int[] PeerPile(GameEngine g,int s)=>V(g,s).PublicPersistentPiles is{}p?p.Where(x=>x.SourceSkillId==Jibing).SelectMany(x=>x.Cards.Select(c=>c.Id)).ToArray():V(g,s).PublicPersistentPileSkillId==Jibing?V(g,s).PublicPersistentPileCards?.Select(c=>c.Id).ToArray()??[]:[];
    private static int[] Pile(GameEngine g)=>PeerPile(g,0);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Action(GameEngine g,string s)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==s)==true;
    private static bool Optional(GameEngine g,string s)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==s&&Action(g,"activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Ma Yuan Yi command.");}catch(Exception e){throw new InvalidOperationException("scene="+ActiveScene+" command="+c.GetType().Name+" cause="+e.GetType().Name+": "+e.Message+" "+Diagnostic(g),e);}}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> match){var p=P(g)!;var c=p.Choices.FirstOrDefault(match);Need(p.PlayerSeat==0&&c is not null,"Missing actual human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Use(GameEngine g,string id,int[]? seats=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],seats??[],g.Revision,P(g)!.PromptId));
    private static void Native(GameEngine g,Func<LegalAction,bool> match){var a=g.GetHumanLegalActions().FirstOrDefault(match);Need(a is not null,"Missing native action: "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Store(GameEngine g){Reach(g,()=>Optional(g,Jibing));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Play(g);}
    private static void Incoming(GameEngine g,int actor){Use(g,"incoming",[actor]);Reach(g,()=>P(g)is{PlayerSeat:0}&&Action(g,"virtual-slash-offer")&&P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([0])));Answer(g,c=>c.Targets.SequenceEqual([0]));}
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)
    {
        for(var n=0;n<340;n++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,$"Ended {caller}:{line}: "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));
            else if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge)Answer(g,c=>c.Cards.Count==0);
            else if(p.Kind==DecisionKind.Nullification)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"let-die"or"pass");
            else if(p.SkillPrompt?.SkillId is Health or Watch)Continue(g);
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException($"Unexpected {caller}:{line}: "+Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded {caller}:{line}: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene=ActiveScene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}),Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name,Stage=(f as ProgramSkillFrame)?.MaYuanYi?.Stage,Program=f is ProgramSkillFrame p?new{p.SkillId,p.TriggerId,p.ActivationId,p.InstructionIndex,PlanCount=PlanCount(g,p),p.SelectedCardIds,p.SelectedTargetSeats,Bindings=p.CardSetBindings.Select(b=>new{b.Name,b.CardIds,b.SourceLocations,b.SelectionActorSeat})}:null}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static int PlanCount(GameEngine g,ProgramSkillFrame f){try{return ProgramInstructionResolver.Default.Resolve(f,g.ContentRegistry.GetSkill(f.SkillId).Program!).Instructions.Count;}catch{return -1;}}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves original pile/response providers, every permitted private view and once-paid native child cursors.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scene)
    {
        ActiveScene=scene;ContentRegistry r;try{r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));}catch(Exception e){throw new InvalidOperationException("scene="+scene+" fixture build cause="+e.Message,e);}var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene is"awake-death"or"judgment-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scene):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ma-yuan-yi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ma-yuan-yi");if(scene=="provider")CharacterFixtureContent.Register(b,"classic:liu-bei");if(scene=="multi")CharacterFixtureContent.Register(b,"ol:yang-fu");if(scene=="judgment-claim")CharacterFixtureContent.Register(b,"classic:guo-jia");
            var awakening=scene.StartsWith("awake",StringComparison.Ordinal);var judgment=scene.Contains("judgment",StringComparison.Ordinal);var loss=scene is"awake-loss"or"judgment-loss";var death=scene is"awake-death"or"judgment-death";
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0},{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0},{"id":"quota","query":"slashLimit","operation":"add","value":4,"priority":0}],"viewAs":[
             {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true},
             {"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
             {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
             {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
             {"id":"hand-cost","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"hand-only"},{"op":"moveBoundCards","target":"owner","sourceBind":"hand-only","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"gain-binghuo","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{(scene=="judgment-loss"?"fixture:my-loss-conditional":Binghuo)}}"]}]},
             {"id":"regrant-awakening","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:moucuan"]}]}]}
            """,$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            if(scene=="provider")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-supplied-pile","window":"cardSupplyCompleted","ownerRelation":"observer","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"supply","options":[{"id":"continue"}]}]}]}""");
            if(scene=="duel")defs.Add("""{"id":"fixture:my-slash-response","revision":1,"viewAs":[{"id":"slash","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true}]}""");
            if(scene=="borrowed")defs.Add("""{"id":"fixture:my-peer-weapon","revision":1,"triggers":[{"id":"actual-weapon","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]}]}""");
            if(scene=="reshuffle")defs.Add("""{"id":"fixture:my-prime","revision":1,"triggers":[{"id":"real-discard-stock","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"prime"},{"op":"moveBoundCards","target":"owner","sourceBind":"prime","destination":"discardPile","awaitMovementTriggers":true}]}]}""");
            if(scene=="skip")defs.Add("""{"id":"fixture:my-skip","revision":1,"triggers":[{"id":"skip","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]}""");
            if(loss&&awakening)defs.Add($$"""{"id":"fixture:my-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{(awakening?Moucuan:Binghuo)}}","minimumHp":5}]}""");
            if(scene=="judgment-loss")defs.Add("""{"id":"fixture:my-loss-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-binghuo","skillId":"ol:binghuo","minimumHp":5}]}""");
            var lethal=death?""",{"op":"loseHp","target":"owner","amount":20}""":"";
            if(awakening)defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-maximum-child","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"maximumHp"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"maximum","options":[{"id":"continue"}]}{{lethal}}]}]}""");
            if(awakening)defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-grant-child","window":"skillsChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grant","options":[{"id":"continue"}]}]}]}""");
            if(scene=="judgment")defs.Add("""{"id":"fixture:my-rejudge","revision":1,"triggers":[{"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],"optional":true,"effects":[{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","club","heart","diamond"],"oldCardDestination":"discardPile"}]}]}""");
            if(scene=="judgment-handoff")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-judgment-handoff","window":"cardsMoved","subject":"owner","sourceZones":["judgment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.judgment.result"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"handoff","options":[{"id":"continue"}]}]}]}""");
            var judgmentTail=death?"""{"op":"loseHp","target":"owner","amount":20}""":"""{"op":"loseHp","target":"owner","amount":1}""";
            if(scene is"judgment-loss"or"judgment-death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-judgment-child","window":"judgmentFinalized","subject":"any","judgmentReasons":["binghuo"],"suits":["spade","club","heart","diamond"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"judgment-child","options":[{"id":"continue"}]},{{judgmentTail}}]}]}""");
            var labels=defs.Select(x=>JsonNode.Parse(x)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生兵牌小夹具",optionLabels=new[]{"triggers","activations"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,x=>x)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"原生真实行为"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:my-pick","实际本人","公开固定选择权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000d)});
            var own=new List<string>{Driver,Jibing,Wangjing};if(scene=="provider")own.AddRange(["classic:jijiang",Watch]);if(scene=="reshuffle")own.Add("fixture:my-prime");if(scene=="multi")own.Add("ol:bingzhong");if(awakening){own.Add(loss?"fixture:my-conditional":Moucuan);own.Add(Health);own.Add(Watch);}if(scene=="skip")own.Add("fixture:my-skip");if(scene=="judgment")own.Add("fixture:my-rejudge");if(scene=="judgment-claim")own.Add("classic:tiandu");if(scene is"judgment-loss"or"judgment-death"or"judgment-handoff")own.Add(Watch);
            b.AddGeneral(new(Owner,"实际马元义","ol-ma-yuan-yi","fixture:my-pick",scene=="provider"?"shu":"qun",scene=="provider"?12:scene=="awake-death"?5:4,own,GeneralGender.Male));
            if(scene=="provider")b.AddSkill(new("fixture:my-provider-pick","实际蜀忠臣供牌者","公开固定角色选将权重"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,x=>x==Role.Loyalist?100000d:-200000d)});
            var peers=Enumerable.Range(1,3).Select(i=>"fixture:my-peer-"+i).ToArray();for(var i=0;i<3;i++){var skills=new List<string>();if(scene=="duel")skills.Add("fixture:my-slash-response");if(scene=="borrowed")skills.AddRange([Jibing,Wangjing,"fixture:my-peer-weapon"]);if(scene=="provider"&&i==0)skills.AddRange([Quiet,Jibing,Wangjing]);b.AddGeneral(new(peers[i],"实际其他角色","supporter",scene=="provider"&&i==0?"fixture:my-provider-pick":scene=="foreign-judgment"?"standard:none":Quiet,scene=="provider"?(i==0?"shu":"wei"):awakening?(i==0?"wei":"shu"):"qun",12,skills));}
            if(scene=="borrowed"){b.AddSkill(new("fixture:my-unused","备用武器","不参与正常选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-200000d)});b.AddGeneral(new("fixture:my-weapon","实际武器素材","supporter","fixture:my-unused","qun",4,[]));}
            var kind=scene=="fan"?CardKind.ZhuqueFan:scene is"slash"or"duel"or"foreign-judgment"?CardKind.Slash:scene=="dodge"?CardKind.Dodge:scene is"fire"or"borrowed"?CardKind.FireSlash:CardKind.Crossbow;
            b.AddCard(new("fixture:my-card","原生"+kind,CardCatalog.Get(kind).CategoryName,"真实原实体",kind));
            b.AddDeck(new("fixture:my-deck","固定同质真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene=="empty"?16:scene is"shortage"or"reshuffle"?17:84).Select(_=>new ContentDeckPhysicalCard("fixture:my-card",scene=="red-judgment"?Suit.Heart:Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"马元义真实原生小局面",4,4,scene=="provider"?new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2}:new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:my-deck",GeneralCandidateCount:scene=="borrowed"?5:4,GeneralPoolIds:scene=="borrowed"?[Owner,..peers,"fixture:my-weapon"]:[Owner,..peers]));
        }
    }
}
