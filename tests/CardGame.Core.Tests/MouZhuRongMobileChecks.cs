using CardGame.Content.Standard;

namespace CardGame.Core.Tests;

internal static class MouZhuRongMobileChecks
{
    private const string Owner = "fixture:mzrm-owner", Driver = "fixture:mzrm-driver", Quiet = "fixture:mzrm-quiet";
    private const string Deck = "fixture:mzrm-deck", Mode = "identity:classic-mzrm";
    public static void NativeDesignationContestDeferredDamageAndOutsideAssaultGift()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        var initial = g.CreateSnapshot(0).Players[0];
        Need(initial.Hp == 9 && initial.SkillRuntimeStates?.Any(s => s.SkillId == "mou-mobile:lieren") == true &&
            g.Events.Select(e => e.Payload).OfType<HpConditionalSkillGrantChangedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == "mou-mobile:lieren" && e.Added && e.OwnerHp == 9),
            $"The native classic-identity fixture starts at 8+Lord HP with its actual conditional Lieren source enabled: hp={initial.Hp}/{initial.MaxHp}, role={initial.Role}, general={initial.GeneralId}, skills={string.Join(',', (initial.SkillRuntimeStates ?? []).Select(s => s.SkillId + ':' + s.IsAcquired))}.");
        var firstTurn = g.CreateSnapshot(0).TurnNumber; var targetHp = g.CreateSnapshot(0).Players[1].Hp;
        var otherHp = g.CreateSnapshot(0).Players[2].Hp;
        var slash = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Slash).Id;
        PlayPhysical(g, slash, [1]);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "mou-mobile:lieren" &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Need(g.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result.SourceWon &&
            g.Events.Select(e => e.Payload).OfType<MouZhuRongLierenWonEvent>().Count() == 1 &&
            g.CreateSnapshot(0).Players[1].Hp == targetHp - 1 && g.CreateSnapshot(0).Players[2].Hp == otherHp,
            "One true single-target Slash starts a paid native Pindian; winning waits for the original Slash damage before the new damage choice.");
        Need(P(g)!.Choices.All(c => c.Targets is [var t] && t != 0 && t != 1),
            "The deferred damage can select another other character, excluding both Zhu Rong and the original Slash target.");
        Need(g.CreateSnapshot(0).Players[0].Hp == 8 && !(g.CreateSnapshot(0).Players[0].SkillRuntimeStates?.Any(skill => skill.SkillId == "mou-mobile:lieren") ?? false) &&
            g.Events.Select(e => e.Payload).OfType<HpConditionalSkillGrantChangedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == "mou-mobile:lieren" && !e.Added && e.OwnerHp == 8),
            "The paid winning contest still offers its original-source tail after real HP loss removes the conditional Lieren source.");
        var saved = GameCheckpointJson.Serialize(g.CreateCheckpoint()); var view = SnapshotJson.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(saved), r);
        Need(SnapshotJson.Serialize(g.CreateSnapshot(0)) == view, "The representative pending deferred choice survives a real checkpoint restore.");
        Answer(g, P(g)!.Choices.Single(c => c.Targets.SequenceEqual([2]))); Play(g);
        Need(g.CreateSnapshot(0).Players[2].Hp == otherHp - 1 &&
            g.Events.Select(e => e.Payload).OfType<MouZhuRongLierenDamageChosenEvent>().Count() == 1 &&
            g.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.CardId == slash) == 1,
            "The later optional damage is issued once and the original whole Slash returns once.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "mou-mobile:juxiang" &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, P(g)!.Choices.Single(c => c.Targets.SequenceEqual([0]))); Play(g);
        var generated = g.Events.Select(e => e.Payload).OfType<MouZhuRongAssaultGeneratedEvent>().Single();
        Need(g.CreateSnapshot(0).TurnNumber != firstTurn && g.CardMovements.Count(m => m.CardId == generated.CardId &&
            m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)) == 1 &&
            g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == generated.CardId).Kind == CardKind.BarbarianAssault,
            "An Ending without an own Assault use creates a distinct real outside entity and gifts it once, even when the fixed mode stock has no Assault.");
        PlayPhysical(g, generated.CardId); Play(g);
        Need(g.Events.Select(e => e.Payload).OfType<MouZhuRongAssaultUsedEvent>().Any(e => e.ActorSeat == 0),
            "The gifted entity enters a genuine native Barbarian Assault use.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Need(g.Events.Select(e => e.Payload).OfType<MouZhuRongAssaultGeneratedEvent>().Count() == 1 &&
            g.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.CardId == generated.CardId) == 1,
            "A true Assault use suppresses this actual turn's Ending gift without affecting the completed native card's payment.");
    }
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static void PlayPhysical(GameEngine g, int id, IReadOnlyList<int>? targets = null)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId == id && (targets is null || a.TargetSeats.SequenceEqual(targets)));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Answer(GameEngine g, PromptChoice c) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, c.Id, g.Revision));
    private static void Reach(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (ready()) return;
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Unexpected terminal cutoff in the small native Zhu Rong scene.");
            var p = P(g);
            if (p is null) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.SkillModule && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"))
                Answer(g, p.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"));
            else if (p.Kind == DecisionKind.ProgramTrigger)
            {
                var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "activate")
                    ?? throw new InvalidOperationException("Unexpected native program choice: " + p.Prompt);
                Answer(g, choice);
            }
            else if (p.Kind.ToString().StartsWith("Pindian", StringComparison.Ordinal))
                Answer(g, p.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"));
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                Answer(g, p.Choices.First(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die"));
            else throw new InvalidOperationException("Unexpected native Zhu Rong boundary: " + p.Kind + " " + p.Prompt);
        }
        throw new InvalidOperationException("Small Zhu Rong scene exceeded its fixed cursor budget.");
    }
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(c); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Rejected native Zhu Rong command."); }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhu-rong-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:zhu-rong-mobile");
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + Driver + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-lieren\",\"skillId\":\"mou-mobile:lieren\",\"minimumHp\":9}],\"triggers\":[{\"id\":\"lose-paid-source\",\"window\":\"afterDamageApplied\",\"subject\":\"damageSource\",\"damageOccurrence\":\"perDamage\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}],\"cardPolicies\":[{\"id\":\"win-heart-contest\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"heart\",\"value\":13}]},{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var presentation = "{\"schemaVersion\":" + SkillProgramCatalog.PresentationSchemaVersion + ",\"skills\":{\"" + Driver + "\":{\"name\":\"Native rank fixture\",\"description\":\"Native printed-rank Pindian policy\"},\"" + Quiet + "\":{\"name\":\"Quiet\",\"description\":\"Small native peer turns\"}}}";
            var programs = SkillProgramCatalog.Load(rules, presentation);
            foreach (var id in new[] { Driver, Quiet }) b.AddSkill(new(id, "Native fixture", "Native fixture") { Program = programs.Programs[id], ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "原生谋祝融", "", Driver, "shu", 8, ["mou-mobile:juxiang"], GeneralGender.Female));
            for (var i = 1; i < 4; i++) b.AddGeneral(new("fixture:mzrm-peer-" + i, "其他角色", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "Small real Slash stock", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48)
                .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(Mode, "Native small Zhu Rong", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                Deck, GeneralCandidateCount: 4, GeneralPoolIds: new[] { Owner }.Concat(Enumerable.Range(1, 3).Select(i => "fixture:mzrm-peer-" + i)).ToArray()));
        }
    }
}
