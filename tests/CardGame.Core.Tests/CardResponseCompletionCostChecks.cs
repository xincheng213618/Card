using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardResponseCompletionCostChecks
{
    private const string Mode = "identity:classic-response-completion-cost";
    private const string Driver = "fixture:response-completion-cost-driver";
    private const string Conversion = "fixture:response-completion-cost-conversion";
    private const string HpObserver = "fixture:response-completion-cost-hp";
    private const string HealthDyingReturn = "fixture:response-completion-health-dying-return";
    private const string HealthSourceReplacement = "fixture:response-completion-health-source-replacement";
    private const string FactionDefense = "fixture:response-completion-cost-faction-defense";
    private const string Zhefu = "ol:zhefu";
    private const string PublicPileMode = "fixture:public-pile-native-cost";
    private const string PublicPile = "fixture:public-pile-native-cost-source";
    private const string PublicPileDamage = "fixture:public-pile-native-cost-damage";
    private const string SlashPaymentMode = "identity:classic-slash-payment-dying";
    private const string SlashPaymentConversion = "fixture:slash-payment-conversion";
    private const string SlashPaymentObserver = "fixture:slash-payment-observer";
    private const string SlashPaymentHpObserver = "fixture:slash-payment-hp-observer";
    private const string SlashPaymentReturn = "fixture:slash-payment-return";
    private const string SlashPaymentReplacement = "fixture:slash-payment-replacement";
    private const string SlashPaymentQuiet = "fixture:slash-payment-quiet";

    public static void PublicPileSlashPaymentRemainsSeparateFromNativeMaterials()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new PublicPileFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = PublicPileMode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:public-pile-native-cost-actor", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p is { PlayerSeat: 1 } && p.SkillPrompt?.SkillId == PublicPile &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        var stored = View(game, 1).Hand.Take(2).Select(c => c.Id).ToArray();
        foreach (var id in stored) Answer(game, c => c.Cards.SequenceEqual([id]));
        Play(game);
        Require(View(game, 1).GeneralId == "fixture:public-pile-native-cost-owner" && View(game, 1).Hp == 4 &&
            View(game, 1).AuthorityCards!.Select(c => c.Id).Order().SequenceEqual(stored.Order()) &&
            registry.GetSkill("ol:yidu").Program!.Triggers.Any(t => t.Effects.Any(e =>
                e.Op == SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor)),
            "A real GameStarting selection stores two public Authority entities while the formal whole-use ledger capability is present.");
        foreach (var viewer in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(viewer).Players[1].AuthorityCards!.Select(c => c.Id).Order().SequenceEqual(stored.Order()),
                "The foreign actor pays published public-pile identities rather than another player's private hand.");
        var legal = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == PublicPile &&
            a.ProgramActivationId == "public-pile-slash" && a.ProgramSkillOwnerSeat == 1 && a.TargetSeats.SequenceEqual([1]));
        Require(legal.SelectableCardIds.Order().SequenceEqual(stored.Order()), "The native foreign policy publishes exactly the two real public costs.");
        Accept(game, new UseProgramSkillCommand(0, PublicPile, "public-pile-slash", stored, [1], game.Revision,
            Prompt(game)!.PromptId) { SkillOwnerSeat = 1 });
        Reach(game, p => p is { PlayerSeat: 1 } && p.SkillPrompt?.SkillId == PublicPileDamage && HasContinue(p));
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
        var action = use.Action!; var useId = use.Id; var actionId = action.ActionId;
        var recorded = Facts<CompletedUndamagedUseDamageRecordedEvent>(game).Single();
        Require(use is { SourceSeat: 0, CardId: 0, CardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } &&
            use.TargetSeats.SequenceEqual([1]) && action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0,
                EffectiveKind: CardKind.Slash, PhysicalCards.Count: 2 } &&
            action.PhysicalCards.Select(c => c.CardId).SequenceEqual(stored) &&
            action.PhysicalCards.All(c => c.From == CardLocation.Authority(1)) &&
            action.ConversionChain is [{ SkillId: PublicPile, BindingId: "public-pile-slash", OwnerSeat: 1 }] &&
            use.CardAttack is { PhysicalCardIds.Count: 0, DamageWasApplied: true } &&
            recorded is { ActorSeat: 0, SourceSeat: 0, TargetSeat: 1, EffectiveKind: CardKind.Slash, Amount: 1,
                SourceLess: false, ChainPropagation: false, Redirected: false } &&
            recorded.CardUseFrameId == useId && recorded.CardActionId == actionId &&
            Facts<CardActionAcceptedEvent>(game).Count(e => e.Action.ActionId == actionId && e.Action.Type == CardActionType.Use) == 1,
            "The exact accepted foreign Slash records real damage with zero native materials and two distinct Authority activation costs.");
        Require(View(game, 1).Hp == 3 && View(game, 1).AuthorityCount == 0 &&
            stored.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Authority(1) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "program.public-pile.slash-payment") == 1) &&
            !game.CardMovements.Any(m => stored.Contains(m.CardId) && m.To == CardLocation.Processing) &&
            Facts<CardUseFinishedEvent>(game).All(e => e.ResolutionId != useId),
            "The native costs are paid once directly to DiscardPile before damage, and the original Use awaits its real damage observer.");
        Frozen(action.PhysicalCards); Frozen(action.ConversionChain); FrozenEmptyNativeMaterials(use.PhysicalCardIds!);
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Play(game);
        Require(Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            Facts<CompletedUndamagedUseDamageRecordedEvent>(game).Count(e => e.CardUseFrameId == useId && e.CardActionId == actionId) == 1 &&
            Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            Facts<CompletedUndamagedTargetRevealStartedEvent>(game).Length == 0 &&
            stored.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Authority(1) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "program.public-pile.slash-payment") == 1) &&
            !game.ResolutionStack.Any(f => f.Id == useId) && View(game, 1).Hp == 3,
            "Cold return completes the original native Slash once without repeating its real costs, damage or ledger fact.");
        _ = Cold(game, registry);
    }

    private static void VerifyOrdinaryHealthDyingReturn()
    {
        foreach (var losePaidSource in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(ordinaryHealthDying: true, losePaidSource: losePaidSource));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
                HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
                AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            Accept(game, new SelectGeneralCommand(0, "fixture:response-completion-cost-owner", game.Revision, Prompt(game)!.PromptId));
            Play(game);
            var armor = View(game, 0).Hand.First(c => c.Kind == CardKind.SilverLion).Id;
            Use(game, "equip-other", [armor], [1]); Play(game);
            Use(game, "wound-other", [], [1]); Play(game);
            var slash = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([1]));
            Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, Prompt(game)!.PromptId,
                slash.PlayedCardKind) { ConversionSource = slash.ConversionSource });
            Reach(game, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            Reach(game, p => p.SkillPrompt?.SkillId == HpObserver && HasContinue(p));
            var useId = game.ResolutionStack.OfType<CardUseFrame>().Single().Id;
            var completionId = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null).Id;
            Private(game); game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            if (losePaidSource)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == HealthSourceReplacement && HasContinue(p));
                var replacement = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == HealthSourceReplacement);
                var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Window == SkillProgramTriggerWindow.DyingEntering);
                Require(replacement.WindowContext?.ParentFrameId == entry.Id &&
                    entry.ResumeDyingFrameId == game.ResolutionStack.OfType<DyingFrame>().Single().Id &&
                    View(game, 1).Skills!.Any(s => s.ContentId == Conversion),
                    "The existing mandatory entry protocol pauses before replacing the actual paid equipment-conversion source.");
                Private(game); game = Cold(game, registry);
                Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Require(Facts<ProgramOwnerSkillsReplacedEvent>(game).Any(e => e.SkillId == HealthSourceReplacement && e.OwnerSeat == 1 &&
                    e.LostSkillIds.SequenceEqual([Conversion]) && e.GrantedSkillId == Driver) &&
                    View(game, 1).Skills!.All(s => s.ContentId != Conversion),
                    "The recorded terminal entry replacement removes only the already-paid conversion and grants one existing capability.");
            }
            Reach(game, p => p.SkillPrompt?.SkillId == HealthDyingReturn && (HasContinue(p) || p.Choices.Any(IsActivate)));
            if (Prompt(game)!.Choices.Any(IsActivate))
            {
                Answer(game, IsActivate);
                Reach(game, p => p.SkillPrompt?.SkillId == HealthDyingReturn && HasContinue(p));
            }
            var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
            var losing = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == dying.ParentFrameId);
            var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == completionId);
            Require(dying is { VictimSeat: 1, KillerSeat: null, Continuation: DyingContinuationKind.ProgramSkill } &&
                losing.SkillId == HpObserver && losing.InstructionIndex == 2 && losing.WindowContext?.ParentFrameId == hp.Id &&
                completion.ResponseCompletion is { CostsDrained: false } receipt && receipt.ActiveHealthChildFrameId == hp.Id &&
                hp.Continuation == PostEventContinuation.ResponseCompletion && hp.ResumeFrameId == completionId &&
                hp.Change.ParentFrameId == useId && View(game, 1).Hp == 0 &&
                Facts<ProgramSkillHpLostEvent>(game).Count(e => e.FrameId == losing.Id && e.TargetSeat == 1 && e.Amount == 4) == 1 &&
                Facts<CardResponseCompletedEvent>(game).Length == 0,
                "One real ordinary HP observer loses exactly four HP and owns Dying beneath the still-unsettled native response invoice.");
            VerifyHealthDyingInstructionProof(game, losing);
            Private(game); game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(game, p => p.SkillPrompt?.SkillId == Zhefu && p.Choices.Any(IsActivate));
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            Play(game);
            Require(View(game, 1).IsAlive && View(game, 1).Hp == 4 &&
                Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == dying.Id && e.Survived) == 1 &&
                Facts<CardResponseCompletionStartedEvent>(game).Count(e => e.FrameId == completionId) == 1 &&
                Facts<CardResponseCompletedEvent>(game).Count(e => e.ParentFrameId == useId) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == 1) == 1 &&
                !Facts<DamageAppliedEvent>(game).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
                game.CardMovements.Count(m => m.CardId == armor && m.Reason == CardMoveReasons.Respond) == 1 &&
                game.CardMovements.Count(m => m.CardId == armor && m.Reason == CardMoveReasons.ResponseFinished) == 1 &&
                !game.ResolutionStack.Any(f => f.Id == useId || f.Id == completionId || f.Id == dying.Id),
                "Cold ordinary Dying returns through one native response, without repaying armor, repeating removal recovery or applying cancelled Slash damage.");
            if (losePaidSource)
                Require(View(game, 1).Skills!.All(s => s.ContentId != Conversion),
                    "The genuinely replaced conversion source remains unavailable while its already-paid native response finishes exactly once.");
            _ = Cold(game, registry);
        }
    }

    private static void VerifyFactionDefenseDodgeHealthDyingReturn()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(ordinaryHealthDying: true, factionDefense: true));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Renegade, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:response-completion-cost-owner", game.Revision, Prompt(game)!.PromptId));
        Play(game);
        var lord = game.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat;
        const int provider = 0;
        Require(lord != provider &&
            View(game, lord).GeneralId == "fixture:response-completion-cost-peer-2" &&
            View(game, lord).Skills!.Any(s => s.ContentId == FactionDefense) &&
            View(game, provider).Skills!.Any(s => s.ContentId == Conversion),
            "Actual role-weighted selection gives one foreign Lord a native faction request and a distinct equipment-Dodge provider.");
        var armor = View(game, 0).Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        Accept(game, new PlayCardCommand(0, armor, [], game.Revision, Prompt(game)!.PromptId)); Play(game);
        Use(game, "wound-self", [], []); Play(game);
        var lordHp = View(game, lord).Hp;
        var slash = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([lord]));
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, Prompt(game)!.PromptId,
            slash.PlayedCardKind) { ConversionSource = slash.ConversionSource });
        Reach(game, p => p.PlayerSeat == lord && p.Kind == DecisionKind.RespondDodge &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-request"));
        var native = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
        var useId = native.Id; var useActionId = native.Action!.ActionId;
        Private(game); game = Cold(game, registry);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.PlayerSeat == provider && p.Kind == DecisionKind.RespondDodge &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-dodge" && c.Cards.SequenceEqual([armor])));
        Private(game); game = Cold(game, registry);
        // On unchanged Core 208 this accepted native faction payment reaches
        // its real health invoice and is incorrectly rejected as malformed.
        Answer(game, c => c.Parameters.GetValueOrDefault("response") == "faction-defense-dodge" && c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.SkillPrompt?.SkillId == HpObserver && HasContinue(p));
        var accepted = Facts<CardActionAcceptedEvent>(game).Single(e => e.Action.Type == CardActionType.Response).Action;
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == HpObserver);
        Require(accepted is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 1 } &&
            accepted.ActorSeat == lord && accepted.ProviderSeat == provider && accepted.RequesterSeat == lord &&
            accepted.ResponderSeat == lord && accepted.OpponentSeat == 0 && accepted.ParentActionId == useActionId &&
            accepted.PhysicalCards[0].CardId == armor && accepted.PhysicalCards[0].From == CardLocation.Equipment(provider) &&
            accepted.ConversionChain is [{ SkillId: Conversion, BindingId: "equipment-dodge" }] &&
            completion.Continuation == ProgramCardContinuation.FactionDefenseDodge &&
            completion.ResponseCompletion is { CostsDrained: false, CostHealthChanges.Count: 1 } receipt &&
            receipt.OriginalContinuation == ProgramCardContinuation.FactionDefenseDodge &&
            receipt.CompletionActorSeat == provider && receipt.ParentFrameId == useId && receipt.ActionId == accepted.ActionId &&
            receipt.ActiveHealthChildFrameId == hp.Id && hp.Change.ParentFrameId == useId &&
            hp.Change.TargetSeat == provider && hp.Change.SourceSeat == provider && hp.Change.Amount == 1 &&
            hp.Change.HpBefore == 3 && hp.Change.HpAfter == 4 &&
            hp.Continuation == PostEventContinuation.ResponseCompletion && hp.ResumeFrameId == completion.Id &&
            observer.WindowContext?.ParentFrameId == hp.Id && Facts<CardResponseCompletedEvent>(game).Length == 0,
            "The real faction response freezes separate principal and provider identities and owns the exact paid armor recovery before completion.");
        VerifyFactionDefenseDirectionProof(game, useId, ownedDyingId: null);
        Frozen(completion.ResponseCompletion!.NativeCosts); Frozen(completion.ResponseCompletion.CostBatches);
        Frozen(completion.ResponseCompletion.CostHealthChanges);
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.SkillPrompt?.SkillId == HealthDyingReturn && HasContinue(p));
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        var losing = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == dying.ParentFrameId);
        Require(dying.VictimSeat == provider && dying.KillerSeat is null &&
            dying.Continuation == DyingContinuationKind.ProgramSkill && losing.Id == observer.Id &&
            losing.InstructionIndex == 2 && View(game, provider).Hp == 0 &&
            Facts<ProgramSkillHpLostEvent>(game).Count(e => e.FrameId == losing.Id && e.TargetSeat == provider && e.Amount == 4) == 1 &&
            Facts<CardResponseCompletedEvent>(game).Length == 0,
            "One ordinary frozen recovery observer owns a real four-HP loss and provider self-rescue below the unsettled faction invoice.");
        VerifyHealthDyingInstructionProof(game, losing);
        VerifyFactionDefenseDirectionProof(game, useId, dying.Id);
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } ||
            p.SkillPrompt?.SkillId == Zhefu && p.Choices.Any(IsActivate));
        if (Prompt(game)!.SkillPrompt?.SkillId == Zhefu)
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Play(game);
        Require(View(game, lord).Hp == lordHp && View(game, provider).IsAlive && View(game, provider).Hp == 4 &&
            Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == dying.Id && e.Survived) == 1 &&
            Facts<CardResponseCompletionStartedEvent>(game).Count(e => e.ActionId == accepted.ActionId) == 1 &&
            Facts<CardResponseCompletedEvent>(game).Count(e => e.ActionId == accepted.ActionId && e.ParentFrameId == useId &&
                e.ActorSeat == provider && e.ProviderSeat == provider && e.NativeActorSeat == lord) == 1 &&
            Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
            Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == provider && e.RecoveredAmount == 1) == 1 &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == provider && e.Amount == 1) == 1 &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == provider && e.Amount == 4) == 1 &&
            !Facts<DamageAppliedEvent>(game).Any(e => e.SourceSeat == 0 && e.TargetSeat == lord) &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(provider) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1 &&
            !game.ResolutionStack.Any(f => f.Id == useId || f.Id == completion.Id || f.Id == hp.Id || f.Id == dying.Id),
            "Cold self-rescue finishes one actual provider response, prevents the Lord's Slash damage and never repays armor or repeats removal recovery.");
        _ = Cold(game, registry);
    }

    private static void VerifyFactionDefenseDirectionProof(GameEngine game, long useId, long? ownedDyingId)
    {
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var receipt = completion.ResponseCompletion!;
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedResponseCompletionHealthCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var positive = project.Invoke(game, null)!;
        Require((positive.GetType().GetProperty("Owner")!.GetValue(positive) as CardUseFrame)?.Id == useId &&
            (long?)positive.GetType().GetProperty("OwnedDyingId")!.GetValue(positive) == ownedDyingId &&
            !(bool)positive.GetType().GetProperty("IsMalformed")!.GetValue(positive)!,
            "A known faction Dodge health chain projects only its exact native owner and actual owned Dying.");
        foreach (var malformed in new[]
        {
            completion with { Action = CopyCommittedSlashAction(completion.Action, providerSeat: completion.Action.ActorSeat) },
            completion with { Action = CopyCommittedSlashAction(completion.Action, requesterSeat: completion.Action.ProviderSeat) },
            completion with { Action = CopyCommittedSlashAction(completion.Action, responderSeat: 0) },
            completion with { Action = CopyCommittedSlashAction(completion.Action, opponentSeat: completion.Action.ActorSeat) },
            completion with { Action = CopyCommittedSlashAction(completion.Action, effectiveKind: CardKind.Slash) },
            completion with { Continuation = ProgramCardContinuation.Dodge },
            completion with { ResponseCompletion = receipt with { OriginalContinuation = ProgramCardContinuation.Dodge } },
            completion with { Continuation = ProgramCardContinuation.Dodge,
                ResponseCompletion = receipt with { OriginalContinuation = ProgramCardContinuation.Dodge } },
            completion with { Continuation = ProgramCardContinuation.DuelSlash,
                ResponseCompletion = receipt with { OriginalContinuation = ProgramCardContinuation.DuelSlash } }
        })
        {
            var rejected = false;
            try
            {
                replace.Invoke(store, [malformed]);
                var projection = project.Invoke(game, null)!;
                Require((bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
                    "A contradictory provider, requester or native response direction must fail closed despite a real paid invoice.");
                try { assert.Invoke(game, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            }
            finally { replace.Invoke(store, [completion]); }
            Require(rejected, "The final invariant rejects native faction direction tampering without a compatibility fallback.");
        }
    }

    private static void VerifyHealthDyingInstructionProof(GameEngine game, ProgramSkillFrame losing)
    {
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedResponseCompletionHealthCursor", BindingFlags.Instance | BindingFlags.NonPublic);
        assert.Invoke(game, null);
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var receipt = completion.ResponseCompletion!;
        var response = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == HealthDyingReturn);
        var native = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == completion.ParentFrameId);
        var originals = game.ResolutionStack.ToDictionary(f => f.Id);
        void Reject(ResolutionFrame malformed)
        {
            var rejected = false;
            try
            {
                replace.Invoke(store, [malformed]);
                if (project is not null)
                {
                    var projection = project.Invoke(game, null)!;
                    Require((bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
                        "A known contradictory native health edge must fail closed before compatibility observers are considered.");
                }
                try { assert.Invoke(game, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            }
            finally { replace.Invoke(store, [originals[malformed.Id]]); }
            Require(rejected, "The final invariant must reject a contradictory native health invoice, paused instruction, frozen observer or Dying return.");
        }
        // This first case is also run against the unchanged pre-fix Core: it
        // must fail there because legacy ancestry accepts a paused ChooseOption.
        Reject(losing with { InstructionIndex = 1 });
        Require(project is not null, "The native paid-health family must have its exact typed cursor proof.");
        var positive = project!.Invoke(game, null)!;
        Require((positive.GetType().GetProperty("Owner")!.GetValue(positive) as CardUseFrame)?.Id == native.Id &&
            (long?)positive.GetType().GetProperty("OwnedDyingId")!.GetValue(positive) == dying.Id &&
            !(bool)positive.GetType().GetProperty("IsMalformed")!.GetValue(positive)!,
            "The real native response health chain projects its exact original use and owned Dying without a skill capability list.");
        foreach (var malformed in new ResolutionFrame[]
        {
            losing with { GameplayHash = "foreign-content" },
            losing with { SkillInstanceId = "foreign-hp-observer" },
            losing with { WindowContext = losing.WindowContext! with { ParentFrameId = native.Id } },
            response with { SkillInstanceId = "foreign-dying-response" },
            response with { GameplayHash = "foreign-response-content" },
            response with { WindowContext = response.WindowContext! with { ParentFrameId = completion.Id } },
            dying with { ParentFrameId = native.Id },
            dying with { Continuation = DyingContinuationKind.AttackHpLoss },
            dying with { KillerSeat = 0 },
            hp with { ResumeFrameId = native.Id },
            hp with { Continuation = PostEventContinuation.RecoveryPaidCardUse },
            hp with { Change = hp.Change with { Id = hp.Change.Id + 1000 } },
            hp with { Change = hp.Change with { ParentFrameId = completion.Id } },
            hp with { Change = hp.Change with { SourceSeat = hp.Change.SourceSeat == 0 ? 1 : 0 } },
            completion with { ParentFrameId = native.Id + 1000 },
            completion with { ResponseCompletion = receipt with { ActionId = receipt.ActionId + 1000 } },
            completion with { ResponseCompletion = receipt with { ActiveHealthChildFrameId = hp.Id + 1000 } },
            completion with { ResponseCompletion = receipt with { CostsDrained = true } },
            completion with { ResponseCompletion = receipt with { CostHealthChanges = [hp.Change with { Amount = 2 }] } },
            completion with { Continuation = receipt.OriginalContinuation == ProgramCardContinuation.Dodge
                ? ProgramCardContinuation.FactionDefenseDodge : ProgramCardContinuation.Dodge },
            completion with { ResponseCompletion = receipt with { OriginalContinuation = receipt.OriginalContinuation == ProgramCardContinuation.Dodge
                ? ProgramCardContinuation.FactionDefenseDodge : ProgramCardContinuation.Dodge } },
            completion with { Continuation = receipt.OriginalContinuation == ProgramCardContinuation.Dodge
                    ? ProgramCardContinuation.FactionDefenseDodge : ProgramCardContinuation.Dodge,
                ResponseCompletion = receipt with { OriginalContinuation = receipt.OriginalContinuation == ProgramCardContinuation.Dodge
                    ? ProgramCardContinuation.FactionDefenseDodge : ProgramCardContinuation.Dodge } },
            native with { SourceSeat = 1 }
        }) Reject(malformed);
    }

    private static void VerifyCommittedSlashPaymentDyingReturn()
    {
        foreach (var (materials, alternative, losePaidSource) in new[] { (1, false, false), (2, false, true), (1, true, true) })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new CommittedSlashFixture(losePaidSource));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
                HumanRole = Role.Lord, ModeId = SlashPaymentMode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
                AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            Accept(game, new SelectGeneralCommand(0, "fixture:slash-payment-owner", game.Revision, Prompt(game)!.PromptId));
            Play(game);
            Require(View(game, 0).Hp == 4 && View(game, 1).Hp == 4, "The fixed real Slash fixture starts with two four-HP participants.");
            var cards = View(game, 0).Hand.Take(materials).Select(c => c.Id).Order().ToArray();
            if (materials == 2)
                Accept(game, new UseProgramSkillCommand(0, SlashPaymentConversion, "two-as-slash", cards, [1], game.Revision, Prompt(game)!.PromptId));
            else if (alternative)
            {
                var action = game.GetHumanLegalActions().First(a => a.CardId == cards[0] && a.TargetSeats.SequenceEqual([1]) &&
                    a.ConversionSource?.SkillId == SlashPaymentConversion && a.ConversionSource.BindingId == "top-cost");
                Accept(game, new PlayCardCommand(0, cards[0], [1], game.Revision, Prompt(game)!.PromptId,
                    action.PlayedCardKind) { ConversionSource = action.ConversionSource });
            }
            else Accept(game, new PlayCardCommand(0, cards[0], [1], game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == SlashPaymentObserver && HasContinue(p));
            var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
            var payment = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == SlashPaymentObserver);
            Require(use.RecoveryPaidContinuation?.Kind == RecoveryPaidCardUseKind.CommittedSlash && !use.ProgramUseCommitted &&
                use.PaymentMovementReceipt is { } issued && issued.OwnerFrameId == use.Id && issued.ActionId == use.Action!.ActionId &&
                payment.ResumePaidCardUseFrameId == use.Id && payment.Batch.ParentFrameId == use.Id &&
                payment.Batch.Movements.Count == materials && payment.Batch.Movements.Select(m => m.CardId).SequenceEqual(cards) &&
                payment.Batch.Movements.All(m => m.Reason == CardMoveReasons.Use && m.From == CardLocation.Hand(0) &&
                    m.To == (alternative ? CardLocation.DrawPile : CardLocation.Processing)) &&
                observer.WindowContext == payment.Contexts![payment.CandidateIndex] && observer.InstructionIndex == 1 &&
                Facts<DamageRequestedEvent>(game).Length == 0,
                "A real accepted Slash pauses on its exact issued physical payment batch before any damage or committed-use tail.");
            VerifyCommittedSlashProjection(game, use.Id, null);
            Private(game); game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            if (losePaidSource)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == SlashPaymentReplacement && HasContinue(p));
                Require(View(game, 0).Skills!.Any(s => s.ContentId == SlashPaymentConversion),
                    "The actual paid conversion remains present until the real mandatory Dying entry replaces it.");
                VerifyCommittedSlashDyingEntryProof(game, use.Id);
                Private(game); game = Cold(game, registry);
                Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Require(Facts<ProgramOwnerSkillsReplacedEvent>(game).Any(e => e.SkillId == SlashPaymentReplacement && e.OwnerSeat == 0 &&
                    e.LostSkillIds.SequenceEqual([SlashPaymentConversion]) && e.GrantedSkillId == SlashPaymentQuiet) &&
                    View(game, 0).Skills!.All(s => s.ContentId != SlashPaymentConversion),
                    "A real entry command removes the already-paid conversion while preserving its frozen native action and invoice.");
            }
            if (losePaidSource)
            {
                static bool SelfRescueChoice(PromptChoice choice) => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                    choice.Parameters.GetValueOrDefault("skill-id") == SlashPaymentReturn &&
                    choice.Parameters.GetValueOrDefault("binding-id") == "actual-self-return";
                Reach(game, p => p.Kind == DecisionKind.RescueDying && p.Choices.Any(SelfRescueChoice));
                VerifyCommittedSlashProjection(game, use.Id, game.ResolutionStack.OfType<DyingFrame>().Single().Id);
                Private(game); game = Cold(game, registry);
                Answer(game, SelfRescueChoice);
            }
            Reach(game, p => p.SkillPrompt?.SkillId == SlashPaymentReturn && HasContinue(p));
            var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
            observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == dying.ParentFrameId);
            Require(dying is { VictimSeat: 0, KillerSeat: null, Continuation: DyingContinuationKind.ProgramSkill } &&
                observer.SkillId == SlashPaymentObserver && observer.InstructionIndex == 2 && View(game, 0).Hp == 0 &&
                Facts<ProgramSkillHpLostEvent>(game).Count(e => e.FrameId == observer.Id && e.TargetSeat == 0 && e.Amount == 4) == 1 &&
                Facts<DamageRequestedEvent>(game).Length == 0,
                "The frozen real payment observer owns ordinary four-HP loss and Dying before the original Slash effect begins.");
            VerifyCommittedSlashProjection(game, use.Id, dying.Id);
            VerifyCommittedSlashDyingProof(game, observer);
            Private(game); game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(game, p => p.SkillPrompt?.SkillId == SlashPaymentHpObserver && HasContinue(p));
            var health = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
            var rescue = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == SlashPaymentReturn);
            Require(health is { Continuation: PostEventContinuation.Program } && health.ResumeFrameId == rescue.Id &&
                health.Change.ParentFrameId == rescue.Id && health.Change is { Kind: HpChangeKind.Recovery, Amount: 4, TargetSeat: 0 } &&
                rescue.InstructionIndex == 2 && View(game, 0).Hp == 4 && Facts<DamageRequestedEvent>(game).Length == 0,
                "Ordinary self Recover really pauses on its own frozen HP observer before Dying and the native payment return.");
            VerifyCommittedSlashProjection(game, use.Id, dying.Id);
            VerifyCommittedSlashRecoveryProof(game, rescue, health);
            Private(game); game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Play(game);
            Require(View(game, 0).IsAlive && View(game, 0).Hp == 4 && View(game, 1).Hp == 3 &&
                Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == dying.Id && e.Survived) == 1 &&
                Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 0 && e.Amount == 4) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id) == 1 &&
                Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
                cards.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == (alternative ? CardLocation.DrawPile : CardLocation.Processing) && m.Reason == CardMoveReasons.Use) == 1 &&
                    game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing &&
                        m.To == CardLocation.DiscardPile) == (alternative ? 0 : 1)) &&
                !game.ResolutionStack.Any(f => f.Id == use.Id || f.Id == payment.Id || f.Id == dying.Id),
                "Cold ordinary self recovery drains the original Slash payment once, then applies one damage and finishes one native use.");
            if (losePaidSource) Require(View(game, 0).Skills!.All(s => s.ContentId != SlashPaymentConversion),
                "The genuinely lost conversion does not return when its already-paid native Slash finishes.");
            _ = Cold(game, registry);
        }
    }

    private static void VerifyCommittedSlashProjection(GameEngine game, long useId, long? dyingId)
    {
        var method = typeof(GameEngine).GetMethod("ProjectTypedCommittedSlashPaymentCursor", BindingFlags.Instance | BindingFlags.NonPublic);
        // The same real chain may be run against an unchanged older Core for a
        // final-invariant negative. The helper assertion applies to the new Core.
        if (method is null) return;
        var projection = method.Invoke(game, null)!;
        Require((projection.GetType().GetProperty("Owner")!.GetValue(projection) as CardUseFrame)?.Id == useId &&
            (long?)projection.GetType().GetProperty("OwnedDyingId")!.GetValue(projection) == dyingId &&
            !(bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
            "The exact real issued-payment suffix projects the original native Slash and only its own Dying.");
    }

    private static void VerifyCommittedSlashDyingProof(GameEngine game, ProgramSkillFrame losing)
    {
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedCommittedSlashPaymentCursor", BindingFlags.Instance | BindingFlags.NonPublic);
        assert.Invoke(game, null);
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        var payment = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        var native = game.ResolutionStack.OfType<CardUseFrame>().Single();
        var receipt = native.PaymentMovementReceipt!;
        var issuedBatch = receipt.Batch!;
        var response = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == SlashPaymentReturn);
        var originals = game.ResolutionStack.ToDictionary(f => f.Id);
        void Reject(ResolutionFrame malformed)
        {
            var rejected = false;
            try
            {
                replace.Invoke(store, [malformed]);
                if (project is not null)
                {
                    var projection = project.Invoke(game, null)!;
                    Require((bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
                        "A known contradictory paid Slash edge cannot regain permission from a compatibility observer.");
                }
                try { assert.Invoke(game, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            }
            finally { replace.Invoke(store, [originals[malformed.Id]]); }
            Require(rejected, "The final invariant rejects an invalid paid Slash invoice, frozen candidate, paused LoseHp or owned Dying return.");
        }
        Reject(losing with { InstructionIndex = 1 });
        Require(project is not null, "The known paid Slash movement family has one exact typed cursor projection.");
        foreach (var malformed in new ResolutionFrame[]
        {
            losing with { GameplayHash = "foreign-payment-content" },
            losing with { SkillInstanceId = "foreign-payment-observer" },
            losing with { WindowContext = losing.WindowContext! with { ParentFrameId = native.Id } },
            losing with { WindowContext = losing.WindowContext! with { MovementBatch = payment.Batch with { Id = payment.Batch.Id + 1000 } } },
            response with { SkillInstanceId = "foreign-payment-rescue" },
            response with { GameplayHash = "foreign-rescue-content" },
            response with { WindowContext = response.WindowContext! with { ParentFrameId = native.Id } },
            dying with { ParentFrameId = native.Id },
            dying with { VictimSeat = 1 },
            dying with { Continuation = DyingContinuationKind.AttackHpLoss },
            dying with { KillerSeat = 1 },
            payment with { ResumePaidCardUseFrameId = native.Id + 1000 },
            payment with { ResumePaidCardUseFrameId = null },
            payment with { ResumeDeclarationFrameId = native.Id },
            payment with { ResumeResponseCompletionFrameId = native.Id },
            payment with { Batch = payment.Batch with { ParentFrameId = native.Id + 1000 } },
            payment with { Batch = payment.Batch with { Id = payment.Batch.Id + 1000 } },
            native with { PaymentMovementReceipt = null },
            native with { PaymentMovementReceipt = receipt with { OwnerFrameId = native.Id + 1000 } },
            native with { PaymentMovementReceipt = receipt with { ActionId = receipt.ActionId + 1000 } },
            native with { PaymentMovementReceipt = receipt with { SequenceBefore = receipt.SequenceBefore + 1 } },
            native with { PaymentMovementReceipt = receipt with { Batch = issuedBatch with { Id = issuedBatch.Id + 1000 } } },
            native with { RecoveryPaidContinuation = native.RecoveryPaidContinuation! with { Kind = RecoveryPaidCardUseKind.DodgeCompletion } },
            native with { RecoveryPaidContinuation = native.RecoveryPaidContinuation! with { SourceSeat = 1 } },
            native with { ProgramUseCommitted = true },
            native with { SourceSeat = 1 },
            native with { Action = CopyCommittedSlashAction(native.Action!, providerSeat: 1) },
            native with { Action = CopyCommittedSlashAction(native.Action!, requesterSeat: 1) },
            native with { Action = CopyCommittedSlashAction(native.Action!, responderSeat: 1) },
            native with { Action = CopyCommittedSlashAction(native.Action!, opponentSeat: 1) },
            native with { Action = CopyCommittedSlashAction(native.Action!, parentActionId: native.Id + 1000) }
        }) Reject(malformed);
        // This host-only contradictory acceptance proves that clearing the flag
        // cannot downgrade an existing same-Action acceptance to declaration proof.
        var pending = (List<IGameEvent>)typeof(GameEngine).GetField("_pendingEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var forgedAcceptance = new CardActionAcceptedEvent(CopyCommittedSlashAction(native.Action!, providerSeat: 1));
        pending.Add(forgedAcceptance);
        try { Reject(native with { ProgramUseAccepted = false }); }
        finally { pending.Remove(forgedAcceptance); }
    }

    private static CardActionContext CopyCommittedSlashAction(CardActionContext action, int? providerSeat = null,
        long? parentActionId = null, int? requesterSeat = null, int? responderSeat = null, int? opponentSeat = null,
        CardKind? effectiveKind = null) =>
        new(action.ActionId, parentActionId ?? action.ParentActionId, action.Type, action.ActorSeat, providerSeat ?? action.ProviderSeat,
            requesterSeat ?? action.RequesterSeat, responderSeat ?? action.ResponderSeat, opponentSeat ?? action.OpponentSeat,
            effectiveKind ?? action.EffectiveKind, action.TargetSeats, action.PhysicalCards,
            action.ConversionChain, action.DesignatedTargetSeats, action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);

    private static void VerifyCommittedSlashDyingEntryProof(GameEngine game, long useId)
    {
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        VerifyCommittedSlashProjection(game, useId, dying.Id);
        var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single();
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == SlashPaymentReplacement);
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedCommittedSlashPaymentCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originals = game.ResolutionStack.ToDictionary(f => f.Id);
        foreach (var malformed in new ResolutionFrame[]
        {
            entry with { ResumeDyingFrameId = dying.Id + 1000 },
            entry with { ResumeProgramFrameId = observer.Id },
            entry with { Continuation = ProgramLifecycleContinuation.CompletePlayPhase },
            entry with { OwnerSeat = 1 },
            entry with { ParticipantFacts = null },
            entry with { CandidateIndex = entry.Candidates.Count },
            observer with { SkillInstanceId = "foreign-dying-entry-instance" },
            observer with { GameplayHash = "foreign-dying-entry-content" },
            observer with { WindowContext = observer.WindowContext! with { ParentFrameId = dying.Id } }
        })
        {
            var rejected = false;
            try
            {
                replace.Invoke(store, [malformed]);
                var projection = project.Invoke(game, null)!;
                Require((bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
                    "A real Dying entry must keep its sole owning return and actually frozen program candidate.");
                try { assert.Invoke(game, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            }
            finally { replace.Invoke(store, [originals[malformed.Id]]); }
            Require(rejected, "The final invariant rejects a foreign Dying lifecycle return or entry observer.");
        }
    }

    private static void VerifyCommittedSlashRecoveryProof(GameEngine game, ProgramSkillFrame rescue, HpChangedTriggerWindowFrame hp)
    {
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var replace = store.GetType().GetMethod("Replace")!;
        var assert = typeof(GameEngine).GetMethod("AssertCoreInvariants", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedCommittedSlashPaymentCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originals = game.ResolutionStack.ToDictionary(f => f.Id);
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == SlashPaymentHpObserver);
        foreach (var malformed in new ResolutionFrame[]
        {
            rescue with { InstructionIndex = 1 },
            hp with { ResumeFrameId = game.ResolutionStack.OfType<CardUseFrame>().Single().Id },
            hp with { Continuation = PostEventContinuation.AwaitedProgramMovement },
            hp with { Change = hp.Change with { ParentFrameId = hp.Change.ParentFrameId + 1000 } },
            hp with { Change = hp.Change with { TargetSeat = 1 } },
            hp with { Change = hp.Change with { SourceSeat = 1 } },
            hp with { Change = hp.Change with { Amount = 5, HpAfter = hp.Change.HpBefore + 5 } },
            hp with { Change = hp.Change with { Kind = HpChangeKind.Loss } },
            observer with { SkillInstanceId = "foreign-recovery-observer" },
            observer with { GameplayHash = "foreign-recovery-content" }
        })
        {
            var rejected = false;
            try
            {
                replace.Invoke(store, [malformed]);
                var projection = project.Invoke(game, null)!;
                Require((bool)projection.GetType().GetProperty("IsMalformed")!.GetValue(projection)!,
                    "A known ordinary Recover child must keep its real paused producer, invoice and frozen observer.");
                try { assert.Invoke(game, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            }
            finally { replace.Invoke(store, [originals[malformed.Id]]); }
            Require(rejected, "The final invariant rejects a contradictory ordinary self-Recover return beneath the same paid Slash.");
        }
    }

    public static void SilverLionEquipmentDodgeDrainsHealthBeforeCompletion()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:response-completion-cost-owner", game.Revision, Prompt(game)!.PromptId));
        Play(game);
        Require(View(game, 1).GeneralId == "fixture:response-completion-cost-peer-1" && View(game, 1).Hp == 4,
            "Published role-weighted selection puts the real equipment-response owner at seat1 with native 4 HP.");
        var windows = registry.GetSkill(Zhefu).Program!.Triggers.Select(t => t.Window).ToHashSet();
        Require(windows.SetEquals([SkillProgramTriggerWindow.CardUseCompleted, SkillProgramTriggerWindow.CardResponseCompleted,
            SkillProgramTriggerWindow.CardSupplyCompleted]), "The fixture retains all three formal Zhefu completion windows.");

        var armor = View(game, 0).Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        Use(game, "equip-other", [armor], [1]); Play(game);
        Require(View(game, 1).Equipment is [{ Id: var equipped, Kind: CardKind.SilverLion }] && equipped == armor,
            "A real selected hand entity is placed in the responder's native armor slot.");
        Use(game, "wound-other", [], [1]); Play(game);
        Require(View(game, 1).Hp == 3 && Facts<RecoveryAppliedEvent>(game).Length == 0,
            "A legal LoseHp operation wounds the armor owner before its actual response payment.");
        var ownerHp = View(game, 0).Hp;
        var slash = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver &&
            a.ConversionSource.BindingId == "slash" && a.TargetSeats.SequenceEqual([1]));
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, Prompt(game)!.PromptId,
            slash.PlayedCardKind) { ConversionSource = slash.ConversionSource });
        Reach(game, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        var incoming = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
        var useId = incoming.Id; var useAction = incoming.Action!.ActionId;
        Require(Prompt(game)!.Choices.Any(c => c.Cards.SequenceEqual([armor])) &&
            View(game, 1).Hand.All(c => c.Kind == CardKind.SilverLion),
            "The published Dodge uses the equipped entity; ordinary hand armor cannot pay the equipment-only conversion.");
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.SkillPrompt?.SkillId == HpObserver && HasContinue(p));

        var response = Facts<CardActionAcceptedEvent>(game).Single(e => e.Action.Type == CardActionType.Response).Action;
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var receipt = completion.ResponseCompletion!;
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == HpObserver);
        var observerId = observer.Id;
        Require(response is { ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 1 } &&
            response.ParentActionId == useAction && response.PhysicalCards[0].CardId == armor &&
            response.PhysicalCards[0].From == CardLocation.Equipment(1) &&
            response.ConversionChain is [{ SkillId: Conversion }],
            "The accepted native Dodge freezes precisely its equipment source, conversion and original Slash action.");
        Require(receipt is { CostsDrained: false, CompletionActorSeat: 1, CostRecoveryCursor: 0, CostHealthCursor: 0,
                CostHealthChanges.Count: 1 } && receipt.ActionId == response.ActionId && receipt.ParentFrameId == useId &&
            receipt.ActiveHealthChildFrameId == hp.Id && hp.Continuation == PostEventContinuation.ResponseCompletion &&
            hp.ResumeFrameId == completion.Id && hp.Change.ParentFrameId == useId &&
            hp.Change is { Kind: HpChangeKind.Recovery, SourceSeat: 1, TargetSeat: 1, Amount: 1, HpBefore: 3, HpAfter: 4 } &&
            observer.WindowContext?.ParentFrameId == hp.Id &&
            game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).CardAttack!.SuccessfulDodgeResponses == 0,
            "The paid response owns the real HP-change child before the original successful-Dodge cursor advances.");
        Require(Facts<SilverLionRemovedRecoveryEvent>(game) is [{ PlayerSeat: 1, RecoveredAmount: 1 }] &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            View(game, 1).Hp == 4 && View(game, 1).Equipment.Count == 0 &&
            Facts<CardResponseCompletedEvent>(game).Length == 0 && Facts<SameNameHandStartedEvent>(game).Length == 0,
            "Leaving the real armor heals exactly once, but neither response completion nor Zhefu precedes its HP observer.");
        Require(game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(1) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1,
            "The native response has already committed its one equipment entry and one finish movement.");
        Frozen(receipt.NativeCosts); Frozen(receipt.CostBatches); Frozen(receipt.CostHealthChanges);
        foreach (var batch in receipt.CostBatches) { Frozen(batch.Movements); Frozen(batch.SourceCounts); }
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.SkillPrompt?.SkillId == Zhefu && p.Choices.Any(IsActivate));
        Require(Facts<CardResponseCompletedEvent>(game) is [{ ActorSeat: 1, ProviderSeat: 1, NativeActorSeat: 1,
                EffectiveKind: CardKind.Dodge } done] && done.ActionId == response.ActionId && done.ParentFrameId == useId &&
            Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == observerId && e.Completed) == 1 &&
            Sequence(game, e => e is RecoveryAppliedEvent { TargetSeat: 1 }) <
                Sequence(game, e => e is ProgramBindingStartedEvent { SkillId: HpObserver }) &&
            Sequence(game, e => e is ProgramBindingResolvedEvent resolved && resolved.FrameId == observerId) <
                Sequence(game, e => e is CardResponseCompletedEvent),
            "The exact cold-restored HP child resolves before the once-only native response completion offers formal Zhefu.");
        Private(game); game = Cold(game, registry); Answer(game, IsActivate);
        Reach(game, p => Demand(p, "target")); Answer(game, c => c.Targets.SequenceEqual([0]));
        Reach(game, p => Demand(p, "damage")); Answer(game, c => c.Parameters.GetValueOrDefault("step") == "damage");
        Play(game);
        var demand = Facts<SameNameHandStartedEvent>(game).Single();
        Require(demand.ActionId == response.ActionId && demand.OriginalParentFrameId == useId && demand.Source.OwnerSeat == 1 &&
            Sequence(game, e => e is CardResponseCompletedEvent) < Sequence(game, e => e is SameNameHandStartedEvent) &&
            Facts<SameNameHandCompletedEvent>(game).Count(e => e.FrameId == demand.FrameId && e.DamageIssued && !e.Discarded) == 1 &&
            Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
            Facts<CardResponseCompletedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<CardResponseCompletionStartedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == 1 && e.RecoveredAmount == 1) == 1 &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(1)) == 1 &&
            View(game, 1).Hp == 4 && View(game, 0).Hp == ownerHp - 1 &&
            !game.ResolutionStack.Any(f => f.Id == useId || f.Id == completion.Id || f.Id == hp.Id),
            "Formal Zhefu returns through the original native Slash once without repaying the armor or repeating its recovery.");
        _ = Cold(game, registry);
        VerifyOrdinaryHealthDyingReturn();
        VerifyCommittedSlashPaymentDyingReturn();
        VerifyFactionDefenseDodgeHealthDyingReturn();
    }

    private static bool IsActivate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool HasContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static bool Demand(PendingDecision p, string step) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "same-name-hand" && c.Parameters.GetValueOrDefault("step") == step);
    private static PlayerSnapshot View(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static long Sequence(GameEngine g, Func<IGameEvent, bool> match) => g.Events.Single(e => match(e.Payload)).Sequence;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real response-cost command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, int[] cards, int[] targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, Prompt(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var n = 0; n < 80; n++)
        {
            var p = Prompt(g); if (p is not null && stop(p)) return;
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                throw new InvalidOperationException("The expected response-cost child already returned to Play. " + JsonSerializer.Serialize(new {
                    Frames = g.ResolutionStack, Recent = g.Events.TakeLast(12).Select(e => new { Name = e.Payload.GetType().Name,
                        Value = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }), Commands = CommandJson.Serialize(g.AcceptedCommands.TakeLast(6)) }));
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed native response-cost boundary was not reached.");
    }
    private static void Frozen<T>(IReadOnlyList<T> items) => Require(items is System.Collections.IList { IsReadOnly: true }, "The exposed cost collection is frozen.");
    private static void FrozenEmptyNativeMaterials(IReadOnlyList<int> items)
    {
        Require(items.Count == 0 && items is System.Collections.IList { IsFixedSize: true },
            "The native zero-material list has fixed zero capacity.");
        var list = (System.Collections.IList)items;
        var addRejected = false;
        try { list.Add(-1); } catch (NotSupportedException) { addRejected = true; }
        var setRejected = false;
        try { list[0] = -1; }
        catch (NotSupportedException) { setRejected = true; }
        catch (ArgumentOutOfRangeException) { setRejected = true; }
        catch (IndexOutOfRangeException) { setRejected = true; }
        Require(addRejected && setRejected && items.Count == 0,
            "The native empty fixed list cannot gain a material or replace an element despite its array IsReadOnly flag.");
    }
    private static void Private(GameEngine g)
    {
        var p = Prompt(g)!; Require(p.IsPrivate, "The native health or formal optional choice is private.");
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[p.PlayerSeat].Hand.Count == 0,
                "Another viewer cannot receive the private choice or foreign hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats);
        var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot advance the response cursor, heal again or alter native payment.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all prepared views, private prompts, typed health invoices, native costs and event order."); return copy;
    }

    private sealed class Fixture(bool ordinaryHealthDying = false, bool losePaidSource = false, bool factionDefense = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:card-response-completion-cost", "1.0.0", "原生装备响应的回复子窗");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            b.AddSkill(new(Zhefu, formal.Presentations[Zhefu].Name, formal.Presentations[Zhefu].Description)
                { Program = formal.Programs[Zhefu], ProgramPresentation = formal.Presentations[Zhefu] });
            var healthDying = ordinaryHealthDying ? ",\"usageScope\":\"game\",\"usageLimit\":1" : "";
            var loss = ordinaryHealthDying ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}" : "";
            var optionalSelfReturn = losePaidSource ? "true" : "false";
            var factionRange = factionDefense ? "\"modifiers\":[{\"id\":\"native-lord-range\",\"query\":\"attackRange\",\"operation\":\"set\",\"value\":3,\"priority\":0}]," : "";
            var factionPolicy = factionDefense ? $$"""
              ,{"id":"{{FactionDefense}}","revision":1,"cardPolicies":[{"id":"native-assistance","kind":"factionResponseRequest","requiredCardKinds":["dodge"],"factionId":"wei","ownerRole":"lord"}]}
              """ : "";
            var factionWound = factionDefense ? ",{\"id\":\"wound-self\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}" : "";
            var dyingReturn = $$"""
              ,{"id":"{{HealthDyingReturn}}","revision":1,"triggers":[{"id":"native-health-return","window":"selfDyingResponse","subject":"owner","optional":{{optionalSelfReturn}},"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"dying-return","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":4}]}]}
              ,{"id":"{{HealthSourceReplacement}}","revision":1,"triggers":[{"id":"native-health-source-loss","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"source-loss","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Conversion}}"],"sourceBind":"{{Driver}}"}]}]}
              """;
            var presentation = new Dictionary<string, object> {
                [Driver] = new { name = "真实装备和伤害驱动", description = "合法实体放置及失去体力" },
                [Conversion] = new { name = "装备转闪", description = "仅将装备区白银狮子当闪响应" },
                [HpObserver] = new { name = "原生回复观察", description = "完成前结清真实回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [HealthDyingReturn] = new { name = "普通濒死返回", description = "真实失去体力的暂停及自救", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [HealthSourceReplacement] = new { name = "真实来源更替", description = "濒死进入时撤销已付费转换来源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (factionDefense) presentation[FactionDefense] = new { name = "真实主公护驾", description = "由实际魏势力角色提供原生闪" };
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,{{{factionRange}}}
                "viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],
                "activations":[
                  {"id":"equip-other","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"armor"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"armor"}]},
                  {"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}{{{factionWound}}}]},
              {"id":"{{{Conversion}}}","revision":1,"viewAs":[{"id":"equipment-dodge","inputKinds":["silverLion"],"inputSuits":[],"sourceZones":["equipment"],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
              {"id":"{{{HpObserver}}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false{{{healthDying}}},"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}{{{loss}}}]}]}{{{dyingReturn}}}{{{factionPolicy}}}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:response-completion-cost-first", "唯一首个AI候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:response-completion-cost-peer", "其他候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddGeneral(new("fixture:response-completion-cost-owner", "真实使用者", "supporter", Driver, factionDefense ? "wei" : "jin", 4,
                factionDefense ? [Conversion, HpObserver, HealthDyingReturn, Zhefu] : []));
            b.AddGeneral(new("fixture:response-completion-cost-peer-1", "真实装备响应者", "supporter", "fixture:response-completion-cost-first", "qun", 4,
                [Conversion, HpObserver, Zhefu, .. (ordinaryHealthDying ? new[] { HealthDyingReturn } : []),
                    .. (losePaidSource ? new[] { HealthSourceReplacement } : [])]));
            if (factionDefense)
            {
                b.AddSkill(new("fixture:response-completion-cost-lord-selection", "唯一真实主公", "原生角色权重")
                    { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -10000d) });
                b.AddGeneral(new("fixture:response-completion-cost-peer-2", "实际护驾主公", "supporter", "fixture:response-completion-cost-lord-selection", "wei", 4, [FactionDefense]));
                b.AddGeneral(new("fixture:response-completion-cost-peer-3", "其他存活角色", "supporter", "fixture:response-completion-cost-peer", "qun", 4));
            }
            else for (var i = 2; i < 4; i++) b.AddGeneral(new($"fixture:response-completion-cost-peer-{i}", "其他存活角色", "supporter", "fixture:response-completion-cost-peer", "qun", 4));
            b.AddCard(new("fixture:response-completion-cost-lion", "白银狮子", "装备牌", "失去装备区里的白银狮子后回复1点体力。",
                CardKind.SilverLion, AiTags: new Dictionary<string, string> { ["slot"] = "armor", ["on-loss"] = "recover-one" }));
            b.AddDeck(new("fixture:response-completion-cost-deck", "固定真实装备实体", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:response-completion-cost-lion", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小型原生响应成本", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:response-completion-cost-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:response-completion-cost-owner",
                    "fixture:response-completion-cost-peer-1", "fixture:response-completion-cost-peer-2", "fixture:response-completion-cost-peer-3"]));
        }
    }

    private sealed class CommittedSlashFixture(bool losePaidSource) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:committed-slash-payment-dying", "1.0.0", "真实出杀付款观察者的普通濒死返回");
        public void Register(IContentRegistryBuilder b)
        {
            var optionalSelfReturn = losePaidSource ? "true" : "false";
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{SlashPaymentConversion}}}","revision":1,
                "viewAs":[
                  {"id":"two-as-slash","inputKinds":[],"inputSuits":[],"inputCount":2,"outputKind":"slash","forPlay":true,"forResponse":true},
                  {"id":"top-cost","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"useOnly":true,"costDestination":"drawPileTop"}],
                "activations":[{"id":"two-as-slash","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                  "effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-as-slash","outputKind":"slash"}]}]},
              {"id":"{{{SlashPaymentObserver}}}","revision":1,"triggers":[{"id":"actual-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
                "movementOccurrence":"perOwnerBatch","optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"payment","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":4}]}]},
              {"id":"{{{SlashPaymentReturn}}}","revision":1,"triggers":[{"id":"actual-self-return","window":"selfDyingResponse","subject":"owner","optional":{{{optionalSelfReturn}}},"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"self-return","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":4}]}]},
              {"id":"{{{SlashPaymentHpObserver}}}","revision":1,"triggers":[{"id":"actual-self-recovery","window":"afterHpRecovered","subject":"owner","optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"health-return","options":[{"id":"continue"}]}]}]},
              {"id":"{{{SlashPaymentReplacement}}}","revision":1,"triggers":[{"id":"actual-source-loss","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"source-loss","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{{SlashPaymentConversion}}}"],"sourceBind":"{{{SlashPaymentQuiet}}}"}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [SlashPaymentConversion] = new { name = "真实出杀材料", description = "双材料与牌堆顶替代费用" },
                [SlashPaymentObserver] = new { name = "实际付款观察", description = "冻结费用批次后普通失去体力", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [SlashPaymentReturn] = new { name = "普通自救返回", description = "真实回复后继续已经支付的杀", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [SlashPaymentHpObserver] = new { name = "实际自救观察", description = "原生费用返回前结清回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [SlashPaymentReplacement] = new { name = "真实来源替换", description = "仅撤销已经付款的转换来源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(SlashPaymentQuiet, "其他存活角色", "无额外规则的真实候选"));
            b.AddGeneral(new("fixture:slash-payment-owner", "真实出杀者", "supporter", SlashPaymentObserver, "jin", 3,
                [SlashPaymentConversion, SlashPaymentReturn, SlashPaymentHpObserver, .. (losePaidSource ? new[] { SlashPaymentReplacement } : [])]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:slash-payment-peer-{i}", "其他存活角色", "supporter", SlashPaymentQuiet, "qun", 4));
            b.AddCard(new("fixture:slash-payment-card", "杀", "基本牌", "固定真实杀材料", CardKind.Slash));
            b.AddDeck(new("fixture:slash-payment-deck", "固定真实无响应牌堆", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:slash-payment-card", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(SlashPaymentMode, "小型真实出杀付款", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:slash-payment-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:slash-payment-owner",
                    "fixture:slash-payment-peer-1", "fixture:slash-payment-peer-2", "fixture:slash-payment-peer-3"]));
        }
    }

    private sealed class PublicPileFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:public-pile-native-cost", "1.0.0", "公开权牌费用与原生虚拟杀材料边界");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            const string yidu = "ol:yidu";
            b.AddSkill(new(yidu, formal.Presentations[yidu].Name, formal.Presentations[yidu].Description)
                { Program = formal.Programs[yidu], ProgramPresentation = formal.Presentations[yidu] });
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{PublicPile}}}","revision":1,
                "triggers":[{"id":"store-two","window":"gameStarting","subject":"owner","optional":false,"effects":[
                  {"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand"],"resultBind":"authority"},
                  {"op":"moveBoundCards","target":"owner","sourceBind":"authority","destination":"ownerPersistentZone","destinationZone":"authority"}]}],
                "cardPolicies":[{"id":"public-pile-slash","kind":"foreignPublicPileSlash"}]},
              {"id":"{{{PublicPileDamage}}}","revision":1,"triggers":[{"id":"actual-damage","window":"afterDamageApplied","subject":"owner",
                "damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [PublicPile] = new { name = "真实公开权牌", description = "开局真实存入两张权，其他角色移去两张权视为对持有者使用杀", authorityName = "权" },
                [PublicPileDamage] = new { name = "真实伤害观察", description = "等待原生伤害返回", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:public-pile-native-cost-first", "唯一公开权牌候选", "真实选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:public-pile-native-cost-quiet", "其他存活角色", "真实选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddGeneral(new("fixture:public-pile-native-cost-actor", "真实外人", "supporter", "fixture:public-pile-native-cost-quiet", "jin", 4));
            b.AddGeneral(new("fixture:public-pile-native-cost-owner", "真实权牌持有者", "supporter", "fixture:public-pile-native-cost-first", "qun", 4,
                [PublicPile, PublicPileDamage]));
            for (var i = 2; i < 4; i++) b.AddGeneral(new($"fixture:public-pile-native-cost-peer-{i}", "其他存活角色", "supporter", "fixture:public-pile-native-cost-quiet", "qun", 4));
            b.AddCard(new("fixture:public-pile-native-cost-slash", "杀", "基本牌", "真实杀实体", CardKind.Slash));
            b.AddDeck(new("fixture:public-pile-native-cost-deck", "固定真实无闪牌堆", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:public-pile-native-cost-slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(PublicPileMode, "公开权牌成本小模式", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:public-pile-native-cost-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:public-pile-native-cost-actor",
                    "fixture:public-pile-native-cost-owner", "fixture:public-pile-native-cost-peer-2", "fixture:public-pile-native-cost-peer-3"]));
        }
    }
}
