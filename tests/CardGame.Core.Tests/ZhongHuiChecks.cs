using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhongHuiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:zhong-hui";
    private const string QuanjiSkillId = "classic:quanji";
    private const string ZiliSkillId = "classic:zili";
    private const string PaiyiSkillId = "classic:paiyi";

    public static void ContentQuanjiAndBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var migrated = current;
        var general = current.Generals[GeneralId];
        Require(general.FactionId == "wei" && general.BaseHp == 4 &&
                general.Gender == GeneralGender.Male && general.PortraitKey == "zhong_hui" &&
                general.SkillIds.SequenceEqual([QuanjiSkillId, ZiliSkillId]),
            "Current Zhong Hui must publish his complete general identity.");
        var quanji = current.Skills[QuanjiSkillId].Program;
        var zili = current.Skills[ZiliSkillId].Program;
        var paiyi = current.Skills[PaiyiSkillId].Program;
        Require(quanji is { RuntimeVersion: "skill-program-v59", MinimumRulesVersion: 169,
                            Triggers.Count: 1, Modifiers.Count: 1 } &&
                quanji.Triggers.Single().Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.Draw,
                    SkillProgramEffectOp.SelectSourceCard,
                    SkillProgramEffectOp.MoveBoundCards
                ]) &&
                zili is { RuntimeVersion: "skill-program-v59", MinimumRulesVersion: 169,
                           Triggers.Count: 2 } &&
                zili.Triggers.All(trigger => trigger.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                    trigger.ChoiceGroup == "awakening-benefit" && !trigger.Optional &&
                    trigger.UsageScope == SkillUsageScope.Game && trigger.UsageLimit == 1) &&
                paiyi is { RuntimeVersion: "skill-program-v59", MinimumRulesVersion: 169,
                            Activations.Count: 1 } &&
                paiyi.Activations.Single().SourceZones.SequenceEqual([CardZoneKind.Authority]) &&
                paiyi.Activations.Single().Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.DiscardSelected,
                    SkillProgramEffectOp.Draw,
                    SkillProgramEffectOp.Damage
                ]),
            "Current Quanji, Zili and Paiyi must expose their authority-zone programs.");
        var paiyiProgram = migrated.Skills[PaiyiSkillId].Program!.Activations.Single();
        var activeRules = ReadResource(typeof(StandardClassicGeneralPackage).Assembly,
            "CardGame.Content.Standard.SkillPrograms.active-persistent-zone-skills.rules.json");
        var activePresentation = ReadResource(typeof(StandardClassicGeneralPackage).Assembly,
            "CardGame.Content.Standard.SkillPrograms.active-persistent-zone-skills.presentation.json");
        RequireLoadFailure(activeRules.Replace("\"sourceZones\": [\"authority\"]",
                "\"sourceZones\": [\"discardPile\"]"),
            activePresentation, "Activation costs must reject non-owner source zones.");
        var damageEffect = paiyiProgram.Effects.Single(effect => effect.Op == SkillProgramEffectOp.Damage);
        Require(damageEffect.Target == SkillProgramEffectTarget.SelectedTarget,
            "Paiyi must damage its selected recipient when the post-draw condition holds.");
        var damageCondition = damageEffect.Condition;
        var ownerContext = new PlayerSkillContext(0, 3, 4, 3, TurnPhase.Play, IsOwnTurn: true);
        Require(damageCondition.Evaluate(ownerContext,
                    new PlayerSkillContext(1, 4, 4, 4, TurnPhase.Play), UnexpectedFrameLookup, UnexpectedFrameLookup) &&
                !damageCondition.Evaluate(ownerContext,
                    new PlayerSkillContext(0, 3, 4, 5, TurnPhase.Play), UnexpectedFrameLookup, UnexpectedFrameLookup) &&
                !damageCondition.Evaluate(ownerContext,
                    new PlayerSkillContext(1, 4, 4, 3, TurnPhase.Play), UnexpectedFrameLookup, UnexpectedFrameLookup),
            "Paiyi's reusable selected-target conditions must compare post-draw state and exclude self.");

        static bool UnexpectedFrameLookup(string _) =>
            throw new InvalidOperationException("Paiyi target conditions must not read a Pindian or state binding.");

        var fixture = FindFixture();
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, fixture.Seed);
        ResolveSelfFireAttack(game, expectQuanji: true, testForgery: true);
        var first = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(first.AuthorityCount == 1 && first.AuthorityCards?.Count == 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == QuanjiSkillId && item.BindingId == "store-authority" && item.Completed),
            "One point of damage must offer one Quanji draw-and-store result in the public Authority zone.");

        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        var snapshot = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(snapshot.Players[HumanSeat] is
                {
                    AuthorityCount: 3,
                    AuthorityCards.Count: 3
                } owner &&
                GetHandLimit(game, HumanSeat) == owner.Hp + 3 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Count(item =>
                    item.SkillId == QuanjiSkillId && item.BindingId == "store-authority" && item.Completed) == 3,
            "Three independent damage points must create three public Authorities and add exactly three to hand limit.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(snapshot),
            "The completed three-Authority command prefix must replay exactly.");

    }

    public static void ZiliAndPaiyiReplay()
    {
        var fixture = FindFixture();
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, fixture.Seed);
        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        var beforeAwakening = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];

        var play = RequirePrompt(game, DecisionKind.PlayCard);
        Require(game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId)).Accepted,
            "The Zhong Hui fixture could not end its play phase.");
        ReachPrompt(game, DecisionKind.ProgramTrigger, 2_048);
        var zili = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(zili.IsPrivate &&
                zili.SkillPrompt?.SkillId == ZiliSkillId &&
                zili.Choices.Select(choice => choice.Parameters.GetValueOrDefault("binding-id"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["draw", "recover"]) &&
                zili.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].MaxHp ==
                    beforeAwakening.MaxHp,
            "Zili must publish one mandatory generic benefit choice at the next preparation phase before losing maximum HP.");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(pausedReplay.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: HumanSeat,
                    IsPrivate: true,
                    SkillPrompt.SkillId: ZiliSkillId
                } &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused generic Zili awakening choice must replay exactly.");

        var drawBranch = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerProgramBranch(drawBranch, "draw");
        ReachPrompt(drawBranch, DecisionKind.PlayCard, 512);
        var drawnOwner = drawBranch.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(drawBranch.CardMovements.Count(move =>
                    move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:zili.Draw") == 2 &&
                drawnOwner.MaxHp == beforeAwakening.MaxHp - 1 &&
                drawBranch.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>().Last() is
                { SkillId: ZiliSkillId, AcquiredSkillIds.Count: 1 } drawAwakening &&
                drawAwakening.AcquiredSkillIds.SequenceEqual([PaiyiSkillId]) &&
                drawBranch.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == ZiliSkillId && item.BindingId == "draw" && item.Activated && item.Completed),
            "Zili's generic draw branch must draw exactly two before losing one maximum HP and acquiring Paiyi.");

        var recoveryHp = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hp;
        AnswerProgramBranch(game, "recover");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var awakened = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(awakened.Skills?.Any(skill => skill.ContentId == PaiyiSkillId &&
                    skill.Kind == SkillKind.None && skill.ActionForms == SkillActionForm.Active) == true &&
                awakened.SkillRuntimeStates?.Single(state => state.SkillId == PaiyiSkillId).IsAcquired == true &&
                awakened.Hp == Math.Min(recoveryHp + 1, awakened.MaxHp) &&
                awakened.MaxHp == beforeAwakening.MaxHp - 1 &&
                game.Events.Select(item => item.Payload).OfType<MaximumHpChangedEvent>().Any(item =>
                    item.PlayerSeat == HumanSeat && item.SkillId == ZiliSkillId && item.Delta == -1) &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.SkillId == ZiliSkillId && item.BindingId == "recover" && item.Activated && item.Completed),
            "Zili's generic recovery branch must recover one before losing one maximum HP and expose dynamically acquired active Paiyi.");

        var beforePaiyi = RoundTrip(game.CreateCheckpoint());
        var selfBranch = GameReplay.Restore(beforePaiyi, registry);
        UsePaiyi(selfBranch, HumanSeat);
        Require(selfBranch.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.OwnerSeat == HumanSeat && item.SkillId == PaiyiSkillId &&
                    item.ActivationId == "remove-authority" && item.Completed) &&
                selfBranch.CardMovements.Count(move =>
                    move.To == CardLocation.Hand(HumanSeat) &&
                    move.Reason.Value == "skill-program.classic:paiyi.Draw") == 2 &&
                selfBranch.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCount == 2,
            "Paiyi may target its owner, draws two, removes one Authority and cannot damage the same player.");

        var lethalBranch = GameReplay.Restore(beforePaiyi, registry);
        var lethalTargetSeat = lethalBranch.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Where(player => player.IsAlive && player.Seat != HumanSeat)
            .OrderByDescending(player => player.HandCount)
            .First().Seat;
        SetPlayerHp(lethalBranch, lethalTargetSeat, 1);
        SubmitPaiyi(lethalBranch, lethalTargetSeat);
        for (var step = 0; step < 128 && !lethalBranch.Events.Select(item => item.Payload)
                 .OfType<ProgramSkillResolvedEvent>().Any(item =>
                     item.SkillId == PaiyiSkillId && item.ActivationId == "remove-authority"); step++)
        {
            CommandResult continued;
            if (lethalBranch.PendingDecision is { PlayerSeat: HumanSeat, Kind: DecisionKind.RescueDying } dying)
            {
                var decline = dying.Choices.Single(choice => choice.Cards.Count == 0);
                continued = lethalBranch.Submit(new AnswerPromptCommand(
                    HumanSeat, dying.PromptId, decline.Id, lethalBranch.Revision));
            }
            else
            {
                continued = lethalBranch.Submit(new AdvanceOneStepCommand(lethalBranch.Revision));
            }
            Require(continued.Accepted, continued.Error?.Message ?? "Lethal Paiyi could not resume its program parent.");
        }
        Require(!lethalBranch.CreateSnapshot(HumanSeat, revealAll: true).Players[lethalTargetSeat].IsAlive &&
                lethalBranch.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.SkillId == PaiyiSkillId && item.ActivationId == "remove-authority" && item.Completed),
            "Lethal active-program damage must finish death handling and then complete its program frame once.");

        var targetSeat = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Where(player => player.IsAlive && player.Seat != HumanSeat)
            .OrderByDescending(player => player.HandCount)
            .First().Seat;
        var targetHp = game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat].Hp;
        UsePaiyi(game, targetSeat);
        Require(game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat].Hp == targetHp - 1 &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCount == 2 &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.OwnerSeat == HumanSeat && item.SkillId == PaiyiSkillId &&
                    item.ActivationId == "remove-authority" && item.Completed) &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == HumanSeat && item.TargetSeat == targetSeat && item.Amount == 1),
            "Paiyi must draw first, compare current hands, then route its conditional damage through the normal damage chain.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(item =>
                    item.SkillId == PaiyiSkillId && item.ActivationId == "remove-authority" && item.Completed),
            "The completed Zili-to-Paiyi chain must replay exactly.");
    }

    public static void ZiliProgramValidationAndCurrentConditions()
    {
        const string rulesResource =
            "CardGame.Content.Standard.SkillPrograms.state-awakening-skills.rules.json";
        const string presentationResource =
            "CardGame.Content.Standard.SkillPrograms.state-awakening-skills.presentation.json";
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        var rules = ReadResource(assembly, rulesResource);
        var presentation = ReadResource(assembly, presentationResource);
        var catalog = SkillProgramCatalog.Load(rules, presentation);
        var triggers = catalog.Programs[ZiliSkillId].Triggers.ToDictionary(item => item.Id);
        var twoAuthorities = new SkillProgramTriggerFacts(
            0, 2, true, CurrentMaxHp: 4,
            OwnedZoneCounts: new SkillProgramOwnedZoneCounts(0, 0, 2, 0));
        var woundedThreeAuthorities = twoAuthorities with
        {
            OwnedZoneCounts = new SkillProgramOwnedZoneCounts(0, 0, 3, 0)
        };
        var fullThreeAuthorities = woundedThreeAuthorities with { CurrentHp = 4 };
        Require(!triggers["recover"].Condition.Evaluate(twoAuthorities) &&
                !triggers["draw"].Condition.Evaluate(twoAuthorities) &&
                triggers["recover"].Condition.Evaluate(woundedThreeAuthorities) &&
                triggers["draw"].Condition.Evaluate(woundedThreeAuthorities) &&
                !triggers["recover"].Condition.Evaluate(fullThreeAuthorities) &&
                triggers["draw"].Condition.Evaluate(fullThreeAuthorities),
            "The generic owned-zone condition must require three Authorities and omit only the illegal recovery branch at full HP.");
        RequireLoadFailure(
            rules.Replace("\"zone\": \"authority\"", "\"zone\": \"hand\"", StringComparison.Ordinal),
            presentation,
            "currentOwnedZoneCount must reject ordinary hand zones");

    }

    private static void ResolveSelfFireAttack(GameEngine game, bool expectQuanji, bool testForgery = false)
    {
        var entryPrompt = game.PendingDecision;
        var entryHand = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand;
        var entryActions = game.GetHumanLegalActions();
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var legalActions = game.GetHumanLegalActions();
        var action = legalActions.FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.FireAttack && candidate.TargetSeat == HumanSeat) ??
            throw new InvalidOperationException(
                "The Zhong Hui fixture has no self Fire Attack action; " +
                $"entryPrompt={entryPrompt?.Kind}/{entryPrompt?.PlayerSeat}, " +
                $"entryHand=[{string.Join(',', entryHand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
                $"entryActions=[{string.Join(',', entryActions.Select(candidate => $"{candidate.Kind}:{candidate.CardId}:{candidate.TargetSeat}"))}], " +
                $"hand=[{string.Join(',', game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
                $"actions=[{string.Join(',', legalActions.Select(candidate => $"{candidate.Kind}:{candidate.CardId}:{candidate.TargetSeat}"))}], " +
                $"authorities=[{string.Join(',', game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCards?.Select(card => $"{card.Id}:{card.Kind}") ?? [])}], " +
                $"fireEvents={game.Events.Select(item => item.Payload).OfType<FireAttackResolvedEvent>().Count()}, " +
                $"quanjiBindings={game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Count(item => item.SkillId == QuanjiSkillId)}, " +
                $"movements=[{string.Join(',', game.CardMovements.TakeLast(20).Select(move => $"{move.CardId}:{move.From}>{move.To}:{move.Reason}"))}], " +
                $"rules={game.CreateCheckpoint().RulesVersion}.");
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var played = game.Submit(new PlayCardCommand(
            HumanSeat,
            action.CardId!.Value,
            [HumanSeat],
            game.Revision,
            play.PromptId));
        Require(played.Accepted, played.Error?.Message ?? "The self Fire Attack was rejected.");
        AnswerCard(game, DecisionKind.FireAttackReveal, CardKind.Dodge);
        AnswerCard(game, DecisionKind.FireAttackDiscard, CardKind.Dodge);

        if (!expectQuanji)
        {
            ReachPrompt(game, DecisionKind.PlayCard, 512);
            return;
        }

        var quanji = RequirePrompt(game, DecisionKind.ProgramTrigger);
        Require(quanji.IsPrivate &&
                quanji.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]),
            "Each damage point must publish one private Quanji use/skip choice.");
        if (testForgery)
        {
            var before = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
            var revision = game.Revision;
            var forged = game.Submit(new AnswerPromptCommand(
                HumanSeat,
                quanji.PromptId,
                new ChoiceId("quanji.forged"),
                revision));
            Require(!forged.Accepted && game.Revision == revision &&
                    SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == before,
                "A forged Quanji answer must be rejected atomically.");
        }

        Answer(game, quanji.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var store = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var dodgeIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == CardKind.Dodge)
            .Select(card => card.Id)
            .ToHashSet();
        var cardChoice = store.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-source-card" &&
            int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot) &&
            slot >= 0 && slot < game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Count &&
            dodgeIds.Contains(game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand[slot].Id));
        Answer(game, cardChoice);
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static void UsePaiyi(GameEngine game, int targetSeat)
    {
        SubmitPaiyi(game, targetSeat);
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static void SubmitPaiyi(GameEngine game, int targetSeat)
    {
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill && candidate.ProgramSkillId == PaiyiSkillId);
        Require(action.SelectableCardIds.Count > 0 && action.SelectableTargetSeats.Contains(targetSeat),
            "Paiyi did not publish its Authority card or requested living target.");
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var result = game.Submit(new UseProgramSkillCommand(
            HumanSeat,
            action.ProgramSkillId!,
            action.ProgramActivationId!,
            [action.SelectableCardIds[0]],
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Paiyi was rejected.");
    }

    private static void AnswerCard(GameEngine game, DecisionKind kind, CardKind cardKind)
    {
        var prompt = RequirePrompt(game, kind);
        var matchingIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == cardKind)
            .Select(card => card.Id)
            .ToHashSet();
        var choice = prompt.Choices.First(candidate =>
            candidate.Cards.Count == 1 && matchingIds.Contains(candidate.Cards[0]));
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, DecisionKind kind, string action)
    {
        var prompt = RequirePrompt(game, kind);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("action") == action);
        Answer(game, choice);
    }

    private static void AnswerProgramBranch(GameEngine game, string bindingId)
    {
        var prompt = RequirePrompt(game, DecisionKind.ProgramTrigger);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("program-action") == "activate" &&
            candidate.Parameters.GetValueOrDefault("skill-id") == ZiliSkillId &&
            candidate.Parameters.GetValueOrDefault("binding-id") == bindingId);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("There is no Zhong Hui prompt.");
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Zhong Hui prompt answer was rejected.");
    }

    private static void ReachPrompt(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
            {
                if (prompt.Kind == kind) return;
                throw new InvalidOperationException($"Unexpected human prompt {prompt.Kind} before {kind}.");
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? $"Could not advance to {kind}.");
        }
        throw new InvalidOperationException($"The fixture did not reach {kind} in bounded steps.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static int GetHandLimit(GameEngine game, int seat)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var method = typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand-limit query was not found.");
        return (int)method.Invoke(game, [players[seat]])!;
    }

    private static void SetPlayerHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (IReadOnlyList<CharacterState>)playersField.GetValue(game)!;
        players[seat].Hp = hp;
    }

    private static Fixture FindFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            var human = game.CreateSnapshot(HumanSeat, revealAll: true).Players
                .Single(player => player.Seat == HumanSeat);
            var fireAttacks = human.Hand.Count(card => card.Kind == CardKind.FireAttack);
            var selfFireAttacks = game.GetHumanLegalActions().Count(action =>
                action.Kind == LegalActionKind.FireAttack && action.TargetSeat == HumanSeat);
            if (fireAttacks == 3 && selfFireAttacks == 3)
            {
                return new Fixture(seed);
            }
        }
        throw new InvalidOperationException("No bounded Zhong Hui fixture dealt all three Fire Attacks to the human seat.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Zhong Hui fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Zhong Hui fixture omitted its formal general.");
        Require(game.Submit(new SelectGeneralCommand(
                HumanSeat,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Zhong Hui fixture could not select its formal general.");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static GameEngine CreateFixtureGame(ContentRegistry registry, int seed)
    {
        var game = CreateGame(registry, seed);
        StartAndSelect(game);
        var before = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat);
        var actions = game.GetHumanLegalActions();
        var after = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat);
        Require(before.Hand.Count(card => card.Kind == CardKind.FireAttack) == 3 &&
                actions.Count(action =>
                    action.Kind == LegalActionKind.FireAttack && action.TargetSeat == HumanSeat) == 3 &&
                after.Hand.Count(card => card.Kind == CardKind.FireAttack) == 3,
            $"Seed {seed} did not reproduce the bounded Zhong Hui deal; " +
            $"before=[{string.Join(',', before.Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
            $"after=[{string.Join(',', after.Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
            $"movements=[{string.Join(',', game.CardMovements.TakeLast(16).Select(move => $"{move.CardId}:{move.From}>{move.To}:{move.Reason}"))}].");
        return game;
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing test resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static void RequireLoadFailure(string rules, string presentation, string message)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhong-hui-test-4";
        private const string DeckId = "fixture:zhong-hui-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:zhong-hui-a",
            "fixture:zhong-hui-b",
            "fixture:zhong-hui-c"
        ];

        public PackageManifest Manifest { get; } = new(
            "zhong-hui-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 86, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "排异测试目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            var physicalCards = new List<ContentDeckPhysicalCard>();
            for (var index = 0; index < 3; index++)
            {
                physicalCards.Add(new ContentDeckPhysicalCard("standard:fire_attack", Suit.Spade, index + 1));
            }
            for (var index = 3; index < 160; index++)
            {
                physicalCards.Add(new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "钟会权计自立排异测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = physicalCards.AsReadOnly()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "钟会技能链测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
