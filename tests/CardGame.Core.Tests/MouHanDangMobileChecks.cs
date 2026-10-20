using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouHanDangMobileChecks
{
    public static void NativeColorResponsesAndRangeAid()
    {
        foreach (var mode in new[] { "discard", "draw", "backwater" }) Run(mode);
    }
    private static void Run(string mode)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var lion = mode == "backwater";
        var setup = new ShanheBattleSetup(7, 1, "mou-han-dang-" + mode, Owner, lion ? 3 : 4, 0, 0, 0, 0, [],
            lion ? [C(CardKind.Slash, Suit.Heart)] : mode == "draw" ? [C(CardKind.Peach, Suit.Heart), C(CardKind.Slash, Suit.Heart), C(CardKind.DrawTwo, Suit.Diamond)] : [C(CardKind.Peach, Suit.Heart), C(CardKind.Slash, Suit.Heart)], lion ? [C(CardKind.SilverLion, Suit.Heart)] : [],
            [new(EnemyA, Hp: 8) { StartingHand = mode == "draw" ? [C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge, Suit.Heart), C(CardKind.Slash, Suit.Club), C(CardKind.Nullification, Suit.Heart)] : [C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge, Suit.Heart), C(CardKind.Slash, Suit.Club)] },
             new(EnemyB, Hp: 8) { StartingHand = [C(CardKind.Dodge, Suit.Club)] }],
            [new(Ally, Hp: 8) { StartingHand = mode == "draw" ? [C(CardKind.Dodge, Suit.Spade), C(CardKind.Nullification, Suit.Spade)] : [C(CardKind.Dodge, Suit.Spade)] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        string CounterspellDiagnostic()
        {
            var p = P();
            return " responses=[" + string.Join(";", E<NullificationRespondedEvent>().Select(e => $"use{e.ResolutionId}/seat{e.ResponderSeat}/card{e.NullificationCardId}/depth{e.ChainDepth}/null{e.EffectNullified}")) +
                "] windows=[" + string.Join(";", g.ResolutionStack.OfType<NullificationWindowFrame>().Select(w => $"id{w.Id}/parent{w.ParentFrameId}/source{w.SourceSeat}/depth{w.ChainDepth}/null{w.EffectNullified}")) +
                $"] prompt={p?.PlayerSeat}/{p?.Kind} choices=[" + string.Join(";", (p?.Choices ?? []).Select(c => $"{c.Id}:cards({string.Join(',', c.Cards)}):{string.Join(',', c.Parameters.Select(x => x.Key + '=' + x.Value))}")) +
                "] restrictions=[" + string.Join(";", E<MouHanDangUseRestrictedEvent>().Select(e => $"action{e.ActionId}/actor{e.ActorSeat}/grant{e.GrantFrameId}")) +
                "] actions=[" + string.Join(";", E<CardActionAcceptedEvent>().Select(e => e.Action).Where(a => a.EffectiveKind is CardKind.DrawTwo or CardKind.Nullification)
                    .Select(a => $"id{a.ActionId}/parent{a.ParentActionId}/{a.Type}/{a.EffectiveKind}/actor{a.ActorSeat}/provider{a.ProviderSeat}/materials({string.Join(',', a.PhysicalCards.Select(c => c.CardId))})")) + "]";
        }
        void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(mode + ": " + message); }
        void Accept(GameCommand c) { var r = g.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "Game ended before the real skill returned.");
                if (P() is { PlayerSeat: 0 }) throw new InvalidOperationException(mode + ": Unexpected Human prompt: " + P()!.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException(mode + ": Bounded native flow was not reached.");
        }
        void Answer(Func<PromptChoice, bool> match)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only actual Human input may be supplied."); var c = p.Choices.Single(match); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision)); }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(new StartGameCommand()); Reach(() => P()?.SkillPrompt?.SkillId == Gongqi);
        Need(g.GetAttackRange(0) == 5, "Gongqi adds four to the genuine native attack range.");
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-han-dang") == true);
        var payment = lion ? g.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id : g.CreateSnapshot(0).Players[0].Hand.Single(c => c.Kind == CardKind.Peach).Id;
        Answer(c => c.Cards.SequenceEqual([payment]));
        if (lion)
        {
            Reach(() => P()?.SkillPrompt?.SkillId == Watch);
            var paid = E<MouHanDangDiscardedEvent>().Single();
            Need(paid.CardId == payment && paid.From == CardLocation.Equipment(0) && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h =>
                h.Change.ParentFrameId == paid.FrameId && h.ResumeFrameId == paid.FrameId && h.Change.Kind == HpChangeKind.Recovery && h.Change.TargetSeat == 0 && h.Change.Amount == 1) &&
                E<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) && E<MouHanDangCompletedEvent>().Length == 0,
                "The real wounded Silver Lion equipment payment suspends Gongqi on its own native recovery child before completing the paid skill.");
            Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        }
        Play();
        var granted = E<MouHanDangGongqiGrantedEvent>().Single();
        Need(granted.CardId == payment && granted.Suit == Suit.Heart && E<CardRecastEvent>().Length == 0 &&
            g.CardMovements.Count(m => m.CardId == payment && m.From == (lion ? CardLocation.Equipment(0) : CardLocation.Hand(0)) && m.To == CardLocation.DiscardPile) == 1,
            "Gongqi pays a genuine discard once, freezes its actual red color, and never recasts it.");
        var targetHand = g.CreateSnapshot(2).Players[2].Hand;
        var blackDodge = targetHand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Spade).Id;
        var redDodge = targetHand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Heart).Id;
        var converted = targetHand.Single(c => c.Kind == CardKind.Slash).Id;
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]));
        Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, P()!.PromptId, slash.PlayedCardKind, slash.TargetCardId) { ConversionSource = slash.ConversionSource });
        Reach(() => P() is { PlayerSeat: 2 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "dodge"));
        var responses = P()!.Choices;
        Need(!responses.Any(c => c.Cards.Contains(blackDodge)) && responses.Any(c => c.Cards.SequenceEqual([redDodge]) && !c.Parameters.ContainsKey("conversion-skill-id")) &&
            responses.Any(c => c.Cards.SequenceEqual([converted]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:longdan"),
            "The native response frontier removes a wrong-color printed Dodge, retains the matching Hand Dodge and a genuinely virtual opposite-color Longdan Dodge.");
        Play();
        if (mode == "draw")
        {
            var counter = g.CreateSnapshot(1).Players[1].Hand.Single(c => c.Kind == CardKind.Nullification).Id;
            var drawCard = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo);
            var beforeDraw = g.CardMovements.Last().Sequence;
            Accept(new PlayCardCommand(0, drawCard.CardId!.Value, drawCard.TargetSeats, g.Revision, P()!.PromptId, drawCard.PlayedCardKind, drawCard.TargetCardId) { ConversionSource = drawCard.ConversionSource });
            Reach(() => P() is { PlayerSeat: 1, Kind: DecisionKind.Nullification });
            Need(E<NullificationRespondedEvent>() is [{ ResponderSeat: 2, ChainDepth: 1 }] &&
                g.ResolutionStack.OfType<NullificationWindowFrame>().Single().ChainDepth == 1 && P()!.Choices.Any(c => c.Cards.SequenceEqual([counter])),
                "After the enemy's genuine red first Nullification, the next frontier directly answers that counterspell and offers the ally's black physical Nullification, despite the original Gongqi Use requiring red." + CounterspellDiagnostic());
            Play(); // Both NPC counterspells are chosen by native AI from their real published choices.
            var responsesToDraw = E<NullificationRespondedEvent>();
            Need(responsesToDraw.Length == 2 && responsesToDraw[1] is { ResponderSeat: 1, ChainDepth: 2, EffectNullified: false } &&
                responsesToDraw[0].ResolutionId == responsesToDraw[1].ResolutionId &&
                g.CardMovements.Count(m => m.Sequence > beforeDraw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2,
                "The real second counterspell is accepted on the same original use, restores its effect and permits exactly the original Draw2.");
        }
        var target = mode == "draw" ? 1 : 0;
        Accept(new UseProgramSkillCommand(0, Jiefan, "aid-by-range", [], [target], g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "backwater") == true);
        Need(P()!.PlayerSeat == target, "The designated target owns the actual mode choice.");
        if (target == 0)
        {
            if (mode == "backwater")
            {
                var before = JsonSerializer.Serialize(g.CreateSnapshot(0));
                g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
                Need(before == JsonSerializer.Serialize(g.CreateSnapshot(0)), "The representative mode-choice checkpoint restores its exact owning frame and published choices.");
            }
            Answer(c => c.Parameters.GetValueOrDefault("branch") == mode);
        }
        Play(); // The actual AI target chooses its own draw branch; participants pay through native NPC input.
        var started = E<MouHanDangStartedEvent>().Single(e => !e.Gongqi);
        Need(E<MouHanDangBranchEvent>().Single().Branch == mode && E<MouHanDangCompletedEvent>().Count(e => e.FrameId == started.FrameId) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Jiefan), "The chosen complete branch returns once and spends this actual Play's activation.");
        var discards = E<MouHanDangDiscardedEvent>().Where(e => e.FrameId == started.FrameId).ToArray();
        if (mode != "draw") Need(discards.Select(e => e.Seat).SequenceEqual([1, 3]) && discards.All(e => e.From == CardLocation.Hand(e.Seat) && e.To == CardLocation.DiscardPile),
            "Only the two other native in-range participants discard their own entities, in frozen action order.");
        else Need(discards.Length == 0, "Draw-only does not pay the discard branch.");
        var draws = E<MouHanDangDrawReturnedEvent>();
        if (mode != "discard") Need(draws is [var d] && d.TargetSeat == target && d.RequestedCount == 2 && d.ActualCount == 2,
            "The genuine Draw2 is issued after the entire discard chain and uses the then-current native coverage, excluding the target itself.");
        else Need(draws.Length == 0, "Discard-only issues no draw.");
        if (mode == "backwater")
        {
            Need(E<MouHanDangJiefanDisabledEvent>().Length == 1, "Backwater signs skill invalidation before its effects.");
            Accept(new UseProgramSkillCommand(0, Finish, "kill", [], [2], g.Revision, P()!.PromptId)); Play();
            Need(E<PlayerDiedEvent>().Any(e => e.VictimSeat == 2 && e.KillerSeat == 0) && E<MouHanDangJiefanRestoredEvent>().Any(e => e.OwnerSeat == 0 && e.VictimSeat == 2),
                "A real attributed native kill restores the already invalidated skill.");
        }
    }
    private const string Gongqi = "mou-mobile:gongqi", Jiefan = "mou-mobile:jiefan", Quiet = "fixture:hd-quiet", Finish = "fixture:hd-finish", Watch = "fixture:hd-watch",
        Owner = "fixture:hd-owner", Ally = "fixture:hd-ally", EnemyA = "fixture:hd-enemy-a", EnemyB = "fixture:hd-enemy-b", Deck = "fixture:hd-deck";
    private static ShanheStartingCard C(CardKind k, Suit s) => new(k, s, 7);
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-han-dang-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["mou:han-dang-mobile", "classic:zhao-yun"]);
            foreach (var k in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.SilverLion, CardKind.DrawTwo, CardKind.Nullification })
            { var d = CardCatalog.Get(k); b.AddCard(new("fixture:hd-card-" + k, d.DisplayName, d.CategoryName, d.Description, k)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}," +
                "{\"id\":\"" + Finish + "\",\"revision\":1,\"activations\":[{\"id\":\"kill\",\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"usesPerTurn\":1,\"effects\":[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}]}]}," +
                "{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"lion-recovery\",\"window\":\"afterHealthChanged\",\"subject\":\"owner\",\"optional\":false,\"condition\":{\"kind\":\"healthChangeKindIs\",\"hpChangeKind\":\"recovery\"},\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]}]}";
            var labels = new Dictionary<string, string> { [Quiet] = "静候", [Finish] = "原生击杀", [Watch] = "真实回复停点" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(x => x.Key, x => new { name = x.Value, description = x.Value,
                    optionLabels = x.Key == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "谋韩当真实检查", "", "standard:none", "wu", 4, [Gongqi, Jiefan, Finish, Watch]));
            b.AddGeneral(new(Ally, "友方", "", "standard:none", "shu", 8, [Quiet]));
            b.AddGeneral(new(EnemyA, "龙胆响应者", "", "standard:none", "shu", 8, [Quiet, "classic:longdan"]));
            b.AddGeneral(new(EnemyB, "范围角色", "", "standard:none", "shu", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("fixture:hd-card-Dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋韩当原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
