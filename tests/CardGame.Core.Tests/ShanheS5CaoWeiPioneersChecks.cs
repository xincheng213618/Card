using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5CaoWeiPioneersChecks
{
    private const string Jianxiong = StandardShanheS5CaoWeiPioneersPackage.JianxiongId;
    private const string Hujia = StandardShanheS5CaoWeiPioneersPackage.HujiaId;
    private const string Driver = "fixture:s5-caowei-driver", Watch = "fixture:s5-caowei-draw-watch";
    private const string Hero = "fixture:s5-caowei-lord", Mode = "identity:classic-s5-caowei";

    public static void JianxiongLostHpDrawBeforeOriginalClaimAndCardlessCold()
    {
        var s = Scene.Start(true); s.Play(1);
        var material = s.V(1).Hand.First().Id;
        var slash = s.G.GetLegalActions(1).First(a => a.CardId == material && a.PlayedCardKind == CardKind.Slash &&
            a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([0]));
        s.Accept(new PlayCardCommand(1, material, [0], s.G.Revision, s.P!.PromptId, slash.PlayedCardKind)
        { ConversionSource = slash.ConversionSource });
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        s.Reach(() => s.Offer(Jianxiong)); var hand = s.V(0).Hand.Count; var x = s.V(0).MaxHp - s.V(0).Hp;
        s.Activate(); s.Reach(() => s.DrawWatch(Jianxiong));
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jianxiong);
        s.Need(root.InstructionIndex == 1 && root.NativeDraw is { } receipt && receipt.RequestedCount == x &&
            s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single().Context.ParentFrameId == root.Id &&
            s.G.CreateCardZoneDiagnostics().Single(z => z.CardId == material).Location == CardLocation.Processing &&
            !s.E<ProgramDamageCardsClaimedEvent>().Any(e => e.SkillId == Jianxiong),
            "Actual transformed Slash damage first issues Draw(current lost HP) on the owning Jianxiong frame; its causal physical card is not claimed before that native draw child returns.");
        s.Cold(); s.Continue(); s.Play(1);
        s.Need(s.V(0).Hand.Count == hand + x + 1 && s.V(0).Hand.Any(c => c.Id == material && c.Kind == CardKind.Dodge) &&
            s.E<ProgramDamageCardsClaimedEvent>().Count(e => e.SkillId == Jianxiong && e.CardIds.SequenceEqual([material])) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == $"skill-program.{Jianxiong}.ClaimDamageCards") == 1,
            "The completed native draw then claims the genuine original transformed material exactly once, retaining its printed face and entity ID after cold replay.");

        var claims = s.E<ProgramDamageCardsClaimedEvent>().Length;
        s.Use(1, "damage", 0); s.Reach(() => s.Offer(Jianxiong));
        hand = s.V(0).Hand.Count; x = s.V(0).MaxHp - s.V(0).Hp;
        s.Activate(); s.Reach(() => s.DrawWatch(Jianxiong)); s.Cold(); s.Continue(); s.Play(1);
        s.Need(s.V(0).Hand.Count == hand + x && s.E<ProgramDamageCardsClaimedEvent>().Length == claims &&
            s.E<DamageAppliedEvent>().Last().TargetSeat == 0,
            "A real cardless damage occurrence still grants its full current-lost-HP draw, with no invented causal card claim.");
        var started = s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Jianxiong);
        s.Use(1, "lose", 0); s.Play(1);
        s.Need(s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Jianxiong) == started,
            "Actual loseHp is not damage and cannot trigger Jianxiong."); s.Cold();
    }

    public static void HujiaActualUsedPlayedForeignChooserSharedTurnQuotaAndCold()
    {
        var s = Scene.Start(false); s.Play(2);
        s.Use(2, "slash", 3); s.Reach(() => s.P is { PlayerSeat: 3, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.Count == 1); s.Play(2);
        s.Need(s.E<ShanheS5HujiaRewardIssuedEvent>().Length == 0,
            "An actual non-Wei Dodge cannot reward the qualified Hujia Lord.");

        s.Use(2, "slash", 0); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-request");
        s.Reach(() => s.P is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge }); s.Cold();
        var supplied = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-dodge").Cards.Single();
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-dodge" && c.Cards.Contains(supplied));
        s.Reach(() => s.Offer(Hujia));
        s.Need(s.P!.PlayerSeat == 1 && s.E<CardActionAcceptedEvent>().Any(e => e.Action.EffectiveKind == CardKind.Dodge &&
            e.Action.ActorSeat == 0 && e.Action.ProviderSeat == 1 && e.Action.PhysicalCards.Any(c => c.CardId == supplied)),
            "The original qualified Lord really receives another Wei role's physical Dodge, and that actual provider owns the optional reward choice.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play(2);
        s.Need(s.E<ShanheS5HujiaRewardIssuedEvent>().Length == 0, "Declining a real provider offer consumes no reward quota.");

        var hand = s.V(0).Hand.Count;
        s.Use(2, "slash", 1); s.Reach(() => s.P is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        var dodge = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); s.Answer(c => c.Cards.Contains(dodge));
        s.Reach(() => s.Offer(Hujia)); s.Need(s.P!.PlayerSeat == 1, "An independently used off-turn Wei Dodge also gives the actual role its choice.");
        s.Activate(); s.Reach(() => s.DrawWatch(Hujia));
        var issued = s.E<ShanheS5HujiaRewardIssuedEvent>().Single();
        s.Need(issued.Receipt.Source.OwnerSeat == 0 && issued.Receipt.ChooserSeat == 1 && issued.Receipt.ActualTurnOwnerSeat == 2 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == issued.FrameId).NativeDraw is { RequestedCount: 1 } &&
            s.V(0).Hand.Count == hand,
            "The accepted actual Dodge has one frozen original actor/provider invoice and one native Draw1 child before cards are gained.");
        s.Cold(); s.Continue(); s.Play(2);
        s.Need(s.V(0).Hand.Count == hand + 1, "Returning the native draw child grants one real card exactly once.");

        s.Use(2, "slash", 0); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-request");
        s.Reach(() => s.P is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "faction-defense-dodge"); s.Play(2);
        s.Need(s.E<ShanheS5HujiaRewardIssuedEvent>().Length == 1 && s.V(0).Hand.Count == hand + 1 &&
            s.E<DamageAppliedEvent>().All(e => e.TargetSeat != 0),
            "Used and played assistance share one actual-turn reward, while later native faction defense remains fully functional.");
        s.Play(3); s.Use(3, "slash", 1); s.Reach(() => s.P is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.Count == 1); s.Reach(() => s.Offer(Hujia)); s.Activate();
        s.Reach(() => s.DrawWatch(Hujia)); s.Continue(); s.Play(3);
        s.Need(s.E<ShanheS5HujiaRewardIssuedEvent>().Length == 2 &&
            s.E<ShanheS5HujiaRewardIssuedEvent>().Select(e => e.Receipt.ActualTurnNumber).Distinct().Count() == 2 &&
            s.V(0).Hand.Count == hand + 2, "The shared reward refreshes on the next actual turn, rather than waiting for a new round."); s.Cold();
    }

    private sealed class Scene(GameEngine game, ContentRegistry content)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() where T : IGameEvent => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Offer(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool DrawWatch(string skill) => P?.SkillPrompt?.SkillId == Watch && G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.Reason.Value == $"skill-program.{skill}.Draw");
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Cao Wei command."); }
        public void Answer(Func<PromptChoice, bool> choose)
        { var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "Required native input is missing."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(int actor, string id, int target) => Accept(new UseProgramSkillCommand(actor, Driver, id, [], [target], G.Revision, P!.PromptId));
        public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 240; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal game.");
                var p = P;
                if (p is { Kind: DecisionKind.PlayCard }) Accept(new EndPlayPhaseCommand(p.PlayerSeat, G.Revision, p.PromptId));
                else if (p is { Kind: DecisionKind.DiscardCards }) Accept(new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision));
                else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue();
                else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else { Need(p is null, "Unexpected live human decision."); Accept(new AdvanceOneStepCommand(G.Revision)); }
            }
            throw new InvalidOperationException("Missing native Cao Wei boundary: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            string All(GameEngine engine) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(engine.CreateSnapshot(i))).ToArray(),
                Frames = JsonSerializer.Serialize(engine.ResolutionStack), Events = engine.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
                engine.CardMovements, Commands = CommandJson.Serialize(engine.AcceptedCommands), Zones = engine.CreateCardZoneDiagnostics() });
            var before = All(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), content);
            Need(before == All(G), "Cold replay preserves every view, original native Draw/response parent, action/provider receipt and accepted command prefix.");
        }
        public void Need(bool value, string why)
        { if (!value) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool jianxiong)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new StandardShanheS5CaoWeiPioneersPackage(), new Fixture(jianxiong));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord,
                ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand());
            for (var n = 0; n < 50; n++)
            {
                if (s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return s;
                if (s.P is { Kind: DecisionKind.SelectGeneral } p) s.Accept(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : $"fixture:s5-caowei-peer-{p.PlayerSeat}", g.Revision, p.PromptId));
                else if (s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) s.Continue();
                else if (s.P is { Kind: DecisionKind.SelectFaction }) s.Answer(_ => true);
                else { s.Need(s.P is null, "The identity opening has one actual human setup choice."); s.Accept(new AdvanceOneStepCommand(g.Revision)); }
            }
            throw new InvalidOperationException("Missing first native Cao Wei Play phase.");
        }
    }

    private sealed class Fixture(bool jianxiong) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-caowei", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,
              "viewAs":[{"id":"physical-slash","inputKinds":["dodge"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false}],
              "activations":[
               {"id":"slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]},
               {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
               {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
             {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"native-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paused","options":[{"id":"continue"}]}]}]}]}
            """;
            var presentation = new { schemaVersion = 3, skills = new Dictionary<string, object> {
                [Driver] = new { name = "原生用牌与伤害", description = "只调用现有原生流程" },
                [Watch] = new { name = "原生摸牌孩子", description = "摸牌结算前暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(presentation));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实原生流程") { Program = catalog.Programs[id] });
            b.AddGeneral(new(Hero, "护驾／奸雄机制", "supporter", Hujia, "wei", 8, jianxiong ? [Jianxiong, Driver, Watch] : [Driver, Watch]) { InitialHp = 8 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:s5-caowei-peer-{i}", "固定其他角色", "supporter", Driver, i == 1 ? "wei" : i == 2 ? "qun" : "shu", 8, []));
            b.AddDeck(new("fixture:s5-caowei-deck", "真实闪实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "曹魏原生命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:s5-caowei-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Hero, "fixture:s5-caowei-peer-1", "fixture:s5-caowei-peer-2", "fixture:s5-caowei-peer-3"]));
        }
    }
}
