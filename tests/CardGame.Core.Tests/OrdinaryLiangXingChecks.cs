using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Liang Xing: docs/content/sources/ol-liang-xing-2026-10-05.json.
internal static class OrdinaryLiangXingChecks
{
    private const string Luelve = "ol:luelve";
    private const string Zhuanxi = "ol:zhuanxi";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:liang-xing"];
        Require(general.Name == "梁兴" && general.FactionId == "qun" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Luelve, Zhuanxi]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Liang Xing general registers the current qun 4HP pair.");
        var luelve = registry.GetSkill(Luelve).Program!.Triggers.Single();
        Require(luelve.Window == SkillProgramTriggerWindow.PlayPhaseStarting && luelve.Optional &&
            luelve.Effects[0].Op == SkillProgramEffectOp.SelectTarget &&
            luelve.Effects[0].TargetKind == SkillProgramTargetKind.OtherLivingWithFewerHandCards &&
            luelve.Effects[1].Op == SkillProgramEffectOp.ChooseOption &&
            luelve.Effects[1].ChooserRef!.Kind == ProgramParticipantRef.SelectedTarget &&
            luelve.Effects[2].Op == SkillProgramEffectOp.GiveSelectedTargetHand &&
            luelve.Effects[3].Op == SkillProgramEffectOp.TurnOver &&
            luelve.Effects[4].Op == SkillProgramEffectOp.TurnOver &&
            luelve.Effects[4].Target == SkillProgramEffectTarget.SelectedTarget,
            "Luelve offers the counterpart the give-hand or flip-and-strike choice.");
        var zhuanxi = registry.GetSkill(Zhuanxi).Program!;
        var modifier = zhuanxi.DamageModifiers.Single();
        Require(modifier.Amount == 1 &&
            modifier.Condition == SkillProgramDamageModifierCondition.FaceStatesDiffer &&
            modifier.SourceScope == SkillProgramDamageModifierSourceScope.DamageParticipant,
            "Zhuanxi raises participant damage when the face states differ.");
        Require(registry.GetSkill(Luelve).ProgramPresentation!.Name == "掳掠" &&
            registry.GetSkill(Zhuanxi).ProgramPresentation!.Name == "追袭",
            "The presentation carries the current OL Luelve/Zhuanxi wording.");
    }

    public static void LuelveChoiceResolvesAndZhuanxiAmplifies()
    {
        var (g, r) = Start();
        EndMyPlayPhase(g);
        // The first round leaves every AI at four cards while the owner holds six,
        // so the next turn's Luelve prompt is the first stop of the settle loop.
        // Turn one has no fewer-hand counterpart yet; the loop answers every Luelve
        // activation until the selection prompt offers a real counterpart.
        var answered = false;
        var eventsBefore = 0;
        for (var i = 0; i < 800 && !answered; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                    c.Targets.Contains(1)))
            {
                Accept(g, new AnswerPromptCommand(0, p.PromptId,
                    p.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                        c.Targets.SequenceEqual([1])).Id, g.Revision));
                answered = true;
                eventsBefore = g.Events.Count;
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Luelve) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                continue;
            }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Require(answered, "A Luelve selection prompt never offered a counterpart.");
        var target = 1;
        SettleToNextOwnPlayPhaseSkipping(g);
        var choiceEvents = g.Events.Skip(eventsBefore).Select(e => e.Payload).ToList();
        var gave = choiceEvents.OfType<CardMovedEvent>().Any(m => m.From == CardLocation.Hand(target) &&
            m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("luelve", StringComparison.Ordinal));
        var damage = choiceEvents.OfType<DamageAppliedEvent>().Where(d => d.SourceSeat == target && d.TargetSeat == 0)
            .Sum(d => d.Amount);
        var ownerFlipped = g.CreateSnapshot(0).Players[0].IsFaceDown;
        if (gave)
        {
            Require(ownerFlipped && HandCount(g, target) == 0,
                "The give-hand branch moves the whole hand and flips the owner. hand=" + HandCount(g, target));
        }
        else
        {
            Require(damage is 1 or 2 && g.CreateSnapshot(0).Players[target].IsFaceDown,
                "The flip-and-strike branch flips the counterpart and damages the owner. damage=" + damage);
            if (g.CreateSnapshot(0).Players[0].IsFaceDown != g.CreateSnapshot(0).Players[target].IsFaceDown)
                Require(damage == 2, "Zhuanxi amplifies face-state-differing damage to two. damage=" + damage);
        }
        Replay(g, r);
    }

    private static int HandCount(GameEngine g, int seat) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!)
        .CardsAt(CardLocation.Hand(seat)).Count;

    private static void ReachProgramChoice(GameEngine g) => Reach(g, p =>
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip") &&
        (p.SkillPrompt?.SkillId is Luelve or Zhuanxi ||
         p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Luelve or Zhuanxi)));

    private static void EndMyPlayPhase(GameEngine g)
    {
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var p = P(g)!;
        if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
    }

    private static void SettleToNextOwnPlayPhaseSkipping(GameEngine g)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return;
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip") &&
                (p.SkillPrompt?.SkillId is Luelve or Zhuanxi ||
                 p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Luelve or Zhuanxi)))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the owner's play phase: " + P(g)?.Prompt);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-lx", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryLiangXingContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddGeneral(new("fixture:lx", "梁兴", "ol-liang-xing", Luelve, "qun", 4, [Zhuanxi]));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:lx-{i}", "其他" + i, "supporter", "standard:none", "wei", 6, null));
            b.AddDeck(new("fixture:lx-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ =>
                    new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 5)).ToArray()
            });
            b.AddMode(new("identity:lx", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:lx-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:lx", "fixture:lx-1", "fixture:lx-2", "fixture:lx-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:lx",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:lx", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
            (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Luelve) &&
             p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip")));
        if (P(g)!.Kind != DecisionKind.PlayCard)
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (g, r);
    }
}
