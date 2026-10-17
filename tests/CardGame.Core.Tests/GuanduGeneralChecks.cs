using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuanduGeneralChecks
{
    private const string Mode = "fixture:guandu-generals", Driver = "fixture:guandu-general-driver", Quiet = "fixture:guandu-general-quiet", Watch = "fixture:guandu-general-watch";

    public static void ShicaiPrivateTopAndFushiLivingFactionQualificationSurviveColdReplay()
    {
        var (g, r) = Start(["shicai", "fushi", "zezhu", "chenggong"]);
        Need(g.CreateSnapshot(0).PrivateRevealedCards?.Count == 1 && Enumerable.Range(1, 7).All(s => g.CreateSnapshot(s).PrivateRevealedCards is null or { Count: 0 }),
            "Only Shicai's actual owner sees the current physical top card during that owner's Play phase.");
        Need(!HasSkillAction(g, "guandu:zezhu"), "Equal living Wei and Qun populations grant neither Fushi branch.");
        Activate(g, "guandu:shicai"); Until(g, () => Stage(g) == "shicai-cost"); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "discard");
        Until(g, () => E<GuanduShicaiObtainedEvent>(g).Any()); Play(g);
        var gained = E<GuanduShicaiObtainedEvent>(g).Single();
        Need(V(g, 0).Hand.Any(c => c.Id == gained.CardId) && !HasSkillAction(g, "guandu:shicai"),
            "A real paid discard obtains the current top entity and locks Shicai while that exact entity remains in hand."); Cold(g, r);
        Use(g, "discard", cards: [gained.CardId]); Play(g);
        Need(HasSkillAction(g, "guandu:shicai") && g.CardMovements.Count(m => m.CardId == gained.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
            "The actual acquired entity's public departure unlocks the next Shicai activation once.");
        Use(g, "kill", 1); Play(g);
        Need(!V(g, 1).IsAlive && HasSkillAction(g, "guandu:zezhu"), "A real allied Wei death makes Qun the larger living faction and enables Zezhu immediately.");
        Use(g, "kill", 5); Play(g);
        Need(!V(g, 5).IsAlive && !HasSkillAction(g, "guandu:zezhu"), "A later Qun death restores the tie and removes the derived activation immediately."); Cold(g, r);
    }

    public static void JieliangPaysBeforeForeignNormalDrawAndUsesActualTurnHandLimit()
    {
        var (g, r) = Start(["jieliang"]);
        Need(!E<GuanduJieliangPaidEvent>(g).Any(), "Jieliang cannot observe its own normal Draw phase.");
        End(g); Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:jieliang");
        Answer(g, IsActivate); Until(g, () => Stage(g) == "jieliang-cost");
        var target = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().GuanduGeneral!.TargetSeat;
        var before = V(g, 0).HandCount; var baseLimit = V(g, target).HandLimit; Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "discard");
        Until(g, () => g.CardMovements.Any(m => m.TurnNumber == E<GuanduJieliangPaidEvent>(g).Single().ActualTurn &&
            m.To == CardLocation.Hand(target) && m.Reason == CardMoveReasons.Draw));
        var paid = E<GuanduJieliangPaidEvent>(g).Single();
        Need(V(g, 0).HandCount == before - 1 && V(g, target).HandLimit == baseLimit - 1 &&
            g.CardMovements.Count(m => m.TurnNumber == paid.ActualTurn && m.To == CardLocation.Hand(target) && m.Reason == CardMoveReasons.Draw) == 1 &&
            E<TurnRuleModifierGrantedEvent>(g).Count(e => e.Modifier.Source.SkillId == "guandu:jieliang") == 1,
            "The foreign role's actual normal draw loses one card only after the owner's real discard, with exactly one scoped hand-limit penalty."); Cold(g, r);
    }

    public static void GrainCollapseAndIncomingOnlyGangzhiUseNativeDrawAndHpReplacement()
    {
        var (g, r) = Start(["cangchu", "sushou", "liangying"], ["gangzhi"]);
        Need(V(g, 0).Markers!.Single(m => m.Kind == PlayerMarkerKind.GuanduGrain).Count == 3 &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 3,
            "Three real opening grain marks give the allied owner one additional actual normal draw.");
        var hp = V(g, 4).Hp; var draws = Enumerable.Range(4, 4).Select(s => V(g, s).HandCount).ToArray();
        Use(g, "damage", 4); Play(g);
        Need(V(g, 4).Hp == hp - 1 && E<DamageReplacedWithHpLossEvent>(g).Any(e => e.TargetSeat == 4) && !E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 4),
            "The activity Gangzhi replaces incoming damage with native HP loss before damage-only triggers.");
        var maximum = V(g, 0).MaxHp;
        for (var i = 0; i < 3; i++) { Use(g, "fire", 0); Play(g); }
        Need((V(g, 0).Markers ?? []).All(m => m.Kind != PlayerMarkerKind.GuanduGrain || m.Count == 0) && V(g, 0).MaxHp == maximum - 1 &&
            Enumerable.Range(4, 4).All(s => V(g, s).HandCount == draws[s - 4] + 2) &&
            E<GuanduGeneralDrawEvent>(g).Count(e => e.Requested == 2 && e.RecipientSeat >= 4) == 4,
            "Each real fire-damage point removes one grain; the first exhaustion lowers maximum HP once and issues four separate real two-card enemy draws.");
        Use(g, "fire", 0); Play(g);
        Need(V(g, 0).MaxHp == maximum - 1 && E<GuanduGeneralDrawEvent>(g).Count(e => e.Requested == 2 && e.RecipientSeat >= 4) == 4,
            "Later fire damage at zero grain does not repeat the collapse or its paid draws."); Cold(g, r);
    }

    public static void ThunderChariotGeneratesRealEquipmentDiscardsBothSlotsAndDestroysOnDeparture()
    {
        var (g, r) = Start(["polu"]);
        var chariot = V(g, 0).Equipment.Single(c => c.Kind == CardKind.ThunderChariot);
        Need(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 4) && E<GuanduChariotCreatedEvent>(g).Single().CardId == chariot.Id &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == chariot.Id).Location == CardLocation.Equipment(0),
            "Polu creates and places one registered physical range-nine weapon on the real turn boundary.");
        foreach (var kind in new[] { CardKind.BaguaFormation, CardKind.DefensiveHorse })
        { var card = V(g, 0).Hand.First(c => c.Kind == kind); Use(g, "place", 5, [card.Id]); Play(g); }
        var equipment = V(g, 5).Equipment.Select(c => c.Id).ToArray();
        Need(equipment.Length == 2, "The native placement fixture puts a real armor and defensive mount on the eventual damaged role.");
        Use(g, "damage", 5); Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:thunder-chariot"); Cold(g, r);
        Answer(g, IsActivate); Play(g);
        Need(V(g, 5).Equipment.Count == 0 && equipment.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Equipment(5) && m.To == CardLocation.DiscardPile) == 1),
            "After genuine non-Slash damage, the equipped weapon optionally discards both actual eligible slots once.");
        Use(g, "discard", cards: [chariot.Id]); Play(g);
        Need(g.CreateCardZoneDiagnostics().Single(c => c.CardId == chariot.Id).Location == CardLocation.OutsideGame &&
            E<GuanduChariotDestroyedEvent>(g).Count(e => e.CardId == chariot.Id && e.FormerOwnerSeat == 0) == 1 && V(g, 0).Equipment.Count == 0,
            "Leaving the equipment zone destroys the same physical weapon and removes its granted skill."); Cold(g, r);
        var hand = V(g, 0).HandCount;
        Use(g, "damage", 0); Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any()); Cold(g, r); Play(g);
        Need(V(g, 0).HandCount == hand + 1 && E<GuanduChariotCreatedEvent>(g).Count() == 1 && E<GuanduGeneralDrawEvent>(g).Count(e => e.Requested == 1 && e.RecipientSeat == 0) == 1,
            "Bare Polu's damage reward is a real once-paid native draw and does not recreate the weapon until a later turn boundary.");
    }

    public static void ChoulueGiftIssuesRealVirtualDuelAndFireAttackAcrossColdChildren()
    {
        foreach (var kind in new[] { CardKind.Duel, CardKind.FireAttack })
        {
            var (g, r) = Start(["choulue"]);
            if (kind == CardKind.Duel) { Use(g, "duel-me", 5); Play(g); }
            else
            {
                Physical(g, CardKind.FireAttack, [0]);
                Until(g, () => P(g)?.Kind == DecisionKind.FireAttackReveal); Answer(g, c => c.Cards.Count == 1);
                Until(g, () => P(g)?.Kind == DecisionKind.FireAttackDiscard); Answer(g, c => c.Cards.Count == 1); Play(g);
            }
            Need(E<GuanduDamagingCardRecordedEvent>(g).Last(e => e.TargetSeat == 0).Kind == kind,
                "Only a genuine damaging non-delayed card use records Choulue's next copy name.");
            End(g); Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:choulue"); Answer(g, IsActivate);
            Until(g, () => Stage(g) == "choulue-target"); Answer(g, c => c.Targets.SequenceEqual([1]));
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "use-virtual-ordinary-trick") == true);
            var armed = E<GuanduChoulueArmedEvent>(g).Single(); Cold(g, r);
            Answer(g, c => c.Targets.SequenceEqual([4]));
            Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.VirtualOrdinaryTrickOrigin?.SkillId == "guandu:choulue"));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.VirtualOrdinaryTrickOrigin?.SkillId == "guandu:choulue");
            Need(use.CardKind == kind && use.CardId == 0 && use.Action!.PhysicalCards.Count == 0 && use.TargetSeats.SequenceEqual([4]) &&
                g.CardMovements.Count(m => m.CardId == armed.GiftCardId && m.Sequence > armed.Before && m.Sequence <= armed.After && m.To == CardLocation.Hand(0)) == 1,
                "The one paid real gift owns an exact zero-material native card with its actual chosen target and counterspell/response children."); Cold(g, r); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && !g.CardMovements.Any(m => m.CardId == 0),
                "The cold-resumed copy completes its real card-use and child return exactly once without inventing a physical card zero."); Cold(g, r);
        }
    }

    public static void YuanlveExactGiftAndMoushiNextActualPlayUseNativeDrawReceipts()
    {
        var (g, r) = Start(["yuanlve", "moushi"]);
        var slash = V(g, 0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        Activate(g, "guandu:yuanlve"); Until(g, () => Stage(g) == "yuanlve-give");
        Answer(g, c => c.Cards.SequenceEqual([slash]) && c.Targets.SequenceEqual([0]));
        Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "jianji-use") == true); Cold(g, r);
        Answer(g, c => c.Targets.SequenceEqual([7]));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any()); Cold(g, r); Play(g);
        Need(E<JianjiUseReturnedEvent>(g).Single().Receipt.CardId == slash && E<GuanduGeneralDrawEvent>(g).Count(e => e.RecipientSeat == 0 && e.Requested == 1) == 1 &&
            g.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.Processing) == 1,
            "Yuanlve offers the original gift through the shared true card-use bridge and rewards only its exact completed native use once.");
        Activate(g, "guandu:moushi"); Until(g, () => Stage(g) == "moushi-give"); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        var producer = E<GuanduMoushiGiftedEvent>(g).Single();
        Use(g, "damage", 4); Play(g); Need(!E<GuanduMoushiDrawnEvent>(g).Any(), "The current Play phase is too early for the newly paid Moushi gift.");
        Use(g, "skip-next"); Play(g); End(g); Play(g);
        var skipped = E<TurnStartedEvent>(g).First(e => e.ActorSeat == 0 && e.TurnNumber > producer.CreatedTurn).TurnNumber;
        var nextPlay = E<ActualDiscardRecoveryPhaseStartedEvent>(g).Last(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play).Phase;
        Need(nextPlay.TurnNumber > skipped && !E<ActualDiscardRecoveryPhaseStartedEvent>(g).Any(e => e.Phase.ActorSeat == 0 &&
            e.Phase.TurnNumber == skipped && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play), "The paid relation's next actual turn genuinely skips its Play phase.");
        Use(g, "damage", 4); Play(g);
        Need(!E<GuanduMoushiDrawnEvent>(g).Any(e => e.ProducerFrameId == producer.FrameId), "Skipping the gifted role's next actual turn Play expires the old relation before any later complete turn.");
        Activate(g, "guandu:moushi"); Until(g, () => Stage(g) == "moushi-give"); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        var activeGift = E<GuanduMoushiGiftedEvent>(g).Last(); End(g); Play(g);
        nextPlay = E<ActualDiscardRecoveryPhaseStartedEvent>(g).Last(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play).Phase;
        Need(E<TurnStartedEvent>(g).First(e => e.ActorSeat == 0 && e.TurnNumber > activeGift.CreatedTurn).TurnNumber == nextPlay.TurnNumber,
            "The second real gift is now observed in its recipient's exact next actual turn and true Play phase.");
        Use(g, "damage", 4); Until(g, () => E<GuanduMoushiDrawnEvent>(g).Any() && P(g)?.SkillPrompt?.SkillId == Watch); Cold(g, r); Play(g);
        Use(g, "damage", 4); Play(g); Use(g, "damage", 5); Play(g);
        Need(E<GuanduMoushiDrawnEvent>(g).Where(e => e.ProducerFrameId == activeGift.FrameId).Select(e => e.VictimSeat).SequenceEqual([4, 5]) &&
            E<GuanduMoushiDrawnEvent>(g).Where(e => e.ProducerFrameId == activeGift.FrameId).All(e => e.ActualPlayToken == nextPlay.Token) &&
            !E<GuanduMoushiDrawnEvent>(g).Any(e => e.ProducerFrameId == producer.FrameId),
            "Only the gifted role's next actual turn Play draws for the source, once per genuinely damaged recipient in that true phase."); Cold(g, r);
    }

    public static void FuyuanOffTurnNativeUseAndZhongjieOwnerDeathKeepTheirPaidChildren()
    {
        var (g, r) = Start(["fuyuan", "zhongjie"], foreignGifts: true);
        Use(g, "sweep"); Play(g); End(g);
        Until(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "jianji-use"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option") != "decline" && c.Targets.Any(s => s >= 4));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:fuyuan"); Cold(g, r);
        Answer(g, IsActivate);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
        Need(E<GuanduGeneralIssuedEvent>(g).Any(e => e.SkillId == "guandu:fuyuan") && V(g, 0).HandCount == 0,
            "A real off-turn issued card pays its material before Fuyuan compares hands and pauses on the smaller hand's native draw."); Cold(g, r); Play(g);
        Need(E<GuanduGeneralDrawEvent>(g).Any(e => e.RecipientSeat == 0 && e.Requested == 1), "Fuyuan's actual off-turn user receives its true one-card draw after the child returns.");
        var maximum = V(g, 2).MaxHp; var hand = V(g, 2).HandCount;
        Use(g, "kill", 0); Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:zhongjie");
        Answer(g, IsActivate); Until(g, () => Stage(g) == "zhongjie-target"); Cold(g, r);
        Answer(g, c => c.Targets.SequenceEqual([2]));
        Until(g, () => E<GuanduGeneralCompletedEvent>(g).Any(e => e.SkillId == "guandu:zhongjie"));
        Need(!V(g, 0).IsAlive && V(g, 2).MaxHp == maximum + 1 && V(g, 2).Hp == maximum + 1 && V(g, 2).HandCount == hand + 1 &&
            E<GuanduGeneralDrawEvent>(g).Any(e => e.RecipientSeat == 2 && e.Requested == 1),
            "The dead owner's exact Zhongjie program increases the chosen ally's maximum HP, recovers one, then performs its actual one-card native draw before battle completion."); Cold(g, r);
    }

    public static void SuishiUsesTrueDyingEntryAndFactionDeathWithoutConfusingTheArmy()
    {
        var (g, r) = Start(["suishi"]); var hp = V(g, 0).Hp;
        Use(g, "kill", 5);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
        Need(E<PlayerDyingEvent>(g).Any(e => e.VictimSeat == 5) && !E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 5) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "guandu:suishi" && f.WindowContext?.Window == SkillProgramTriggerWindow.DyingEntering),
            "A true damage-caused other dying entry owns the mandatory draw before the real rescue/death cursor continues."); Cold(g, r); Play(g);
        Need(!V(g, 5).IsAlive && V(g, 0).Hp == hp && E<GuanduGeneralDrawEvent>(g).Count(e => e.RecipientSeat == 0) == 1,
            "A Wei damage source qualifies Suishi's draw, while the dead Qun enemy does not qualify its faction-based HP loss.");
        Use(g, "kill", 1); Play(g);
        Need(!V(g, 1).IsAlive && V(g, 0).Hp == hp - 1 && E<GuanduGeneralDrawEvent>(g).Count(e => e.RecipientSeat == 0) == 2 &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.SkillId == "guandu:suishi" && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "A later Wei death separately causes exactly one native HP loss; the earlier dying draw is neither repeated nor substituted for death."); Cold(g, r);
    }

    public static void XiyingPaidRefusalBlocksTrueUseAndResponseForOnlyTheActualTurn()
    {
        var (g, r) = Start(["xiying"], reachPlay: false);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:xiying"); Answer(g, IsActivate);
        Until(g, () => Stage(g) == "xiying-cost"); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "discard"); Play(g);
        Need(E<GuanduAllCardBanIssuedEvent>(g).Any(e => e.TargetSeat == 1), "The allied AI's genuine refusal creates an exact actual-turn all-card ban after the owner's cost.");
        var revision = g.Revision;
        var rejected = g.Submit(new UseProgramSkillCommand(0, Driver, "duel-me", [], [1], revision, P(g)!.PromptId));
        Need(!rejected.Accepted && g.Revision == revision && !E<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 1 && e.CardKind == CardKind.Duel),
            "A banned role cannot issue a native virtual Duel through another character's selected-actor request.");
        var hp = V(g, 1).Hp; Physical(g, CardKind.Slash, [1]); Play(g);
        Need(V(g, 1).Hp == hp - 1 && !E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 1),
            "The same exact ban also prevents the damaged ally's real card response, without bypassing the ordinary Slash damage route."); Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Start(string[] skills, string[]? enemy = null, bool foreignGifts = false, bool reachPlay = true)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(skills, enemy ?? [], foreignGifts));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:cao", HumanGuanduRole = GuanduSeatRole.Commander, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 48 }, r);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, P(g)!.Choices.First().ContentIds.Single(), g.Revision, P(g)!.PromptId)); if (reachPlay) Play(g); return (g, r);
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 8).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static string? Stage(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.GuanduGeneral?.Stage;
    private static bool IsActivate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool HasSkillAction(GameEngine g, string skill) => g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == skill);
    private static void Activate(GameEngine g, string skill) => Accept(g, new UseProgramSkillCommand(0, skill, "play", [], [], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int? target = null, IReadOnlyList<int>? cards = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, cards ?? [], target is { } t ? [t] : [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Physical(GameEngine g, CardKind kind, IReadOnlyList<int> targets)
    { var a = g.GetHumanLegalActions().First(a => a.Kind.ToString() == kind.ToString() && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets)); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId)); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Until(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 480; i++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0 } p)
            {
                var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip" ||
                    c.Parameters.GetValueOrDefault("response") is "pass" or "decline" or "take-damage" or "fire-attack-skip" || c.Parameters.GetValueOrDefault("option") == "decline");
                if (choice is not null) { Answer(g, c => c.Id == choice.Id); continue; }
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException($"Guandu general boundary missing: status={g.CreateSnapshot(0).Status}, prompt={P(g)?.Kind}/{P(g)?.SkillPrompt?.SkillId}, stage={Stage(g)}.");
    }
    private static void Cold(GameEngine g, ContentRegistry r) => GuanduCardChecks.Cold(g, r);
    private static void Accept(GameEngine g, GameCommand c) => GuanduCardChecks.Accept(g, c);
    private static void Need(bool condition, string text) => GuanduCardChecks.Need(condition, text);

    private sealed class Fixture(string[] human, string[] enemy, bool foreignGifts) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-guandu-generals", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            foreach (var bundle in new[] { "guandu-activity", "guandu-events" })
            {
                using var rules = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.rules.json")!);
                using var presentation = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.presentation.json")!);
                var actual = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
                foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                    { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id is "guandu:yongdi" or "guandu:niepan" ? SkillTag.Limited : SkillTag.None });
            }
            var json = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"initialHandSize","operation":"add","value":16,"priority":0,"condition":{"kind":"always"}}],
              "activations":[
               {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
               {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
               {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
               {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
               {"id":"sweep","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
               {"id":"skip-next","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:guandu-general-skipplay"]}]},
               {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLivingMale","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"cost"}]},
               {"id":"duel-me","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
             {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"draw","window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"fixture:guandu-general-skipplay","revision":1,"triggers":[{"id":"skip-once","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var own = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Watch, Quiet, "fixture:guandu-general-skipplay" }.ToDictionary(id => id, id => new { name = id, description = "Compact native Guandu general fixture." }) }));
            foreach (var (id, program) in own.Programs) b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = own.Presentations[id] });
            foreach (var kind in new[] { CardKind.Intoxication, CardKind.StoreGrain, CardKind.Reinforcements, CardKind.UnitedEffort, CardKind.ThunderChariot })
                b.AddCard(new("fixture:guandu-general-card-" + kind, kind.ToString(), CardCatalog.Get(kind).CategoryName, kind.ToString(), kind));
            var pools = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var army in new[] { "cao", "yuan" })
            {
                var ids = Enumerable.Range(0, 9).Select(i => $"fixture:guandu-general-{army}-{i}").ToArray();
                for (var i = 0; i < ids.Length; i++)
                {
                    var skillIds = army == "cao" && i != 0 && foreignGifts ? new[] { "guandu:yuanlve" } : army == "cao" && i == 0 ? new[] { Driver, Watch, Quiet }.Concat(human.Select(s => "guandu:" + s)) :
                        new[] { Quiet }.Concat(army == "yuan" && i == 0 ? enemy.Select(s => "guandu:" + s) : []);
                    var all = skillIds.ToArray(); b.AddGeneral(new(ids[i], ids[i], "", all[0], army == "cao" ? "wei" : "qun", 8, all.Skip(1).ToArray()));
                }
                pools["team:" + army] = ids;
            }
            var cards = new[] { "standard:slash", "standard:bagua", "standard:defensive_horse", "standard:fire_attack", "standard:slash", "standard:slash" };
            const string deck = "fixture:guandu-general-deck";
            b.AddDeck(new(deck, deck, 4, 2, []) { PhysicalCards = Enumerable.Range(0, 360).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            var layout = new[] { "team:cao", "team:cao", "team:cao", "team:cao", "team:yuan", "team:yuan", "team:yuan", "team:yuan" };
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 6, pools.Values.SelectMany(ids => ids).ToArray(), ContentModeKind.Team,
                new Dictionary<string, int> { ["team:cao"] = 4, ["team:yuan"] = 4 })
            { GuanduActivity = new(pools, new Dictionary<string, string> { ["team:cao"] = pools["team:cao"][0], ["team:yuan"] = pools["team:yuan"][0] },
                [GuanduBattleEvent.Underdog], Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)layout).ToArray(), PlayerTier: 1) });
        }
    }
}
