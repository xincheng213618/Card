using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class GuanYuVariantChecks
{

    public static void OlOpeningUsesAndIdentity()
    {
        var (g, r) = Start("mou:guan-yu-ol", mixed: true); Settle(g);
        var red = g.CreateSnapshot(0).Players[0].Hand!.First(c => c.Suit is Suit.Heart or Suit.Diamond);
        var conversion = g.GetHumanLegalActions().First(a => a.CardId == red.Id && a.TargetSeats.SequenceEqual([2]) &&
            a.PlayedCardKind == CardKind.FireSlash && a.ConversionSource?.SkillId == "mou-ol:weilin");
        Accept(g, new PlayCardCommand(0, red.Id, [2], g.Revision, P(g)!.PromptId, CardKind.FireSlash) { ConversionSource = conversion.ConversionSource });
        Settle(g); ReplayFourViews(g, r);
        Require(g.Events.Select(e => e.Payload).OfType<WeilinHandColorEvent>().Any(e => e.TargetSeat == 2) &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == "mou-ol:weilin"),
            "The first distant red converted use issues the target identity and consumes Weilin once per actual turn.");
        Require(g.CreateSnapshot(2).Players[2].Hand!.Where(c => c.Suit is Suit.Heart or Suit.Diamond)
            .All(c => EffectiveKind(g, 2, c.Id) == CardKind.Slash), "Same-color target hand cards are viewed as Slash.");
        Slash(g, 1); Settle(g);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The first basic use is free; the next ordinary Slash consumes the normal allowance.");
        Require(g.Events.Select(e => e.Payload).OfType<GuanOpeningUseEvent>().Count(e => e.OwnerSeat == 0) == 2,
            "Each actual use is recorded once, including a converted Slash.");
        ReplayFourViews(g, r);

        var (self, selfRegistry) = Start("mou:guan-yu-ol", mixed: true); Settle(self);
        var selfRed = self.CreateSnapshot(0).Players[0].Hand!.First(c => c.Suit is Suit.Heart or Suit.Diamond);
        var alcohol = self.GetHumanLegalActions().First(a => a.CardId == selfRed.Id && a.PlayedCardKind == CardKind.Alcohol &&
            a.ConversionSource?.SkillId == "mou-ol:weilin");
        Accept(self, new PlayCardCommand(0, selfRed.Id, alcohol.TargetSeats, self.Revision, P(self)!.PromptId, CardKind.Alcohol)
            { ConversionSource = alcohol.ConversionSource }); Settle(self);
        var rewritten = self.CreateSnapshot(0).Players[0].Hand!.First(c => c.Kind == CardKind.Dodge && c.Suit is Suit.Heart or Suit.Diamond);
        var rewrittenUse = self.GetHumanLegalActions().First(a => a.CardId == rewritten.Id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        Accept(self, new PlayCardCommand(0, rewritten.Id, [1], self.Revision, P(self)!.PromptId, CardKind.Slash)
            { ConversionSource = rewrittenUse.ConversionSource }); Settle(self); ReplayFourViews(self, selfRegistry);
        Require(self.Events.Select(e => e.Payload).OfType<WeilinHandColorEvent>().Count() == 1,
            "Using a rewritten Slash is a card identity, and does not issue Weilin a second time.");
    }

    public static void DemonRoundReturnAndDodgeAid()
    {
        var (g, r) = Start("magic:guan-yu"); Activate(g);
        Reach(g, p => p.Choices.Any(c => c.Targets.Contains(2))); Answer(g, c => c.Targets.SequenceEqual([2])); Settle(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == "magic:weiwo"); Skip(g);
        Reach(g, p => p.SkillPrompt?.SkillId == "magic:hanguo");
        Require(g.State.Players[2].HandCount == 12 && g.State.Players[2].RoundHoldCount == 0 &&
            g.CardMovements.Count(m => m.From == CardLocation.RoundHold(2) && m.To == CardLocation.Hand(2)) == 12,
            "Living held cards return once at the next round boundary.");
        ReplayFourViews(g, r); Activate(g);
        Reach(g, p => p.Choices.Any(c => c.Targets.Count == 1));
        Require(P(g)!.Choices.All(c => !c.Targets.Contains(2)), "The previous round's focus is excluded.");
        ReplayFourViews(g, r);

        var (aid, ar) = Start("magic:guan-yu", mixed: true); Activate(aid);
        Reach(aid, p => p.Choices.Any(c => c.Targets.Contains(1))); Answer(aid, c => c.Targets.SequenceEqual([1])); Settle(aid);
        var hp = aid.State.Players[1].Hp;
        Slash(aid, 1);
        Reach(aid, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondDodge);
        ReplayFourViews(aid, ar); Answer(aid, c => c.Parameters.GetValueOrDefault("response") == "faction-defense-decline");
        Settle(aid);
        var provided = aid.Events.Select(e => e.Payload).OfType<FactionDefenseResolvedEvent>().Last(e => e.Succeeded);
        Require(provided.OwnerSeat == 1 && provided.ProviderSeat is { } provider && provider != 0 &&
            aid.State.Players[1].Hp == hp && aid.State.Players[1].IsAlive &&
            aid.CardMovements.Count(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(provider) &&
                m.Reason.Value.Contains("magic:hanguo.SelectAndMoveOwnedCard")) == 1,
            "Another faction can supply Dodge and obtains exactly one magic owner's card through the private chooser: " +
            $"owner={provided.OwnerSeat}, provider={provided.ProviderSeat}, hp={aid.State.Players[1].Hp}/{hp}, " +
            string.Join("; ", aid.CardMovements.Where(m => m.Reason.Value.Contains("hanguo")).Select(m => $"{m.From}->{m.To}: {m.Reason.Value}")));
        ReplayFourViews(aid, ar);

        var (selfAid, sr) = Start("magic:guan-yu", mixed: true); Activate(selfAid);
        Reach(selfAid, p => p.Choices.Any(c => c.Targets.Contains(1))); Answer(selfAid, c => c.Targets.SequenceEqual([1])); Settle(selfAid);
        Slash(selfAid, 1); Reach(selfAid, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondDodge);
        Answer(selfAid, c => c.Cards.Count == 1);
        Reach(selfAid, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        ReplayFourViews(selfAid, sr); Answer(selfAid, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"); Settle(selfAid);
        Require(selfAid.State.Players[1].Hp == 8 && selfAid.Events.Select(e => e.Payload).OfType<FactionDefenseResolvedEvent>()
            .Any(e => e.OwnerSeat == 1 && e.ProviderSeat == 0 && e.Succeeded) &&
            !selfAid.CardMovements.Any(m => m.Reason.Value.Contains("magic:hanguo.SelectAndMoveOwnedCard")),
            "The magic owner can supply Dodge himself; choosing his own hand card does not manufacture loss/gain events.");
        ReplayFourViews(selfAid, sr);
    }

    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId != null && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, [target], g.Revision, P(g)!.PromptId, a.PlayedCardKind));
    }

    private static CardKind EffectiveKind(GameEngine g, int seat, int id)
    {
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        var zones = (CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        var card = zones.CardsAt(CardLocation.Hand(seat)).Single(c => c.Id == id);
        return (CardKind)typeof(GameEngine).GetMethod("AdvancedEffectiveHandKind", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [players[seat], card])!;
    }

    private static (GameEngine, ContentRegistry) Start(string general, int hp = 8, bool mixed = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(general, hp, mixed));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:classic-guan-variants", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, general, g.Revision, P(g)!.PromptId));
        for (var step = 0; step < 100; step++)
        {
            var p = P(g);
            if (p?.PlayerSeat == 0 && (p.SkillPrompt?.SkillId is "mou-mobile:wusheng" or "magic:hanguo" || p.Kind == DecisionKind.PlayCard)) return (g, r);
            if (p?.PlayerSeat == 0 && p.Choices.Count > 0) Answer(g, _ => true);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The Guan fixture did not reach its opening rule.");
    }

    private sealed class Fixture(string general, int hp, bool mixed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("guan-variants-fixture", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.GuanYuVariantsContent")!
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [b]);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            var blanks = Enumerable.Range(1, 3).Select(i => "fixture:guan-blank-" + i).ToArray();
            foreach (var id in blanks) b.AddGeneral(new(id, id, "supporter", "standard:none", "qun", hp));
            b.AddDeck(new("fixture:guan-deck", "Guan fixture", 12, 0, []) {
                PhysicalCards = Enumerable.Range(0, 128).Select(i => new ContentDeckPhysicalCard(
                    mixed && i % 3 == 0 ? "standard:dodge" : "standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-guan-variants", "Guan fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:guan-deck", GeneralCandidateCount: 4, GeneralPoolIds: [general, .. blanks]));
        }
    }
}
