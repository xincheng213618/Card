using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZuoCiChecks
{
    private const string General = "classic:zuo-ci";
    private const string HuaShen = "classic:huashen";
    private const string XinSheng = "classic:xinsheng";
    private const string Mode = "identity:zuo-ci-check-5";
    private const string FactionMode = "identity:zuo-ci-faction-check-4";
    private const string AvatarFemale = "fixture:zuo-ci-avatar-f";
    private const string AvatarMale = "fixture:zuo-ci-avatar-m";
    private const string AvatarLordSkill = "fixture:zuo-ci-avatar-lord";
    private const string AvatarQunA = "fixture:zuo-ci-avatar-qun-a";
    private const string AvatarQunB = "fixture:zuo-ci-avatar-qun-b";
    private const string AvatarWeiA = "fixture:zuo-ci-avatar-wei-a";
    private const string AvatarWeiB = "fixture:zuo-ci-avatar-wei-b";
    private const string HuangtianLord = "fixture:zuo-ci-huangtian-lord";
    private const string Mashu = "classic:mashu";
    private const string Hujia = "classic:hujia";
    private const string Qingnang = "classic:qingnang";
    private const string RoulinOwner = "classic:dong-zhuo";
    private const string Huangtian = "classic:huangtian";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun", Gender: GeneralGender.Male }
                general && general.SkillIds.SequenceEqual([HuaShen, XinSheng]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "左慈 must be a three-HP qun general in the current identity pools.");

        var huaShen = current.Skills[HuaShen].Program!;
        Require(huaShen.Triggers.Select(trigger => trigger.Window).SequenceEqual([
                SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
            SkillProgramTriggerWindow.TurnEnding]),
            "Huashen must offer the avatar change at the own turn start and end windows.");
        foreach (var trigger in huaShen.Triggers)
        {
            Require(trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Optional &&
                    trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                    trigger.Effects.Select(effect => effect.Op).SequenceEqual([
                        SkillProgramEffectOp.HuaShenChangeAvatar]),
                "Each Huashen window must be one optional always-on avatar change.");
            var change = trigger.Effects.Single();
            Require(change.Target == SkillProgramEffectTarget.Owner &&
                    change.DeclaredSkillTags.SequenceEqual([
                        SkillTag.Limited,
                        SkillTag.Awakening,
                        SkillTag.Lord]),
                "The avatar change must carry the BWIKI declared-skill exclusion list.");
        }

        var xinSheng = current.Skills[XinSheng].Program!;
        var replenish = xinSheng.Triggers.Single();
        Require(replenish.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                replenish.Subject == SkillProgramTriggerSubject.Owner &&
                replenish.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint &&
                replenish.Optional &&
                replenish.Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.HuaShenXinSheng]) &&
                replenish.Effects.Single().Target == SkillProgramEffectTarget.Owner,
            "Xinsheng must replenish the avatar pile once per damage point as an optional owner trigger.");

        const string changeTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:huashen","revision":1,
            "minimumRulesVersion": 192,
            "triggers":[{"id":"change","window":"turnEnding","subject":"owner","optional":true,
            "effects":[{"op":"huaShenChangeAvatar","target":"owner",
            "declaredSkillTags":["limited","awakening","lord"]}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:huashen":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(changeTemplate, presentation)
                .Programs["fixture:huashen"].Triggers.Single().Effects.Single().Op ==
            SkillProgramEffectOp.HuaShenChangeAvatar,
            "The avatar change must be independently definable.");
        Reject(changeTemplate.Replace("\"declaredSkillTags\":[\"limited\",\"awakening\",\"lord\"]",
                "\"declaredSkillTags\":[\"limited\",\"locked\"]"),
            presentation, "an unsupported declared-skill exclusion tag");
        Reject(changeTemplate.Replace(
                "\"op\":\"huaShenChangeAvatar\",\"target\":\"owner\"",
                "\"op\":\"huaShenChangeAvatar\",\"target\":\"owner\",\"condition\":{\"kind\":\"wounded\"}"),
            presentation, "a conditional avatar change");
        Reject(changeTemplate.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            presentation, "a non-owner avatar change");
        Reject(changeTemplate.Replace(
            "\"op\":\"huaShenChangeAvatar\"", "\"op\":\"huaShenXinSheng\""),
            presentation, "a xinsheng effect carrying declared-skill tags");
    }

    public static void SetupDeclarationIsSeedReproducible()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (game is null) continue;
            var prompt = game.PendingDecision;
            if (prompt is not { Kind: DecisionKind.HuaShen, PlayerSeat: 0 }) continue;
            var pile = game.GetHuaShenAvatarGeneralIds(0);
            if (!pile.Contains(AvatarFemale)) continue;
            Require(pile.Count == 2 && game.GetHuaShenRevealedAvatarGeneralId(0) is null,
                "The setup must draw exactly two unrevealed avatar cards.");

            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                "The avatar reveal prompt must pause at an identical checkpoint.");

            AnswerHuaShen(game, AvatarFemale);
            AnswerHuaShen(paused, AvatarFemale);
            Require(game.PendingDecision is { Kind: DecisionKind.HuaShen, PlayerSeat: 0 },
                "Revealing an avatar must open the declared-skill prompt.");

            var pausedAtSkill = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(State(game) == State(pausedAtSkill) && Events(game).SequenceEqual(Events(pausedAtSkill)),
                "The declared-skill prompt must pause at an identical checkpoint.");

            AnswerHuaShen(game, Mashu);
            AnswerHuaShen(pausedAtSkill, Mashu);
            DriveUntilSettled(game);
            DriveUntilSettled(pausedAtSkill);
            Require(State(game) == State(pausedAtSkill) && Events(game).SequenceEqual(Events(pausedAtSkill)),
                "The initial declaration must replay identically after settlement.");

            Require(game.GetHuaShenRevealedAvatarGeneralId(0) == AvatarFemale &&
                    game.GetHuaShenDeclaredSkillId(0) == Mashu &&
                    game.GetHuaShenAvatarGeneralIds(0).Count == 2,
                "The revealed avatar must carry the declared skill.");
            var revealed = game.Events.Select(item => item.Payload).OfType<HuaShenAvatarRevealedEvent>()
                .Single(item => item.OwnerSeat == 0);
            Require(revealed.GeneralId == AvatarFemale && revealed.DeclaredSkillId == Mashu,
                "The public reveal event must name the revealed avatar and skill.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced an avatar declaration window.");
    }

    public static void DeclarationExcludesLordSkills()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (game is null) continue;
            var prompt = game.PendingDecision;
            if (prompt is not { Kind: DecisionKind.HuaShen, PlayerSeat: 0 }) continue;
            if (!game.GetHuaShenAvatarGeneralIds(0).Contains(AvatarLordSkill)) continue;

            AnswerHuaShen(game, AvatarLordSkill);
            var skillPrompt = game.PendingDecision;
            Require(skillPrompt is { Kind: DecisionKind.HuaShen, PlayerSeat: 0 },
                "Revealing an avatar must open the declared-skill prompt.");
            var declared = skillPrompt!.Choices
                .Select(choice => choice.Parameters.GetValueOrDefault("skill-id"))
                .Where(id => id is not null).ToArray();
            Require(declared.SequenceEqual([Mashu]),
                "The lord-tagged 护驾 must be excluded from the avatar declaration choices.");
            AnswerHuaShen(game, Mashu);
            DriveUntilSettled(game);
            Require(game.GetHuaShenDeclaredSkillId(0) == Mashu,
                "The declaration must settle on the only eligible skill.");
            completed++;
        }
        Require(completed == 1, "No seeded setup offered the lord-skill avatar.");
    }

    public static void GenderTreatedAsRevealedAvatar()
    {
        var registry = Registry();
        int? femaleRequired = null;
        int? maleRequired = null;
        for (var seed = 1; seed <= 1600 && (femaleRequired is null || maleRequired is null); seed++)
        {
            var wanted = femaleRequired is null ? AvatarFemale : AvatarMale;
            var required = TryObserveRoulinRequiredDodges(registry, seed, wanted);
            if (required is null) continue;
            if (wanted == AvatarFemale) femaleRequired = required;
            else maleRequired = required;
        }
        Require(femaleRequired == 2 && maleRequired == 1,
            "肉林 must read the revealed avatar's gender: a female-effective 左慈 demands two dodges from 董卓 while a male-effective one demands a single dodge.");
    }

    public static void FactionTreatedAsRevealedAvatar()
    {
        var registry = FactionRegistry();
        var qunObserved = 0;
        var otherObserved = 0;
        for (var seed = 1; seed <= 800 && (qunObserved < 1 || otherObserved < 1); seed++)
        {
            var observation = TryObserveHuangtianContribution(registry, seed);
            if (observation is not { } found) continue;
            var expectQun = registry.Generals[found.AvatarId].FactionId == "qun";
            Require(found.Offered == expectQun,
                "黄天 must follow the effective faction of the revealed avatar: only a qun-effective 左慈 may contribute.");
            if (expectQun) qunObserved++;
            else otherObserved++;
        }
        Require(qunObserved >= 1 && otherObserved >= 1,
            "No seeded fixture produced comparable huangtian observations.");
    }

    public static void XinShengGainsAvatarOnDamageAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (game is null) continue;
            if (game.PendingDecision is { Kind: DecisionKind.HuaShen, PlayerSeat: 0 })
            {
                AnswerHuaShen(game, game.GetHuaShenAvatarGeneralIds(0).Order().First());
                AnswerHuaShen(game, Mashu);
            }
            if (!TryPlayDuelAndConcede(game)) continue;
            DriveUntil(game, () => IsProgramTriggerPrompt(game, XinSheng), stopAtSkills: [XinSheng]);
            if (!IsProgramTriggerPrompt(game, XinSheng)) continue;

            var pileBefore = game.GetHuaShenAvatarGeneralIds(0).Count;
            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                "The Xinsheng window must pause at an identical checkpoint.");

            Activate(game);
            Activate(paused);
            DriveUntilSettled(game);
            DriveUntilSettled(paused);
            Require(State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                "The Xinsheng replenishment must replay identically from the paused window.");

            var gain = game.Events.Select(item => item.Payload).OfType<HuaShenAvatarGainedEvent>()
                .Single(item => item.OwnerSeat == 0 && item.SkillId == XinSheng);
            Require(game.GetHuaShenAvatarGeneralIds(0).Count == pileBefore + 1 &&
                    gain.PileCount == pileBefore + 1,
                "Xinsheng must add exactly one out-of-game general card to the avatar pile.");
            Require(game.CreateCardZoneDiagnostics().Count ==
                    paused.CreateCardZoneDiagnostics().Count,
                "The avatar pile must not move physical game cards.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Xinsheng window.");
    }

    public static void ChangeAvatarAtTurnBoundaries()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (game is null) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.HuaShen, PlayerSeat: 0 }) continue;
            var pile = game.GetHuaShenAvatarGeneralIds(0);
            var first = pile.Order().First();
            var second = pile.Order().Last();
            AnswerHuaShen(game, first);
            AnswerHuaShen(game, Mashu);
            DriveUntilSettled(game);

            DriveUntil(game, () => IsProgramTriggerPrompt(game, HuaShen), stopAtSkills: [HuaShen]);
            if (!IsProgramTriggerPrompt(game, HuaShen)) continue;
            var pausedAtTrigger = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(State(game) == State(pausedAtTrigger) && Events(game).SequenceEqual(Events(pausedAtTrigger)),
                "The turn-start avatar window must pause at an identical checkpoint.");

            Activate(game);
            Activate(pausedAtTrigger);
            Require(game.PendingDecision is { Kind: DecisionKind.HuaShen, PlayerSeat: 0 },
                "An activated avatar change must open the reveal prompt.");
            var alternatives = game.PendingDecision!.Choices
                .Select(choice => choice.Parameters.GetValueOrDefault("general-id"))
                .Where(id => id is not null).ToArray();
            Require(alternatives.SequenceEqual([second]),
                "The change prompt must offer only the unrevealed avatar cards.");

            AnswerHuaShen(game, second);
            AnswerHuaShen(pausedAtTrigger, second);
            AnswerHuaShen(game, Mashu);
            AnswerHuaShen(pausedAtTrigger, Mashu);
            Require(game.GetHuaShenRevealedAvatarGeneralId(0) == second &&
                    game.GetHuaShenDeclaredSkillId(0) == Mashu,
                "The avatar change must move the reveal and declaration to the new card.");

            DriveUntil(game, () => game.PendingDecision is
            { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                stopAtSkills: null);
            Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The fixture did not reach the human play phase after the avatar change.");
            EndPlayPhase(game);
            DriveUntil(game, () => IsProgramTriggerPrompt(game, HuaShen), stopAtSkills: [HuaShen]);
            if (IsProgramTriggerPrompt(game, HuaShen))
            {
                var revealedBefore = game.GetHuaShenRevealedAvatarGeneralId(0);
                var pausedAtEnd = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Require(State(game) == State(pausedAtEnd) && Events(game).SequenceEqual(Events(pausedAtEnd)),
                    "The turn-end avatar window must pause at an identical checkpoint.");
                SkipProgramTrigger(game);
                SkipProgramTrigger(pausedAtEnd);
                DriveUntilSettled(game);
                DriveUntilSettled(pausedAtEnd);
                Require(State(game) == State(pausedAtEnd) && Events(game).SequenceEqual(Events(pausedAtEnd)),
                    "Skipping the turn-end change must replay identically.");
                Require(game.GetHuaShenRevealedAvatarGeneralId(0) == revealedBefore,
                    "Skipping the change must keep the revealed avatar.");
            }
            completed++;
        }
        Require(completed == 1, "No seeded setup produced an avatar change window.");
    }

    private static int? TryObserveRoulinRequiredDodges(ContentRegistry registry, int seed, string avatar)
    {
        var game = Start(registry, seed);
        if (game is null) return null;
        if (game.PendingDecision is not { Kind: DecisionKind.HuaShen, PlayerSeat: 0 }) return null;
        if (!game.GetHuaShenAvatarGeneralIds(0).Contains(avatar)) return null;
        AnswerHuaShen(game, avatar);
        AnswerHuaShen(game, Mashu);
        if (!DriveUntil(game, () => game.PendingDecision is
            { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })) return null;

        var players = game.CreateSnapshot(0, revealAll: true).Players;
        var dongZhuoSeat = players.Where(item => item.GeneralId == RoulinOwner && item.IsAlive)
            .Select(item => (int?)item.Seat).SingleOrDefault();
        if (dongZhuoSeat is not { } seat || seat == 0) return null;
        // A single dodge keeps both branches deterministic: the female-effective
        // slasher must still demand a second dodge (damage follows), the
        // male-effective one must not.
        if (players[seat].Hand.Count(card => card.Kind == CardKind.Dodge) != 1) return null;
        var playPrompt = game.PendingDecision!;
        var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.TargetSeats!.Contains(seat));
        if (slash is null) return null;
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            game.Revision, playPrompt.PromptId, slash.PlayedCardKind, slash.TargetCardId)));
        DriveUntilSettled(game);

        if (avatar == AvatarFemale)
        {
            var progress = game.Events.Select(item => item.Payload)
                .OfType<RequiredResponseProgressEvent>()
                .SingleOrDefault(item => item.ResponderSeat == seat);
            if (progress is null) return null;
            Require(progress.RequiredResponseCount == 2,
                "肉林 must demand two dodges when the female-effective 左慈 slashes 董卓.");
            Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat == seat && item.SourceSeat == 0),
                "A single dodge cannot stop the female-effective 左慈's slash.");
            return 2;
        }
        Require(!game.Events.Select(item => item.Payload).OfType<RequiredResponseProgressEvent>()
                .Any(item => item.ResponderSeat == seat),
            "肉林 must never demand a second dodge from the male-effective 左慈's slash.");
        Require(game.CardMovements.Any(movement =>
                movement.CardKind == CardKind.Dodge &&
                movement.From.OwnerSeat == seat &&
                movement.From.Zone == CardZoneKind.Hand),
            "董卓 must pay his single dodge against the male-effective 左慈.");
        Require(!game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Any(item => item.TargetSeat == seat && item.SourceSeat == 0),
            "A male-effective 左慈's slash must be stopped by the single dodge.");
        return 1;
    }

    private static (string AvatarId, bool Offered)? TryObserveHuangtianContribution(
        ContentRegistry registry, int seed)
    {
        var game = StartFaction(registry, seed);
        if (game is null) return null;
        if (game.PendingDecision is not { Kind: DecisionKind.HuaShen, PlayerSeat: 0 }) return null;
        var wanted = game.GetHuaShenAvatarGeneralIds(0).FirstOrDefault(id =>
            id.StartsWith("fixture:zuo-ci-avatar", StringComparison.Ordinal));
        if (wanted is null) return null;
        AnswerHuaShen(game, wanted);
        AnswerHuaShen(game, Mashu);
        DriveUntilSettled(game);

        var lord = game.CreateSnapshot(0, revealAll: true).Players
            .SingleOrDefault(player => player.Role == Role.Lord);
        if (lord is null || lord.GeneralId != HuangtianLord || !lord.IsAlive) return null;

        if (!DriveUntil(game, () => game.PendingDecision is
            { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })) return null;
        return (wanted, game.PendingDecision!.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("action") == "use-program-skill" &&
            choice.Parameters.GetValueOrDefault("skill-id") == Huangtian));
    }

    private static bool TryPlayDuelAndConcede(GameEngine game)
    {
        for (var step = 0; step < 64 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Duel) is { } duel)
            {
                Accept(game.Submit(new PlayCardCommand(0, duel.CardId!.Value, duel.TargetSeats,
                    game.Revision, game.PendingDecision!.PromptId, duel.PlayedCardKind,
                    duel.TargetCardId)));
                return true;
            }
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (!AnswerPassively(game, prompt)) return false;
        }
        return false;
    }

    private static void AnswerHuaShen(GameEngine game, string contentId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "No pending decision for the hua-shen choice.");
        Require(prompt.Kind == DecisionKind.HuaShen,
            $"The hua-shen prompt must be pending, not {prompt.Kind}.");
        var choice = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.Values.Contains(contentId)) ??
            throw new InvalidOperationException(
                $"The hua-shen prompt lost the {contentId} choice.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static bool AnswerPassively(GameEngine game, PendingDecision prompt)
    {
        switch (prompt.Kind)
        {
            case DecisionKind.PlayCard:
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                return true;
            case DecisionKind.DiscardCards:
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                return true;
            case DecisionKind.ProgramTrigger:
                SkipProgramTrigger(game);
                return true;
            default:
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
                    prompt.Choices.FirstOrDefault();
                if (choice is null) return false;
                Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision)));
                return true;
        }
    }

    private static bool IsProgramTriggerPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            SkillPrompt.SkillId: var id
        } && id == skillId;

    private static void Activate(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "No pending decision to activate.");
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            throw new InvalidOperationException("The window lost its activate choice.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            activate.Id, game.Revision)));
    }

    private static void SkipProgramTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "No pending decision to skip.");
        var skip = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
            throw new InvalidOperationException("The window lost its skip choice.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            skip.Id, game.Revision)));
    }

    private static bool DriveUntil(GameEngine game, Func<bool> done, string[]? stopAtSkills = null,
        int budget = 1200)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return true;
            }
            if (prompt.PlayerSeat != 0)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (!AnswerPassively(game, prompt)) return false;
        }
        return done();
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0 && game.PendingDecision is null ||
            game.State.Status == EngineStatus.Completed);

    private static GameEngine? Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The 左慈 fixture did not start.");
        if (game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } choice ||
            !choice.ValidContentIds.Contains(General, StringComparer.Ordinal))
        {
            return null;
        }
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "左慈 selection failed.");
        for (var step = 0; step < 64 && game.PendingDecision is null; step++)
        {
            if (!AdvanceStep(game)) break;
        }
        return game;
    }

    private static GameEngine? StartFaction(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            HumanSeat = 0,
            HumanRole = Role.Loyalist,
            ModeId = FactionMode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The 黄天 fixture did not start.");
        for (var step = 0; step < 32; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } choice)
            {
                if (!choice.ValidContentIds.Contains(General, StringComparer.Ordinal)) return null;
                var selected = game.Submit(new SelectGeneralCommand(
                    0, General, game.Revision, choice.PromptId));
                Require(selected.Accepted, selected.Error?.Message ?? "左慈 selection failed.");
                break;
            }
            if (!AdvanceStep(game)) return null;
        }
        for (var step = 0; step < 64 && game.PendingDecision is null; step++)
        {
            if (!AdvanceStep(game)) break;
        }
        return game;
    }

    private static void EndPlayPhase(GameEngine game)
    {
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "Ending the play phase requires the human play decision.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt!.PromptId)));
    }

    private static bool AdvanceStep(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        return result.Accepted;
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "左慈 fixture command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected the invalid 左慈 composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static ContentRegistry FactionRegistry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new FactionScenario());

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zuo-ci-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-f", "测试幻化女将", "zuo_ci", Mashu, "wu",
                BaseHp: 4, Gender: GeneralGender.Female));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-m", "测试幻化男将", "zuo_ci", Mashu, "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-lord", "测试幻化主公将", "zuo_ci", Mashu, "shu", BaseHp: 4,
                AdditionalSkillIds: [Hujia]));
            for (var index = 1; index <= 5; index++)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    $"fixture:zuo-ci-avatar-x{index}", $"测试幻化填充将{index}", "zuo_ci", Mashu,
                    index % 2 == 0 ? "wei" : "wu", BaseHp: 4));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-bank-a", "测试对手一", "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-bank-b", "测试对手二", "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-bank-c", "测试对手三", "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-bank-d", "测试对手四", "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 5) switch
            {
                0 => "standard:duel",
                1 or 2 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:zuo-ci-deck", "左慈测试牌堆", 4, 2, [])
            {
                PhysicalCards = cards
            });
            builder.AddMode(new ContentModeDefinition(Mode, "左慈测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:zuo-ci-deck", GeneralCandidateCount: 14,
                GeneralPoolIds: [General,
                    RoulinOwner,
                    AvatarFemale,
                    AvatarMale,
                    AvatarLordSkill,
                    "fixture:zuo-ci-avatar-x1",
                    "fixture:zuo-ci-avatar-x2",
                    "fixture:zuo-ci-avatar-x3",
                    "fixture:zuo-ci-avatar-x4",
                    "fixture:zuo-ci-avatar-x5",
                    "fixture:zuo-ci-bank-a",
                    "fixture:zuo-ci-bank-b",
                    "fixture:zuo-ci-bank-c",
                    "fixture:zuo-ci-bank-d"]));
        }
    }

    private sealed class FactionScenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zuo-ci-faction-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-huangtian-lord", "测试黄天主公", "zhang_jiao", Huangtian, "qun", BaseHp: 4,
                AdditionalSkillIds: [Qingnang]));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-qun-a", "测试群化身一", "zuo_ci", Mashu, "qun", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-qun-b", "测试群化身二", "zuo_ci", Mashu, "qun", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-wei-a", "测试魏化身一", "zuo_ci", Mashu, "wei", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-avatar-wei-b", "测试魏化身二", "zuo_ci", Mashu, "wei", BaseHp: 4));
            for (var index = 6; index <= 8; index++)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    $"fixture:zuo-ci-avatar-x{index}", $"测试幻化填充将{index}", "zuo_ci", Mashu,
                    index % 2 == 0 ? "wei" : "wu", BaseHp: 4));
            }
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-wei-bank", "测试魏对手", "supporter", "standard:none", "wei", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "fixture:zuo-ci-shu-bank", "测试蜀对手", "supporter", "standard:none", "shu", BaseHp: 8));
            var cards = Enumerable.Range(0, 120).Select((_, index) =>
                    new ContentDeckPhysicalCard("standard:dodge", (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:zuo-ci-faction-deck", "左慈势力测试牌堆", 4, 2, [])
            {
                PhysicalCards = cards
            });
            builder.AddMode(new ContentModeDefinition(FactionMode, "左慈势力测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2
                }, "fixture:zuo-ci-faction-deck", GeneralCandidateCount: 12,
                GeneralPoolIds: [General,
                    HuangtianLord,
                    AvatarQunA,
                    AvatarQunB,
                    AvatarWeiA,
                    AvatarWeiB,
                    "fixture:zuo-ci-avatar-x6",
                    "fixture:zuo-ci-avatar-x7",
                    "fixture:zuo-ci-avatar-x8",
                    "fixture:zuo-ci-wei-bank",
                    "fixture:zuo-ci-shu-bank"]));
        }
    }
}
