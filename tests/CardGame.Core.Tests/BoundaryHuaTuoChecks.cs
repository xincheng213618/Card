using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class BoundaryHuaTuoChecks
{
    public static void MandatoryPaymentCancellationPreservesLegalComposition()
    {
        var(g,r)=Start("audit-tail");
        var before=g.CardMovements.Count;
        Begin(g);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(!g.CardMovements.Skip(before).Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard"),"Missing mandatory owner payment has no fake receipt.");
        AssertCancelledTail(g);Replay(g,r);Conserve(g);

        var(paid,pr)=Start("audit-paid-tail");Begin(paid);
        Answer(paid,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(paid,c=>c.Cards.Count==1);
        Reach(paid,p=>p.Kind==DecisionKind.PlayCard);
        Require(paid.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard")==1&&
            paid.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward")==1&&
            paid.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.Draw")==1,
            "A paid zero-other composition completes its real cost/reward and legal tail draw once.");Replay(paid,pr);Conserve(paid);

        // Remaining paths are host/store mechanism fixtures, not replay of external changes.
        foreach(var resume in new[]{false,true})
        {
            var(v,_)=Start("audit-paid-tail");Begin(v);Answer(v,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");
            var choice=P(v)!.Choices.First();var frame=v.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            EmptyFixtureHand(v,0);
            if(resume)typeof(GameEngine).GetMethod("ResumeDistinctFactionDiscards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(v,[frame.Id]);
            else Accept(v,new AnswerPromptCommand(0,P(v)!.PromptId,choice.Id,v.Revision));
            AssertCancelledTail(v);Conserve(v);
        }
        foreach(var state in new[]{"source-answer","source-resume","winner-answer","winner-resume"})
        {
            var(v,_)=Start("audit-paid-tail");Begin(v);var choice=P(v)!.Choices.Last();var frame=v.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            if(state.StartsWith("source",StringComparison.Ordinal))
                Players(v)[0].SkillGrants.SetEnabled(Players(v)[0].SkillGrants.Grants.Single(x=>x.SkillId=="boundary:chuli").GrantId,false);
            else typeof(GameEngine).GetField("_winner",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(v,Winner.LordAndLoyalists);
            if(state.EndsWith("resume",StringComparison.Ordinal))typeof(GameEngine).GetMethod("ResumeDistinctFactionDiscards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(v,[frame.Id]);
            else if(state.StartsWith("source",StringComparison.Ordinal))Accept(v,new AnswerPromptCommand(0,P(v)!.PromptId,choice.Id,v.Revision));
            else typeof(GameEngine).GetMethod("ResolveDistinctFactionDiscardChoice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(v,[choice]);
            AssertCancelledTail(v);Conserve(v);
        }
        var(skip,_)=Start("audit-paid-tail");Begin(skip);Answer(skip,c=>c.Targets.SequenceEqual(new[]{2}));
        Answer(skip,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(skip,c=>c.Cards.Count==1);
        var later=P(skip)!.Choices.First();EmptyFixtureHand(skip,2);Accept(skip,new AnswerPromptCommand(0,P(skip)!.PromptId,later.Id,skip.Revision));Reach(skip,p=>p.Kind==DecisionKind.PlayCard);
        Require(skip.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard")==1&&
            skip.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.Draw")==1,
            "A later bare other participant is skipped while the paid composition completes its legal tail.");Conserve(skip);

        var(child,cr)=Start("audit-paid-tail-claim");Begin(child);Answer(child,c=>c.Targets.SequenceEqual(new[]{2}));
        Answer(child,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(child,c=>c.Cards.Count==1);
        Answer(child,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-discard");Reach(child,p=>p.SkillPrompt?.SkillId=="fixture:claim");Replay(child,cr);
        var prefix=child.CardMovements.Where(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard").ToArray();
        Players(child)[0].SkillGrants.SetEnabled(Players(child)[0].SkillGrants.Grants.Single(x=>x.SkillId=="boundary:chuli").GrantId,false);
        Answer(child,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");Reach(child,p=>p.Kind==DecisionKind.PlayCard);
        AssertCancelledTail(child);
        Require(prefix.Length==2&&child.CardMovements.Where(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard").SequenceEqual(prefix)&&
            !child.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward"),
            "Already paid prefix and completed child remain intact; cancellation causes no new cost, reward or tail.");Conserve(child);
    }
    private static void EmptyFixtureHand(GameEngine g,int seat)
    {
        var store=Store(g);var zone=CardLocation.Hand(seat);store.MoveMany(store.CardsAt(zone).Select(c=>c.Id).ToArray(),zone,CardLocation.DiscardPile);
    }
    private static void AssertCancelledTail(GameEngine g)
    {
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:chuli")&&
            !g.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.Draw")&&
            g.Events.Select(e=>e.Payload).Concat((IEnumerable<IGameEvent>)typeof(GameEngine).GetField("_pendingEvents",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!).OfType<ProgramSkillResolvedEvent>().Any(e=>e.SkillId=="boundary:chuli"&&!e.Completed),
            "Cancellation retires the exact owning program with Completed=false and no later draw.");
    }
    public static void OrderedActualDiscardsAndEffectiveSuitReceipts()
    {
        var(g,r)=Start("claim");
        var ownEquip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);
        Accept(g,new PlayCardCommand(0,ownEquip.CardId!.Value,[],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Begin(g);var first=P(g)!;
        var same=first.Choices.First(c=>c.Targets.Count==1&&r.Generals[g.State.Players[c.Targets[0]].GeneralId!].FactionId=="qun").Targets[0];
        var hong=Enumerable.Range(1,3).Single(s=>g.State.Players[s].GeneralId=="fixture:hongyan");
        Answer(g,c=>c.Targets.SequenceEqual(new[]{same}));
        Require(P(g)!.Choices.All(c=>c.Targets.Count==0||r.Generals[g.State.Players[c.Targets[0]].GeneralId!].FactionId!="qun"),"Other factions exclude each other, without excluding owner's faction.");
        Reject(g,first.Choices.First(c=>c.Targets.Count==1&&r.Generals[g.State.Players[c.Targets[0]].GeneralId!].FactionId=="qun"&&c.Targets[0]!=same).Id);
        Answer(g,c=>c.Targets.SequenceEqual(new[]{hong}));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");
        var plan=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().DistinctFactionDiscards!;
        Require(plan.ParticipantSeats.SequenceEqual(new[]{0}.Concat(new[]{same,hong}.OrderBy(s=>s))),"Frozen plan uses actual +seat action order, not clicked order.");
        Answer(g,c=>c.Cards.Contains(ownEquip.CardId!.Value));
        for(var i=0;i<40&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:chuli");i++)
        {
            var p=P(g);
            if(p is null){Step(g);continue;}
            if(p.SkillPrompt?.SkillId=="boundary:chuli")
            {
                Require(p.Choices.All(c=>c.Cards.Count==0),"Other Hand payment publishes opaque slots, no private identity.");
                foreach(var v in Enumerable.Range(1,3))Require(g.CreateSnapshot(v).PendingDecision is null,"Private chooser prompt stays with owner.");Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-discard");
            }
            else if(p.SkillPrompt?.SkillId=="fixture:claim")
            {
                var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().First(f=>f.SkillId=="boundary:chuli");var receipt=frame.DistinctFactionDiscards!.Receipts.Last();
                Require(receipt.EffectiveSuit==(receipt.OwnerSeat==hong?Suit.Heart:Suit.Spade)&&g.CardMovements.Any(m=>m.CardId==receipt.CardId&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(0)),"Actual child claims entity while original owner effective suit remains frozen in paid receipt.");
                Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");
            }
            else Step(g);
        }
        var costs=g.CardMovements.Where(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard").ToArray();
        var rewards=g.CardMovements.Where(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward").ToArray();
        Require(costs.Select(m=>m.From.OwnerSeat!.Value).SequenceEqual(plan.ParticipantSeats)&&costs.Length==3,"Owner pays first, each participant exactly once after children.");
        Require(rewards.Select(m=>m.To.OwnerSeat!.Value).SequenceEqual(new[]{0,same}.OrderBy(s=>Array.IndexOf(plan.ParticipantSeats.ToArray(),s)))&&rewards.All(m=>m.Sequence>costs.Last().Sequence),"All costs precede rewards; printed Spade under Hongyan gives no draw.");
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"Real phase allowance consumed once.");Replay(g,r);Conserve(g);
    }
    public static void ZeroOthersAndIllegalInputs()
    {
        var(g,r)=Start();Begin(g);Reject(g,new("forged"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");
        var own=P(g)!.Choices.First(c=>c.Cards.Count==1);var before=g.CardMovements.Count;Replay(g,r);Accept(g,new AnswerPromptCommand(0,P(g)!.PromptId,own.Id,g.Revision));
        Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(g.CardMovements.Skip(before).Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard")==1&&g.CardMovements.Skip(before).Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward")==1,"Zero others still pays own actual Spade and earns one actual draw.");
        var result=g.Submit(new UseProgramSkillCommand(0,"boundary:chuli","discard-participants",[],[],g.Revision,P(g)!.PromptId));Require(!result.Accepted,"Second activation in same phase rejects.");Replay(g,r);Conserve(g);
    }
    public static void ExtraPlayAndSourceLifecycleMechanisms()
    {
        var(g,r)=Start("extra");var turn=g.CreateSnapshot(0).TurnNumber;Begin(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(g,c=>c.Cards.Count==1);Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"First actual Play allowance used.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(g.CreateSnapshot(0).TurnNumber==turn&&g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"Inserted and normal actual Play reset shared phase quota within one turn.");Begin(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(g,c=>c.Cards.Count==1);Reach(g,p=>p.Kind==DecisionKind.PlayCard);Replay(g,r);
        // External grant model: not command replay coverage after source mutation.
        var owner=Players(g)[0];var grant=owner.SkillGrants.Grants.Single(x=>x.SkillId=="boundary:chuli");owner.SkillGrants.RemoveGrant(grant.GrantId);owner.SkillGrants.Grant(grant);
        var revision=g.Revision;var events=g.Events.Count;var movement=g.CardMovements.Count;
        for(var i=0;i<3;i++)Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"Losing and regranting exact source cannot refresh named phase usage.");
        Require(g.Revision==revision&&g.Events.Count==events&&g.CardMovements.Count==movement,"Repeated pure queries change no state/events/entities.");
        var(source,_)=Start();Begin(source);Answer(source,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");var cost=P(source)!.Choices.First(c=>c.Cards.Count==1);var before=source.CardMovements.Count;
        var src=Players(source)[0].SkillGrants.Grants.Single(x=>x.SkillId=="boundary:chuli");Players(source)[0].SkillGrants.SetEnabled(src.GrantId,false);
        Accept(source,new AnswerPromptCommand(0,P(source)!.PromptId,cost.Id,source.Revision));Reach(source,p=>p.Kind==DecisionKind.PlayCard);
        Require(source.CardMovements.Count==before,"Disabled exact source cancels unpaid cost, no fake paid receipt or reward.");Players(source)[0].SkillGrants.SetEnabled(src.GrantId,true);Require(!source.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"Re-enabled source cannot reset accepted phase allowance.");
    }
    public static void MissingParticipantAndActualOwnerDeath()
    {
        var(g,r)=Start("claim");Begin(g);Answer(g,c=>c.Targets.SequenceEqual(new[]{3}));Answer(g,c=>c.Targets.SequenceEqual(new[]{1}));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(g,c=>c.Cards.Count==1);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-discard");Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:claim");Replay(g,r);
        // Direct store mechanism setup makes later participant genuinely bare. Not command-replay coverage.
        var store=(CardZoneStore)typeof(GameEngine).GetField("_cardZones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
        var bare=CardLocation.Hand(3);store.MoveMany(store.CardsAt(bare).Select(c=>c.Id).ToArray(),bare,CardLocation.DiscardPile);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard"&&m.From.OwnerSeat==3)&&!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward"&&m.To.OwnerSeat==3),"Later bare participant is skipped without receipt or reward.");Conserve(g);
        var(dead,dr)=Start("death");Begin(dead);Answer(dead,c=>c.Targets.SequenceEqual(new[]{2}));Answer(dead,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(dead,c=>c.Cards.Count==1);
        for(var i=0;i<20&&dead.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:chuli");i++)Step(dead);
        Require(!dead.State.Players[0].IsAlive&&dead.CardMovements.Count(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard")==1&&!dead.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward"),"Actual payment child owner death stops new costs and all rewards.");Replay(dead,dr);Conserve(dead);
    }
    public static void JijiuEquipmentRealOutsideTurnAndOwnTurnNegative()
    {
        var(g,r)=Start("rescue");var equip=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Accept(g,new PlayCardCommand(0,equip.CardId!.Value,[],g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);
        var chosen=P(g)!.Choices.First(c=>c.Cards.Contains(equip.CardId!.Value));Replay(g,r);Accept(g,new AnswerPromptCommand(0,P(g)!.PromptId,chosen.Id,g.Revision));
        Require(g.State.Players[0].Hp==1&&g.State.Players[0].IsAlive&&g.CardMovements.Any(m=>m.CardId==equip.CardId&&m.From==CardLocation.Equipment(0)&&m.To==CardLocation.Processing),"Outside own turn red HE performs actual Peach and rescues self.");Replay(g,r);Conserve(g);
        var(own,or)=Start("own-dying");Accept(own,new UseProgramSkillCommand(0,"fixture:loss","lose",[],[],own.Revision,P(own)!.PromptId));for(var i=0;i<10&&own.State.Players[0].IsAlive;i++)Step(own);
        Require(!own.State.Players[0].IsAlive&&!own.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.EffectiveKind==CardKind.Peach),"Own-turn red HE does not create a Jijiu Peach; real dying exhausts without a rescue option.");Replay(own,or);
    }
    public static void EffectiveFactionAndPublicEquipmentMechanisms()
    {
        var(g,r)=Start();Replay(g,r);
        // Chosen faction is the existing actual god-faction query input; this is an external model fixture.
        Players(g)[1].ChosenFactionId="qun";
        Begin(g);Answer(g,c=>c.Targets.SequenceEqual(new[]{2}));
        Require(P(g)!.Choices.All(c=>c.Targets.Count==0),"Effective chosen faction, rather than printed Wu, excludes a second same-faction participant.");
        var revision=g.Revision;var count=g.Events.Count;for(var i=0;i<3;i++)g.CreateSnapshot(i);
        Require(g.Revision==revision&&g.Events.Count==count,"Public view query does not move cards or advance a private cursor.");
        var(other,or)=Start();Replay(other,or);var store=(CardZoneStore)typeof(GameEngine).GetField("_cardZones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(other)!;
        var equip=store.CardsAt(CardLocation.Hand(2)).First();store.MoveMany([equip.Id],CardLocation.Hand(2),CardLocation.Equipment(2));
        Begin(other);Answer(other,c=>c.Targets.SequenceEqual(new[]{2}));Answer(other,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(other,c=>c.Cards.Count==1);
        Require(P(other)!.Choices.Any(c=>c.Cards.SequenceEqual(new[]{equip.Id}))&&P(other)!.Choices.Any(c=>c.Cards.Count==0),"Foreign Equipment is public selectable identity; foreign Hand remains opaque slots.");
        Answer(other,c=>c.Cards.SequenceEqual(new[]{equip.Id}));Reach(other,p=>p.Kind==DecisionKind.PlayCard);
        Require(other.CardMovements.Any(m=>m.CardId==equip.Id&&m.From==CardLocation.Equipment(2)&&m.To==CardLocation.DiscardPile),"Published foreign equipment pays through an actual command discard.");Conserve(other);
    }
    public static void MandatoryOwnerPaymentSourceAndVanishingMechanisms()
    {
        var(g,r)=Start();Replay(g,r);var store=Store(g);var owner=Players(g)[0];
        // Host grant/card setup is explicitly a mechanism fixture, not command replay of external mutation.
        store.MoveMany(store.CardsAt(CardLocation.Hand(0)).Select(c=>c.Id).ToArray(),CardLocation.Hand(0),CardLocation.DiscardPile);
        var native=owner.SkillGrants.Grants.Single(x=>x.SkillId=="boundary:chuli");owner.SkillGrants.RemoveGrant(native.GrantId);
        var source=AddFixtureEquipment(g,new Card(81,CardKind.XingtianAxe,Suit.Spade,1));
        owner.SkillGrants.Grant(new("equipment:xingtian:81","boundary:chuli","equipment:xingtian:81","equipment:xingtian:81"));
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:chuli"),"Exact source Equipment alone cannot offer an activation with an unpaid mandatory owner cost.");
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));var reject=g.Submit(new UseProgramSkillCommand(0,"boundary:chuli","discard-participants",[],[],g.Revision,P(g)!.PromptId));
        Require(!reject.Accepted&&before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"Source-only activation rejection is atomic.");
        var(v,_)=Start();Begin(v);Answer(v,c=>c.Targets.SequenceEqual(new[]{2}));Answer(v,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");var cost=P(v)!.Choices[0];var count=v.CardMovements.Count;var vs=Store(v);
        vs.MoveMany(vs.CardsAt(CardLocation.Hand(0)).Select(c=>c.Id).ToArray(),CardLocation.Hand(0),CardLocation.DiscardPile);
        Accept(v,new AnswerPromptCommand(0,P(v)!.PromptId,cost.Id,v.Revision));Reach(v,p=>p.Kind==DecisionKind.PlayCard);
        Require(v.CardMovements.Count==count&&!v.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:chuli"),"Owner cost becoming empty cancels the plan before any other discard or reward.");Conserve(v);
    }
    public static void GeneralWeaponActualOutsideReceiptMechanism()
    {
        var(g,r)=Start("weapon");Replay(g,r);
        AddFixtureEquipment(g,new Card(81,CardKind.GeneralWeapon,Suit.None,0){IsGeneralWeapon=true,PrintedName="固定武将武器",PrintedAttackRange=3});
        Begin(g);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="distinct-faction-finish");Answer(g,c=>c.Cards.SequenceEqual(new[]{81}));Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:claim");
        var paidFrame=g.ResolutionStack.OfType<ProgramSkillFrame>().First(f=>f.SkillId=="boundary:chuli");var receipt=paidFrame.DistinctFactionDiscards!.Receipts.Single();
        Require(((IList<ProgramDiscardReceipt>)paidFrame.DistinctFactionDiscards.Receipts).IsReadOnly&&((IList<int>)paidFrame.DistinctFactionDiscards.ParticipantSeats).IsReadOnly,"Nested typed receipt and plan collections are immutable.");
        RejectReceiptInvariant(g,r,paidFrame with{DistinctFactionDiscards=paidFrame.DistinctFactionDiscards with{Receipts=Array.AsReadOnly(new[]{receipt with{MovementSequence=receipt.MovementSequence+1000}})}});
        RejectReceiptInvariant(g,r,paidFrame with{DistinctFactionDiscards=paidFrame.DistinctFactionDiscards with{RewardIndex=1}});
        Require(receipt.Destination==CardLocation.OutsideGame&&receipt.Source==CardLocation.Equipment(0)&&receipt.EffectiveSuit==Suit.None&&g.CardMovements.Any(m=>m.Sequence==receipt.MovementSequence&&m.CardId==81&&m.From==receipt.Source&&m.To==receipt.Destination),"Real general weapon normalization retains exact typed source/destination/sequence receipt.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");Reach(g,p=>p.Kind==DecisionKind.PlayCard);
        Require(Store(g).GetLocation(81)==CardLocation.OutsideGame&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==81&&!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-reward"),"Weapon entity is conserved outside game and Suit.None earns no reward.");
    }
    public static void AutomaticAiBoundedActualCompletion()
    {
        var(g,r)=Start("ai");Require(g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:chuli").ProgramAiHint!.OwnerDraw==0,"AI estimate does not promise an unconditionally rewarded draw.");Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
        for(var i=0;i<120&&!(P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});i++)Step(g);
        Require(P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:chuli"),"Fixed automatic AI turn finishes finite private selection/cost/reward cursors.");
        Require(g.CardMovements.Any(m=>m.Reason.Value=="skill-program.boundary:chuli.participant-discard"&&m.From.OwnerSeat!=0),"Actual AI commands pay a participant cost, rather than only scoring a helper.");
        Replay(g,r);Conserve(g);
    }
    private static void RejectReceiptInvariant(GameEngine g,ContentRegistry r,ProgramSkillFrame frame)
    {
        try{typeof(GameEngine).GetMethod("AssertDistinctFactionDiscardDraft",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(g,[frame,r.GetSkill("boundary:chuli").Program!.Activations.Single().Effects.Single()]);}
        catch(System.Reflection.TargetInvocationException e)when(e.InnerException is InvalidOperationException){return;}
        throw new InvalidOperationException("Corrupt receipt history/reward cursor must fail its actual owning invariant.");
    }
    private static CardZoneStore Store(GameEngine g)=>(CardZoneStore)typeof(GameEngine).GetField("_cardZones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
    private static Card AddFixtureEquipment(GameEngine g,Card card)
    {
        Store(g).AddGeneratedCard(card);((HashSet<int>)typeof(GameEngine).GetField("_generatedPhysicalCardIds",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!).Add(card.Id);
        Store(g).MoveMany([card.Id],CardLocation.OutsideGame,CardLocation.Equipment(0));return card;
    }
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
    private static (GameEngine,ContentRegistry) Start(string mode="basic")
    {
        var all=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(all,mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-hua-tuo-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:owner",g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private static void Begin(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"boundary:chuli","discard-participants",[],[],g.Revision,P(g)!.PromptId));
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Require(result.Accepted,result.Error?.Message??"Rejected command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Step(GameEngine g){var p=P(g);if(p is{PlayerSeat:0,Kind:DecisionKind.ProgramTrigger})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{PlayerSeat:0,Kind:DecisionKind.PlayCard})Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> condition){for(var i=0;i<80;i++){if(P(g)is{} p&&condition(p))return;Step(g);}throw new InvalidOperationException("Fixture did not reach expected prompt: "+P(g)?.Prompt);}
    private static void Reject(GameEngine g,ChoiceId id){var before=SnapshotJson.Serialize(g.CreateSnapshot(0));var result=g.Submit(new AnswerPromptCommand(0,P(g)!.PromptId,id,g.Revision));Require(!result.Accepted&&before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"Illegal choice is atomic.");}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(v=>SnapshotJson.Serialize(g.CreateSnapshot(v))==SnapshotJson.Serialize(restored.CreateSnapshot(v)))&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack)&&g.CardMovements.SequenceEqual(restored.CardMovements),"Four viewer snapshots and exact typed paid cursor replay.");}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==80,"Physical entities conserved.");
    private static void Require(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    private sealed class Fixture(ContentRegistry all,string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:hua-tuo",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var id in new[]{"boundary:chuli","boundary:jijiu","classic:hongyan"})
            {
                var skill=all.GetSkill(id);
                if(id=="boundary:chuli"&&mode.StartsWith("audit-",StringComparison.Ordinal))
                {
                    var prefix=mode=="audit-tail"?"{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\",\"equipment\"]},":"";
                    var c=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"boundary:chuli","revision":1,"activations":[{"id":"discard-participants","usesPerTurn":null,"usesPerPhase":1,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","condition":{"kind":"always"},"effects":[{{{prefix}}}{"op":"discardDistinctFactionParticipants","target":"owner"},{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"boundary:chuli":{"name":"审查组合","description":"审查组合"}}}""");
                    skill=skill with{Program=c.Programs[id]};
                }
                b.AddSkill(skill);
            }
            if(mode is "claim" or "weapon" or "audit-paid-tail-claim")
            {
                var c=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:claim","revision":1,"triggers":[{"id":"claim","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perCard","movementReasons":["skill-program.boundary:chuli.participant-discard"],"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"},{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue","condition":{"kind":"always"}}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:claim":{"name":"真实取回子链","description":"取回并暂停","optionLabels":{"continue":"继续"}}}}""");
                if(mode=="weapon")c=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:claim","revision":1,"triggers":[{"id":"pause","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:chuli.participant-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue","condition":{"kind":"always"}}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:claim":{"name":"真实移动子链","description":"暂停","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:claim","真实取回子链","取回并暂停"){Program=c.Programs["fixture:claim"]});
            }
            if(mode is "extra" or "death" or "rescue" or "own-dying")
            {
                var rules=mode=="extra"?"\"triggers\":[{\"id\":\"extra\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]":mode=="own-dying"?"\"activations\":[{\"id\":\"lose\",\"usesPerTurn\":1,\"condition\":{\"kind\":\"always\"},\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}]}]":mode=="rescue"?"\"triggers\":[{\"id\":\"loss\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}]}]":"\"triggers\":[{\"id\":\"loss\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementReasons\":[\"skill-program.boundary:chuli.participant-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}]}]";
                var c=SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\"fixture:loss\",\"revision\":1,"+rules+"}]}","{\"schemaVersion\":3,\"skills\":{\"fixture:loss\":{\"name\":\"真实阶段驱动\",\"description\":\"真实失血与阶段\"}}}");
                b.AddSkill(new("fixture:loss","真实阶段驱动","真实失血与阶段"){Program=c.Programs["fixture:loss"]});
            }
            b.AddGeneral(new("fixture:owner","华佗夹具","supporter","boundary:jijiu","qun",mode is "rescue" or "own-dying" or "death"?3:20,new[]{"boundary:chuli"}.Concat(mode is "claim" or "weapon" or "audit-paid-tail-claim"?["fixture:claim"]:mode is "extra" or "death" or "rescue" or "own-dying"?["fixture:loss"]:Array.Empty<string>()).ToArray()));
            b.AddGeneral(new("fixture:hongyan","红颜夹具","supporter","classic:hongyan","wu",20,mode=="ai"?["boundary:chuli"]:[]));
            for(var i=1;i<=2;i++)b.AddGeneral(new("fixture:qun"+i,"同势力夹具"+i,"supporter","standard:none","qun",20,mode=="ai"?["boundary:chuli"]:[]));
            b.AddDeck(new("fixture:deck","固定黑桃实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard("standard:crossbow",mode is "rescue" or "own-dying"?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-hua-tuo-fixture","界华佗机制",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:owner","fixture:hongyan","fixture:qun1","fixture:qun2"]));
        }
    }
}
