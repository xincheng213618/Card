using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NationalWarChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static GameEngine Create(int seed, int human = 0) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed,
        HumanSeat = human,
        HumanRole = null,
        PlayerCount = 4,
        ModeId = "national:lite-4",
        UseInteractiveSetup = true,
        UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false,
        AiPolicyVersion = 2,
        MaxTurns = 160
    }, StandardContentRegistry.CreateWithNationalWarLite());

    private static GameEngine CreateAmbitious(int seed, int human = 0) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed,
        HumanSeat = human,
        HumanRole = null,
        PlayerCount = 6,
        ModeId = "national:ambitious-6",
        UseInteractiveSetup = true,
        UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false,
        AiPolicyVersion = 2,
        MaxTurns = 240
    }, StandardContentRegistry.CreateWithNationalWarAmbitious());

    public static void PrivateSetupAndReveal()
    {
        var game = Create(721019);
        Require(game.Submit(new StartGameCommand()).Accepted, "National setup failed to start.");
        var selected = new List<string>();
        for (var step = 0; step < 100 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
            {
                Require(prompt.PlayerSeat == 0 && game.CreateSnapshot(1).PendingDecision is null, "National candidates leaked to another viewer.");
                var id = prompt.ValidContentIds.First();
                selected.Add(id);
                Require(game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted, "Private national choice failed.");
            }
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "National setup could not advance.");
        }
        Require(selected.Count == 2 && selected.Distinct().Count() == 2, "Human must select two distinct generals.");
        var own = game.CreateSnapshot(0).Players[0];
        Require(own.GeneralId == selected[0] && own.SecondaryGeneralId == selected[1] && own.FactionId is not null &&
            !own.IsGeneralPublic && !own.IsSecondaryGeneralPublic && !own.IsFactionRevealed && own.Role is null,
            "Own hidden dual-general metadata is incomplete.");
        Require(own.Skills is { Count: > 0 } && own.SecondarySkills is { Count: > 0 },
            "The owner must see both ordered skill collections before revealing either slot.");
        using (var playerJson = JsonDocument.Parse(JsonSerializer.Serialize(own)))
        {
            foreach (var singular in new[] { "Skill", "SkillName", "SkillDescription",
                         "SecondarySkill", "SecondarySkillName", "SecondarySkillDescription" })
                Require(!playerJson.RootElement.TryGetProperty(singular, out _),
                    $"PlayerSnapshot still serialized the singular compatibility field {singular}.");
        }
        var hidden = game.CreateSnapshot(1).Players[0];
        Require(hidden.FactionId is null && hidden.GeneralId != selected[0] && hidden.SecondaryGeneralId != selected[1] &&
            hidden.Hand.Count == 0 && hidden.Role is null && hidden.Skills is null && hidden.SecondarySkills is null,
            "Unrevealed national information leaked to another player.");
        var actions = game.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.RevealGeneral).ToArray();
        Require(actions.Length == 2 && actions.Select(action => action.GeneralSlot).Distinct().Count() == 2, "Both generals need independent reveal actions.");
        var primary = new RevealGeneralCommand(0, GeneralSelectionSlot.Primary, game.Revision, game.PendingDecision!.PromptId);
        Require(game.Submit(primary).Accepted, "Primary reveal failed.");
        var revealed = game.CreateSnapshot(1).Players[0];
        Require(revealed.GeneralId == selected[0] && revealed.IsGeneralPublic && revealed.FactionId == own.FactionId &&
            !revealed.IsSecondaryGeneralPublic && revealed.SecondaryGeneralId != selected[1] &&
            revealed.Skills is { Count: > 0 } && revealed.SecondarySkills is null,
            "Primary reveal leaked the secondary general or failed to reveal faction.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted && game.PendingDecision is { Kind: DecisionKind.PlayCard },
            "Primary reveal did not return to a playable decision.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0, true));
        Require(!game.Submit(new RevealGeneralCommand(0, GeneralSelectionSlot.Primary, game.Revision, game.PendingDecision!.PromptId)).Accepted &&
            SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == before, "Duplicate reveal changed the game.");
        Require(game.Submit(new RevealGeneralCommand(0, GeneralSelectionSlot.Secondary, game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Secondary reveal failed.");
        Require(game.CreateSnapshot(1).Players[0].SecondaryGeneralId == selected[1] && game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.RevealGeneral),
            "Secondary reveal did not finish both public slots.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), StandardContentRegistry.CreateWithNationalWarLite());
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)), "National reveal state did not replay.");
    }

    public static void CompleteAiMatches()
    {
        var decisive = 0;
        for (var seed = 721019; seed < 721027; seed++)
        {
            var game = Create(seed, -1);
            Require(game.Submit(new StartGameCommand()).Accepted, "National AI match did not start.");
            for (var step = 0; step < 16000 && game.State.Status != EngineStatus.Completed; step++)
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "National AI continuation failed.");
            Require(game.State.Status == EngineStatus.Completed, "National AI match stalled.");
            if (game.State.Winner != Winner.Draw) decisive++;
            var result = game.CreateSnapshot(-1);
            Require(result.Players.All(player => player.IsGeneralPublic && player.IsSecondaryGeneralPublic && player.IsFactionRevealed), "Completed national match retained hidden generals.");
            Require(result.Players.SelectMany(player => new[] { player.GeneralId, player.SecondaryGeneralId }).Distinct().Count() == 8,
                "National setup reused a general.");
            if (result.Winner != Winner.Draw)
                Require(result.WinnerFactionId is not null && result.Players.Where(player => player.IsAlive).All(player => player.FactionId == result.WinnerFactionId),
                    "National winner differs from the surviving faction.");
            var restored = GameReplay.Restore(game.CreateCheckpoint(), StandardContentRegistry.CreateWithNationalWarLite());
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(-1, true)) == SnapshotJson.Serialize(game.CreateSnapshot(-1, true)), "Completed national match replay diverged.");
        }
        Require(decisive > 0, "All national games timed out instead of reaching a faction victory.");
        Console.WriteLine($"  National War Lite: 8 complete AI matches, {decisive} faction victories, replay parity verified.");
    }

    public static void SkillGatingAndLegacy()
    {
        foreach (var slot in new[] { GeneralSelectionSlot.Primary, GeneralSelectionSlot.Secondary })
        {
            var game = SkillFixture("national:shu-guan-yu", "national:shu-zhang-fei", slot, requireRed: true);
            bool Converts(GameEngine engine) => engine.GetHumanLegalActions().Any(action => action.PlayedCardKind == CardKind.Slash);
            Require(!Converts(game), "An unrevealed Wusheng slot enabled conversion.");
            var otherSlot = slot == GeneralSelectionSlot.Primary ? GeneralSelectionSlot.Secondary : GeneralSelectionSlot.Primary;
            Reveal(game, otherSlot);
            Require(!Converts(game), "Revealing the other general enabled hidden Wusheng.");
            Reveal(game, slot);
            Require(Converts(game), "A revealed Wusheng slot failed to enable red-card Slash.");
            var restored = GameReplay.Restore(game.CreateCheckpoint(), StandardContentRegistry.CreateWithNationalWarLite());
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)), "Version 7 skill reveal failed to replay.");
        }

        var response = SkillFixture("national:shu-zhao-yun", "national:shu-zhang-fei", GeneralSelectionSlot.Secondary, requireRed: false);
        var runtime = ((System.Collections.IEnumerable)typeof(GameEngine).GetField("_players", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(response)!).Cast<object>().First();
        var getResponses = typeof(GameEngine).GetMethod("GetResponseCards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Card[] Responses(CardKind kind) => ((IReadOnlyList<Card>)getResponses.Invoke(response, [runtime, kind])!).ToArray();
        var hand = response.CreateSnapshot(0).Players[0].Hand;
        Require(Responses(CardKind.Dodge).All(card => card.Kind == CardKind.Dodge), "Hidden Longdan enabled a Dodge conversion.");
        Reveal(response, GeneralSelectionSlot.Secondary);
        Require(Responses(CardKind.Dodge).Select(card => card.Id).ToHashSet().SetEquals(hand.Where(card => card.Kind is CardKind.Dodge or CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash).Select(card => card.Id)) &&
            Responses(CardKind.Dodge).Any(card => card.Kind != CardKind.Dodge), "Secondary Longdan failed to enable Slash-as-Dodge responses.");

        var damage = SkillFixture("national:wei-cao-cao", "national:wei-guo-jia", GeneralSelectionSlot.Primary, requireRed: false);
        var collect = typeof(GameEngine).GetMethod("CollectDamageTriggerCandidates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var attackType = typeof(GameEngine).GetNestedType("AttackResolution",
            System.Reflection.BindingFlags.NonPublic)!;
        var attack = Activator.CreateInstance(attackType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [0L, 1, 0, null, 1, CardKind.Slash, false, false, null, null, null, false, null, null, null, null, null, null],
            culture: null)!;
        string[] Candidates()
        {
            var context = new DamageSkillContext(new PlayerSkillContext(0, 3, 4, 4, TurnPhase.Play), 1, CardKind.Slash, true,
                SourceCardId: 1, TargetSeat: 0, TargetHp: 3, TargetMaxHp: 4);
            return ((IReadOnlyList<DamageTriggerCandidate>)collect.Invoke(damage,
                [context, attack, SkillProgramTriggerWindow.AfterDamageApplied])!)
                .Select(candidate => candidate.ProgramId ?? candidate.Skill.ToString())
                .ToArray();
        }
        Require(Candidates().Length == 0, "Hidden generals entered a damage trigger window.");
        Reveal(damage, GeneralSelectionSlot.Primary);
        Require(Candidates().SequenceEqual(["standard:jianxiong"]),
            "Primary reveal did not enable only Jianxiong.");
        Reveal(damage, GeneralSelectionSlot.Secondary);
        Require(Candidates().ToHashSet(StringComparer.Ordinal)
                .SetEquals(["standard:jianxiong", "standard:yiji"]),
            "Two revealed damage skills lost their separate trigger identities.");
    }

    public static void MultiSkillRevealAndLegacy()
    {
        const string modeId = "national:multi-skill-fixture-2";
        const string multiId = "national:test-multi";
        const string partnerId = "national:test-partner";
        const string programId = "national:test-program";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new SyntheticPackage(
                "national-multi-skill-fixture",
                builder =>
                {
                    var catalog = SkillProgramCatalog.Load(
                        """
                        {"schemaVersion":60,"skills":[{"id":"national:test-program","revision":1,"minimumRulesVersion":170,"modifiers":[{"id":"extra-draw","priority":0,"query":"drawCount","operation":"add","value":1,"condition":{"kind":"ownTurn"}}]}]}
                        """,
                        """
                        {"schemaVersion":3,"skills":{"national:test-program":{"name":"试验程序技","description":"摸牌阶段额外摸一张牌。"}}}
                        """);
                    var program = catalog.Programs[programId];
                    var presentation = catalog.Presentations[programId];
                    builder.AddSkill(new ContentSkillDefinition(
                        programId, presentation.Name, presentation.Description)
                    { Program = program });
                    builder.AddGeneral(new ContentGeneralDefinition(
                        multiId, "多技能将", "guan_yu", "standard:paoxiao", "wei",
                        AdditionalSkillIds: ["standard:wusheng", programId]));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        partnerId, "同势力搭档", "cao_cao", "standard:none", "wei"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-shu-a", "蜀将甲", "zhang_fei", "standard:paoxiao", "shu"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-shu-b", "蜀将乙", "zhao_yun", "standard:longdan", "shu"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-wu-a", "吴将甲", "sun_quan", "standard:none", "wu"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-wu-b", "吴将乙", "zhou_yu", "standard:yingzi", "wu"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-qun-a", "群将甲", "hua_tuo", "standard:feedback", "qun"));
                    builder.AddGeneral(new ContentGeneralDefinition(
                        "national:test-qun-b", "群将乙", "xun_yu", "standard:yiji", "qun"));
                    builder.AddMode(new ContentModeDefinition(
                        modeId, "国战多技能夹具", 4, 4,
                        new Dictionary<string, int>(),
                        "standard:basic-demo",
                        2,
                        [
                            multiId,
                            partnerId,
                            "national:test-shu-a",
                            "national:test-shu-b",
                            "national:test-wu-a",
                            "national:test-wu-b",
                            "national:test-qun-a",
                            "national:test-qun-b"
                        ],
                        ContentModeKind.NationalWarLite,
                        FactionCounts: new Dictionary<string, int>
                        {
                            ["wei"] = 1,
                            ["shu"] = 1,
                            ["wu"] = 1,
                            ["qun"] = 1
                        }));
                },
                new PackageDependency("standard", new Version(1, 11, 0))));

        foreach (var slot in new[] { GeneralSelectionSlot.Primary, GeneralSelectionSlot.Secondary })
        {
            var game = FindFixture(slot);
            var own = game.CreateSnapshot(0).Players[0];
            var observer = game.CreateSnapshot(1).Players[0];
            var projected = slot == GeneralSelectionSlot.Primary ? own.Skills : own.SecondarySkills;
            Require(projected is { Count: 3 } && projected.Select(skill => skill.ContentId)
                    .SequenceEqual(["standard:paoxiao", "standard:wusheng", programId]) &&
                    projected[2].Name == "试验程序技",
                "The private national slot did not project every ordered configured skill.");
            Require((slot == GeneralSelectionSlot.Primary ? observer.Skills : observer.SecondarySkills) is null,
                "An observer saw a multi-skill list before its national slot was revealed.");

            var otherSlot = slot == GeneralSelectionSlot.Primary
                ? GeneralSelectionSlot.Secondary
                : GeneralSelectionSlot.Primary;
            Reveal(game, otherSlot);
            Require(EnabledKinds(game).Length == 0 && EnabledPrograms(game).Length == 0,
                "Revealing the other national slot enabled the hidden multi-skill general.");
            Reveal(game, slot);
            Require(EnabledKinds(game).Length == 0 &&
                    EnabledPrograms(game).SequenceEqual([programId, "standard:paoxiao", "standard:wusheng"]),
                "The revealed national general did not enable every configured skill.");
            var publicView = game.CreateSnapshot(1).Players[0];
            Require((slot == GeneralSelectionSlot.Primary ? publicView.Skills : publicView.SecondarySkills) is { Count: 3 },
                "The revealed national multi-skill list stayed hidden from another viewer.");
            var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(replay.CreateSnapshot(1, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(1, revealAll: true)),
                "National multi-skill reveal did not replay exactly.");

        }

        GameEngine FindFixture(GeneralSelectionSlot desiredSlot)
        {
            for (var seed = 1; seed <= 128; seed++)
            {
                var game = GameEngine.CreateStandard(new GameOptions
                {
                    Seed = seed,
                    HumanSeat = 0,
                    HumanRole = null,
                    PlayerCount = 4,
                    ModeId = modeId,
                    UseInteractiveSetup = true,
                    UseInteractiveDiscard = false,
                    AdvanceAfterHumanCommands = false,
                    AiPolicyVersion = 2,
                    MaxTurns = 40
                }, registry);
                if (!game.Submit(new StartGameCommand()).Accepted) continue;
                var selection = 0;
                var usable = true;
                for (var step = 0; step < 80 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
                {
                    if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
                    {
                        var id = (selection++ == 0) == (desiredSlot == GeneralSelectionSlot.Primary)
                            ? multiId
                            : partnerId;
                        if (!prompt.ValidContentIds.Contains(id))
                        {
                            usable = false;
                            break;
                        }
                        if (!game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted)
                        {
                            usable = false;
                            break;
                        }
                    }
                    else if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                    {
                        usable = false;
                        break;
                    }
                }
                if (usable && game.State.Status == EngineStatus.AwaitingHumanPlay) return game;
            }
            throw new InvalidOperationException("No bounded seed assigned the national multi-skill fixture to the human seat.");
        }

        static SkillKind[] EnabledKinds(GameEngine game)
        {
            var players = ((System.Collections.IEnumerable)typeof(GameEngine)
                .GetField("_players", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(game)!).Cast<object>().ToArray();
            var enabled = (System.Collections.IEnumerable)typeof(GameEngine)
                .GetMethod("EnabledSkillRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(game, [players[0]])!;
            return enabled.Cast<SkillRuleDefinition>().Select(skill => skill.Kind)
                .Where(kind => kind != SkillKind.None).ToArray();
        }

        static string[] EnabledPrograms(GameEngine game)
        {
            var players = ((System.Collections.IEnumerable)typeof(GameEngine)
                .GetField("_players", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(game)!).Cast<object>().ToArray();
            var enabled = (System.Collections.IEnumerable)typeof(GameEngine)
                .GetMethod("EnabledSkillPrograms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(game, [players[0]])!;
            return enabled.Cast<SkillProgram>().Select(program => program.Id).ToArray();
        }
    }

    public static void AiRevealPolicy()
    {
        var game = SkillFixture(
            "national:shu-guan-yu",
            "national:shu-liu-bei",
            GeneralSelectionSlot.Primary,
            requireRed: true);
        var view = game.CreateSnapshot(0);
        Require(view.Players[0].Skills?.Any(skill =>
                    skill.ContentId == "standard:wusheng" &&
                    skill.ViewAsOpportunities?.Any(rule =>
                        rule.OutputKind == CardKind.Slash && rule.ForPlay) == true) == true &&
                game.CreateSnapshot(1).Players[0].Skills is null,
            "The hidden general's conversion capability must be visible only to its owner.");
        var revealActions = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.RevealGeneral)
            .ToArray();
        Require(revealActions.Length == 2, "National AI reveal fixture did not publish both slots.");

        var brain = new SimpleAiBrain(0, game.Seed, policyVersion: 2);
        var decision = brain.ChoosePlay(view, revealActions, thoughtSequence: 1);
        Require(decision.Action.GeneralSlot == GeneralSelectionSlot.Primary,
            "National AI did not prefer the revealed Wusheng slot when a red conversion was available.");
        var primary = decision.Thought.Candidates.Single(candidate =>
            candidate.Action.GeneralSlot == GeneralSelectionSlot.Primary);
        var secondary = decision.Thought.Candidates.Single(candidate =>
            candidate.Action.GeneralSlot == GeneralSelectionSlot.Secondary);
        Require(primary.Score > secondary.Score &&
                primary.Reason.Contains("转化为杀", StringComparison.Ordinal) &&
                !primary.Reason.Contains("牌堆", StringComparison.Ordinal),
            "National AI reveal thought did not expose a private-view-safe, opportunity-based reason.");
        var repeated = new SimpleAiBrain(0, game.Seed, policyVersion: 2)
            .ChoosePlay(view, revealActions, thoughtSequence: 1);
        Require(repeated.Action.GeneralSlot == decision.Action.GeneralSlot &&
                repeated.Thought.Candidates.Select(candidate => candidate.Score)
                    .SequenceEqual(decision.Thought.Candidates.Select(candidate => candidate.Score)),
            "National AI reveal scoring was not deterministic for the same view and seed.");

        var emptyHand = view with
        {
            Players = view.Players
                .Select(player => player.Seat == 0
                    ? player with
                    {
                        Hand = [], HandCount = 0,
                        Skills = [new GeneralSkillDefinition(SkillKind.None, "无技能", "")],
                        SecondarySkills = [new GeneralSkillDefinition(SkillKind.Kongcheng, "空城", "")]
                    }
                    : player)
                .ToArray()
        };
        var kongcheng = new LegalAction(
            LegalActionKind.RevealGeneral,
            null,
            null,
            "明置【空城】")
        {
            GeneralSlot = GeneralSelectionSlot.Secondary
        };
        var neutral = new LegalAction(
            LegalActionKind.RevealGeneral,
            null,
            null,
            "明置【无技能】")
        {
            GeneralSlot = GeneralSelectionSlot.Primary
        };
        var emptyDecision = brain.ChoosePlay(emptyHand, [neutral, kongcheng], thoughtSequence: 2);
        Require(emptyDecision.Action.GeneralSlot == GeneralSelectionSlot.Secondary,
            "National AI did not prioritize Kongcheng when its private hand was empty.");
    }

    public static void AmbitiousFactionMode()
    {
        var registry = StandardContentRegistry.CreateWithNationalWarAmbitious();
        var mode = registry.Modes["national:ambitious-6"];
        Require(mode.FactionCounts is not null && mode.FactionCounts.Count == 3 &&
            mode.FactionCounts["wei"] == 3 && mode.FactionCounts["shu"] == 2 && mode.FactionCounts["ambitious"] == 1,
            "The ambitious mode did not register its three-faction distribution.");
        Require(mode.SoloFactionIds is { Count: 1 } && mode.SoloFactionIds[0] == "ambitious" &&
            registry.Packages.Any(package => package.Id == "standard-national-war-ambitious"),
            "The ambitious mode lost its explicit solo-faction content contract.");
        Require(registry.ContentHash != StandardContentRegistry.CreateWithNationalWarLite().ContentHash,
            "The ambitious content package did not participate in content drift detection.");

        var game = CreateAmbitious(721031);
        Require(game.Submit(new StartGameCommand()).Accepted, "Six-player national setup failed to start.");
        var selected = new List<string>();
        for (var step = 0; step < 240 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
            {
                Require(prompt.PlayerSeat == 0 && game.CreateSnapshot(1).PendingDecision is null,
                    "Six-player national candidates leaked outside the human view.");
                var id = prompt.ValidContentIds.First();
                selected.Add(id);
                Require(game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted,
                    "Six-player human general choice failed.");
            }
            else
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Six-player national AI setup could not advance.");
            }
        }

        Require(game.State.Status == EngineStatus.AwaitingHumanPlay && selected.Count == 2 && selected.Distinct().Count() == 2,
            "Six-player national setup did not reach play with two distinct human generals.");
        var full = game.CreateSnapshot(-1, revealAll: true);
        var factionCounts = full.Players.Where(player => player.FactionId is not null)
            .GroupBy(player => player.FactionId!)
            .ToDictionary(group => group.Key, group => group.Count());
        Require(full.Players.Count == 6 && factionCounts.GetValueOrDefault("wei") == 3 &&
            factionCounts.GetValueOrDefault("shu") == 2 && factionCounts.GetValueOrDefault("ambitious") == 1,
            "Six-player national seats did not preserve the registered faction counts.");
        Require(full.Players.All(player => player.SecondaryGeneralId is not null &&
            player.GeneralId != player.SecondaryGeneralId), "Six-player setup did not reserve two distinct generals per seat.");
        var own = game.CreateSnapshot(0).Players[0];
        var hiddenOpponent = game.CreateSnapshot(1).Players[0];
        Require(own.FactionId is not null && own.GeneralId == selected[0] && own.SecondaryGeneralId == selected[1] &&
            !own.IsFactionRevealed && !own.IsGeneralPublic && !own.IsSecondaryGeneralPublic,
            "The six-player human view did not retain private dual-general information.");
        Require(hiddenOpponent.FactionId is null && !hiddenOpponent.IsGeneralPublic &&
            hiddenOpponent.SecondaryGeneralId is null && hiddenOpponent.Hand.Count == 0,
            "The six-player opponent view exposed hidden faction or general information.");
        var restoredSetup = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restoredSetup.CreateSnapshot(0, true)) ==
            SnapshotJson.Serialize(game.CreateSnapshot(0, true)), "Six-player setup replay diverged.");

        var ai = CreateAmbitious(721032, human: -1);
        Require(ai.Submit(new StartGameCommand()).Accepted, "Six-player AI match failed to start.");
        for (var step = 0; step < 24000 && ai.State.Status != EngineStatus.Completed; step++)
            Require(ai.Submit(new AdvanceOneStepCommand(ai.Revision)).Accepted, "Six-player AI continuation failed.");
        Require(ai.State.Status == EngineStatus.Completed, "Six-player AI match stalled before a result.");
        var result = ai.CreateSnapshot(-1);
        Require(result.Winner != Winner.Draw && result.WinnerFactionId is not null &&
            result.Players.Where(player => player.IsAlive).All(player => player.FactionId == result.WinnerFactionId) &&
            result.Players.All(player => player.IsFactionRevealed && player.IsGeneralPublic && player.IsSecondaryGeneralPublic),
            "Six-player AI match did not publish one surviving faction and all final information.");
        Require(GameRules.EvaluateWinningFaction([
            new FactionLifeState("wei", false),
            new FactionLifeState("shu", false),
            new FactionLifeState("ambitious", true)]) == "ambitious",
            "Faction winner evaluation did not support the solo faction.");
        var restored = GameReplay.Restore(ai.CreateCheckpoint(), StandardContentRegistry.CreateWithNationalWarAmbitious());
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(-1, true)) ==
            SnapshotJson.Serialize(ai.CreateSnapshot(-1, true)), "Six-player national replay diverged at completion.");
        Console.WriteLine($"  National M3: 6 seats, Wei 3 / Shu 2 / ambitious 1, winner {result.WinnerFactionId}, replay parity verified.");
    }

    public static void PublicEvidenceCheckpointReplay()
    {
        var registry = StandardContentRegistry.CreateWithNationalWarAmbitious();
        GameEngine? game = null;
        GameCheckpoint? checkpoint = null;

        // Use a bounded, deterministic seed window. The first accepted attack is
        // enough: it proves that the engine has routed public faction evidence into
        // AI state before the checkpoint is captured, without inspecting hidden data.
        for (var seed = 721040; seed < 721080 && checkpoint is null; seed++)
        {
            game = CreateAmbitious(seed);
            Require(game.Submit(new StartGameCommand()).Accepted, "Evidence replay fixture failed to start.");
            FinishHumanSetup(game);

            for (var step = 0; step < 12000 && checkpoint is null; step++)
            {
                var eventCount = game.Events.Count;
                SubmitSafeHumanStepOrAdvance(game);
                if (game.Events.Skip(eventCount).Any(envelope => IsNationalAttack(envelope.Payload)) &&
                    game.PendingDecision is not null &&
                    game.State.Status != EngineStatus.Completed &&
                    game.CreateSnapshot(0).Players.Any(player =>
                        player.FactionId is null && player.IsAlive))
                {
                    checkpoint = GameCheckpointJson.Deserialize(
                        GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                }

                if (game.State.Status == EngineStatus.Completed)
                {
                    break;
                }
            }
        }

        Require(game is not null && checkpoint is not null,
            "The bounded national fixture did not reach a public attack checkpoint.");
        var original = game!;
        var restored = GameReplay.Restore(checkpoint!, registry);

        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(original.CreateSnapshot(0, revealAll: true)),
            "Public-evidence checkpoint changed the paused national snapshot.");
        Require(EventSignature(restored) == EventSignature(original),
            "Public-evidence checkpoint changed the committed event stream.");
        Require(JsonSerializer.Serialize(restored.AiThoughts) == JsonSerializer.Serialize(original.AiThoughts) &&
                JsonSerializer.Serialize(restored.AiGeneralThoughts) == JsonSerializer.Serialize(original.AiGeneralThoughts),
            "Public-evidence checkpoint changed deterministic AI diagnostics.");
        Require(restored.PendingDecision is not null,
            "The evidence checkpoint did not preserve a paused prompt.");
        var capturedCheckpoint = checkpoint!;
        var capturedPrompt = restored.PendingDecision!;
        Console.WriteLine($"  National M3-5: public attack checkpoint at revision {capturedCheckpoint.Revision}, paused {capturedPrompt.Kind}, replay parity verified.");
    }

    private static void Reveal(GameEngine game, GeneralSelectionSlot slot)
    {
        Require(game.Submit(new RevealGeneralCommand(0, slot, game.Revision, game.PendingDecision!.PromptId)).Accepted, "Skill fixture reveal failed.");
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Reveal failed to return to play.");
    }

    private static void FinishHumanSetup(GameEngine game)
    {
        for (var step = 0; step < 240 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
            {
                Require(prompt.PlayerSeat == 0 && prompt.ValidContentIds.Count > 0,
                    "Evidence replay fixture did not publish a private human general choice.");
                Require(game.Submit(new SelectGeneralCommand(
                    0,
                    prompt.ValidContentIds[0],
                    game.Revision,
                    prompt.PromptId)).Accepted,
                    "Evidence replay fixture could not select a general.");
            }
            else
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Evidence replay fixture could not finish AI setup.");
            }
        }

        Require(game.State.Status == EngineStatus.AwaitingHumanPlay,
            "Evidence replay fixture did not reach the human play boundary.");
    }

    private static void SubmitSafeHumanStepOrAdvance(GameEngine game)
    {
        if (game.PendingDecision is not { PlayerSeat: 0 } prompt)
        {
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Evidence replay fixture could not advance one engine step.");
            return;
        }

        if (prompt.Kind == DecisionKind.PlayCard)
        {
            Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted,
                "Evidence replay fixture could not end the human play phase.");
            return;
        }

        if (prompt.Kind == DecisionKind.DiscardCards)
        {
            Require(prompt.RequiredCardCount > 0 && prompt.ValidCardIds.Count >= prompt.RequiredCardCount,
                "Evidence replay fixture published an incomplete discard prompt.");
            Require(game.Submit(new DiscardCardsCommand(
                0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision)).Accepted,
                "Evidence replay fixture could not complete the human discard.");
            return;
        }

        var response = prompt.Kind switch
        {
            DecisionKind.RespondDodge or DecisionKind.RespondSlash => "take-damage",
            DecisionKind.RescueDying => "let-die",
            DecisionKind.FireAttackReveal => "fire-attack-reveal",
            DecisionKind.FireAttackDiscard => "fire-attack-skip",
            DecisionKind.Feedback => "take-damage",
            DecisionKind.Yiji => "yiji-skip",
            DecisionKind.Jieming => "jieming-skip",
            DecisionKind.Yuanhu => "yuanhu-skip",
            DecisionKind.Ganglie => "skip-ganglie",
            DecisionKind.GangliePunish => "ganglie-lose-hp",
            DecisionKind.Guicai => "skip-guicai",
            DecisionKind.Nullification => "pass",
            _ => string.Empty
        };
        var choice = prompt.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == response) ?? prompt.Choices.FirstOrDefault();
        Require(choice is not null,
            $"Evidence replay fixture had no safe choice for {prompt.Kind}.");
        Require(game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice!.Id,
            game.Revision)).Accepted,
            $"Evidence replay fixture could not answer {prompt.Kind}.");
    }

    private static bool IsNationalAttack(IGameEvent payload) => payload switch
    {
        CardUsedEvent { CardKind: CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or CardKind.FireAttack } => true,
        GroupCardUsedEvent { CardKind: CardKind.BarbarianAssault or CardKind.ArrowBarrage } => true,
        _ => false
    };

    private static string EventSignature(GameEngine game) => string.Join(
        "\n",
        game.Events.Select(envelope =>
            $"{envelope.Id.Value}|{envelope.ParentId?.Value.ToString() ?? "-"}|{envelope.Sequence}|{envelope.Revision}|{envelope.CorrelationId}|{envelope.Payload.GetType().FullName}|{JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())}"));

    internal static GameEngine SkillFixture(
        string desired,
        string other,
        GeneralSelectionSlot slot,
        bool requireRed,
        int? exactSeed = null)
    {
        var firstSeed = exactSeed ?? 721000;
        var lastSeed = exactSeed is { } fixedSeed ? fixedSeed + 1 : 721200;
        for (var seed = firstSeed; seed < lastSeed; seed++)
        {
            var game = Create(seed);
            game.Submit(new StartGameCommand());
            var selection = 0;
            var usable = true;
            for (var step = 0; step < 100 && game.State.Status != EngineStatus.AwaitingHumanPlay; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral } prompt)
                {
                    var id = (selection++ == 0) == (slot == GeneralSelectionSlot.Primary) ? desired : other;
                    if (!prompt.ValidContentIds.Contains(id)) { usable = false; break; }
                    Require(game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted, "Skill fixture selection failed.");
                }
                else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Skill fixture setup stalled.");
            }
            if (usable && game.State.Status == EngineStatus.AwaitingHumanPlay && (!requireRed || game.CreateSnapshot(0).Players[0].Hand.Any(card =>
                card.Suit is Suit.Heart or Suit.Diamond && card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))) return game;
        }
        throw new InvalidOperationException("No bounded seed supplied the national skill fixture.");
    }
}
