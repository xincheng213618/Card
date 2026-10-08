using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChainedStateBasicConversionChecks
{
    private const string Skill = "ol:xianwan", Driver = "fixture:chain-basic-driver", Child = "fixture:chain-basic-child";
    private const string Completed = "fixture:chain-basic-completed", Foreign = "fixture:chain-basic-foreign", Quiet = "fixture:chain-basic-quiet";
    private const string Mode = "identity:classic-chained-state-basic";
    private static bool IsState(PromptChoice c) => c.Parameters.ContainsKey("chained-state-basic");

    public static void OwnPlayStateCostUsesNativeQuotaAndReturnsOnce()
    {
        LoaderBoundary();
        var (g, r) = Start(); Play(g);
        Require(g.GetHumanLegalActions().All(a => !a.ChainedStateBasicUse.HasValue), "An unchained owner cannot pay release for an active Slash.");
        Use(g, "chain", [0]); Play(g);
        var retained = g.CreateSnapshot(0); var hand = V(g, 0).Hand.Select(c => c.Id).ToArray(); var hp = V(g, 0).Hp;
        var action = g.GetHumanLegalActions().First(a => a.ChainedStateBasicUse == false && a.TargetSeats.SequenceEqual([1]));
        Require(action is { CardId: 0, PlayedCardKind: CardKind.Slash } && action.ConversionSource?.SkillId == Skill && V(g, 0).IsChained,
            "The actual chain state publishes an ordinary neutral zero-material Slash with its exact live view-as source.");
        var before = State(g);
        Require(!g.Submit(new PlayCardCommand(1, 0, [1], g.Revision, P(g)!.PromptId, CardKind.Slash) { ConversionSource = action.ConversionSource }).Accepted && State(g) == before,
            "A wrong actor cannot pay another actor's published chain-state use.");
        g = Cold(g, r); SubmitPlay(g, action);
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.ChainedStateBasicUse is not null);
        var receipt = use.ChainedStateBasicUse!; var payment = receipt.Payment;
        Require(payment is { Intent: ChainedStateBasicIntent.Play, WasChained: true, DesiredChained: false, CharacterStateChangeId: null } &&
            payment.Source == action.ConversionSource && payment.ParentFrameId is null && payment.RequestFrameId is null &&
            !V(g, 0).IsChained && V(g, 0).Hp == hp && V(g, 0).Hand.Select(c => c.Id).SequenceEqual(hand) && retained.Players[0].IsChained &&
            use is { CardId: 0, PhysicalCardIds.Count: 0, Action: { Type: CardActionType.Use, EffectiveKind: CardKind.Slash,
                EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false, PhysicalCards.Count: 0 } } &&
            use.Action.ConversionChain.SequenceEqual([payment.Source]) && use.TargetSeats.SequenceEqual([1]) &&
            F<ChainedStateBasicReturnedEvent>(g).Length == 0,
            "Release pays a real true-to-false change once, preserves real hand entities, and owns a neutral native Slash through its completion child.");
        Private(g); Reject(g); g = Cold(g, r); Continue(g); Play(g); Once(g, receipt);
        Require(F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            g.CardMovements.All(m => m.CardId != 0), "One real logical Slash finishes once without ever moving a physical entity zero.");
        Use(g, "chain", [0]); Play(g);
        Require(V(g, 0).IsChained && g.GetHumanLegalActions().All(a => !a.ChainedStateBasicUse.HasValue),
            "Paying a state cost does not bypass the ordinary own-Play Slash quota.");
        before = State(g);
        var paidBeforeRejection = F<ChainedStateBasicPaidEvent>(g).Length;
        var rejected = g.Submit(new PlayCardCommand(0, 0, [1], g.Revision, P(g)!.PromptId, CardKind.Slash) { ConversionSource = action.ConversionSource });
        var unchanged = State(g) == before; var paidCount = F<ChainedStateBasicPaidEvent>(g).Length;
        var rejection = JsonSerializer.Serialize(new { rejected.Accepted, rejected.Error, StateUnchanged = unchanged, PaidBefore = paidBeforeRejection, PaidCount = paidCount,
            V(g, 0).IsChained, PromptKind = P(g)?.Kind, FrameIds = g.ResolutionStack.Select(f => f.Id).ToArray() });
        Require(!rejected.Accepted, "An exhausted native quota must reject the unpublished use. " + rejection);
        Require(unchanged, "The exhausted-quota rejection must preserve all views, frames, facts, movements, accepted commands and physical zones. " + rejection);
        Require(paidCount == paidBeforeRejection && F<ChainedStateBasicPaidEvent>(g).Count(e => e.Payment.Source.OwnerSeat == 0) == 1,
            "The exhausted-quota rejection must not add any chain payment; the owner still pays exactly once independently of the target's genuine Dodge fee. " + rejection);
        _ = Cold(g, r);
        UnpayableStateCosts();
    }

    public static void OwnSlashDodgeWaitsForStateChildrenAndCompletesOnce()
    {
        var (g, r) = Start(stateChild: true, incoming: true); Play(g); End(g);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } && p.Choices.Any(IsState));
        var attack = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardAttack is not null);
        var response = g.ResolutionStack.OfType<ResponseWindowFrame>().Last(); var hand = V(g, 0).Hand.Select(c => c.Id).ToArray(); var hp = V(g, 0).Hp;
        var retained = g.CreateSnapshot(0); var offered = P(g)!; var choice = offered.Choices.Single(IsState);
        Require(!V(g, 0).IsChained && choice.Cards.Count == 0 && choice.Targets.Count == 0 &&
            choice.Parameters.GetValueOrDefault("output-kind") == nameof(CardKind.Dodge), "Actual Slash defense publishes a zero-material Dodge Use, with no invented target or hand payment.");
        Private(g); Reject(g); g = Cold(g, r); Answer(g, IsState);
        Reach(g, p => p.SkillPrompt?.SkillId == Child);
        var paid = g.ResolutionStack.OfType<ChainedStateBasicFrame>().Single();
        var window = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.CharacterStateContinuation == CharacterStateContinuation.ChainedStateBasic);
        Require(paid.Payment is { Intent: ChainedStateBasicIntent.OwnSlashDodge, WasChained: false, DesiredChained: true, Cursor: 0 } &&
            paid.Payment.ParentFrameId == attack.Id && paid.Payment.RequestFrameId == response.Id && paid.Payment.ParentActionId == attack.Action!.ActionId &&
            paid.Payment.CharacterStateChangeId == window.Id && paid.ActiveChildFrameId == window.Id && window.ResumeProgramFrameId == paid.Id &&
            window.Window == SkillProgramTriggerWindow.CharacterEnteredChain && V(g, 0).IsChained && !retained.Players[0].IsChained &&
            F<ChainedStateBasicIssuedEvent>(g).Length == 0 && F<CharacterStateChangedEvent>(g).Count(e => e.Change.ParentFrameId == paid.Id) == 1,
            "Entering chain pays before a true state-change child, freezing the original Slash action, response window and successful-Dodge cursor.");
        DrawFundedBasicFixture.Frozen(paid.OriginalDecision); Private(g); Reject(g); g = Cold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        var owner = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == attack.Id); var receipt = owner.ChainedStateBasicResponse!;
        var accepted = F<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == receipt.CardActionId).Action;
        Require(receipt.Payment == paid.Payment && receipt.OwnerFrameId == attack.Id &&
            accepted is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } &&
            accepted.ActorSeat == 0 && accepted.ProviderSeat == 0 && accepted.ResponderSeat == 0 && accepted.OpponentSeat == attack.SourceSeat &&
            accepted.ParentActionId == attack.Action!.ActionId && accepted.ConversionChain.SequenceEqual([paid.Payment.Source]) &&
            owner.CardAttack!.SuccessfulDodgeResponses == 0 && V(g, 0).HandCount == hand.Length + 1 && V(g, 0).Hp == hp && !V(g, 0).IsChained &&
            F<ChainedStateBasicReturnedEvent>(g).Length == 0,
            "The native child really draws and releases chain again; the already-paid Dodge still issues once and waits for its real response-use completion.");
        Private(g); g = Cold(g, r); Continue(g);
        Until(g, e => F<ChainedStateBasicReturnedEvent>(e).Any(f => f.Receipt.Payment.PaymentFrameId == paid.Id));
        Once(g, receipt);
        Require(V(g, 0).Hp == hp && hand.All(id => g.CreateCardZoneDiagnostics().Any(z => z.CardId == id && z.Location == CardLocation.Hand(0))) &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.ChainedStateBasicResponse?.Payment.PaymentFrameId == paid.Id) && g.CardMovements.All(m => m.CardId != 0),
            "The successful Dodge returns once without re-entering chain, spending a hand entity or retaining its completed response receipt.");
        beforeStale(g, offered, choice); _ = Cold(g, r);
        PaidSourceLossDodge();
    }

    private static void UnpayableStateCosts()
    {
        var (forced, fr) = Start(chainPolicy: "forceChained"); Play(forced); Use(forced, "chain", [0]); Play(forced);
        Require(V(forced, 0).IsChained && forced.GetHumanLegalActions().All(a => !a.ChainedStateBasicUse.HasValue) && F<ChainedStateBasicPaidEvent>(forced).Length == 0,
            "A genuinely forced chain cannot publish a release fee whose resolved state would still be chained.");
        _ = Cold(forced, fr);
        var (prevented, pr) = Start(incoming: true, chainPolicy: "preventEnteringChain"); Play(prevented); End(prevented);
        Reach(prevented, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        Require(!V(prevented, 0).IsChained && P(prevented)!.Choices.All(c => !IsState(c)) && F<ChainedStateBasicPaidEvent>(prevented).Length == 0,
            "A real Slash defense cannot publish an entry fee which the owner's native policy prevents from changing state.");
        prevented = Cold(prevented, pr); Answer(prevented, c => c.Cards.Count == 1);
        Require(F<ChainedStateBasicStartedEvent>(prevented).Length == 0, "Using a real physical Dodge after the negative does not manufacture a chain payment.");
    }

    private static void PaidSourceLossDodge()
    {
        var (g, r) = Start(stateChild: true, incoming: true, sourceLoss: true); Play(g); End(g);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } && p.Choices.Any(IsState));
        var hp = V(g, 0).Hp; var hand = V(g, 0).HandCount;
        var sourceState = V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!
            .Single(s => s.StateId == "qualification-probe");
        Require(sourceState.Value, "The original Xianwan instance is genuinely qualified before the state fee.");
        Answer(g, IsState); Reach(g, p => p.SkillPrompt?.SkillId == Child);
        var payment = g.ResolutionStack.OfType<ChainedStateBasicFrame>().Single().Payment;
        Require(payment.Source.SkillInstanceId == sourceState.SkillInstanceId, "The exact currently qualified source pays the original need.");
        Private(g); g = Cold(g, r); Continue(g);
        Until(g, e => F<ChainedStateBasicReturnedEvent>(e).Any(f => f.Receipt.Payment.PaymentFrameId == payment.PaymentFrameId));
        var receipt = F<ChainedStateBasicIssuedEvent>(g).Single(f => f.Receipt.Payment.PaymentFrameId == payment.PaymentFrameId).Receipt;
        Require(V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!.All(s => s.SkillInstanceId != sourceState.SkillInstanceId) &&
            F<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Child && e.SkillIds.Contains("fixture:chain-basic-suppress")) &&
            V(g, 0).Hp == hp && V(g, 0).HandCount == hand + 1 && !V(g, 0).IsChained &&
            F<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == receipt.CardActionId).Action is { EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 0 },
            "A real state child suppresses the original Xianwan instance and changes state again, while the already-paid native Dodge still issues and protects its original target. " +
            JsonSerializer.Serialize(new { Hp = V(g, 0).Hp, Hand = V(g, 0).HandCount, V(g, 0).IsChained, Source = sourceState, State = V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Skill) }));
        Once(g, receipt); _ = Cold(g, r);
    }

    public static void RequestedUsesExcludePureProvisionAndKeepNativeParents()
    {
        foreach (var kind in new[] { CardKind.Duel, CardKind.ArrowBarrage }) Provision(kind);
        ProgramRequest(); BorrowedSword(); Qinglong();
    }

    private static void Provision(CardKind kind)
    {
        var (g, r) = Start(pure: kind); Play(g);
        if (kind == CardKind.Duel)
        {
            Use(g, "chain", [0]); Play(g); Use(g, "foreign-duel", [1]);
            Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        }
        else
        {
            Use(g, "arrows", cards: V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray());
            Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge } && p.IncomingCard == CardKind.ArrowBarrage);
        }
        var p = P(g)!;
        Require(p.IncomingCard == kind && p.Choices.All(c => !IsState(c)) && F<ChainedStateBasicStartedEvent>(g).Length == 0 &&
            V(g, p.PlayerSeat).IsChained == (kind == CardKind.Duel), "A real Duel Slash or Arrow Barrage Dodge provision cannot pay a use-only chained-state conversion.");
        DrawFundedBasicFixture.Frozen(p); g = Cold(g, r);
        if (p.PlayerSeat == 0) Answer(g, c => c.Cards.Count == 0);
        Play(g);
        Require(F<ChainedStateBasicPaidEvent>(g).Length == 0 && F<CardUseFinishedEvent>(g).Any(e => e.CardKind == kind),
            "The excluded native pure-response card resolves without a chain payment or zero-material basic issuance.");
        _ = Cold(g, r);
    }

    private static void ProgramRequest()
    {
        var (g, r) = Start(); Play(g); Use(g, "chain", [1]); Play(g); Use(g, "target-slash", [1]);
        Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(IsState));
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver); var cursor = parent.InstructionIndex;
        Require(V(g, 1).IsChained, "The actual requested actor has a genuinely payable release cost.");
        Private(g); g = Cold(g, r);
        Until(g, e => F<ChainedStateBasicIssuedEvent>(e).Any(f => f.Receipt.Payment.Intent == ChainedStateBasicIntent.ProgramSlash));
        var receipt = F<ChainedStateBasicIssuedEvent>(g).Single(f => f.Receipt.Payment.Intent == ChainedStateBasicIntent.ProgramSlash).Receipt;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == receipt.OwnerFrameId);
        Require(receipt.Payment.Source.OwnerSeat == 1 && receipt.Payment.ParentFrameId == parent.Id && receipt.Payment.RequestFrameId == parent.Id &&
            receipt.Payment.Cursor == cursor && use.CardAttack?.ProgramSkillCardUseFrameId == parent.Id && use.SourceSeat == 1 && use.TargetSeats.SequenceEqual([0]) &&
            !V(g, 1).IsChained && F<ChainedStateBasicReturnedEvent>(g).All(e => e.Receipt.Payment.PaymentFrameId != receipt.Payment.PaymentFrameId),
            "The native AI request uses its own chain fee and retains the exact original requesting program instruction as the Slash return.");
        g = Cold(g, r); Play(g); Once(g, receipt);
        Require(!g.ResolutionStack.Any(f => f.Id == parent.Id), "The requested actual Use returns through the original instruction once.");
    }

    private static void BorrowedSword()
    {
        var (g, r) = Start(weapon: CardKind.BorrowedSword); Play(g); Use(g, "equip", [1]); Play(g); Use(g, "chain", [1]); Play(g);
        var weapon = V(g, 1).Equipment.Single().Id;
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 2])));
        Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash } && p.Choices.Any(IsState));
        var outer = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword); var window = g.ResolutionStack.OfType<ResponseWindowFrame>().Last();
        Private(g); g = Cold(g, r);
        Until(g, e => F<ChainedStateBasicIssuedEvent>(e).Any(f => f.Receipt.Payment.Intent == ChainedStateBasicIntent.BorrowedSword) ||
            !e.ResolutionStack.Any(f => f.Id == outer.Id));
        Require(F<ChainedStateBasicIssuedEvent>(g).Any(f => f.Receipt.Payment.Intent == ChainedStateBasicIntent.BorrowedSword),
            "The native AI must fulfill this published Borrowed Sword need before its original parent completes. " + Boundary(g));
        var receipt = F<ChainedStateBasicIssuedEvent>(g).Single(f => f.Receipt.Payment.Intent == ChainedStateBasicIntent.BorrowedSword).Receipt;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == receipt.OwnerFrameId);
        Require(receipt.Payment.ParentFrameId == outer.Id && receipt.Payment.RequestFrameId == window.Id && receipt.Payment.ParentActionId == outer.Action!.ActionId &&
            receipt.Payment.Source.OwnerSeat == 1 && receipt.Payment.TargetSeat == 2 && use.TargetSeats.SequenceEqual([2]) && use.Action!.ParentActionId == outer.Action!.ActionId &&
            use.PhysicalCardIds is { Count: 0 } && !V(g, 1).IsChained && V(g, 1).Equipment.Any(c => c.Id == weapon),
            "Borrowed Sword's genuine equipped actor releases chain for an actual Slash with its exact compound-card parent and original response window.");
        g = Cold(g, r); Play(g); Once(g, receipt);
        Require(F<BorrowedSwordResolvedEvent>(g).Single(e => e.ResolutionId == outer.Id) is { UsedSlash: true, SlashCardId: 0, TransferredWeaponCardId: null } &&
            V(g, 1).Equipment.Any(c => c.Id == weapon), "The genuine promised Slash fulfills Borrowed Sword once and preserves the equipped weapon.");
    }

    private static void Qinglong()
    {
        var (g, r) = Start(weapon: CardKind.QinglongCrescentBlade); Play(g); Use(g, "equip", [0]); Play(g); Use(g, "chain", [0]); Play(g);
        Require(V(g, 1).Hand.Any(c => c.Kind == CardKind.Dodge), "The fixed physical target holds a real Dodge for a native Qinglong request.");
        SubmitPlay(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId != 0 && a.TargetSeats.SequenceEqual([1])));
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade } && p.Choices.Any(IsState));
        var outer = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardAttack is not null); var weapon = V(g, 0).Equipment.Single().Id;
        Require(outer.CardAttack!.SuccessfulDodgeResponses == 1, "A true first Slash receives an actual Dodge before the chain-paid Qinglong follow-up.");
        g = Cold(g, r); Answer(g, IsState);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade } &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.ChainedStateBasicUse is not null));
        var followed = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.ChainedStateBasicUse is not null);
        Require(followed.Id != outer.Id && followed.CardAttack!.SuccessfulDodgeResponses == 1 &&
            !V(g, 0).IsChained && P(g)!.Choices.All(c => !IsState(c)) &&
            F<ChainedStateBasicPaidEvent>(g).Length == 1,
            "The second real Dodge opens the follow-up Slash's own Qinglong menu; its already-released actor cannot pay the same chain fee again.");
        Private(g); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip");
        Reach(g, p => p.SkillPrompt?.SkillId == Completed && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.ChainedStateBasicUse is not null));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.ChainedStateBasicUse is not null); var receipt = use.ChainedStateBasicUse!;
        Require(receipt.Payment.Intent == ChainedStateBasicIntent.Qinglong && receipt.Payment.ParentFrameId == outer.Id && receipt.Payment.ParentActionId == outer.Action!.ActionId &&
            use.Id != outer.Id && use.TargetSeats.SequenceEqual([1]) && !V(g, 0).IsChained && V(g, 0).Equipment.Any(c => c.Id == weapon) &&
            F<QinglongCrescentBladeResolvedEvent>(g).Single(e => e.ResolutionId == outer.Id) is { Used: true, SlashCardIds.Count: 0 },
            "Qinglong's true follow-up pays release once and owns a distinct logical Slash despite the first ordinary Slash debit.");
        Private(g); g = Cold(g, r); Continue(g); Play(g); Once(g, receipt);
        Require(F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == outer.Id) == 1,
            "Both the genuine first Slash and its chain-paid Qinglong Use finish exactly once.");
    }

    private static void Once(GameEngine g, ChainedStateBasicUseReceipt receipt)
    {
        var id = receipt.Payment.PaymentFrameId;
        Require(F<ChainedStateBasicStartedEvent>(g).Count(e => e.Payment.PaymentFrameId == id) == 1 &&
            F<ChainedStateBasicPaidEvent>(g).Count(e => e.Payment.PaymentFrameId == id) == 1 &&
            F<ChainedStateBasicIssuedEvent>(g).Count(e => e.Receipt.Payment.PaymentFrameId == id) == 1 &&
            F<ChainedStateBasicReturnedEvent>(g).Count(e => e.Receipt == receipt) == 1 &&
            F<ChainedStateBasicCancelledEvent>(g).All(e => e.Payment.PaymentFrameId != id) && !g.ResolutionStack.Any(f => f.Id == id),
            "Each exact original need owns one state payment, one issuance and one native return, without cancellation or a surviving payment frame.");
    }
    private static void beforeStale(GameEngine g, PendingDecision prompt, PromptChoice choice)
    { var state = State(g); Require(!g.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, g.Revision)).Accepted && State(g) == state,
        "A stale published answer cannot change state or pay the completed need again."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected native chain-cost fixture command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var c = p.Choices.FirstOrDefault(predicate); Require(c is not null, "Missing published choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void SubmitPlay(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, e => P(e) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> predicate)
    {
        for (var i = 0; i < 220; i++)
        {
            if (predicate(g)) return;
            if (g.CreateSnapshot(0).Status == EngineStatus.Completed) break;
            Step(g);
        }
        throw new InvalidOperationException("Fixed chain-cost fixture missed native boundary: " + Boundary(g));
    }
    private static string Boundary(GameEngine g) => JsonSerializer.Serialize(new { Prompt = P(g),
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Last = g.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) Answer(g, c => c.Cards.Count == 0 && !IsState(c));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }) Answer(g, c => c.Cards.Count == 0 && !IsState(c));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var next = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(next) == State(g), "Actual serialized commands restore four player views, exact costs, pending native children and accepted action provenance."); return next; }
    private static void Private(GameEngine g) => DrawFundedBasicFixture.Private(g);
    private static void Reject(GameEngine g) { var state = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("chain-unpublished"), g.Revision)).Accepted && State(g) == state,
        "An unpublished answer rejects before any state cost or native cursor changes."); }
    private static (GameEngine, ContentRegistry) Start(bool stateChild = false, bool incoming = false, CardKind? pure = null, CardKind? weapon = null, string? chainPolicy = null, bool sourceLoss = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(stateChild, incoming, pure, weapon, chainPolicy, sourceLoss));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Require(P(g)!.ValidContentIds.Contains("fixture:chain-basic-owner"), "Fixed published candidates contain the shared mechanism owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:chain-basic-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }

    private static JsonNode ActualRules()
    {
        using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-yang-yan.rules.json")!;
        using var reader = new StreamReader(stream); return JsonNode.Parse(reader.ReadToEnd())!;
    }
    private static void LoaderBoundary()
    {
        var root = ActualRules(); var skill = root["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Skill)!.DeepClone();
        var rules = new JsonObject { ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion, ["skills"] = new JsonArray(skill) };
        var p = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Skill] = new { name = "状态费用", description = "共享节点合同" } } });
        _ = SkillProgramCatalog.Load(rules.ToJsonString(), p);
        foreach (var (field, value) in new (string, JsonNode?)[] { ("inputCount", JsonValue.Create(1)), ("useOnly", JsonValue.Create(false)),
                     ("usesPerPhase", JsonValue.Create(1)), ("chainedStateCost", JsonValue.Create(true)), ("sourceZones", new JsonArray("hand")) })
        {
            var bad = rules.DeepClone(); var slash = bad["skills"]![0]!["viewAs"]!.AsArray().Single(n => n!["outputKind"]!.GetValue<string>() == "slash")!;
            slash[field] = value?.DeepClone(); var rejected = false; try { _ = SkillProgramCatalog.Load(bad.ToJsonString(), p); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new state fee rejects incompatible material, direction and stacked usage contracts: " + field);
        }
        var legacy = JsonNode.Parse("""{"skills":[{"id":"fixture:chain-legacy","revision":1,"viewAs":[{"id":"physical","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]}]}""")!;
        legacy["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        var label = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { ["fixture:chain-legacy"] = new { name = "旧实体转换", description = "旧可选字段保持" } } });
        var old = SkillProgramCatalog.Load(legacy.ToJsonString(), label).Programs["fixture:chain-legacy"];
        legacy["skills"]![0]!["viewAs"]![0]!["chainedStateCost"] = null;
        Require(SkillProgramCatalog.Load(legacy.ToJsonString(), label).Programs["fixture:chain-legacy"].GameplayHash == old.GameplayHash,
            "An absent or null new optional field preserves the old positive-material conversion hash.");
    }
    private sealed class Fixture(bool stateChild, bool incoming, CardKind? pure, CardKind? weapon, string? chainPolicy, bool sourceLoss) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:chained-state-basic", "1.0.0", "真实连环状态费用与基本牌使用");
        public void Register(IContentRegistryBuilder b)
        {
            var actual = ActualRules(); var actualSkill = actual["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Skill)!.DeepClone();
            if (sourceLoss) actualSkill["states"] = JsonNode.Parse("""[{"id":"qualification-probe","initialValue":true,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}]""");
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:chain-basic-driver","revision":1,"viewAs":[
                {"id":"physical-slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},
                {"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},
                {"id":"arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false}],"activations":[
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"target-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},
                {"id":"foreign-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"arrows","outputKind":"arrowBarrage"}]}]},
              {"id":"fixture:chain-basic-child","revision":1,"triggers":[{"id":"entered","window":"characterEnteredChain","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1},{"op":"setChainedState","target":"owner","chained":false}]}]},
              {"id":"fixture:chain-basic-completed","revision":1,"triggers":[{"id":"done","window":"cardUseCompleted","ownerRelation":"actor","includeResponseUses":true,"optional":false,"cardKinds":["slash","fireSlash","thunderSlash","dodge"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:chain-basic-foreign","revision":1,"viewAs":[{"id":"real","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true}],"triggers":[{"id":"slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"useOwnerSlashAgainstTurnOwner","target":"owner","ignoreDistance":true}]}]},
              {"id":"fixture:chain-basic-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion; root["skills"]!.AsArray().Add(actualSkill);
            if (chainPolicy is not null) root["skills"]![0]!["cardPolicies"] = new JsonArray(new JsonObject { ["id"] = "native-state-policy", ["kind"] = chainPolicy });
            if (sourceLoss) root["skills"]![1]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:chain-basic-suppress"]}"""));
            var labels = root["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var label = new Dictionary<string, object> { ["name"] = "共享转换夹具", ["description"] = "固定真实命令" };
                if (n!["id"]!.GetValue<string>() is Child or Completed) label["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)label;
            });
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实连环状态转换") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:chain-basic-suppress", "真实已付来源抑制", "Lord初始7HP的原生资格抑制，不修改状态") { SuppressionRule = new(7) });
            b.AddSkill(new("fixture:chain-basic-selection", "固定候选", "小型真实夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddCard(new("fixture:chain-basic-qinglong", "青龙偃月刀", "装备牌", "真实后续杀", LegacyKind: CardKind.QinglongCrescentBlade));
            var ownerSkills = new List<string> { Driver, Completed }; if (stateChild) ownerSkills.Add(Child);
            b.AddGeneral(new("fixture:chain-basic-owner", "状态费用拥有者", "supporter", Skill, "jin", 6, ownerSkills, GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:chain-basic-peer-{seat}", "真实原生目标", "supporter", "fixture:chain-basic-selection", "qun", 4,
                incoming ? [Quiet, Foreign] : weapon == CardKind.QinglongCrescentBlade ? [Quiet] : [Quiet, Skill, Completed], GeneralGender.Male));
            var key = pure == CardKind.Duel ? "standard:slash" : "standard:dodge";
            var deck = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(key, Suit.Spade, i % 13 + 1));
            if (weapon.HasValue) deck = deck.Concat(Enumerable.Range(0, 20).Select(i => new ContentDeckPhysicalCard(
                weapon == CardKind.QinglongCrescentBlade ? "fixture:chain-basic-qinglong" : "standard:crossbow", Suit.Spade, i % 13 + 1)));
            b.AddDeck(new("fixture:chain-basic-deck", "固定真实实体", 4, 2, []) { PhysicalCards = deck.ToArray() });
            b.AddMode(new(Mode, "真实状态费用", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:chain-basic-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:chain-basic-owner", "fixture:chain-basic-peer-1", "fixture:chain-basic-peer-2", "fixture:chain-basic-peer-3"]));
        }
    }
}
