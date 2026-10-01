using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinGuanqiuJianChecks
{
    public static void FinalTargetsAnonymousAndUnlimited()
    {
        var(g,r)=Start();Driver(g,"supply");Settle(g);
        Accept(g,new UseProgramSkillCommand(0,"fixture:gj-driver","equip-target",[],[1],g.Revision,P(g)!.PromptId));Reach(g,p=>Action(p,"select-and-move-owned-card"));Choose(g,c=>true);Settle(g);PrepareArrows(g,5);
        for(var i=0;i<5;i++)
        {
            PlayArrow(g);Reach(g,p=>Action(p,"public-pile-flow"));var prompt=P(g)!;
            var use=g.ResolutionStack.OfType<CardUseFrame>().Last();
            Require(use.TargetSeats.Count==3 && prompt.Choices.All(c=>c.Targets.Count==1&&use.TargetSeats.Contains(c.Targets[0])),"Only the one real finalized multi-target action supplies collection targets.");
            if(i==0)
            {
                Require(GetTargetHands(g,prompt).Any(count=>count==g.State.Players[0].HandCount),"The >= boundary includes exact hand-count ties.");
                Require(prompt.Choices.Any(c=>c.Parameters["source-zone"]=="Equipment"&&c.Cards.Count==1),"Real target equipment is public and selectable as well as anonymous hand slots.");
            }
            var hidden=prompt.Choices.First(c=>c.Parameters["source-zone"]==nameof(CardZoneKind.Hand));
            Require(hidden.Cards.Count==0&&prompt.Choices.Where(c=>c.Parameters["source-zone"]==nameof(CardZoneKind.Hand)).All(c=>c.Cards.Count==0&&!c.Description.Contains("【")),"Other hand slots contain neither faces nor entity IDs.");
            Require(g.CreateSnapshot(1,false).PendingDecision is null,"Target hand selection belongs only to the skill owner.");
            var chosen=i==0?prompt.Choices.First(c=>c.Parameters["source-zone"]=="Equipment"):hidden;
            var seat=chosen.Targets.Single();var slot=int.Parse(chosen.Parameters["slot-index"]);
            var actual=chosen.Cards.Count==1?chosen.Cards.Single():g.CreateSnapshot(seat,true).Players[seat].Hand[slot].Id;
            Atomic(g,new AnswerPromptCommand(1,prompt.PromptId,hidden.Id,g.Revision));Atomic(g,new AnswerPromptCommand(0,prompt.PromptId,new("unpublished"),g.Revision));Replay(g,r);
            Answer(g,chosen);Settle(g);
            Require(Pile(g).Count==i+1&&Pile(g).Any(c=>c.Id==actual),"One actual target-owned entity enters the public pile once per use.");Public(g);Conserve(g);
        }
        Require(g.Events.Select(e=>e.Payload).OfType<FinalTargetCardStoredEvent>().Select(e=>e.ActionId).Distinct().Count()==5&&Pile(g).Count==5,"The new opt-in pile has no four-card book cap and no per-target repeated collection.");Replay(g,r);
    }
    public static void AwakeningZeroAndEqualHandExchange()
    {
        foreach(var n in new[]{0,2})
        {
            var(g,r)=Start();var initialMax=g.State.Players[0].MaxHp;Collect(g,3);var original=Pile(g).Select(c=>c.Id).ToArray();Driver(g,"supply");Settle(g);
            End(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="owned"));
            Require(g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f=>f.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)&&g.State.Players[0].MaxHp==initialMax,"Mandatory awakening waits for zero-or-equal hand exchange before reducing maximum HP. max="+g.State.Players[0].MaxHp+" frames="+string.Join(",",g.ResolutionStack.Select(f=>f.GetType().Name))+" context="+g.ResolutionStack.OfType<ProgramSkillFrame>().Last().WindowContext?.Window);
            var entering=P(g)!.Choices.Where(c=>c.Cards.Count==1).Take(n).Select(c=>c.Cards[0]).ToArray();
            Require(P(g)!.Choices.Where(c=>c.Cards.Count==1).All(c=>g.CreateCardZoneDiagnostics().Any(z=>z.CardId==c.Cards[0]&&z.Location==CardLocation.Hand(0))),"Awakening only exchanges real hand cards, excluding equipment.");
            foreach(var id in entering){var selected=P(g)!.Choices.Single(c=>c.Cards.SequenceEqual([id]));Answer(g,selected);if(P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="owned"))Atomic(g,new AnswerPromptCommand(0,P(g)!.PromptId,selected.Id,g.Revision));Replay(g,r);}
            Choose(g,c=>c.Parameters.GetValueOrDefault("token")=="finish");
            if(n>0)
            {
                Require(!P(g)!.IsPrivate&&g.CreateSnapshot(1,false).PublicRevealedCards.Select(c=>c.Id).Order().SequenceEqual(original.Order()),"Outgoing pile choices publish their clickable public entities.");
                foreach(var id in original.Take(n)){Choose(g,c=>c.Cards.SequenceEqual([id]));if(id!=original[n-1])Replay(g,r);}
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gj-gain");
                Require(Pile(g).Count==3&&entering.All(id=>Pile(g).Any(c=>c.Id==id))&&original.Take(n).All(id=>g.CreateCardZoneDiagnostics().Any(z=>z.CardId==id&&z.Location==CardLocation.Hand(0))),"Equal sides settle atomically before the actual gain child.");Replay(g,r);Choose(g,c=>true);
            }
            Settle(g);Require(g.State.Players[0].MaxHp==initialMax-1&&g.CreateSnapshot(0,true).Players[0].Skills!.Any(s=>s.ContentId=="classic:qingce"),"Three honors awaken without a dead player, including zero exchange, then grant the active skill.");
            Require(g.State.Players.All(p=>p.IsAlive),"Awakening must not require any death.");Replay(g,r);Conserve(g);
        }
    }
    public static void GainPaymentFieldAndNestedReplay()
    {
        var(g,r)=Start("field");Awaken(g);EquipField(g);var delayed=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Lightning);Accept(g,new PlayCardCommand(0,delayed.CardId!.Value,delayed.TargetSeats,g.Revision,P(g)!.PromptId,delayed.PlayedCardKind));Settle(g);var pile=Pile(g).Select(c=>c.Id).ToArray();
        UseQingce(g);Reach(g,p=>Action(p,"public-pile-flow"));var take=P(g)!.Choices.First();var obtained=take.Cards.Single();
        Require(g.CreateSnapshot(2,false).PublicRevealedCards.Select(c=>c.Id).Order().SequenceEqual(pile.Order()),"Public obtain choice is a real public face; private future hand cost is not exposed.");Replay(g,r);Answer(g,take);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gj-gain");Require(g.CreateCardZoneDiagnostics().Single(z=>z.CardId==obtained).Location==CardLocation.Hand(0),"Gain observer sees the obtained physical honor in hand.");Replay(g,r);Choose(g,c=>true);
        Reach(g,p=>Action(p,"select-and-move-owned-card"));Require(P(g)!.IsPrivate&&g.CreateSnapshot(1,false).PendingDecision is null,"Only the owner chooses the actual post-gain hand payment.");
        var payment=P(g)!.Choices.First(c=>c.Cards.SequenceEqual([obtained]));Replay(g,r);Answer(g,payment);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gj-discard");Require(g.CreateCardZoneDiagnostics().Single(z=>z.CardId==obtained).Location==CardLocation.DiscardPile,"Hand payment is committed before its discard child.");Replay(g,r);Choose(g,c=>true);
        Reach(g,p=>Action(p,"public-pile-flow"));Require(P(g)!.Choices.All(c=>c.Parameters["source-zone"] is "Equipment" or "Judgment"),"Final removal accepts only public E/J, never hand.");
        Require(P(g)!.Choices.Any(c=>c.Parameters["source-zone"]=="Equipment")&&P(g)!.Choices.Any(c=>c.Parameters["source-zone"]=="Judgment"),"Both genuine E and J zones are offered.");
        var field=P(g)!.Choices.First(c=>c.Targets.SequenceEqual([0])&&c.Parameters["source-zone"]=="Judgment");var fieldId=field.Cards.Single();Atomic(g,new AnswerPromptCommand(1,P(g)!.PromptId,field.Id,g.Revision));Replay(g,r);Answer(g,field);Settle(g);
        Require(g.CardMovements.Count(m=>m.CardId==obtained&&m.Reason.Value=="skill-program.public-pile.obtain")==1&&g.CardMovements.Count(m=>m.CardId==fieldId&&m.Reason.Value=="skill-program.public-pile.field-discard")==1,"Obtain/payment/field child returns do not repeat physical costs.");Replay(g,r);Conserve(g);
    }
    public static void GainEntityMovedAndOwnerDeath()
    {
        foreach(var mode in new[]{"strip-gain","death-gain"})
        {
            var(g,r)=Start(mode);Awaken(g);EquipField(g);UseQingce(g);Reach(g,p=>Action(p,"public-pile-flow"));Choose(g,c=>true);
            Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gj-gain");Replay(g,r);Choose(g,c=>true);
            for(var i=0;i<80&&g.ResolutionStack.Count>0;i++)Tick(g);
            Require(!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.public-pile.field-discard"),"A gain child removing the hand or killing owner cancels unpaid field removal without rollback.");
            if(mode=="strip-gain")Require(g.State.Players[0].IsAlive&&g.State.Players[0].HandCount==0,"No actual hand means no illegal free discard of a field card.");
            else Require(!g.State.Players[0].IsAlive&&Pile(g).Count==0,"True owner death clears remaining public entities and retires the skill cursor.");Replay(g,r);Conserve(g);
        }
    }
    public static void SourceLossAndNativeAi()
    {
        var(g,r)=Start();Collect(g,1);var id=Pile(g).Single().Id;Driver(g,"loss");Require(Pile(g).Count==0&&g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location==CardLocation.DiscardPile,"Actual storage source loss clears unbounded public honors.");Replay(g,r);
        (g,r)=Start("ai");End(g);
        for(var i=0;i<180&&!g.Events.Select(e=>e.Payload).OfType<FinalTargetCardStoredEvent>().Any(e=>e.OwnerSeat!=0);i++)Tick(g);
        Require(g.Events.Select(e=>e.Payload).OfType<FinalTargetCardStoredEvent>().Any(e=>e.OwnerSeat!=0),"AdvanceOneStep normal AI uses a real damage card and autonomously chooses an anonymous valid target card.");Replay(g,r);Conserve(g);
    }
    public static void MultiplePublicPileSources()
    {
        var (game, registry) = Start("multi");
        Collect(game, 5);
        var owner = game.CreateSnapshot(0, false).Players[0];
        var sources = owner.PublicPersistentPiles!;
        var honors = sources.Single(p => p.SourceSkillId == "classic:zhengrong");
        var books = sources.Single(p => p.SourceSkillId == "classic:bizhuan");
        Require(honors.Count == 5 && books.Count == 4 && honors.Location != books.Location && honors.Location.PublicPileId is null && books.Location.PublicPileId is not null,
            "Real uses collect five honors and four books into independent authoritative physical zones.");
        Require(owner.PublicPersistentPileCards is null && owner.PublicPersistentPileCount == 0 && owner.PublicPersistentPileName is null,
            "A multiple-source view cannot mislabel the combined cards as its legacy scalar pile.");
        Require(Enumerable.Range(0, 4).All(seat => JsonSerializer.Serialize(game.CreateSnapshot(seat, false).Players[0].PublicPersistentPiles) == JsonSerializer.Serialize(sources)),
            "Every viewer sees the same named-source faces and physical locations.");
        var mutationRejected = false;
        try { ((IList<CardSnapshot>)books.Cards)[0] = honors.Cards[0]; } catch (NotSupportedException) { mutationRejected = true; }
        Require(mutationRejected, "Nested public source cards are deeply frozen.");
        mutationRejected = false;
        try { ((IList<PublicPersistentPileSnapshot>)sources)[0] = books; } catch (NotSupportedException) { mutationRejected = true; }
        Require(mutationRejected, "The outer public source list is deeply frozen.");
        Replay(game, registry); Conserve(game);
        Driver(game, "supply"); Settle(game); Driver(game, "supply"); Settle(game); Driver(game, "supply"); Settle(game);
        var hand = game.State.Players[0].HandCount;
        End(game); Reach(game, p => p.Kind == DecisionKind.DiscardCards);
        Require(P(game)!.RequiredCardCount == hand - game.State.Players[0].Hp - books.Count,
            "The real discard phase applies only the four books to hand limit, excluding five honors.");
        Tick(game);
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:hongju" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "owned"));
        var bookIds = books.Cards.Select(c => c.Id).Order().ToArray();
        var honorIds = Pile(game).Select(c => c.Id).Order().ToArray();
        var entering = P(game)!.Choices.First(c => c.Cards.Count == 1); Answer(game, entering);
        Choose(game, c => c.Parameters.GetValueOrDefault("token") == "finish");
        Require(P(game)!.Choices.All(c => honorIds.Contains(c.Cards.Single())) && game.CreateSnapshot(0, false).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(honorIds),
            "Awakening publishes only its referenced honors and cannot withdraw a book.");
        var outgoing = P(game)!.Choices[0];
        Atomic(game, new AnswerPromptCommand(0, P(game)!.PromptId, new(outgoing.Id.Value.Replace("card-" + outgoing.Cards.Single(), "card-" + bookIds[0], StringComparison.Ordinal)), game.Revision));
        Replay(game, registry); Answer(game, outgoing);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:gj-gain"); Replay(game, registry); Choose(game, c => true);
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:tongbo" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "owned"));
        Require(game.CreateSnapshot(0, false).Players[0].PublicPersistentPiles!.Single(p => p.SourceSkillId == "classic:bizhuan").Cards.Select(c => c.Id).Order().SequenceEqual(bookIds),
            "The honor exchange and gain child leave every book entity in place.");
        var honorAfter = Pile(game).Select(c => c.Id).Order().ToArray();
        Answer(game, P(game)!.Choices.First(c => c.Cards.Count == 1)); Choose(game, c => c.Parameters.GetValueOrDefault("token") == "finish");
        Require(P(game)!.Choices.All(c => bookIds.Contains(c.Cards.Single())) && game.CreateSnapshot(1, false).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(bookIds),
            "The existing HE book exchange publishes only book entities, excluding honors.");
        Replay(game, registry); Choose(game, c => true); Settle(game);
        Require(Pile(game).Select(c => c.Id).Order().SequenceEqual(honorAfter), "Book exchange does not steal or replace honors.");
        Driver(game, "loss"); Settle(game);
        var remaining = game.CreateSnapshot(0, false).Players[0];
        Require(remaining.PublicPersistentPiles is null && remaining.PublicPersistentPileName == "书" && remaining.PublicPersistentPileCount == 4 &&
            honorAfter.All(id => game.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location == CardLocation.DiscardPile),
            "Losing the honor source clears only its entities and restores the legacy scalar book view.");
        Replay(game, registry); Conserve(game);
        Driver(game, "loss-book"); Settle(game);
        Require(game.CreateSnapshot(0, false).Players[0].PublicPersistentPileCount == 0, "Losing the remaining book source cleans its exact named location.");
        Replay(game, registry); Conserve(game);
    }
    public static void IndependentSourceThresholds()
    {
        foreach (var honors in new[] { 0, 1 })
        {
            var (game, registry) = Start("multi"); Collect(game, 4);
            Driver(game, "loss"); Settle(game);
            if (honors == 1)
            {
                Driver(game, "grant-second"); Settle(game); Collect(game, 1);
            }
            var maxHp = game.State.Players[0].MaxHp;
            End(game); Settle(game);
            Require(game.State.Players[0].MaxHp == maxHp && !game.CreateSnapshot(0, false).Players[0].Skills!.Any(s => s.ContentId == "classic:qingce"),
                "Four books with zero or one honor cannot qualify the honor awakening, including a missing source in frozen facts.");
            Replay(game, registry); Conserve(game);
        }
        {
            var (game, registry) = Start("multi"); Collect(game, 4);
            Driver(game, "loss-book"); Settle(game); Driver(game, "grant-book"); Settle(game);
            PrepareArrows(game, 1); PlayArrow(game); Reach(game, p => Action(p, "public-pile-flow")); Choose(game, c => true); Settle(game);
            var sources = game.CreateSnapshot(0, false).Players[0].PublicPersistentPiles!;
            Require(sources.Single(s => s.SourceSkillId == "classic:zhengrong").Count == 5 && sources.Single(s => s.SourceSkillId == "classic:bizhuan").Count == 1,
                "Five honors cannot block a real new book store when its own source has fewer than four cards.");
            Replay(game, registry); Conserve(game);
        }
        Require(!JsonSerializer.Serialize(CardLocation.Hand(0)).Contains("PublicPileId", StringComparison.Ordinal), "Legacy card locations omit the optional pile identity.");
        var invalid = false;
        try { _ = new CardLocation(CardZoneKind.Hand, 0, "illegal"); } catch (ArgumentException) { invalid = true; }
        Require(invalid, "A source identity cannot create an alternate hand zone.");
    }
    public static void MultipleInstancesAndDeath()
    {
        var (game, registry) = Start("instances");
        Driver(game, "grant-second"); Settle(game); Accept(game, new UseProgramSkillCommand(0, "fixture:gj-consumer-grant", "grant", [], [], game.Revision, P(game)!.PromptId)); Settle(game); PrepareArrows(game, 3);
        for (var index = 0; index < 3; index++)
        {
            PlayArrow(game); Reach(game, p => Action(p, "public-pile-flow")); Choose(game, c => true); Settle(game);
        }
        var sources = game.CreateSnapshot(0, false).Players[0].PublicPersistentPiles!;
        Require(sources.Count == 2 && sources.All(s => s.SourceSkillId == "classic:zhengrong" && s.Count == 3) && sources.Select(s => s.SourceSkillInstanceId).Distinct().Count() == 2,
            "Two genuine enabled grants of one skill have distinct source instances and physical zones.");
        End(game);
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "source"));
        Require(P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("stage") == "source") == true, "Missing explicit source choice.");
        Require(P(game)!.Choices.Count == 2 && P(game)!.Choices.All(c => c.Cards.Count == 0), "An ambiguous consumer explicitly selects its qualified source before exchanging.");
        Replay(game, registry); Atomic(game, new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices[0].Id, game.Revision));
        Choose(game, c => true); var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Last().PublicPileDraft!;
        Require(draft.SourceSkillInstanceId is not null && draft.SourceLocation is not null, "The owning frame freezes the selected exact source instance and location.");
        Choose(game, c => c.Parameters.GetValueOrDefault("token") == "finish"); Settle(game);
        EquipField(game); UseQingce(game); Reach(game, p => Action(p, "public-pile-flow"));
        Require(P(game)!.Choices.Count == 6, "The gain consumer can select an actual card from either referenced instance, without merging physical zones.");
        var selected = P(game)!.Choices[0].Cards.Single(); var location = game.CreateCardZoneDiagnostics().Single(z => z.CardId == selected).Location;
        Choose(game, c => c.Cards.SequenceEqual([selected])); Reach(game, p => p.SkillPrompt?.SkillId == "fixture:gj-gain");
        Require(game.CardMovements.Last(m => m.CardId == selected).From == location, "The exact selected source is used for the real gain movement.");
        Replay(game, registry); Choose(game, c => true); Reach(game, p => Action(p, "select-and-move-owned-card")); Choose(game, c => true); Reach(game, p => p.SkillPrompt?.SkillId == "fixture:gj-discard"); Choose(game, c => true); Reach(game, p => Action(p, "public-pile-flow")); Choose(game, c => true); Settle(game);
        Driver(game, "die"); for (var index = 0; index < 90 && game.ResolutionStack.Count > 0; index++) Tick(game);
        Require(!game.State.Players[0].IsAlive && !game.CreateCardZoneDiagnostics().Any(z => z.Location.Zone == CardZoneKind.PublicPersistentPile && z.Location.OwnerSeat == 0),
            "True death cleans every source instance's physical zone and resumes the typed death owner.");
        Replay(game, registry); Conserve(game);
    }
    public static void StrictResourceContracts()
    {
        var invalid = new[]
        {
            "\"activations\":[{\"id\":\"x\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"collectFinalTargetCardInPublicPile\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"drawTwo\"],\"effects\":[{\"op\":\"collectFinalTargetCardInPublicPile\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"effects\":[{\"op\":\"collectFinalTargetCardInPublicPile\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"x\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"effects\":[{\"op\":\"obtainPublicPileCard\",\"target\":\"owner\",\"skillIds\":[\"fixture:source\"]}]}]",
            "\"activations\":[{\"id\":\"x\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"discardPublicZoneAfterHandPayment\",\"target\":\"owner\"}]}]"
        };
        foreach(var body in invalid)
        {
            var failed=false;try{SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid","revision":1,{{body}}}]}""","""{"schemaVersion":3,"skills":{"fixture:invalid":{"name":"非法","description":"错误窗口与资源"}}}""");}catch(InvalidOperationException){failed=true;}
            Require(failed,"Public pile operations reject a fictitious window/actor/card family or field removal without real hand payment.");
        }
        var unknownSourceRejected = false;
        try { ContentRegistry.Build(new UnknownPileSource()); }
        catch (InvalidOperationException error) { unknownSourceRejected = error.Message.Contains("fixture:missing-pile", StringComparison.Ordinal); if (!unknownSourceRejected) throw new InvalidOperationException("Unexpected source fixture rejection: " + error.Message, error); }
        Require(unknownSourceRejected, "The new public-pile activation validates its explicit source reference when compiling the registry.");
        var(g,r)=Start();Collect(g,1);
        Atomic(g,new UseProgramSkillCommand(0,"classic:qingce","clear-field",[],[],g.Revision,P(g)!.PromptId));Replay(g,r);
        (g,r)=Start("slash");Driver(g,"supply");Settle(g);
        var draw=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.DrawTwo);
        Accept(g,new PlayCardCommand(0,draw.CardId!.Value,draw.TargetSeats,g.Revision,P(g)!.PromptId,draw.PlayedCardKind));Settle(g);
        Require(Pile(g).Count==0,"An actual nondamage trick cannot trigger target collection.");
        var slash=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&g.CreateSnapshot(0,true).Players[0].Hand.Single(c=>c.Id==a.CardId).Kind is CardKind.FireSlash or CardKind.ThunderSlash);
        var remove=g.CreateSnapshot(0,true).Players[0].Hand.Where(c=>c.Id!=slash.CardId).Select(c=>c.Id).ToArray();
        Driver(g,"trim");foreach(var card in remove)Choose(g,c=>c.Cards.SequenceEqual([card]));Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");Settle(g);
        slash=g.GetHumanLegalActions().First(a=>a.CardId==slash.CardId);
        Accept(g,new PlayCardCommand(0,slash.CardId!.Value,slash.TargetSeats,g.Revision,P(g)!.PromptId,slash.PlayedCardKind));Reach(g,p=>Action(p,"public-pile-flow"));
        Require(P(g)!.Choices.All(c=>c.Targets.SequenceEqual(slash.TargetSeats)),"Native elemental Slash selects only its true final target, excluding every other eligible player.");Replay(g,r);Choose(g,c=>true);Settle(g);
        Require(Pile(g).Count==1&&g.Events.Select(e=>e.Payload).OfType<FinalTargetCardStoredEvent>().Count()==1,"One real elemental Slash collects exactly once before actual response/damage.");Replay(g,r);Conserve(g);

    }
    private static (GameEngine,ContentRegistry) Start(string mode="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-honor-pile-check",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:gj-owner",g.Revision,P(g)!.PromptId));Settle(g);return(g,r);
    }
    private static IEnumerable<int> GetTargetHands(GameEngine g,PendingDecision p)=>p.Choices.SelectMany(c=>c.Targets).Distinct().Select(s=>g.State.Players[s].HandCount);
    private static IReadOnlyList<CardSnapshot> Pile(GameEngine g)
    {
        var player = g.CreateSnapshot(0, true).Players[0];
        return player.PublicPersistentPiles?.SingleOrDefault(p => p.SourceSkillId == "classic:zhengrong")?.Cards ??
            (player.PublicPersistentPileSkillId == "classic:zhengrong" ? player.PublicPersistentPileCards : null) ?? [];
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static bool Action(PendingDecision p,string name)=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==name);
    private static void Driver(GameEngine g,string id)=>Accept(g,new UseProgramSkillCommand(0,"fixture:gj-driver",id,[],[],g.Revision,P(g)!.PromptId));
    private static void PrepareArrows(GameEngine g,int n)
    {
        Driver(g,"supply");Settle(g);var kept=g.CreateSnapshot(0,true).Players[0].Hand.Where(c=>c.Kind==CardKind.ArrowBarrage).Take(n).Select(c=>c.Id).ToHashSet();Require(kept.Count==n,"Fixed deck supplies the representative arrow entities.");
        var remove=g.CreateSnapshot(0,true).Players[0].Hand.Where(c=>!kept.Contains(c.Id)).Select(c=>c.Id).ToArray();Driver(g,"trim");foreach(var id in remove)Choose(g,c=>c.Cards.SequenceEqual([id]));Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");Settle(g);
    }
    private static void PlayArrow(GameEngine g)
    {
        var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.ArrowBarrage);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind));
    }
    private static void Collect(GameEngine g,int n){PrepareArrows(g,n);for(var i=0;i<n;i++){PlayArrow(g);Reach(g,p=>Action(p,"public-pile-flow"));Choose(g,c=>c.Parameters["source-zone"]=="Hand");Settle(g);}Require(Pile(g).Count==n,"Collect real honors once.");}
    private static void Awaken(GameEngine g){Collect(g,3);End(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="owned"));Choose(g,c=>c.Parameters.GetValueOrDefault("token")=="finish");Settle(g);}
    private static void EquipField(GameEngine g){Driver(g,"supply");Settle(g);var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind));Settle(g);}
    private static void UseQingce(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"classic:qingce","clear-field",[],[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Choose(GameEngine g,Func<PromptChoice,bool> predicate)=>Answer(g,P(g)!.Choices.First(predicate));
    private static void Answer(GameEngine g,PromptChoice c){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
    private static void Tick(GameEngine g)
    {
        if(P(g) is {} p&&p.PlayerSeat==0)
        {
            if(p.Kind==DecisionKind.PlayCard){End(g);return;}
            if(p.Kind==DecisionKind.DiscardCards){Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));return;}
            if(p.Choices.Count>0){Answer(g,p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("token")=="finish")??p.Choices.First());return;}
            throw new InvalidOperationException("Unhandled fixed prompt "+JsonSerializer.Serialize(p));
        }
        Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var i=0;i<260;i++){if(P(g) is {}p&&predicate(p))return;Tick(g);}throw new InvalidOperationException("Missing honor boundary "+JsonSerializer.Serialize(P(g)));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0&&g.ResolutionStack.Count==0);
    private static void Public(GameEngine g){var ids=Pile(g).Select(c=>c.Id).ToArray();Require(Enumerable.Range(0,4).All(s=>g.CreateSnapshot(s,false).Players[0].PublicPersistentPileCards!.Select(c=>c.Id).SequenceEqual(ids)),"All viewers see the same public honors.");}
    private static void Replay(GameEngine g,ContentRegistry r){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s,false))==SnapshotJson.Serialize(copy.CreateSnapshot(s,false)))&&g.CardMovements.SequenceEqual(copy.CardMovements)&&g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).SequenceEqual(copy.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))),"All viewer checkpoint/accepted command JSON reconstruct physical moves and facts.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,"Fixture command rejected: "+result.Error?.Message);}
    private static void Atomic(GameEngine g,GameCommand c){var before=GameCheckpointJson.Serialize(g.CreateCheckpoint());Require(!g.Submit(c).Accepted&&before==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Wrong actor/unknown choice must reject atomically.");}
    private static void Conserve(GameEngine g)=>Require(g.CreateCardZoneDiagnostics().Count==64&&g.CreateCardZoneDiagnostics().Select(z=>z.CardId).Distinct().Count()==64,"Every real entity belongs to exactly one zone.");
    private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private sealed class UnknownPileSource : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-unknown-pile-source", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:unknown-pile-source","revision":1,"activations":[{"id":"obtain","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":[{"op":"obtainPublicPileCard","target":"owner","skillIds":["fixture:missing-pile"]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:unknown-pile-source":{"name":"源引用","description":"编译时拒绝未知公开牌堆源"}}}""");
            builder.AddSkill(new("fixture:unknown-pile-source", "源引用", "未知源") { Program = catalog.Programs["fixture:unknown-pile-source"] });
        }
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-honor-pile",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var ending=mode=="strip-gain"?",{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}":mode=="death-gain"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}":"";
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"fixture:gj-driver","revision":1,"activations":[
              {"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":10}]},
              {"id":"trim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":0,"maximumCards":null,"zones":["hand"],"resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},
              {"id":"grant-consumer","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:gj-instance-consumer"]}]},
              {"id":"grant-second","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:zhengrong"]}]},
              {"id":"grant-book","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:bizhuan"]}]},
              {"id":"loss-book","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:bizhuan"],"sourceBind":"standard:none"}]},
              {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:zhengrong"],"sourceBind":"standard:none"}]}
             ]},
             {"id":"fixture:gj-consumer-grant","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:gj-instance-consumer"]}]}]},
             {"id":"fixture:gj-instance-consumer","revision":1,"triggers":[{"id":"exchange","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"publicPersistentPile"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":3} },"effects":[{"op":"exchangePublicPileHand","target":"owner","skillIds":["classic:zhengrong"]}]}]},
             {"id":"fixture:gj-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.public-pile.obtain","skill-program.public-pile.exchange-obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{ending}}]}]},
             {"id":"fixture:gj-discard","revision":1,"triggers":[{"id":"discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.classic:qingce.SelectAndMoveOwnedCard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var c=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:gj-consumer-grant":{"name":"独立消费来源","description":"获得另一来源的公开牌堆消费能力"},"fixture:gj-instance-consumer":{"name":"真实多源消费","description":"跨来源显式选择荣堆"},"fixture:gj-driver":{"name":"荣机制驱动","description":"真实补牌清理与失源"},"fixture:gj-gain":{"name":"真实获牌观察","description":"暂停获牌子链","optionLabels":{"continue":"继续"}},"fixture:gj-discard":{"name":"真实弃置观察","description":"付款后暂停","optionLabels":{"continue":"继续"}}}}""");
            foreach(var item in c.Programs)b.AddSkill(new(item.Key,item.Key,item.Key){Program=item.Value});
            b.AddGeneral(new("fixture:gj-owner","荣机制将","supporter",mode=="ai"?"standard:none":"classic:zhengrong","wei",20,["fixture:gj-driver",..(mode=="instances"?new[]{"fixture:gj-consumer-grant"}:Array.Empty<string>()),..(mode=="multi"?new[]{"classic:bizhuan","classic:tongbo"}:Array.Empty<string>()),..(mode=="ai"?Array.Empty<string>():new[]{"classic:hongju","fixture:gj-gain","fixture:gj-discard"})]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:gj-{i}","目标"+i,"supporter",mode=="ai"?"classic:zhengrong":"standard:none","wei",20,mode=="ai"?["classic:hongju"]:[]));
            b.AddDeck(new("fixture:gj-deck","荣机制真实牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(mode=="slash"?i%4==0?"standard:slash":i%4==1?"standard:fire_slash":i%4==2?"standard:thunder_slash":"standard:draw_two":mode=="field"&&i%8==0?"standard:lightning":i%4==0?"standard:qinggang_sword":"standard:arrow_barrage",mode=="multi"?Suit.Spade:(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-honor-pile-check","荣机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:gj-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:gj-owner","fixture:gj-1","fixture:gj-2","fixture:gj-3"]));
        }
    }
}
