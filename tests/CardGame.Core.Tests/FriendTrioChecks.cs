using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class FriendTrioChecks
{
    public static void PredictionPrivacyFullRewardAndColdReplay()
    {
        foreach (var revealed in new[] { false, true })
        {
            var (g, registry) = Start(false);
            Activate(g); Value(g, "predict"); Value(g, "category");
            for (var i = 0; i < 3; i++) { Replay(g, registry); Value(g, "trick"); }
            Value(g, revealed ? "reveal" : "hidden"); Settle(g);
            var view = g.CreateSnapshot(0).FriendPredictions!.Single();
            Require(view.Values.SequenceEqual(["锦囊牌", "锦囊牌", "锦囊牌"]) && view.Index == 0 && view.Revealed == revealed,
                "The persistent prediction keeps the chosen sequence, progress and reveal state.");
            Require((g.CreateSnapshot(1).FriendPredictions is not null) == revealed &&
                (typeof(GameEngine).GetMethod("GetFriendPredictionViews", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [null]) is not null) == revealed,
                "Unrevealed predictions remain private to the owner, including the public viewer.");
            Require(((IList<FriendPredictionView>)g.CreateSnapshot(0).FriendPredictions!).IsReadOnly && ((IList<string>)view.Values).IsReadOnly,
                "Persistent prediction outer and inner lists are immutable.");
            Replay(g, registry);
            for (var i = 0; i < 3; i++)
            {
                Play(g, CardKind.DrawTwo);
                if (i == 2) { Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "trick")); Replay(g, registry); Value(g, "trick"); }
                Settle(g); Replay(g, registry);
            }
            var events = g.Events.Select(e => e.Payload).ToArray();
            Require(events.OfType<FriendPredictionObservedEvent>().Count() == 3 && events.OfType<FriendPredictionObservedEvent>().All(e => e.Correct),
                "Each real used card verifies exactly one prediction.");
            Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "friend.yance.correct") == 3 &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "friend.yance.reward") == (revealed ? 2 : 1) &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "friend.yance.all-correct") == (revealed ? 3 : 2),
                "Correct draws, declared deck rewards and all-correct draws apply once with the complete Fangqiu bonus.");
            Require(g.CreateSnapshot(0).FriendPredictions is null && events.OfType<FriendPredictionSettledEvent>().Single().NextCount == (revealed ? 5 : 4),
                "Settlement removes the visible policy and changes the next sequence length.");
            if (revealed)
            {
                var original = (IList<string>)events.OfType<FriendPredictionRevealedEvent>().Single().Predictions;
                Require(original.IsReadOnly, "Committed reveal event collections freeze before observers.");
                try { original[0] = "黑色"; throw new InvalidOperationException("Reveal history was mutable."); } catch (NotSupportedException) { }
            }
        }
    }

    public static void XuanjianWholeSuitQihuiAndReclaim()
    {
        var (g, registry) = Start(true); Settle(g); Replay(g, registry);
        Require(g.CreateSnapshot(0).Players[0].Equipment.Any(e => e.Kind == CardKind.Xuanjian) &&
            g.State.Players[0].Markers!.Any(m => m.Kind == PlayerMarkerKind.QihuiEquipment),
            "Xiaxing obtains and really uses its generated Xuanjian, so Qihui sees an equipment use.");
        var hand = g.State.Players[0].Hand!.Select(c => c.Id).ToArray();
        var checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint());
        Require(!g.Submit(new UseProgramSkillCommand(0, "equipment:xuanjian", "suit-slash", [hand[0]], [1], g.Revision, P(g)!.PromptId)).Accepted &&
            GameCheckpointJson.Serialize(g.CreateCheckpoint()) == checkpoint,
            "An ordinary Xuanjian activation must select every hand card of that suit.");
        Accept(g, new UseProgramSkillCommand(0, "equipment:xuanjian", "suit-slash", hand, [1], g.Revision, P(g)!.PromptId)); Settle(g);
        Require(g.State.Players[0].Markers!.Any(m => m.Kind == PlayerMarkerKind.QihuiBasic), "The multi-card Xuanjian Slash is a real basic-card use.");
        Driver(g, "draw"); Settle(g); Play(g, CardKind.DrawTwo);
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == PlayerMarkerKind.QihuiEquipment.ToString()));
        Replay(g, registry); Value(g, PlayerMarkerKind.QihuiEquipment.ToString()); Value(g, PlayerMarkerKind.QihuiTrick.ToString());
        Replay(g, registry); Value(g, "unlimited"); Settle(g); Replay(g, registry);
        Require(g.State.Players[0].Markers!.Count(m => m.Kind is PlayerMarkerKind.QihuiBasic or PlayerMarkerKind.QihuiTrick or PlayerMarkerKind.QihuiEquipment) == 1,
            "Three categories remove exactly two chosen distinct marks before the benefit.");
        var next = g.State.Players[0].Hand!.Select(c => c.Id).ToArray();
        Accept(g, new UseProgramSkillCommand(0, "equipment:xuanjian", "suit-slash", next, [1], g.Revision, P(g)!.PromptId)); Settle(g); Replay(g, registry);
        Require(g.Events.Select(e => e.Payload).OfType<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0 && e.Debit.Query == SkillRuleQuery.SlashLimit) == 1 &&
            g.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash)
                .Select(e => e.Action.ActionId).Distinct().Count() == 2,
            "Qihui's next Slash bypasses the exhausted quota and adds no finite-use debit.");
        Driver(g, "draw"); Settle(g); Play(g, CardKind.DrawTwo); Settle(g); Driver(g, "discard-weapon");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "friend:xiaxing")); Activate(g);
        Replay(g, registry); Value(g, PlayerMarkerKind.QihuiBasic.ToString()); Value(g, PlayerMarkerKind.QihuiTrick.ToString()); Settle(g); Replay(g, registry);
        var generated = g.Events.Select(e => e.Payload).OfType<FriendXuanjianGeneratedEvent>().Single().CardId;
        Require(g.State.Players[0].Hand!.Any(c => c.Id == generated) && g.CardMovements.Count(m => m.CardId == generated &&
            m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0)) == 1,
            "Discarding the owner's weapon is eligible and paid mark removal reclaims the same physical entity once.");
    }

    private static void Value(GameEngine g, string value) => Answer(g, c => c.Parameters.GetValueOrDefault("value") == value);
    private static void Driver(GameEngine g, string activation) => Accept(g, new UseProgramSkillCommand(0, "fixture:friend-driver", activation, [], [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g, CardKind kind)
    {
        var card = g.State.Players[0].Hand!.First(c => c.Kind == kind);
        Accept(g, new PlayCardCommand(0, card.Id, [], g.Revision, P(g)!.PromptId));
    }
    private static (GameEngine, ContentRegistry) Start(bool xu, CardKind? responseKind = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(xu, responseKind));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:friend-fixture", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:friend", g.Revision, P(g)!.PromptId));
        if (!xu) Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "friend:yance"));
        return (g, registry);
    }
    private sealed class Fixture(bool xu, CardKind? responseKind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-friend-trio", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.FriendTrioContent")!
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [b]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
            var driver = SkillProgramCatalog.Load($$"""
              {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:friend-driver","revision":1,"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
              {"id":"discard-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]},
              {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]}]}
              """, """{"schemaVersion":3,"skills":{"fixture:friend-driver":{"name":"夹具","description":"夹具"}}}""").Programs["fixture:friend-driver"];
            b.AddSkill(new("fixture:friend-driver", "夹具", "fixture") { Program = driver });
            if (responseKind is CardKind.Dodge or CardKind.Nullification)
            {
                var trigger = responseKind == CardKind.Dodge ? """
                    {"id":"foreign","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}
                    """ : """
                    {"id":"foreign","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo"}]}
                    """;
                var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:friend-foreign","revision":1,"triggers":[{{trigger}}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:friend-foreign":{"name":"响应夹具","description":"响应夹具"}}}""");
                b.AddSkill(new("fixture:friend-foreign", "响应夹具", "fixture") { Program = catalog.Programs["fixture:friend-foreign"] });
            }
            b.AddGeneral(new("fixture:friend", xu ? "友徐庶" : "友诸葛亮", "supporter", xu ? "friend:xiaxing" : "friend:yance", "qun", 3,
                xu ? ["friend:qihui", "friend:gongli-xu-shu", "fixture:friend-driver"] : ["friend:fangqiu", "friend:gongli-zhuge-liang", "fixture:friend-driver"]));
            for (var i = 1; i <= 3; i++) b.AddGeneral(new($"fixture:friend-peer-{i}", "夹具对手", "supporter", "standard:none", "wei", 7,
                (responseKind is CardKind.Dodge or CardKind.Nullification) ? ["fixture:friend-foreign"] : []));
            b.AddDeck(new("fixture:friend-deck", "固定牌堆", 4, 1, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(responseKind switch
                { CardKind.Dodge => "standard:dodge", CardKind.Nullification => "standard:nullification", CardKind.Slash => "standard:slash", _ => "standard:draw_two" }, Suit.Heart, 5)).ToArray() });
            b.AddMode(new("identity:friend-fixture", "友夹具", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:friend-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:friend", "fixture:friend-peer-1", "fixture:friend-peer-2", "fixture:friend-peer-3"]));
        }
    }
}
