using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShiXiaoQiaoChecks
{
    private const string Hero = "mobile:shi-xiao-qiao", Heyun = "mobile:shi-heyun", Yinhui = "mobile:shi-yinhui",
        Shared = "fixture:shi-xiao-qiao-shared", Borrowed = "fixture:shi-xiao-qiao-limited", Donor = "fixture:shi-xiao-qiao-donor",
        Quiet = "fixture:shi-xiao-qiao-quiet", Watch = "fixture:shi-xiao-qiao-watch", Peer = "fixture:shi-xiao-qiao-peer",
        Mode = "fixture:shi-xiao-qiao-mode", Deck = "fixture:shi-xiao-qiao-deck";

    public static void ExactCopySourcePreservesIndependentInstanceAndState()
    {
        var s = Scene.Start(true); s.AtOffer();
        var state = s.Runtime(Shared); var original = state.BooleanStates!.Single();
        s.Need(original.Value && state.Usages.Any(u => u.Scope == SkillUsageScope.Game && u.Count == 1),
            "The independent skill already owns a real used game record and public runtime state before copying.");
        var before = s.V(0).HandCount; s.Copy(1, Shared); s.Play(0);
        var copied = s.E<ShiXqCopiedEvent>().Single();
        s.Need(copied.SkillInstanceId == original.SkillInstanceId && s.Runtime(Shared).BooleanStates!.Single() == original &&
            s.V(0).HandCount == before + 1 && !s.E<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Yinhui && e.SkillIds.Contains(Shared)),
            "A second source for the same skill reuses its active instance, keeps its state and fires its real turn-start draw only once.");
        s.Cold(); s.AtOffer(2); s.Activate(); s.AtBranch("donor");
        s.Need(s.E<ShiXqGrantClearedEvent>().Single() is { WasPresent: true } cleared && cleared.GrantId == copied.GrantId &&
            s.Has(Shared) && s.Runtime(Shared).BooleanStates!.Single() == original && s.Runtime(Shared).Usages.Any(u => u.Scope == SkillUsageScope.Game && u.Count == 1) &&
            s.Runtime(Yinhui).PublicRuleStates!.Single(p => p.StateId == "yinhui-grants").Value == 0,
            "The next round revokes only the exact Yinhui GrantId, preserving the independent same-name capability, instance and game state.");
        s.Cold(); s.Replay();
    }

    public static void CurrentAcquiredLimitedSkillAndSelfLossDrawCold()
    {
        var s = Scene.Start(false); s.AtOffer();
        s.Need(s.E<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == 2 && e.SkillIds.Contains(Borrowed)) &&
            s.V(2).Skills!.Any(skill => skill.Id == Borrowed), "A donor's genuine earlier acquisition is already part of its current first-round skills.");
        s.Copy(2, Borrowed); s.Play(0);
        s.Need(s.Has(Borrowed) && s.E<ShiXqCopiedEvent>().Single().SkillId == Borrowed,
            "The current formal rule copies an actually acquired Limited skill without the old reveal-version blacklist.");
        var borrowed = s.E<ShiXqCopiedEvent>().Single(); var hand = s.V(0).HandCount;
        s.HeyunSelf(Borrowed); s.AtWatch();
        s.Need(!s.Has(Borrowed) && s.V(0).HandCount == hand + 2 && s.Root?.ShiXiaoQiao is { Stage: ShiXqStage.DrawChildren, Paid: true } &&
            s.E<ShiXqHeyunPaidEvent>().Length == 1 && s.E<ShiXqDrawPaidEvent>().Length == 1,
            "Self is a legal common-skill target; actual loss of the copied skill precedes one two-card native draw and its observer child.");
        s.Cold(); s.ContinueWatch(); s.Play(0); hand = s.V(0).HandCount;
        s.HeyunSelf(Heyun); s.AtWatch();
        s.Need(!s.Has(Heyun) && s.Has(Yinhui) && s.V(0).HandCount == hand + 2 &&
            s.Root?.SkillId == Heyun && s.Root.ShiXiaoQiao is { Paid: true, Stage: ShiXqStage.DrawChildren },
            "Losing Heyun itself retains the original paid frame through the actual draw child.");
        s.Cold(); s.ContinueWatch(); s.Play(0);
        s.Need(s.E<ShiXqHeyunPaidEvent>().Length == 2 && s.E<ShiXqDrawPaidEvent>().Length == 2,
            "Cold return neither loses nor repeats either original skill payment or native draw.");
        s.AtOffer(2); s.Activate(); s.AtBranch("donor");
        s.Need(s.E<ShiXqGrantClearedEvent>().Single() is { WasPresent: false } cleared && cleared.GrantId == borrowed.GrantId &&
            s.E<ShiXqCopiedEvent>().Length == 1, "A copied source already lost to Heyun is skipped safely at the next actual round clear.");
        s.Cold(); s.Replay();
    }

    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShiXiaoQiao is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public SkillRuntimeStateSnapshot Runtime(string id) => V(0).SkillRuntimeStates!.Single(s => s.SkillId == id);
        public bool Has(string id) => V(0).Skills!.Any(skill => skill.Id == id);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void AtOffer(int round = 1) => Reach(() => P?.SkillPrompt?.SkillId == Yinhui &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate") && E<RoundStartedEvent>().Length == round);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void AtBranch(string branch) => Reach(() => P?.Choices.Any(c => Branch(c) == branch) == true);
        public void Copy(int donor, string id)
        {
            Activate(); AtBranch("donor"); Answer(c => Branch(c) == "donor" && c.Targets.SequenceEqual([donor]));
            AtBranch("copy"); Answer(c => Branch(c) == "copy" && c.Parameters.GetValueOrDefault("skill-id") == id);
        }
        public void HeyunSelf(string lost)
        {
            Submit(new UseProgramSkillCommand(0, Heyun, "lose-for-draw", [], [], G.Revision, P!.PromptId));
            AtBranch("target"); Answer(c => Branch(c) == "target" && c.Targets.SequenceEqual([0]));
            AtBranch("lose"); Answer(c => Branch(c) == "lose" && c.Parameters.GetValueOrDefault("skill-id") == lost);
        }
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The real current published skill choice exists."); Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 220; n++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { ContinueWatch(); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind == DecisionKind.PlayCard) { Submit(new EndPlayPhaseCommand(p.PlayerSeat, G.Revision, p.PromptId)); continue; }
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    throw new InvalidOperationException("Unexpected Xiao Qiao native continuation. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short real-turn copy fixture stays live."); Submit(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded actual round/phase flow reaches its expected continuation.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact grants, original lost source, paid draw child and accepted command prefix."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Native command replay reconstructs current copied skills and exact-source revocation without repeated payments."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Dump()); }
        private void Submit(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Real Xiao Qiao command rejected."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Root?.ShiXiaoQiao, Copies = E<ShiXqCopiedEvent>(), Clear = E<ShiXqGrantClearedEvent>() });
        public static Scene Start(bool independent)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(independent));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var s = new Scene(game, registry); s.Submit(new StartGameCommand());
            for (var n = 0; n < 50; n++)
            {
                if (s.P is { Kind: DecisionKind.SelectGeneral } p)
                { s.Submit(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : Peer + p.PlayerSeat, game.Revision, p.PromptId)); continue; }
                if (s.P?.SkillPrompt?.SkillId == Yinhui) return s;
                if (s.P is { Kind: DecisionKind.SelectFaction }) { s.Answer(_ => true); continue; }
                s.Need(s.P is null, "The fixed identity opening exposes only native setup and the first real copy offer."); s.Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("The fixed identity opening did not reach Xiao Qiao's first real round.");
        }
    }
    private sealed class Fixture(bool independent) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-xiao-qiao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b, independent), Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],
               "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Shared}}","revision":1,"states":[{"id":"spent","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],
               "triggers":[{"id":"initialize","window":"gameStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"setBooleanState","target":"owner","stateId":"spent","value":true}]},
                 {"id":"one-real-turn-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{Donor}}","revision":1,"triggers":[{"id":"actual-acquisition","window":"gameStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Borrowed}}"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-xiao-qiao.heyun.draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "固定真实手牌", description = "保留手牌并关闭普通摸牌。" },
                    [Shared] = new { name = "独立同名来源", description = "真实游戏次数与状态，每个真实回合开始摸一张。" },
                    [Donor] = new { name = "先前实际授予", description = "首轮之前以原生节点实际获得限定技能。" },
                    [Watch] = new { name = "合韵原生摸牌子结算", description = "已失去技能并完成真实摸牌后等待。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Borrowed, "异源限定技能", "正式音洄没有爆料黑名单。") { Tags = SkillTag.Limited });
            b.AddGeneral(new(Peer + 1, "独立来源持有人", "", Shared, "wei", 8, [Quiet]));
            b.AddGeneral(new(Peer + 2, "当前异源技能持有人", "", Donor, "wei", 8, [Quiet]));
            b.AddGeneral(new(Peer + 3, "固定同桌", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "实际固定桃牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "势小乔真实来源与付款", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b, bool independent) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds ?? [], Quiet, Watch, .. independent ? new[] { Shared } : []] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
