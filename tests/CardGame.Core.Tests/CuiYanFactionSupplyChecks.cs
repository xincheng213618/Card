using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CuiYanFactionSupplyChecks
{
    private const string Owner = "fixture:cfs-owner", Provider = "fixture:cfs-provider", Mode = "identity:classic-cui-faction";
    private const string Driver = "fixture:cfs-driver", Watch = "fixture:cfs-watch", Quiet = "fixture:cfs-quiet";
    private static string Scene = "";

    public static void RequesterSuitRestrictionAndNativeLiubingProjectionCold()
    {
        foreach (var liubing in new[] { false, true })
        {
            Scene = liubing ? "requester-liubing" : "requester-heart-forbidden";
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(liubing));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, ModeId = Mode, PlayerCount = 4,
                HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            Accept(g, new StartGameCommand());
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
            Play(g);
            var provider = Enumerable.Range(1, 3).Single(s => V(g, s).GeneralId == Provider);
            var target = new[] { 1, 3 }.First(s => s != provider);
            var originalHand = V(g, provider).Hand.Select(c => c.Id).ToArray();
            Need(originalHand.Length > 0 && V(g, provider).Hand.All(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart),
                "The actual allied provider owns genuine Heart Slash entities before any request.");
            var wine = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.SkillId == Driver);
            Accept(g, new PlayCardCommand(0, wine.CardId!.Value, wine.TargetSeats, g.Revision, P(g)!.PromptId,
                wine.PlayedCardKind, wine.TargetCardId) { ConversionSource = wine.ConversionSource });
            Play(g);
            var grant = E<ShutongNextUseGrantedEvent>(g).Single(e => e.RecipientSeat == 0);
            Need(grant.Suit == Suit.Heart && !E<ShutongNextUseConsumedEvent>(g).Any(),
                "A real native self Wine first issues the requester's Heart prohibition without spending a Slash.");
            var request = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == "classic:jijiang");
            Accept(g, new UseProgramSkillCommand(0, "classic:jijiang", request.ProgramActivationId!, [], [target], g.Revision, P(g)!.PromptId));
            if (!liubing)
            {
                Play(g);
                Need(V(g, provider).Hand.Select(c => c.Id).SequenceEqual(originalHand) &&
                    !E<CardUseDeclaredEvent>(g).Any(e => e.CardKind == CardKind.Slash) &&
                    !E<ShutongNextUseConsumedEvent>(g).Any(e => e.GrantFrameId == grant.FrameId),
                    "An unavailable Heart supply fails through the original request without paying a provider card or consuming the ban.");
                _ = Cold(g, registry);
                continue;
            }
            Reach(g, () => P(g) is { Kind: DecisionKind.RespondSlash } p && p.PlayerSeat == provider);
            var prompt = P(g)!;
            Need(prompt.Choices.Any(c => c.Cards.Count == 1 && originalHand.Contains(c.Cards[0])) &&
                g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(target).PendingDecision is null,
                "The exact provider privately sees the real Heart material because the actual requester's first physical Slash will be Diamond.");
            g = Cold(g, registry);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Last(u => u.CardKind == CardKind.Slash);
            var action = use.Action!;
            Need(action.ActorSeat == 0 && action.ProviderSeat == provider && action.RequesterSeat == 0 &&
                action.PhysicalCards is [var cost] && originalHand.Contains(cost.CardId) && cost.From == CardLocation.Hand(provider) &&
                action.EffectiveSuit == Suit.Diamond && action.EffectiveIsRed == true && action.ConversionChain.Count == 0 &&
                V(g, provider).HandCount == originalHand.Length - 1 &&
                E<ShutongNextUseConsumedEvent>(g).Count(e => e.GrantFrameId == grant.FrameId && e.ActionId == action.ActionId) == 1,
                "The accepted supplied Use freezes actual-user Liubing appearance, separate provider payment, and one consumed Heart restriction.");
            var useId = use.Id;
            var paidId = action.PhysicalCards.Single().CardId;
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1 &&
                g.CardMovements.Count(m => m.CardId == paidId && m.From == CardLocation.Hand(provider) && m.To == CardLocation.Processing) == 1 &&
                !g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == useId),
                "Cold completion pays the real provider exactly once and returns through the original Jijiang parent.");
            _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason + " Scene=" + Scene); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, (result.Error?.Message ?? "Rejected native faction command") + " " + Diagnostic(g));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var p = P(g)!; var c = p.Choices.FirstOrDefault(choose);
        Need(p.PlayerSeat == 0 && c is not null, "Missing actual human choice: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision));
    }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 220; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Unexpected completion at line " + line + ": " + Diagnostic(g));
            var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.SkillPrompt?.SkillId == Watch) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException("Unexpected faction prompt at line " + line + ": " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing faction boundary at line " + line + ": " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.Phase, g.State.Winner,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(3).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(State(g) == State(replay), "Cold recovery preserves the private request, raw material, appearance and once-return proof."); return replay;
    }

    private sealed class Fixture(bool liubing) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cui-faction", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cui-yan", "classic:liu-bei", "ol:wu-jing");
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"real-wine","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"inputCount":1,"outputKind":"alcohol","forPlay":true,"forResponse":false}],"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"completed-real-supply","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"supply","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = new[] { Driver, Quiet, Watch }.ToDictionary(id => id, id => new { name = id, description = "真实请求者花色与原生供牌",
                optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "原生供杀夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:cfs-own-pick", "本人", "真实手动选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            b.AddSkill(new("fixture:cfs-provider-pick", "蜀忠臣", "真实角色权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Loyalist ? 100000d : -200000d) });
            b.AddGeneral(new(Owner, "实际请求者", "supporter", "fixture:cfs-own-pick", "shu", 12, liubing ? [Driver, Watch, "ol:shutong", "classic:jijiang", "ol:liubing"] : [Driver, Watch, "ol:shutong", "classic:jijiang"]));
            b.AddGeneral(new(Provider, "实际蜀供牌者", "supporter", "fixture:cfs-provider-pick", "shu", 12, [Quiet]));
            var peers = new[] { "fixture:cfs-peer-a", "fixture:cfs-peer-b" };
            foreach (var peer in peers) b.AddGeneral(new(peer, "其他真实角色", "supporter", Quiet, "wei", 12, []));
            b.AddCard(new("fixture:cfs-slash", "真实红桃杀", "基本牌", "原始实体", CardKind.Slash));
            b.AddDeck(new("fixture:cfs-deck", "固定红桃杀牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("fixture:cfs-slash", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "请求者花色真实小局面", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:cfs-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, Provider, ..peers]));
        }
    }
}
