using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouLuZhiMobileChecks
{
    public static void PrivateRenVariableCostAndCompletedCategories()
    {
        Run(false); Run(true);
    }
    private static void Run(bool played)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(played));
        var setup = new ShanheBattleSetup(7, 1, "mou-lu-zhi-" + played, Owner, 4, 0, 0, 0, 0, [],
            [C(CardKind.Slash, Suit.Spade), C(CardKind.Slash, Suit.Club), C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge, Suit.Club), C(CardKind.Peach, Suit.Heart)], [],
            [new(EnemyA, Hp: 8) { StartingHand = played ? [C(CardKind.Duel, Suit.Heart)] : [] }, new(EnemyB, Hp: 6)],
            [new(Ally, Hp: 8)], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException($"Mou Lu Zhi ({played}): " + message); }
        void Accept(GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Answer(Func<PromptChoice, bool> match)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only genuine Human input is supplied."); var c = p.Choices.First(match); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision)); }
        void Reach(Func<bool> done)
        {
            for (var n = 0; n < 240; n++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "The real flow ended before its expected boundary.");
                if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected human boundary: " + P()!.Prompt + " choices=" + string.Join(';', P()!.Choices.Select(c => c.Id)));
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Lu Zhi did not reach the bounded native main flow. " +
                $"phase={g.State.Phase};prompt={P()?.PlayerSeat}/{P()?.Kind}/{P()?.Prompt};frames=[" +
                string.Join(';', g.ResolutionStack.Select(f => f.GetType().Name + "/" + f.Id + (f is ProgramSkillFrame p
                    ? $"/instruction={p.InstructionIndex}/stage={p.MouLuZhi?.Stage}/attack={JsonSerializer.Serialize(p.AttackAttempt)}/return={JsonSerializer.Serialize(p.AttackReturn)}/draw={JsonSerializer.Serialize(p.NativeDraw)}" : ""))) +
                "];recent=[" + string.Join(';', g.Events.TakeLast(10).Select(e => e.Payload.GetType().Name + ":" + JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))) + "]");
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(new StartGameCommand());
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "ren") == true);
        var initial = E<MouLuZhiDrawReturnedEvent>().Single();
        Need(initial is { Requested: 2, Actual: 2, RecipientSeat: 0 }, "Mingren issues a genuine native Draw2 before selecting a current Hand entity.");
        var renId = g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        Answer(c => c.Cards.SequenceEqual([renId])); Play();
        var ownRen = g.CreateSnapshot(0).Players[0].PrivateWhirlPiles!.Single(p => p.SkillId == Mingren);
        var otherRen = g.CreateSnapshot(1).Players[0].PrivateWhirlPiles!.Single(p => p.SkillId == Mingren);
        Need(ownRen.PileName == "任" && ownRen.Cards!.Single().Id == renId && otherRen.Count == 1 && otherRen.Cards is null &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == renId).Location.Zone == CardZoneKind.PrivatePersistentPile,
            "Ren is a genuine private persistent entity; another viewer receives only the pile count.");
        Accept(new UseProgramSkillCommand(0, Zhenliang, "yang-damage", [], [], g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true);
        Answer(c => c.Targets.SequenceEqual([3]));
        var costs = g.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Dodge && c.Suit is Suit.Spade or Suit.Club).Take(2).Select(c => c.Id).ToArray();
        Need(P() is { RequiredCardCount: 2, MinimumCardCount: 2 } && P()!.Choices.All(c =>
                c.Parameters.GetValueOrDefault("owned-selection-role") == "pick"),
            "The real HP-difference cost publishes one complete two-card same-color owned draft before payment.");
        var beforeCost = g.CardMovements.Count; var costFrame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id;
        Answer(c => c.Cards.SequenceEqual([costs[0]]));
        Need(P() is { RequiredCardCount: 1, MinimumCardCount: 1 } && g.CardMovements.Count == beforeCost &&
            P()!.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == costs[0].ToString()),
            "The first genuine native cost remains buffered, highlighted and unpaid on the original frame.");
        var costViews = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))).ToArray();
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(costViews.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i)))) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Last() is { Id: var restoredId, MouLuZhi.Cards: var selected } &&
            restoredId == costFrame && selected.SequenceEqual([costs[0]]),
            "Cold recovery retains the original same-color collector, exact selected entity and every safe projection.");
        Answer(c => c.Cards.SequenceEqual([costs[1]]));
        Play();
        var paid = E<MouLuZhiPaidEvent>().Single();
        Need(paid is { Count: 2, TargetSeat: 3, IsRed: false } && g.CreateSnapshot(0).Players[3].Hp == 5 && E<CardRecastEvent>().Length == 0 &&
            costs.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1) &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zhenliang), "Yang pays the true HP-difference X=2 in same-color HE entities once, causes native damage1 and spends this actual Play.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => P()?.SkillPrompt?.SkillId == Mingren);
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "ren") == true);
        if (!played)
        {
            var snapshot = JsonSerializer.Serialize(g.CreateSnapshot(0));
            g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
            Need(snapshot == JsonSerializer.Serialize(g.CreateSnapshot(0)), "One representative Ren-exchange pending restores its owning frame and exact private choice.");
        }
        var replacement = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Dodge).Id;
        Answer(c => c.Cards.SequenceEqual([replacement]));
        Reach(() => P()?.SkillPrompt?.SkillId == Zhenliang || P() is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash });
        Need(g.CreateSnapshot(0).Players[0].PrivateWhirlPiles!.Single(p => p.SkillId == Mingren).Cards!.Single().Id == replacement &&
            g.CardMovements.Any(m => m.CardId == renId && m.From.Zone == CardZoneKind.PrivatePersistentPile && m.To == CardLocation.Hand(0)), "Ending exchanges the exact old Ren and current Hand without drawing or discarding the replacement.");
        if (P() is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }) Answer(c => c.Parameters.GetValueOrDefault("response") == "slash" && c.Cards.Count == 1);
        else if (P() is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Reach(() => P()?.SkillPrompt?.SkillId == Zhenliang);
        var completed = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        Need(played ? completed.ResponseCompletion is { CostsDrained: true } && completed.Action.PhysicalCards.Count == 1 && completed.Action.EffectiveKind == CardKind.Slash :
            completed.Action.Type == CardActionType.Use && completed.Action.EffectiveKind == CardKind.Slash && completed.Action.PhysicalCards.Count == 0,
            "The two real fixtures distinguish completed pure Played from an entire zero-entity Use; neither depends on a discard-material hook.");
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true);
        Answer(c => c.Targets.SequenceEqual([1]));
        Reach(() => E<MouLuZhiDrawReturnedEvent>().Length == 2);
        var reward = E<MouLuZhiDrawReturnedEvent>().Last(); var yin = E<MouLuZhiStartedEvent>().Single(e => e.Op == SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin);
        Need(reward is { RecipientSeat: 1, Requested: 2, Actual: 2 } && yin.ActionId is > 0 && g.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.MouLuZhi?.Op != SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang),
            "Outside the owner's turn, an actual completed matching basic Use or pure Played action issues exactly one native Draw2 to the freely selected living recipient.");
    }
    private const string Mingren = "mou-mobile:mingren", Zhenliang = "mou-mobile:zhenliang", Quiet = "fixture:mlz-quiet",
        Native = "fixture:mlz-native", Owner = "fixture:mlz-owner", Ally = "fixture:mlz-ally", EnemyA = "fixture:mlz-enemy-a", EnemyB = "fixture:mlz-enemy-b", Deck = "fixture:mlz-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit) => new(kind, suit, 7);
    private sealed class Fixture(bool played) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-lu-zhi-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["mou:lu-zhi-mobile"]);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Duel })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mlz-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            if (!played) rules = rules[..^2] + "," + "{\"id\":\"" + Native + "\",\"revision\":1,\"triggers\":[{\"id\":\"virtual\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingVirtualSlashTarget\"},{\"op\":\"useVirtualCard\",\"target\":\"selectedTarget\",\"outputKind\":\"slash\",\"targetRestriction\":\"distanceUnlimitedAgainstTarget\",\"useCardActionWindows\":true}]}]}]}";
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "静候", description = "跳过出牌" } };
            if (!played) labels[Native] = new { name = "原生零实体使用", description = "真实虚拟杀" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, id == Quiet ? "静候" : "真实使用", "真实检查") { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "谋卢植真实检查", "", "standard:none", "qun", 4, [Mingren, Zhenliang]));
            b.AddGeneral(new(Ally, "同队", "", "standard:none", "qun", 8, [Quiet]));
            b.AddGeneral(new(EnemyA, played ? "真实决斗者" : "真实杀使用者", "", "standard:none", "qun", 8, played ? [] : [Native]));
            b.AddGeneral(new(EnemyB, "阳面对象", "", "standard:none", "qun", 6, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("fixture:mlz-card-Dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋卢植原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
