using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouHuLieChecks
{
    private const string Hero = "mou:hu-lie", Plain = "fixture:mhl-plain", Hold = "fixture:mhl-hold", Deck = "fixture:mhl-deck";
    public static void ActualSelfYuRelayYitouRoundSlashesAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        ShanheStartingCard[] hand = [new(CardKind.DrawTwo, Suit.Spade, 1), new(CardKind.Slash, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Diamond, 4)];
        var setup = new ShanheBattleSetup(7, 1, "mou-hu-lie-native", Hero, 4, 0, 0, 0, 0, [Hold], hand, [],
            [new(Plain, Hp: 16) { StartingHand = [] }, new(Plain, Hp: 16) { StartingHand = [] }],
            [new(Plain, Hp: 16) { StartingHand = Enumerable.Range(0, 16).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i % 13 + 1)).ToArray() }], new string('a', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouHuLieReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouHuLie is not null)?.MouHuLie;
        CardLocation Position(int id) => game.CardMovements.Last(m => m.CardId == id).To;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(), Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount }), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command) { try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual Hu Lie command rejected."); } catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); } }
        void Answer(Func<PromptChoice, bool> pick) { var p = Prompt(); Need(p?.PlayerSeat == 0, "The actual human chooser answers the original prompt."); var c = p!.Choices.FirstOrDefault(pick); Need(c is not null, "Required actual Hu Lie choice missing."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision)); }
        bool Offer(string id) => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == id);
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal at " + line);
                if (Prompt() is not { PlayerSeat: 0 } p) { Submit(new AdvanceOneStepCommand(game.Revision)); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                if (p.Kind == DecisionKind.DiscardCards) { Submit(new DiscardCardsCommand(0, p.ValidCardIds.OrderBy(id => Player(0).Hand.Single(c => c.Id == id).Kind == CardKind.Slash ? 1 : 0).Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision)); continue; }
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                throw new InvalidOperationException("Unexpected actual human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual Hu Lie boundary not reached at " + line + Diagnostic());
        }
        void Activate(string id) { Reach(() => Offer(id)); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
        void Gift(int target)
        {
            Reach(() => Receipt()?.Stage == MouHuLieStage.GiftTarget);
            var previous = Events<MouHuLieObtainedEvent>().Select(e => e.Recipient).ToArray();
            Need(!Prompt()!.Choices.Any(c => c.Targets.Any(previous.Contains)), "This actual round's previous recipients cannot receive the same physical Yu again.");
            Answer(c => c.Targets.SequenceEqual([target]));
        }
        Submit(new StartGameCommand()); Activate("mou:chuanyu"); Reach(() => Receipt()?.Stage == MouHuLieStage.Card);
        var yu = Player(0).Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        var slash = Player(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        Answer(c => c.Cards.SequenceEqual([yu]));
        var movements = game.CardMovements.Count; var sequence = game.CardMovements.Max(m => m.Sequence);
        Gift(0); Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var marked = Events<MouHuLieMarkedEvent>().Single();
        Need(marked.Self && marked.InitialRecipient == 0 && marked.Sequence == sequence && game.CardMovements.Skip(movements).All(m => m.CardId != yu) && Position(yu) == CardLocation.Hand(0) && Player(0).Hand.Single(c => c.Id == yu).SkillMarkLabel == "舆", "Self Chuanyu marks the original current Hand entity in place, issues no fake Give movement, and exposes its actual public entity label.");
        var action = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == yu && a.ConversionSource is null && a.Kind == LegalActionKind.DrawTwo);
        Need(action is not null, "The original marked physical DrawTwo is truly legal through the ordinary Use entry.");
        Submit(new PlayCardCommand(0, yu, action!.TargetSeats, game.Revision, Prompt()!.PromptId)); Activate("mou:chuanyu"); Gift(1);
        Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(Position(yu) == CardLocation.Hand(1) && Events<MouHuLieMovedEvent>().Any(e => e.CardId == yu && e.From == CardLocation.DiscardPile && e.To == CardLocation.Hand(1)) && Events<CardUseDeclaredEvent>().Count(e => e.CardId == yu) == 1, "The real used entity enters Discard through native Use before its exact callback gives that same card to the next recipient.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Activate("mou:yitou");
        Reach(() => Events<MouHuLieYitouIssuedEvent>().Length == 1);
        var lease = Events<MouHuLieYitouIssuedEvent>().Single();
        Need(lease.Recipient == 1 && lease.Turn == 2 && Player(0).HandCount == 0 && Position(slash) == CardLocation.Hand(1) && Events<MouHuLieMovedEvent>().Count(e => e.FrameId == lease.FrameId && e.From == CardLocation.Hand(0) && e.To == CardLocation.Hand(1)) == 4, "At the actual other largest-Hand Play, Yitou genuinely transfers every one of Hu Lie's four remaining Hand entities, including the ordinary physical Slash, before issuing this actual-turn damage lease.");
        Activate("mou:chuanyu"); Gift(2); Activate("mou:chuanyu"); Gift(3);
        Reach(() => Receipt()?.Stage == MouHuLieStage.SlashOffer);
        var round = Receipt()!;
        Need(round.Actors.SequenceEqual([3, 0, 1, 2]) && Events<MouHuLieObtainedEvent>().Select(e => e.Recipient).SequenceEqual([0, 1, 2, 3]) && Position(yu) == CardLocation.DiscardPile && Events<MouHuLieClearedEvent>().Count(e => e.MarkId == marked.Yu.MarkId) == 1 && !Events<MouHuLieMovedEvent>().Any(e => e.Stage == MouHuLieStage.ClearChildren), "All four real recipients are frozen in native action order; after the fourth true Use the already-Discard Yu loses its mark without a fake second discard movement.");
        var damage = Events<MouHuLieDamageEvent>().Where(e => e.LeaseId == lease.FrameId).ToArray();
        Need(damage.Length > 0 && damage.All(e => e.SourceSeat == 1 && e.Turn == 2) && damage.All(e => Events<MouHuLieDrawReturnedEvent>().Count(d => d.DamageId == e.DamageId && d.Actual == 1 && d.Replacement == 0) == 1), "The actual recipient's ordinary native Slash causes real damage during its leased turn, and each real damage occurrence returns exactly one genuine native draw to Hu Lie.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "slash" && c.Targets.SequenceEqual([2])); Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        var issued = Events<MouHuLieSlashIssuedEvent>().Single().Origin;
        Need(issued.Actor == 3 && issued.Target == 2 && Receipt()?.Stage == MouHuLieStage.NativeSlash && game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.MouHuLieSlash == issued) && Events<CardUseAppearanceCapturedEvent>().Any(e => e.Action.ActionId == issued.ActionId && e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 3 && e.Action.ProviderSeat == 3 && e.Action.PhysicalCards.Count == 0 && e.Action.TargetSeats.SequenceEqual([issued.Target]) && e.Action.ConversionChain.SequenceEqual([issued.Source])), "The first round-end actor is the genuine foreign recipient: its native card-use frame and captured zero-material Use retain that actor/provider and the issuing Hu Lie source.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Receipt()?.UseId == issued.UseId && Events<MouHuLieSlashIssuedEvent>().Length == 1 && Events<MouHuLieYitouIssuedEvent>().Length == 1 && Events<MouHuLieClearedEvent>().Length == 1, "Cold retains the original round boundary, fixed recipient queue, source-specific native foreign-actor Use and previously paid Give/clear facts.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(() => Events<RoundEndedEvent>().Any(e => e.RoundNumber == 1));
        var queue = Events<MouHuLieSlashIssuedEvent>().Select(e => e.Origin).ToArray();
        Need(queue.Select(o => o.Actor).SequenceEqual([3, 0, 1]) && queue.All(o => o.Target == 2 && Events<CardUseDeclaredEvent>().Any(e => e.ResolutionId == o.UseId && e.SourceSeat == o.Actor && e.CardId == 0 && e.CardKind == CardKind.Slash) && Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == o.UseId) == 1 && Events<MouHuLieSlashReturnedEvent>().Count(e => e.UseId == o.UseId) == 1 && !Events<CardUseDebitRecordedEvent>().Any(e => e.Debit.CardActionId == o.ActionId)) && Events<MouHuLieObtainedEvent>().Length == 4 && Events<MouHuLieClearedEvent>().Length == 1 && Position(yu) == CardLocation.DiscardPile, "After Cold the original whole native Slash chain returns once per actual legal recipient, skips the selected target's illegal self-Slash, preserves ordinary Slash allowance, and never repeats the physical Yu payment or clearing.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-hu-lie", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.DrawTwo, CardKind.Slash }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mhl-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = """
                {"schemaVersion":__SCHEMA__,"skills":[
                  {"id":"__HOLD__","revision":1,"triggers":[{"id":"actual-round-zero-slash","window":"cardUseCommitted","ownerRelation":"observer","cardKinds":["slash"],"condition":{"kind":"compare","left":{"kind":"cardActionPhysicalCardCount"},"operator":"equal","right":{"kind":"integerConstant","value":0}},"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """.Replace("__SCHEMA__", SkillProgramCatalog.RulesSchemaVersion.ToString()).Replace("__HOLD__", Hold);
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Hold] = new { name = "真实轮末杀", description = "真实零材料使用提交后的原生子窗口暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules, labels); b.AddSkill(new(Hold, catalog.Presentations[Hold].Name, catalog.Presentations[Hold].Description) { Program = catalog.Programs[Hold], ProgramPresentation = catalog.Presentations[Hold] });
            b.AddGeneral(new(Plain, "真实胡烈目标", "", "standard:none", "wei", 16));
            b.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:mhl-" + CardKind.Dodge, Suit.Spade, 10)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋胡烈真实传舆与倚投", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
