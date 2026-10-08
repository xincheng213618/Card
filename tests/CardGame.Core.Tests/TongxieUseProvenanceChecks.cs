using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class TongxieUseProvenanceChecks
{
    private const string Mode = "team:tongxie-use-provenance";
    private const string Owner = "fixture:tongxie-provenance-owner";
    private const string Driver = "fixture:tongxie-provenance-driver";
    private const string Return = "fixture:tongxie-provenance-return";
    private const string Completion = "fixture:tongxie-provenance-completion";
    private const string Tongxie = "ol:tongxie";

    public static void LiveFollowUpDoesNotReenterAndReturnedEntityHasIndependentUseOrResponse()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var prepared = Prepare(registry);
        var checkpoint = GameCheckpointJson.Serialize(prepared.Game.CreateCheckpoint());
        var preparedState = State(prepared.Game);
        var useGame = prepared.Game;
        ExerciseNewUse(ref useGame, registry, prepared.ReturnedCard, prepared.ChildUse, prepared.AllySeat, prepared.MemberEnemy);
        var responseGame = GameReplay.Restore(GameCheckpointJson.Deserialize(checkpoint), registry);
        Require(State(responseGame) == preparedState,
            "The Use and Response branches start independently from the same real paid-and-returned entity checkpoint.");
        ExerciseResponse(ref responseGame, registry, prepared.ReturnedCard, prepared.ChildUse, prepared.TargetEnemy, prepared.InitialTargetHp);
    }

    private sealed record PreparedScenario(GameEngine Game, int ReturnedCard, TongxieSettledSlashUseEvent ChildUse,
        int AllySeat, int MemberEnemy, int TargetEnemy, int InitialTargetHp);

    private static PreparedScenario Prepare(ContentRegistry registry)
    {
        var g = Start(registry);
        var players = g.CreateSnapshot(0).Players;
        var allySeat = players.Single(p => p.Seat != 0 && p.TeamId == players[0].TeamId).Seat;
        var enemies = players.Where(p => p.TeamId != players[0].TeamId).Select(p => p.Seat).Order().ToArray();
        var memberEnemy = enemies[^1];
        var targetEnemy = enemies.Single(seat => seat != memberEnemy);
        var initialTargetHp = players[targetEnemy].Hp;
        Require(allySeat < memberEnemy, () => "The real public team assignment leaves an opposing member after the allied responder: " + Diagnostic(g));
        var armPrompt = P(g)!;
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => HasAction(p, "tongxie-arm"));
        var armChoice = P(g)!.Choices.Single(c => c.Targets.SequenceEqual(new[] { allySeat, memberEnemy }.Order()));
        Reject(g, new AnswerPromptCommand(allySeat, P(g)!.PromptId, armChoice.Id, g.Revision));
        Answer(g, c => c.Id == armChoice.Id);
        Reach(g, IsPlay);
        var arm = Facts<ProgramTongxieArmedEvent>(g).Single();
        var expectedArm = arm.OwnerSeat == 0 && arm.MemberSeats.SequenceEqual(new[] { 0, allySeat, memberEnemy }.Order()) &&
                g.CreateSnapshot(0).Players[0].TeamId == g.CreateSnapshot(0).Players[allySeat].TeamId &&
                g.CreateSnapshot(0).Players[0].TeamId != g.CreateSnapshot(0).Players[targetEnemy].TeamId;
        if (!expectedArm)
            Require(false, "Fixed Seed17 genuinely arms owner, native AI ally and a later opposing member, leaving the other enemy as the real first target: " +
                JsonSerializer.Serialize(new
                {
                    Armed = arm,
                    Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.TeamId, p.Hp, p.MaxHp, p.HandCount }).ToArray()
                }));
        Frozen(arm.MemberSeats);
        Reject(g, new AnswerPromptCommand(0, armPrompt.PromptId, armChoice.Id, armPrompt.Revision));

        var draw = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == Driver && a.ProgramActivationId == "supply");
        Accept(g, new UseProgramSkillCommand(0, Driver, "supply", [], draw.TargetSeats, g.Revision, P(g)!.PromptId));
        Reach(g, IsPlay);
        var beforeRoot = g.Events.Last().Sequence;
        var root = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([targetEnemy]));
        Play(g, root);
        Reach(g, p => HasAction(p, "tongxie-follow-up") && p!.PlayerSeat == allySeat);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TongxieFollowUp is not null);
        var parentId = parent.Id;
        var rootUse = Facts<TongxieSettledSlashUseEvent>(g).Single(e => e.CardId == root.CardId && e.ActorSeat == 0 && e.TargetSeat == targetEnemy);
        Require(parent.OwnerSeat == 0 && parent.SkillId == Tongxie && parent.InstructionIndex == 1 &&
                parent.TongxieFollowUp is { ActorSeat: 0 } pending && pending.TargetSeat == targetEnemy && pending.CardId == root.CardId &&
                pending.PendingResponders.SequenceEqual([allySeat, memberEnemy]) && pending.UsedCards.Count == 0 &&
                rootUse.ParentProgramFrameId is null && parent.WindowContext?.MovementBatch?.Id == rootUse.BatchId,
            "One settled root use pins its original action and movement, with two ordered responders on the exact suspended parent.");
        Private(g, allySeat); Frozen(P(g)!.Choices);
        var followChoice = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "tongxie-follow-up");
        var returnedCard = followChoice.Cards.Single();
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, followChoice.Id, g.Revision));
        g = Cold(g, registry);
        Step(g); // The actual AI chooses its own published private Slash.
        Reach(g, p => p?.SkillPrompt?.SkillId == Completion && p.PlayerSeat == allySeat);

        var childUse = Facts<TongxieSettledSlashUseEvent>(g).Single(e => e.ParentProgramFrameId == parentId);
        var liveParent = Chain(g, parentId);
        var childFrame = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == childUse.CardUseFrameId);
        Require(childUse.CardId == returnedCard && childUse.ActorSeat == allySeat && childUse.TargetSeat == targetEnemy &&
                childUse.ActionId != rootUse.ActionId && childUse.EffectiveKind == CardKind.Slash &&
                childFrame.Action?.ActionId == childUse.ActionId && childFrame.CardAttack?.ProgramSkillCardUseFrameId == parentId &&
                liveParent.ReexecuteParticipantInstruction && liveParent.TongxieFollowUp!.Used.SequenceEqual([allySeat]) &&
                liveParent.TongxieFollowUp.UsedCards.SequenceEqual([returnedCard]) &&
                liveParent.TongxieFollowUp.PendingResponders.SequenceEqual([memberEnemy]) &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Count(f => f.TongxieFollowUp is not null) == 1 &&
                !Facts<ProgramTongxieFollowUpResolvedEvent>(g).Any() &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == returnedCard).Location == CardLocation.DiscardPile &&
                g.CardMovements.Count(m => m.CardId == returnedCard && m.Sequence == childUse.MovementSequence &&
                    m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
            () => "Before the parent can resolve, the paid child Slash is settled in Discard with exact use/action/parent provenance and never reopens a chain: " + Diagnostic(g));
        var completedChild = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Completion);
        Require(completedChild.WindowContext?.CardUse is { } completed && completed.ParentCardUseFrameId == childUse.CardUseFrameId &&
                completed.CardActionId == childUse.ActionId && completed.ActorSeat == allySeat &&
                !Facts<ProgramXunxianGiftEvent>(g).Any(e => e.CardId == returnedCard && e.DiscardMovementSequence == childUse.MovementSequence) &&
                Chain(g, parentId).TongxieFollowUp!.PendingResponders.SequenceEqual([memberEnemy]) &&
                !Facts<ProgramTongxieFollowUpResolvedEvent>(g).Any(),
            () => "The native completion child precedes global movement observers and preserves the same unpaid remaining responder on its exact live parent: " + Diagnostic(g));
        var completionId = completedChild.Id;
        var completionWindowId = completedChild.WindowContext!.ParentFrameId;
        Private(g, allySeat); g = Cold(g, registry); Step(g);
        Reach(g, p => HasAction(p, "xunxian-gift") && p!.PlayerSeat == allySeat);
        Require(Facts<ProgramTongxieFollowUpResolvedEvent>(g).Count(e => e.FrameId == parentId) == 1 &&
                Facts<ProgramCardTriggerResolvedEvent>(g).Count(e => e.FrameId == completionWindowId && e.SkillId == Completion && e.Activated) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == completionId || f.Id == parentId || f.Id == childUse.CardUseFrameId) &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.TongxieFollowUp is not null) &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == returnedCard).Location == CardLocation.DiscardPile,
            () => "Only after the exact completion child and remaining native responder return does the real Xunxian movement observer offer the settled entity: " + Diagnostic(g));
        Require(P(g) is { IsPrivate: false, PlayerSeat: var giftActor } giftPrompt && giftActor == allySeat &&
                giftPrompt.Choices.All(c => c.Cards.Count == 0) &&
                Enumerable.Range(0, 4).All(viewer => (viewer == allySeat
                    ? g.CreateSnapshot(viewer).PendingDecision?.PromptId == giftPrompt.PromptId
                    : g.CreateSnapshot(viewer).PendingDecision is null) &&
                    g.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)),
            () => "The actual Xunxian recipient choice is public and reveals no foreign Hand faces: " + Diagnostic(g));
        Frozen(P(g)!.Choices);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices[0].Id, g.Revision));
        g = Cold(g, registry);
        Step(g); // Native AI gives the public settled entity to the eligible first recipient.
        Reach(g, IsPlay);
        var first = Facts<ProgramTongxieFollowUpResolvedEvent>(g).Single();
        Require(first.FrameId == parentId && first.OwnerSeat == 0 && first.ActorSeat == 0 && first.TargetSeat == targetEnemy &&
                first.UsedBy.SequenceEqual([allySeat]) && first.DeclinedBy.SequenceEqual([memberEnemy]) && first.UsedCardIds.SequenceEqual([returnedCard]) &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.SkillId == Tongxie && e.Completed) == 1 &&
                Facts<ProgramCardTriggerResolvedEvent>(g).Count(e => e.FrameId == completionWindowId && e.SkillId == Completion && e.Activated) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == completionId) &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.TongxieFollowUp is not null) &&
                Facts<ProgramXunxianGiftEvent>(g).Count(e => e.SkillId == Return && e.OwnerSeat == allySeat && e.RecipientSeat == 0 &&
                    e.CardId == returnedCard && e.DiscardMovementSequence == childUse.MovementSequence) == 1 &&
                g.CardMovements.Count(m => m.CardId == returnedCard && m.From == CardLocation.DiscardPile &&
                    m.To == CardLocation.Hand(0) && m.Reason.Value == $"skill-program.{Return}.xunxian-gift") == 1 &&
                g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == returnedCard) &&
                g.Events.Where(e => e.Sequence > beforeRoot).Select(e => e.Payload).OfType<DamageAppliedEvent>()
                    .Count(e => e.TargetSeat == targetEnemy && e.Amount == 1 && (e.SourceSeat == 0 || e.SourceSeat == allySeat)) == 2 &&
                g.CreateSnapshot(0).Players[targetEnemy].Hp == initialTargetHp - 2,
            "One real AI payment and one later AI decline finish the original chain once; the exact used entity is genuinely back in the human Hand.");
        Frozen(first.UsedBy); Frozen(first.DeclinedBy); Frozen(first.UsedCardIds);
        AssertUsePaidOnce(g, rootUse, 0); AssertUsePaidOnce(g, childUse, allySeat);
        g = Cold(g, registry);
        return new(g, returnedCard, childUse, allySeat, memberEnemy, targetEnemy, initialTargetHp);
    }

    private static void ExerciseNewUse(ref GameEngine g, ContentRegistry registry, int returnedCard, TongxieSettledSlashUseEvent previous, int allySeat, int targetSeat)
    {
        var action = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.CardId == returnedCard &&
            a.ConversionSource is null && a.TargetSeats.SequenceEqual([targetSeat]));
        Play(g, action);
        Reach(g, p => HasAction(p, "tongxie-follow-up") && p!.PlayerSeat == allySeat);
        var second = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TongxieFollowUp is not null);
        var secondId = second.Id;
        var newUse = Facts<TongxieSettledSlashUseEvent>(g).Single(e => e.CardId == returnedCard && e.ParentProgramFrameId is null);
        var diagnosticEngine = g;
        Require(secondId != previous.ParentProgramFrameId && second.TongxieFollowUp!.ActorSeat == 0 &&
                second.TongxieFollowUp.TargetSeat == targetSeat && second.TongxieFollowUp.CardId == returnedCard &&
                second.TongxieFollowUp.PendingResponders.SequenceEqual([allySeat]) && newUse.ActionId != previous.ActionId &&
                newUse.CardUseFrameId != previous.CardUseFrameId && newUse.ActorSeat == 0 && newUse.TargetSeat == targetSeat &&
                second.WindowContext?.MovementBatch?.Id == newUse.BatchId &&
                Facts<ProgramTongxieFollowUpResolvedEvent>(g).Count() == 1,
            () => "A genuinely returned entity's new ordinary Use has independent action provenance and opens its own chain instead of inheriting a lifetime CardId ban: " + Diagnostic(diagnosticEngine));
        Private(g, allySeat); g = Cold(g, registry); Step(g);
        Reach(g, p => p?.SkillPrompt?.SkillId == Completion && p.PlayerSeat == allySeat);
        var newChild = Facts<TongxieSettledSlashUseEvent>(g).Single(e => e.ParentProgramFrameId == secondId);
        Require(newChild.CardId != returnedCard && newChild.ActorSeat == allySeat && newChild.TargetSeat == targetSeat &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Count(f => f.TongxieFollowUp is not null) == 1 &&
                Facts<ProgramTongxieFollowUpResolvedEvent>(g).Count() == 1,
            "The second native AI child also settles once without recursively opening another chain.");
        g = Cold(g, registry); Step(g); Reach(g, IsPlay);
        var settled = Facts<ProgramTongxieFollowUpResolvedEvent>(g).Single(e => e.FrameId == secondId);
        Require(settled.UsedBy.SequenceEqual([allySeat]) && settled.DeclinedBy.Count == 0 && settled.UsedCardIds.SequenceEqual([newChild.CardId]) &&
                Facts<ProgramTongxieFollowUpResolvedEvent>(g).Count() == 2 &&
                Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == secondId && e.SkillId == Tongxie && e.Completed) == 1 &&
                Facts<TongxieSettledSlashUseEvent>(g).Count(e => e.CardId == returnedCard) == 2 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.TongxieFollowUp is not null),
            "Both independent chains finish once and only the two actual Uses of the returned entity enter the settled-use ledger.");
        AssertUsePaidOnce(g, newUse, 0); AssertUsePaidOnce(g, newChild, allySeat); _ = Cold(g, registry);
    }

    private static void ExerciseResponse(ref GameEngine g, ContentRegistry registry, int returnedCard, TongxieSettledSlashUseEvent previous, int targetSeat, int initialTargetHp)
    {
        Require(g.CreateSnapshot(0).Players[targetSeat].Hp == initialTargetHp - 2 && g.CreateSnapshot(0).Players[targetSeat].Hp > 0 &&
                g.CreateSnapshot(0).Players[targetSeat].MaxHp - g.CreateSnapshot(0).Players[targetSeat].Hp >= 2,
            "The Duel responder's real two wounds came from the ordinary Slash and the native follow-up, with no injected HP or AI values.");
        var duel = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.CardId != returnedCard &&
            a.ConversionSource?.BindingId == "slash-as-duel" && a.TargetSeats.SequenceEqual([targetSeat]));
        Play(g, duel);
        Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } && p.IncomingCard == CardKind.Duel);
        var prompt = P(g)!;
        var choice = prompt.Choices.Single(c => c.Cards.SequenceEqual([returnedCard]));
        Require(Facts<CardRespondedEvent>(g).Any(e => e.ResponderSeat == targetSeat && e.SourceSeat == 0 && e.EffectiveCardKind == CardKind.Slash),
            "The legitimately wounded native AI genuinely responds to the real Duel before the human pays the returned card.");
        Reject(g, new AnswerPromptCommand(1, prompt.PromptId, choice.Id, g.Revision));
        g = Cold(g, registry); Answer(g, c => c.Id == choice.Id);
        var responseAction = Facts<CardActionAcceptedEvent>(g).Select(e => e.Action).Single(a => a.Type == CardActionType.Response &&
            a.ActorSeat == 0 && a.EffectiveKind == CardKind.Slash && a.PhysicalCards.Any(c => c.CardId == returnedCard));
        var diagnosticEngine = g;
        Require(responseAction.ActionId != previous.ActionId && responseAction.ParentActionId is not null &&
                Facts<CardRespondedEvent>(g).Count(e => e.CardId == returnedCard && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Slash) == 1 &&
                g.CardMovements.Count(m => m.CardId == returnedCard && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == returnedCard && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1 &&
                Facts<TongxieSettledSlashUseEvent>(g).Count(e => e.CardId == returnedCard) == 1 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.TongxieFollowUp is not null),
            () => "This entity's real Response has a fresh response action but cannot borrow its older child Use to open a Tongxie chain: " + Diagnostic(diagnosticEngine));
        g = Cold(g, registry); Reach(g, IsPlay);
        Require(Facts<ProgramTongxieFollowUpResolvedEvent>(g).Count() == 1 &&
                Facts<TongxieSettledSlashUseEvent>(g).Count(e => e.CardId == returnedCard) == 1 &&
                Facts<CardRespondedEvent>(g).Count(e => e.CardId == returnedCard) == 1 &&
                g.CardMovements.Count(m => m.CardId == returnedCard && m.Reason == CardMoveReasons.ResponseFinished) == 1,
            "Cold restoration after the paid Response and the real Duel's remaining responses cannot replay payment or create a new follow-up chain.");
        _ = Cold(g, registry);
    }

    private static ProgramSkillFrame Chain(GameEngine g, long id)
    {
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id && f.TongxieFollowUp is not null);
        Require(frame is not null, () => $"The exact live Tongxie parent {id} is absent: " + Diagnostic(g));
        return frame!;
    }
    private static void AssertUsePaidOnce(GameEngine g, TongxieSettledSlashUseEvent use, int provider)
    {
        var accepted = Facts<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == use.ActionId).Action;
        Require(accepted.Type == CardActionType.Use && accepted.ActorSeat == use.ActorSeat && accepted.ProviderSeat == provider &&
                accepted.PhysicalCards is [var cost] && cost.CardId == use.CardId && cost.From == CardLocation.Hand(provider) &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.CardUseFrameId && e.CardId == use.CardId && e.CardKind == CardKind.Slash) == 1 &&
                g.CardMovements.Count(m => m.CardId == use.CardId && m.Sequence == use.MovementSequence &&
                    m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
            "Each exact use/action retains its real provider Hand material, one completion and one settled payment.");
    }

    private static GameEngine Start(ContentRegistry registry)
    {
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:tongxie-red", ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6
        }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Reach(g, p => p?.SkillPrompt?.SkillId == Tongxie && HasAction(p, "activate"));
        return g;
    }

    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool HasAction(PendingDecision? p, string action) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Play(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats,
        g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "The fixture never answers for an AI actor.");
        var choice = p.Choices.FirstOrDefault(select); Require(choice is not null, () => "Required published choice is absent: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
        { Answer(g, c => c.Cards.Count == 0); return; }
        if (HasAction(p, "tongxie-guard-decline"))
        { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "tongxie-guard-decline"); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"))
        { Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); return; }
        throw new InvalidOperationException("Unexpected human boundary: " + Diagnostic(g));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 288; step++) { if (stop(P(g))) return; Step(g); }
        throw new InvalidOperationException("The bounded native regression did not reach its intended boundary: " + Diagnostic(g));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real native command was rejected.");
    }
    private static void Reject(GameEngine g, GameCommand command)
    {
        var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!result.Accepted && result.Error is not null && before == State(g), "Wrong actors and stale choices reject without changing the command journal, frames, facts or any player view.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(restored) == State(g), "Cold accepted-command replay preserves exact live chains, action provenance, private snapshots and already paid entities.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Prompt = P(g), g.State.CurrentSeat, g.State.Phase,
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }).ToArray(),
        Chains = g.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.TongxieFollowUp is not null).ToArray(),
        ChainCompletions = Facts<ProgramTongxieFollowUpResolvedEvent>(g),
        LastFacts = g.Events.TakeLast(12).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray()
    });
    private static void Private(GameEngine g, int actor) => Require(P(g) is { IsPrivate: true } p && p.PlayerSeat == actor &&
        Enumerable.Range(0, 4).Where(s => s != actor).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[actor].Hand.Count == 0),
        "Only the actual native chooser sees its private Hand cards and prompt.");
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        Require(list.Count > 0 && list is IList<T> { IsReadOnly: true }, "A genuine exposed nonempty collection is readonly.");
        var blocked = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { blocked = true; }
        Require(blocked, "Committed nested collections cannot be changed by an observer.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Require(bool value, Func<string> message) { if (!value) throw new InvalidOperationException(message()); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("tongxie-use-provenance-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"modifiers":[
                    {"id":"range","query":"attackRange","operation":"add","value":2,"priority":0},
                    {"id":"second-real-use","query":"slashLimit","operation":"add","value":1,"priority":0}],
                    "viewAs":[{"id":"slash-as-duel","inputKinds":["slash"],"inputSuits":[],"outputKind":"duel","singleCardTrickUse":true,"forPlay":true,"forResponse":false}],
                    "activations":[{"id":"supply","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
                      "effects":[{"op":"draw","target":"owner","amount":3}]}]},
                  {"id":"{{{Return}}}","revision":1,"triggers":[{"id":"return-used-entity","window":"discardPileReceived","subject":"owner",
                    "movementOccurrence":"perCard","priority":-100,"usageScope":"turn","usageLimit":1,"optional":false,
                    "effects":[{"op":"xunxianGiftUsedCard","target":"owner"}]}]},
                  {"id":"{{{Completion}}}","revision":1,"triggers":[{"id":"real-slash-completion","window":"cardUseCompleted","ownerRelation":"actor",
                    "cardKinds":["slash"],"singleActionInstance":true,"optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实使用驱动", description = "公开范围与次数、真实摸牌、单实体决斗转换" },
                    [Return] = new { name = "真实已用实体归还", description = "使用逊贤的真实弃牌来源与合法接收者" },
                    [Completion] = new { name = "原生子杀完成窗口", description = "保留未返回的同协父程序", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "精确使用来源回归夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:tongxie-provenance-peer", "普通其他角色", "使用既有原生AI"));
            b.AddGeneral(new(Owner, "同协真实来源拥有者", "supporter", Tongxie, "wu", 4, [Driver]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:tongxie-provenance-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", "fixture:tongxie-provenance-peer", "wei", 4, [Return, Completion]));
            const string deck = "fixture:tongxie-provenance-deck";
            b.AddDeck(new(deck, "固定同类实体牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实公开2v2使用来源", 4, 4, new Dictionary<string, int>(), deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:tongxie-blue"] = 2, ["team:tongxie-red"] = 2 }));
        }
    }
}
