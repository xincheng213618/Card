using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouXiahouDunMobileChecks
{
    public static void NativeRetaliationAndNonUseDistribution()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-xiahou-dun", Owner, 4, 0, 0, 0, 0, [],
            [C(CardKind.Slash, Suit.Heart), C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge, Suit.Club), C(CardKind.Peach, Suit.Heart), C(CardKind.DrawTwo, Suit.Diamond)], [],
            [new(EnemyA, Hp: 8), new(EnemyB, Hp: 8) { StartingHand = [C(CardKind.Dodge, Suit.Spade)] }], [new(Ally, Hp: 8)], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
        void Accept(GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Answer(Func<PromptChoice, bool> match)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only real Human input is supplied."); var c = p.Choices.First(match); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision)); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "The actual game ended before the skill returned.");
                if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Human prompt: " + P()!.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Xiahou Dun bounded native flow not reached; phase=" + g.State.Phase + ";frames=" + string.Join(';', g.ResolutionStack.Select(f =>
                f.GetType().Name + '/' + f.Id + (f is ProgramSkillFrame p ? $"/{p.InstructionIndex}/{p.MouXiahouDunMobile?.Stage}/{p.MouXiahouDunMobile?.Index}/attack:{JsonSerializer.Serialize(p.AttackAttempt)}" : ""))));
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Driver(string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, Drive, id, cards ?? [], targets ?? [], g.Revision, P()!.PromptId));
        void Physical(LegalActionKind kind, int? target = null)
        {
            var a = g.GetHumanLegalActions().First(a => a.Kind == kind && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P()!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        Accept(new StartGameCommand()); Play();
        Need(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Ganglie), "Ganglie cannot invent a historical attacker.");
        foreach (var source in new[] { 2, 3 })
        {
            Driver("sourced-damage", targets: [source]); Play();
            Need(E<DamageAppliedEvent>().Any(e => e.SourceSeat == source && e.TargetSeat == 0 && e.Amount == 1), "A genuine native Damage1 with its explicit selected-target source must establish each historical attacker.");
        }
        Need(g.CreateSnapshot(0).Players[0].Hp == 2, "The two genuine sourced damages leave the native Qingjian capacity at one.");
        Accept(new UseProgramSkillCommand(0, Ganglie, "retaliate-all", [], [], g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "toggle") == true);
        Answer(c => c.Targets.SequenceEqual([2])); Answer(c => c.Targets.SequenceEqual([3])); Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm"); Play();
        Need(E<MouXiahouDunMobileGanglieSpentEvent>().Select(e => e.TargetSeat).SequenceEqual([2, 3]) && E<MouXiahouDunMobileDamageIssuedEvent>().Length == 2 &&
            g.CreateSnapshot(0).Players[2].Hp == 6 && g.CreateSnapshot(0).Players[3].Hp == 6 && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Ganglie),
            "One accepted complete roster spends both targets for the game and sequentially returns from two real Damage2 attempts; it is also once this actual Play.");
        var slash = g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Kind == CardKind.Slash).Id;
        Physical(LegalActionKind.Slash, 3); Play();
        Need(g.CardMovements.Any(m => m.CardId == slash && m.To == CardLocation.DiscardPile) && E<MouXiahouDunMobileCollectedEvent>().Length == 0,
            "A true physical Slash Use and any native defending Dodge Use enter the real discard pile without becoming Qingjian. " +
            $"slash={slash};collected=[" + string.Join(';', E<MouXiahouDunMobileCollectedEvent>().Select(e => $"card{e.CardId}/discard{e.DiscardSequence}/claim{e.ClaimSequence}/cap{e.Capacity}/frame{e.FrameId}")) +
            "];entries=[" + string.Join(';', E<NativeTrueUseProcessingEntryEvent>().Select(e => JsonSerializer.Serialize(e))) +
            "];actions=[" + string.Join(';', E<CardActionAcceptedEvent>().Select(e => $"id{e.Action.ActionId}/{e.Action.Type}/{e.Action.EffectiveKind}/actor{e.Action.ActorSeat}/provider{e.Action.ProviderSeat}/parent{e.Action.ParentActionId}/cards:{string.Join(',', e.Action.PhysicalCards.Select(c => c.CardId + "@" + c.From))}")) +
            "];responses=[" + string.Join(';', E<CardResponseCompletedEvent>().Select(e => JsonSerializer.Serialize(e))) +
            "];moves=[" + string.Join(';', g.CardMovements.Where(m => m.CardId == slash || E<MouXiahouDunMobileCollectedEvent>().Any(e => e.CardId == m.CardId))
                .Select(m => $"seq{m.Sequence}/card{m.CardId}/{m.CardKind}/{m.From}->{m.To}/{m.Reason.Value}")) + "]");
        var costs = g.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).Take(2).ToArray();
        Driver("discard-two", costs); Play();
        var collected = E<MouXiahouDunMobileCollectedEvent>();
        Need(collected is [{ Capacity: 1 }] && costs.Contains(collected[0].CardId) && costs.Count(id => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location.Zone == CardZoneKind.PublicPersistentPile) == 1,
            "A genuine two-entity non-Use discard batch is processed in native sequence and cannot exceed current HP2's single Qingjian slot.");
        Physical(LegalActionKind.Peach); Play();
        Need(g.CreateSnapshot(0).Players[0].Hp == 3 && E<MouXiahouDunMobileCollectedEvent>().Length == 1, "A real Peach heals before the next live capacity query; its used material remains excluded.");
        var remaining = g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        Driver("discard-one", [remaining]); Play();
        Need(E<MouXiahouDunMobileCollectedEvent>() is [_, { CardId: var final, Capacity: 2 }] && final == remaining,
            "The next genuine non-Use arrival reads the increased HP3 capacity and claims the actual second entity.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "give") == true);
        var snapshot = JsonSerializer.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(snapshot == JsonSerializer.Serialize(g.CreateSnapshot(0)), "One representative pending Qingjian distribution restores its complete real entity roster and chooser.");
        Answer(c => c.Targets.SequenceEqual([1]));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "give") == true);
        Answer(c => c.Targets.SequenceEqual([0]));
        Reach(() => E<TurnEndedEvent>().Any(e => e.TurnNumber == 1 && e.ActorSeat == 0));
        var gifts = E<MouXiahouDunMobileGiftEvent>();
        Need(gifts.Length == 2 && gifts.Select(e => e.RecipientSeat).SequenceEqual([1, 0]) && gifts.Select(e => e.CardId).ToHashSet().SetEquals(E<MouXiahouDunMobileCollectedEvent>().Select(e => e.CardId)) &&
            gifts.All(e => g.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From.Zone == CardZoneKind.PublicPersistentPile && m.To == CardLocation.Hand(e.RecipientSeat)) == 1),
            "The actual own Play end distributes the full frozen pile once through real Hand transfers, including an allowed self recipient, then returns the phase.");
    }
    private const string Ganglie = "mou-mobile:ganglie", Qingjian = "mou-mobile:qingjian", Drive = "fixture:mxhd-drive", Quiet = "fixture:mxhd-quiet",
        Owner = "fixture:mxhd-owner", Ally = "fixture:mxhd-ally", EnemyA = "fixture:mxhd-enemy-a", EnemyB = "fixture:mxhd-enemy-b", Deck = "fixture:mxhd-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit) => new(kind, suit, 7);
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-xiahou-dun-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["mou:xiahou-dun-mobile"]);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.DrawTwo, CardKind.Duel })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mxhd-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Drive + "\",\"revision\":1,\"activations\":[" +
                "{\"id\":\"sourced-damage\",\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1,\"sourceRef\":{\"kind\":\"selectedTarget\"}}]}," +
                "{\"id\":\"discard-two\",\"minCards\":2,\"maxCards\":2,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":2}]}," +
                "{\"id\":\"discard-one\",\"minCards\":1,\"maxCards\":1,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var labels = new Dictionary<string, string> { [Drive] = "原生动作驱动", [Quiet] = "静候" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels.ToDictionary(x => x.Key, x => new { name = x.Value, description = x.Value }) }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "谋夏侯惇真实检查", "", "standard:none", "wei", 4, [Ganglie, Qingjian, Drive]));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "真实对象", "", "standard:none", "wei", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("fixture:mxhd-card-Dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋夏侯惇原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
