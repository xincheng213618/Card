using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActivityOrdinaryTrickChecks
{
    private const string Driver = "fixture:activity-trick-driver", Quiet = "fixture:activity-trick-quiet", Mode = "fixture:activity-trick-mode", Actor = "fixture:activity-trick-actor";
    private const string Jiaozhao = "boundary:jiaozhao-round-current";

    public static void QiceUsesModeNamesWithAllRealMaterialsAndUnchangedSource()
    {
        foreach (var hulao in new[] { false, true })
        {
            var (g, registry) = Start(hulao ? "hulao" : "shenwu", ["classic:qice", "classic:jizhi"]);
            var source = registry.GetSkill("classic:qice").Program!; var hash = source.GameplayHash;
            var hand = V(g, 0).Hand.Select(c => c.Id).ToArray();
            Accept(g, new UseProgramSkillCommand(0, "classic:qice", "all-hand-as-ordinary-trick", hand, [], g.Revision, P(g)!.PromptId));
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "use-all-hand-as-ordinary-trick") == true);
            var kind = hulao ? CardKind.AllianceFeast : CardKind.SowBeans;
            var names = P(g)!.Choices.Select(c => c.Parameters.GetValueOrDefault("card-kind")).ToHashSet();
            Require(names.Contains(kind.ToString()) && (hulao ? !names.Contains(nameof(CardKind.FiveGrains)) && !names.Contains(nameof(CardKind.PeachGarden)) :
                !names.Contains(nameof(CardKind.DrawTwo)) && !names.Contains(nameof(CardKind.BorrowedSword)) && names.Contains(nameof(CardKind.TransplantWood))),
                "The all-ordinary producer offers the actual activity names and removes the activity's replaced names.");
            Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("card-kind") == kind.ToString());
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "classic:jizhi");
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == kind);
            Require(use.Action is { } action && action.PhysicalCards.Count == hand.Length &&
                action.ConversionChain.Single().SkillId == "classic:qice" &&
                g.CardMovements.Count(m => hand.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == hand.Length,
                "Qice pays every exact hand entity into one native activity-trick action.");
            Cold(g, registry); Play(g);
            Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "classic:jizhi" && e.Completed) == 1 &&
                source.GameplayHash == hash && registry.GetSkill("classic:qice").Program == source,
                "The static all-trick Jizhi filter recognizes the activity card and the real child returns once without rewriting source content.");
            Cold(g, registry);
        }
    }

    public static void TieredOneAndZeroMaterialUsesKeepNativeReceiptsAndQuota()
    {
        foreach (var tier in new[] { 0, 2 })
        {
            var (g, registry) = Start("shenwu", [Jiaozhao, "classic:jizhi"]);
            for (var i = 0; i < tier; i++)
            {
                Accept(g, new UseProgramSkillCommand(0, Driver, "upgrade", [], [], g.Revision, P(g)!.PromptId)); Play(g);
            }
            var original = registry.GetSkill(Jiaozhao).Program!;
            Require(original.ViewAs.Any(r => r.OutputKind == CardKind.DrawTwo) && original.ViewAs.All(r => r.OutputKind != CardKind.SowBeans),
                "Runtime activity projection leaves the registered source's original rules intact.");
            Require(g.GetHumanLegalActions().Any(a => a.ConversionSource?.BindingId == "fixed-named-drawtwo" && a.PlayedCardKind == CardKind.DrawTwo) &&
                g.GetHumanLegalActions().All(a => a.ConversionSource?.BindingId != "fixed-named-drawtwo" || a.PlayedCardKind == CardKind.DrawTwo),
                "An explicit one-name DrawTwo conversion stays DrawTwo despite the activity's all-ordinary name replacement.");
            var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Jiaozhao && a.PlayedCardKind == CardKind.SowBeans);
            var hand = V(g, 0).HandCount;
            Require(action.CardId == (tier == 2 ? 0 : action.CardId) && (tier == 2 || action.CardId > 0), "The published action retains the current tier's real material arity.");
            Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "classic:jizhi");
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.SowBeans);
            var issued = E<TieredRoundConversionUseIssuedEvent>(g).Single().Receipt;
            Require(issued.FrozenTier == tier && issued.MaterialCount == (tier == 2 ? 0 : 1) &&
                issued.GameplayHash == original.GameplayHash && issued.EffectiveKind == CardKind.SowBeans &&
                issued.Source.BindingId == $"level-{tier}-drawTwo" && use.Action!.PhysicalCards.Count == issued.MaterialCount,
                "Projected names keep the exact original binding, hash, tier, accepted action and physical-material invoice.");
            Cold(g, registry); Play(g);
            Require(V(g, 0).HandCount == hand - issued.MaterialCount + 1 + 4 &&
                !g.CardMovements.Any(m => m.CardId == 0) &&
                g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Jiaozhao),
                "Native god SowBeans and static Jizhi draw their real cards; the paid tier allowance prevents another conversion.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start("standard", [Jiaozhao]);
            var actions = g.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Jiaozhao).ToArray();
            Require(actions.Any(a => a.PlayedCardKind == CardKind.DrawTwo) && actions.All(a => a.PlayedCardKind is not (CardKind.SowBeans or CardKind.TransplantWood or CardKind.AllianceFeast)),
                "The same registered skills retain their original names when no activity is selected.");
            Cold(g, registry);
        }
    }

    public static void ZhenlieStaticOrdinaryFilterCancelsActualActivityTarget()
    {
        var (g, registry) = Start("hulao", ["classic:zhenlie"], card: "hulao:alliance-feast", quiet: false);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "classic:zhenlie");
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.AllianceFeast);
        Require(use.SourceSeat != 0 && use.TargetSeats.Contains(0), "A real other actor's physical activity card reaches the human target's original Zhenlie binding.");
        Cold(g, registry);
        Until(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id));
        Require(V(g, 0).Hp == 3 && E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "classic:zhenlie" && e.Completed) == 1 &&
            !E<HulaoAllianceFeastBenefitEvent>(g).Any(e => e.CardUseFrameId == use.Id && e.TargetSeat == 0),
            "The actual Zhenlie command loses one HP and nullifies just this activity-card target's native benefit.");
        Cold(g, registry);
    }

    public static void ClassicDeclaredEntityKeepsActivityNamePrivateDeclarerAndNativeCost()
    {
        const string declaration = "classic:jiaozhao";
        var (g, registry) = Start("hulao", [declaration, "classic:jizhi"], stateId: "declared-card-tier");
        var source = registry.GetSkill(declaration).Program!;
        var originalHash = source.GameplayHash;
        Accept(g, new UseProgramSkillCommand(0, Driver, "upgrade", [], [], g.Revision, P(g)!.PromptId));
        Play(g);
        var material = V(g, 0).Hand.First().Id;
        Accept(g, new UseProgramSkillCommand(0, declaration, "declare-card", [material], [], g.Revision, P(g)!.PromptId));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == declaration);
        Require(frame.CardDeclarationDraft is { Tier: 1, DeclarerSeat: null } draft && draft.CardId == material &&
            Enumerable.Range(0, 4).All(seat => g.CreateSnapshot(seat).PublicRevealedCards.Single().Id == material),
            "The classic upgraded declaration must publicly reveal its exact physical entity and keep the draft on its owning frame.");
        Cold(g, registry);
        Answer(g, choice => choice.Targets.Count == 1);
        var prompt = P(g)!;
        Require(prompt.PlayerSeat != 0 && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice") == nameof(CardKind.AllianceFeast)) &&
            prompt.Choices.All(choice => choice.Parameters.GetValueOrDefault("choice") is not (nameof(CardKind.FiveGrains) or nameof(CardKind.PeachGarden))) &&
            Enumerable.Range(0, 4).Where(seat => seat != prompt.PlayerSeat).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "Only the chosen nearest declarer sees the activity's actual ordinary names, including AllianceFeast and excluding its replaced names.");
        Cold(g, registry);
        Answer(g, choice => choice.Parameters.GetValueOrDefault("choice") == nameof(CardKind.AllianceFeast));
        Play(g);
        var converted = g.GetHumanLegalActions().First(action => action.ConversionSource?.SkillId == declaration &&
            action.PlayedCardKind == CardKind.AllianceFeast);
        Require(converted.CardId == material && !converted.TargetSeats.Contains(0) &&
            g.GetHumanLegalActions().Where(action => action.ConversionSource?.SkillId == declaration).All(action => action.CardId == material),
            "The activity name remains tied to exactly the declared hand entity and preserves classic Jiaozhao's owner exclusion.");
        var binding = converted.ConversionSource!.BindingId;
        var movementStart = g.CardMovements.Count;
        Accept(g, new PlayCardCommand(0, material, converted.TargetSeats, g.Revision, P(g)!.PromptId,
            converted.PlayedCardKind, converted.TargetCardId) { ConversionSource = converted.ConversionSource });
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "classic:jizhi");
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.AllianceFeast);
        Require(use.Action is { } action && action.PhysicalCards.Single().CardId == material &&
            action.ConversionChain.Single().BindingId == binding &&
            g.CardMovements.Skip(movementStart).Count(move => move.CardId == material && move.From == CardLocation.Hand(0) &&
                move.To == CardLocation.Processing) == 1,
            "The declared activity trick must pay the original entity once into one native action while retaining its original conversion binding.");
        Cold(g, registry);
        Play(g);
        Require(E<CardUseFinishedEvent>(g).Count(fact => fact.ResolutionId == use.Id) == 1 &&
            !E<HulaoAllianceFeastBenefitEvent>(g).Any(fact => fact.CardUseFrameId == use.Id && fact.TargetSeat == 0) &&
            g.CardMovements.Any(move => move.CardId == material && move.To == CardLocation.DiscardPile) &&
            !g.GetHumanLegalActions().Any(action => action.ProgramSkillId == declaration || action.ConversionSource?.SkillId == declaration) &&
            source.GameplayHash == originalHash && registry.GetSkill(declaration).Program == source &&
            source.ViewAs.All(rule => rule.OutputKind != CardKind.AllianceFeast),
            "Native completion preserves the owner exclusion and phase quota, discards the exact material and leaves source rules and their hash unchanged.");
        Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(string mode, string[] skills, string card = "standard:dodge", bool quiet = true,
        string stateId = "boundary-jiaozhao-round-tier")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new StandardHulaoActivityPackage(), new StandardShenwuActivityPackage(), new Fixture(mode, skills, card, quiet, stateId));
        var count = mode == "shenwu" ? 6 : 4;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = count, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Actor, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, g.CreateSnapshot(0).Players.Count).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 240; i++)
        {
            if (condition()) return;
            var p = P(g);
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p?.Choices.Count > 0 && p.Kind != DecisionKind.PlayCard) Answer(g, _ => true);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Activity-trick fixture missed its native boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, g.CreateSnapshot(0).Players.Count).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements), "Mode projection and native trick costs, choices and returns cold-replay from accepted commands.");
    }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(c); Require(result.Accepted, result.Error?.Message ?? "Activity trick command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string mode, string[] skills, string card, bool quiet, string stateId) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("activity-trick-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Driver}}}","revision":1,"viewAs":[{"id":"fixture-tier-reference","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"conversionStateId":"{{{stateId}}}"},{"id":"fixed-named-drawtwo","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "activations":[{"id":"upgrade","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"upgradeConversionTier","target":"owner","stateId":"{{{stateId}}}"}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Quiet, Driver }.ToDictionary(id => id, id => new { name = id, description = "Activity native trick fixture." }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Activity native trick fixture.") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Actor, "Activity trick actor", "", Driver, "god", 4, skills));
            var peers = Enumerable.Range(0, 15).Select(i => $"fixture:activity-trick-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, id, "", quiet ? Quiet : "hulao:mashu", "wei", 4));
            string[] gods = [Actor, "fixture:activity-trick-god-1", "fixture:activity-trick-god-2"];
            foreach (var id in gods.Skip(1)) b.AddGeneral(new(id, id, "", Quiet, "god", 4));
            string[] leaders = ["fixture:activity-trick-leader"], minions = ["fixture:activity-trick-left", "fixture:activity-trick-right"];
            foreach (var id in leaders.Concat(minions)) b.AddGeneral(new(id, id, "", quiet ? Quiet : "hulao:mashu", "qun", 20));
            const string deck = "fixture:activity-trick-deck";
            b.AddDeck(new(deck, "Real native trick materials", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 220).Select(_ => new ContentDeckPhysicalCard(card, Suit.Heart, 7)).ToArray() });
            if (mode == "shenwu")
                b.AddMode(new(Mode, Mode, 6, 6, new Dictionary<string, int>(), deck, 5, peers.Concat(gods).Concat(leaders).Concat(minions).ToArray(), ContentModeKind.Team,
                    new Dictionary<string, int> { ["team:allies"] = 3, ["team:beasts"] = 3 })
                { ShenwuActivity = new(peers, gods, leaders, minions, [0, 1, 2], 4, [3, 5], 1, [], new Dictionary<string, string> { [leaders[0]] = "shenwu:yinzei" }) });
            else if (mode == "hulao")
                b.AddMode(new(Mode, Mode, 4, 4, new Dictionary<string, int>(), deck, 4, new[] { Actor, peers[0], peers[1], leaders[0] }, ContentModeKind.Team,
                    new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
                { BossActivity = new([Actor, peers[0], peers[1]], [new("first", leaders, 8), new("second", leaders, 4)], 1, []), HulaoActivity = new(4, 2) });
            else
                b.AddMode(new(Mode, Mode, 4, 4, new Dictionary<string, int>(), deck, 4, new[] { Actor, peers[0], peers[1], peers[2] }, ContentModeKind.Team,
                    new Dictionary<string, int> { ["team:allies"] = 2, ["team:boss"] = 2 }));
        }
    }
}
