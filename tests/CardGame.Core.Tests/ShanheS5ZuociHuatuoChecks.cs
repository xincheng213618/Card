using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class ShanheS5ZuociHuatuoChecks
{
    private const string Hero = StandardShanheS5ZuociHuatuoPackage.ZuociId,
        Huashen = StandardShanheS5ZuociHuatuoPackage.HuashenId, Xinsheng = StandardShanheS5ZuociHuatuoPackage.XinshengId,
        Peer = "fixture:s5-avatar-peer", Borrowed = "fixture:s5-avatar-borrowed", Limited = "fixture:s5-avatar-limited",
        NoDraw = "fixture:s5-avatar-no-draw", Quiet = "fixture:s5-avatar-quiet", Driver = "fixture:s5-avatar-driver",
        Hold = "fixture:s5-avatar-hold", Deck = "fixture:s5-avatar-deck";

    public static void ThreeInitialTwoExchangedAndThreePerActualDamagePointUseNativeLibraryCold()
    {
        var s = Scene.Start(); s.InitialDeclaration();
        var initial = s.Library;
        s.Need(initial.Count == 3 && initial.GeneralIds!.Distinct().Count() == 3 && initial.Gender == GeneralGender.Female &&
            s.V(0).FactionId == "wu" && s.G.CreateSnapshot(1).Players[0].PrivateGeneralLibraries!.Single().GeneralIds is null,
            "Three real private avatars initialize once; the native displayed avatar supplies its actual gender, faction and borrowed skill without exposing the hidden library.");
        s.StartChoice(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "exchange");
        var removed = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var choice = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "select");
            removed.Add(choice.Parameters["general-id"]); s.Answer(c => c.Id == choice.Id);
        }
        var selecting = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShanheS5AvatarChoice is not null);
        s.Need(selecting.ShanheS5AvatarChoice!.SelectedGeneralIds.SequenceEqual(removed) &&
            selecting.ShanheS5AvatarChoice.Source == initial.Source && s.Library.GeneralIds!.SequenceEqual(initial.GeneralIds!) &&
            !removed.Contains(initial.RevealedGeneralId!) && !s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "select") &&
            s.G.CreateSnapshot(1).PendingDecision is null,
            "The original owning frame holds at most two unshown selections; payment and random replacements wait for confirmation, and the private prompt stays with its owner.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "finish"); s.Play();
        s.Need(s.Library.Count == 3 && removed.All(id => !s.Library.GeneralIds!.Contains(id)) &&
            s.Library.GeneralIds!.Except(initial.GeneralIds!).Count() == 2 &&
            s.Library.RevealedGeneralId == initial.RevealedGeneralId && s.Library.DeclaredSkillId == Borrowed &&
            s.Library.Gender == GeneralGender.Female && s.V(0).FactionId == "wu" &&
            s.E<ShanheS5AvatarsExchangedEvent>().Count(e => e.ProgramFrameId == selecting.Id && e.RemovedCount == 2 && e.GainedCount == 2) == 1,
            "Cold confirmation removes exactly the selected unshown avatars, draws genuinely new replacements through the native pool, and preserves the displayed source grant.");

        var hand = s.V(0).HandCount;
        s.Use(Borrowed, "draw"); s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Borrowed);
        var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == producer.Id);
        s.Need(producer.SkillInstanceId.Contains("general-library:", StringComparison.Ordinal) && draw.Context.RequestedCount == 1 &&
            draw.Return.Kind == NativeDrawAttemptReturnKind.ProgramInstruction && s.V(0).HandCount == hand,
            "The displayed avatar's real owned grant starts a normal native draw on its original skill frame.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.V(0).HandCount == hand + 1 &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && e.RequestedCount == 1 && !e.Replaced) == 1,
            "The borrowed skill returns from its exact native draw child once after cold recovery.");

        s.Use(Driver, "hurt"); s.Reach(() => s.P?.SkillPrompt?.SkillId == Xinsheng); s.Activate();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Xinsheng);
        s.Need(s.V(0).Hp == 1 && s.Library.Count == 6 &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 2) == 1,
            "The first actual point of a real two-point damage has already granted three new avatars while the second optional point remains on the native damage frame.");
        s.Cold(); s.Activate(); s.Play();
        s.Need(s.Library.Count == 9 && s.Library.GeneralIds!.Distinct().Count() == 9 &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Xinsheng && e.BindingId == "damage" && e.Activated && e.Completed) == 2 &&
            s.G.CreateSnapshot(1).Players[0].PrivateGeneralLibraries!.Single() is { Count: 9, GeneralIds: null },
            "Cold never repeats the first grant; each real damage point supplies three through the exact original source library and other viewers receive only the count.");
    }

    public static void ExhaustedAvatarPoolKeepsDisplayedCardAndAllowsZeroExchangeCold()
    {
        var s = Scene.Start(avatars: 3); s.InitialDeclaration(); var initial = s.Library;
        s.StartChoice(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "exchange");
        s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "select") &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShanheS5AvatarChoice is not null)
                .ShanheS5AvatarChoice!.SelectedGeneralIds.Count == 0,
            "With every remaining native candidate already held, an up-to-two exchange offers a legal zero selection and cannot consume an unreplaceable avatar.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("action") == "finish"); s.Play();
        s.Need(s.Library.GeneralIds!.SequenceEqual(initial.GeneralIds!) && s.Library.RevealedGeneralId == initial.RevealedGeneralId &&
            s.E<ShanheS5AvatarsExchangedEvent>().Length == 0,
            "Zero exchange preserves the exact source library, displayed skill and RNG-consuming transaction count.");
    }

    private sealed class Fixture(int avatars) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-avatar", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{NoDraw}}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Hold}}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}]},
              {"id":"{{{Borrowed}}}","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """;
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { NoDraw, Quiet, Hold, Driver, Borrowed }.ToDictionary(id => id,
                    id => new { name = id, description = "真实伤害与原生化身技能摸牌暂停" })
            }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
            { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddSkill(new(Limited, "不可借限定技能", "沿原生化身候选过滤") { Tags = SkillTag.Limited });
            b.AddGeneral(new(Peer, "保持阶段的对手", "supporter", Quiet, "wei", 8));
            var ids = new List<string> { Hero, Peer };
            for (var i = 0; i < avatars; i++)
            {
                var id = $"fixture:s5-avatar-{i}"; ids.Add(id);
                b.AddGeneral(new(id, "私有化身" + i, "supporter", Borrowed, "wu", 3, [Limited], GeneralGender.Female));
            }
            b.AddDeck(new(Deck, "固定小库存", 0, 2, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "S5原生化身池", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: ids, ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public PrivateGeneralLibrarySnapshot Library => V(0).PrivateGeneralLibraries!.Single();
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void InitialDeclaration()
        {
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "private-general-library") == true);
            Answer(c => c.Parameters.GetValueOrDefault("value")?.StartsWith("fixture:s5-avatar-", StringComparison.Ordinal) == true);
            Need(P!.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == Borrowed) &&
                P.Choices.All(c => c.Parameters.GetValueOrDefault("value") != Limited), "Native avatar declarations keep the existing limited-skill filter.");
            Answer(c => c.Parameters.GetValueOrDefault("value") == Borrowed);
        }
        public void StartChoice()
        { Reach(() => P?.SkillPrompt?.SkillId == Huashen && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Activate(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string skill, string binding) => Accept(new UseProgramSkillCommand(0, skill, binding, [], [], G.Revision, P!.PromptId));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip();
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected S5 avatar boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded native avatar flow was not reached.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            var facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))) &&
                facts.SequenceEqual(G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
                "Cold retains the private original source, selections, real RNG results and native child return without repeating an exchange or avatar grant.");
        }
        public void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected S5 avatar command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Owner = V(0), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(int avatars = 12)
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheS5ZuociHuatuoPackage(), new Fixture(avatars)]);
            var setup = new ShanheBattleSetup(37, 1, "fixture-s5-avatar", Hero, 3, 0, 0, 0, 0,
                [NoDraw, Hold, Driver], [], [],
                [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], [], new string('f', 64));
            var g = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
