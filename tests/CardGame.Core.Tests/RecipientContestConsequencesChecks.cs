using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

// Unexecuted focused drafts. Homogeneous physical recipes remove setup-shuffle
// assumptions; these methods use the existing command/runner framework.
internal static class RecipientContestConsequencesChecks
{
    private const string Contest = "ol:jianshu", Benefit = "ol:yongdi", Driver = "fixture:rcc-driver", Pause = "fixture:rcc-pause";
    private const string Mode = "identity:classic-recipient-consequences-check";

    public static void ActualBlackGiftSelfSecondAndTie()
    {
        var (g, r) = Start(); var before = g.State.Players.Select(p => p.Hp).ToArray();
        Gift(g); Reach(g, p => ProgramAction(p, "black-gift-contest"));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().BlackGiftContest!;
        Require(receipt.RecipientSeat == 1 && g.CardMovements.Count(m => m.Sequence > receipt.GiftBefore && m.Sequence <= receipt.GiftAfter) == 1,
            "A genuine one-card Hand gift is paid before the second participant choice.");
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])), "The issuer is a legal distinct second participant with actual remaining Hand.");
        Require(g.CreateSnapshot(2).PendingDecision is null, "The private second-participant choice is visible only to its chooser.");
        Cold(g, r); RejectUnpublished(g); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.Kind == DecisionKind.SkillModule && p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-card"));
        Require(g.ResolutionStack.OfType<PindianFrame>().Single().SourceSeat == 1, "The recipient chooses its own Pindian input through the mature child.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-card"); ReachPlay(g);
        var result = Facts<BlackGiftContestResultCapturedEvent>(g).Single().Result;
        Require(result.SourceRank == 7 && result.OpponentRank == 7 && Facts<BlackGiftContestDiscardPaidEvent>(g).Length == 0,
            "A true equal-rank result has no invented winner or discard.");
        Require(g.State.Players[0].Hp == before[0] - 1 && g.State.Players[1].Hp == before[1] - 1 && Facts<BlackGiftContestLossPaidEvent>(g).Length == 2,
            "Both original non-winning participants pay one real HP loss in a tie.");
        Require(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Contest), "The game-limited issued fact removes the second activation.");
        Cold(g, r); Conserve(g);
    }

    public static void ActualWinnerDiscardPaidChildAndReturn()
    {
        var (g, r) = Start(winner: true); var before = g.State.Players[1].Hp;
        Gift(g); Reach(g, p => ProgramAction(p, "black-gift-contest")); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SkillModule); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-card");
        Reach(g, p => ProgramAction(p, "black-gift-contest") && p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Cold(g, r); Answer(g, c => c.Cards.Count == 1); Cold(g, r); Answer(g, c => c.Cards.Count == 1);
        Reach(g, p => p.SkillPrompt?.SkillId == Pause);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BlackGiftContest is not null);
        Require(root.BlackGiftContest is { Stage: BlackGiftContestStage.DiscardPaid, Discard.CardIds.Count: 2 } &&
            Facts<BlackGiftContestLossPaidEvent>(g).Length == 0, "The real two-entity discard suspends later HP loss until its paid observer returns.");
        Cold(g, r); Answer(g, _ => true); ReachPlay(g);
        Require(Facts<BlackGiftContestDiscardPaidEvent>(g) is [{ WinnerSeat: 0, Count: 2 }] && g.State.Players[1].Hp == before - 1,
            "The exact winning issuer discards once; the original losing recipient pays actual HP after all discard children.");
        Cold(g, r); Conserve(g);
        ActualWinnerWoodenOxDiscardPaidChildAndReturn();
    }

    private static void ActualWinnerWoodenOxDiscardPaidChildAndReturn()
    {
        var (g, r) = Start(winner: true, woodenOx: true); var before = g.State.Players[1].Hp;
        var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.CardId is not null);
        var oxId = equip.CardId!.Value;
        Accept(g, new PlayCardCommand(0, oxId, [], g.Revision, P(g)!.PromptId)); ReachPlay(g);
        var store = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.WoodenOx);
        var grainId = store.SelectableCardIds.First();
        Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grainId], [], g.Revision, P(g)!.PromptId)); ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].WoodenOxGrain!.Single().Id == grainId &&
            g.CreateSnapshot(1).Players[0].WoodenOxGrain!.Count == 0,
            "The exact grain was stored by its real equipment command and stays private before discard.");
        Cold(g, r);
        Gift(g); Reach(g, p => ProgramAction(p, "black-gift-contest")); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SkillModule); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-card");
        Reach(g, p => ProgramAction(p, "black-gift-contest") && p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([oxId]));
        var handCost = g.CreateSnapshot(0).Players[0].Hand.First().Id;
        Answer(g, c => c.Cards.SequenceEqual([handCost]));
        var costSeen = false; var grainSeen = false; var batches = new HashSet<long>();
        for (var i = 0; i < 2; i++)
        {
            Reach(g, p => p.SkillPrompt?.SkillId == Pause);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BlackGiftContest is not null);
            var paid = root.BlackGiftContest!.Discard!;
            var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            Require(root.BlackGiftContest.Stage == BlackGiftContestStage.DiscardPaid && paid.CardIds.Count == 2 &&
                paid.CardIds.Contains(oxId) && paid.CardIds.Contains(handCost) && Facts<BlackGiftContestLossPaidEvent>(g).Length == 0 &&
                g.State.Players[1].Hp == before && moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.ResumeProgramFrameId is null && batches.Add(moved.Batch.Id),
                "Both exact awaited discard batches suspend the original loser's HP until every child returns.");
            if (moved.Batch.Movements.All(m => paid.CardIds.Contains(m.CardId)))
            {
                Require(!costSeen && moved.Batch.Movements.Count == 2 && moved.Batch.Movements.All(m =>
                    m.Sequence > paid.Before && m.Sequence <= paid.After && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.ol:jianshu.GiveBlackHandAndResolveRecipientContest.discard"),
                    "Only the two selected real costs belong to the frozen paid interval.");
                costSeen = true;
            }
            else
            {
                Require(!grainSeen && moved.Batch.Movements is [var grain] && grain.CardId == grainId && grain.Sequence > paid.After &&
                    grain.From == CardLocation.WoodenOxGrain(0) && grain.To == CardLocation.DiscardPile && grain.Reason == CardMoveReasons.WoodenOxGrainDiscard,
                    "The separately queued actual grain child follows the paid interval after equipment removal.");
                grainSeen = true;
            }
            Cold(g, r); Answer(g, _ => true);
        }
        ReachPlay(g);
        Require(costSeen && grainSeen && Facts<BlackGiftContestDiscardPaidEvent>(g) is [{ WinnerSeat: 0, Count: 2 }] &&
            g.State.Players[1].Hp == before - 1 && g.CreateSnapshot(0).Players[0].WoodenOxGrainCount == 0,
            "Both movement children complete exactly once before one real losing-participant HP payment.");
        Cold(g, r); Conserve(g);
    }

    public static void ActualPrintedLordQualificationNativeAndPersistent()
    {
        var (g, r) = Start(benefit: true); var target = Players(g)[1]; var before = target.MaxHp;
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => ProgramAction(p, "printed-lord-benefit")); Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([1])); ReachPlay(g);
        var q = target.SkillGrants.Grants.Single(s => s.SkillId == "classic:hujia").PrintedLordQualification;
        Require(target.Role == Role.Rebel && target.MaxHp == before + 1 && target.Hp == before + 1 && q is not null,
            "Actual MaxHp payment and mature recovery precede qualification without role conversion.");
        Require(Invoke<bool>(g, "HasSkillRoleQualification", target, "classic:hujia", q!.SkillInstanceId, Role.Lord) &&
            Invoke<IEnumerable<int>>(g, "GetFactionDefenseCandidateSeats", 1).Any(), "The qualified exact printed instance reaches the native response-provider entry.");
        var payloads = g.Events.Select(e => e.Payload).ToArray();
        var recovery = Array.FindIndex(payloads, e => e is PrintedLordRecoveryRequestedEvent);
        var capture = Array.FindIndex(payloads, e => e is PrintedLordQualificationCapturedEvent);
        var issuance = Array.FindIndex(payloads, e => e is PrintedLordQualificationIssuedEvent);
        Require(recovery >= 0 && capture > recovery && issuance > capture, "Qualification is captured at the post-recovery step.");
        DriverUse(g, "source-loss"); ReachPlay(g);
        Require(Invoke<bool>(g, "HasSkillRoleQualification", target, "classic:hujia", q.SkillInstanceId, Role.Lord), "Issued qualification survives actual loss of the issuing Yongdi skill.");
        Cold(g, r); Conserve(g);
    }

    public static void PrintedQualificationExactGrantAndReplacement()
    {
        var (g, _) = Start(benefit: true); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => ProgramAction(p, "printed-lord-benefit")); Answer(g, c => c.Targets.SequenceEqual([1])); ReachPlay(g);
        var target = Players(g)[1]; var qualified = target.SkillGrants.Grants.Single(s => s.SkillId == "classic:hujia");
        // Source-API unit boundary after a genuine command-issued qualification.
        // These real grant/general mutations are separate from command replay above.
        target.SkillGrants.Grant(new("fixture:acquired-hujia", qualified.SkillId, "fixture:acquired-instance", "acquired:fixture"));
        Require(!Invoke<bool>(g, "HasSkillRoleQualification", target, qualified.SkillId, "fixture:acquired-instance", Role.Lord), "An acquired same-id lord skill never borrows the printed qualification.");
        Require(Invoke<bool>(g, "HasSkillRoleQualification", target, qualified.SkillId, null, Role.Lord) &&
            Invoke<string>(g, "GetRuntimeSkillInstanceId", target, qualified.SkillId) == qualified.SkillInstanceId,
            "Menu and execution prefer the same active qualified printed instance even when an unqualified acquired instance sorts first.");
        target.SkillGrants.SetEnabled(qualified.GrantId, false);
        Require(!Invoke<bool>(g, "HasSkillRoleQualification", target, qualified.SkillId, qualified.SkillInstanceId, Role.Lord), "Local disable still excludes the qualified printed instance.");
        target.SkillGrants.SetEnabled(qualified.GrantId, true);
        Require(Invoke<bool>(g, "HasSkillRoleQualification", target, qualified.SkillId, qualified.SkillInstanceId, Role.Lord), "Explicit enable restores only its exact printed instance.");
        var previous = target.General; target.General = previous with { Id = "fixture:rcc-replacement" };
        Require(target.SkillGrants.Grants.Single(s => s.GrantId == qualified.GrantId).PrintedLordQualification is null &&
            !Invoke<bool>(g, "HasSkillRoleQualification", target, qualified.SkillId, qualified.SkillInstanceId, Role.Lord),
            "A genuine template replacement clears qualification even when the same skill and grant instance remain.");
        target.General = previous;
        Require(target.SkillGrants.Grants.Single(s => s.GrantId == qualified.GrantId).PrintedLordQualification is null, "Returning to the old general does not silently reissue qualification.");
    }

    public static void ActualOrdinaryTrickProtectionAndDelayedBoundary()
    {
        var (g, r) = Start(trick: true); var actor = Players(g)[0]; var before = g.State.Players[0].HandCount;
        var action = g.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.DrawTwo && a.CardId is not null);
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind)); ReachPlay(g);
        var protection = Facts<OrdinaryTrickCannotNullifyIssuedEvent>(g).Single().Receipt;
        Require(Facts<NullificationRequestedEvent>(g).All(e => e.ResolutionId != protection.CardUseFrameId) &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == protection.CardUseFrameId && e.CardKind == CardKind.DrawTwo) == 1 &&
            g.State.Players[0].HandCount == before + 1, "The actual converted ordinary trick pays one entity, resolves Draw2 and finishes without its prohibited counterspell node.");
        Require(Invoke<bool>(g, "IsCardTargetProhibited", actor, CardKind.Indulgence, (Suit?)Suit.Club, (bool?)false) &&
            Invoke<bool>(g, "IsCardTargetProhibited", actor, CardKind.SupplyShortage, (Suit?)Suit.Club, (bool?)false) &&
            !Invoke<bool>(g, "IsCardTargetProhibited", actor, CardKind.Duel, (Suit?)Suit.Club, (bool?)false), "The reused target policy blocks delayed tricks without blanket ordinary-target immunity.");
        Cold(g, r); Conserve(g);
    }

    public static void ReceiptCollectionsAndLegacyGrantAbi()
    {
        var source = new CardConversionSource("ol:jianshu", "limited", 0, "template:primary:ol:jianshu");
        var ids = new[] { 1, 2 }; var locations = new[] { CardLocation.Hand(0), CardLocation.Equipment(0) };
        var discard = new BlackGiftContestDiscard(0, ids, locations, 1, 3); ids[0] = 99; locations[0] = CardLocation.OutsideGame;
        Require(discard.CardIds[0] == 1 && discard.Locations[0] == CardLocation.Hand(0), "Constructor clones both nested discard lists.");
        var selected = new[] { 4 }; var losses = new[] { new BlackGiftContestLoss(1, 8, 7) };
        var receipt = new BlackGiftContestReceipt(1, source, "hash", 1, 0, 1, 4, Suit.Club, 0, 1, SelectedDiscardIds: selected, Discard: discard, Losses: losses);
        var changed = receipt with { SelectedDiscardIds = selected, Losses = losses }; selected[0] = 88; losses[0] = new(2, 9, 8);
        Require(receipt.SelectedDiscardIds[0] == 4 && changed.SelectedDiscardIds[0] == 4 && changed.Losses[0].Seat == 1, "Constructor and init/with clone mutable receipt inputs.");
        var q = new[] { new PrintedLordSkillQualification(1, "general", CharacterState.PrimarySkillSource, "grant", "classic:hujia", "instance", 7, source, "hash") };
        var benefit = new PrintedLordBenefitReceipt(1, source, "hash", 1, 5, PrintedLordBenefitStage.Complete, PrintedQualifications: q);
        var copy = benefit with { PrintedQualifications = q }; q[0] = q[0] with { GeneralId = "changed" };
        Require(copy.PrintedQualifications[0].GeneralId == "general" &&
            JsonSerializer.Deserialize<PrintedLordBenefitReceipt>(JsonSerializer.Serialize(copy))!.PrintedQualifications[0].GeneralId == "general", "Qualification lists freeze constructor/init/JSON while their elements and issuer are scalar immutable records.");
        Require(!JsonSerializer.Serialize(new SkillGrant("a", "standard:none", "a", "b")).Contains("PrintedLordQualification"), "Unqualified old grants preserve their nullable JSON ABI.");
    }

    private static void Gift(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Contest, "limited-black-hand-recipient-contest",
        [g.CreateSnapshot(0).Players[0].Hand.First().Id], [1], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static bool ProgramAction(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static List<CharacterState> Players(GameEngine g) => (List<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
    private static T Invoke<T>(GameEngine g, string name, params object?[] args) => (T)typeof(GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, args)!;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) throw new InvalidOperationException("Unexpected own Play before the focused boundary.");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed focused boundary not reached.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real fixture command."); }
    private static void RejectUnpublished(GameEngine g) { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice is atomically rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Stack = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All private views, exact paid receipts, typed children and event history cold-restore through accepted commands.");
    private static void Conserve(GameEngine g) => Require(g.CreateCardZoneDiagnostics().Count == 80 && g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80, "Every actual card entity retains one zone.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(bool winner = false, bool benefit = false, bool trick = false, bool woodenOx = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(winner, trick, woodenOx));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:rcc-owner", g.Revision, P(g)!.PromptId));
        if (benefit) Reach(g, p => p.SkillPrompt?.SkillId == Benefit && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        else ReachPlay(g);
        return (g, registry);
    }
    private sealed class Fixture(bool winner, bool trick, bool woodenOx) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-recipient-consequences", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"viewAs":[{"id":"real-hand-as-draw-two","inputKinds":["nullification"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"drawTwo","singleCardTrickUse":true,"forPlay":true,"forResponse":false}],"activations":[{"id":"source-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:yongdi"],"sourceBind":"standard:none"}]}]},{"id":"{{Pause}}","revision":1,"triggers":[{"id":"paid-discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.ol:jianshu.GiveBlackHandAndResolveRecipientContest.discard","equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-child","options":[{"id":"continue"}]}]}]},{"id":"fixture:rcc-rank","revision":1,"cardPolicies":[{"id":"club-rank","kind":"pindianRankBySuit","inputSuit":"club","value":13}]}]}""";
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实来源变化与用牌", description = "共享原生能力夹具" }, [Pause] = new { name = "实际弃牌孩子", description = "真实成本后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, ["fixture:rcc-rank"] = new { name = "原生拼点点数", description = "确定性 13 点" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "Focused source fixture") { Program = pair.Value });
            foreach (var owner in new[] { false, true }) b.AddSkill(new(owner ? "fixture:rcc-owner-pick" : "fixture:rcc-peer-pick", "固定公开选将", "角色评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:rcc-owner", "真实发行者", "supporter", "fixture:rcc-owner-pick", "wei", 8,
                winner ? [Contest, Benefit, "ol:zhenlue", Driver, Pause, "fixture:rcc-rank"] : [Contest, Benefit, "ol:zhenlue", Driver, Pause]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:rcc-peer-{i}", "真实其他角色", "supporter", "fixture:rcc-peer-pick", "wei", 8, ["classic:hujia", "classic:xueyi"]));
            b.AddDeck(new("fixture:rcc-deck", "同质实体", woodenOx ? 6 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard(woodenOx ? "classic:wooden-ox" : trick ? "standard:nullification" : "standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实赠牌与主公技资格", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:rcc-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:rcc-owner", "fixture:rcc-peer-1", "fixture:rcc-peer-2", "fixture:rcc-peer-3"]));
        }
    }
}
