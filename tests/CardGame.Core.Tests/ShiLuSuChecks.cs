using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiLuSuChecks
{
    private const string Hero = "mobile:shi-lu-su", Haoshi = "mobile:shi-haoshi", Dimeng = "mobile:shi-dimeng",
        Qizhou = "mobile:shi-qizhou", Shanxi = "mobile:shi-shanxi",
        Driver = "fixture:shi-lu-su-driver", Quiet = "fixture:shi-lu-su-quiet", Aggressor = "fixture:shi-lu-su-aggro", Watch = "fixture:shi-lu-su-watch",
        Peer = "fixture:shi-lu-su-peer", Foe = "fixture:shi-lu-su-foe", Deck = "fixture:shi-lu-su-deck";
    public static void ActualForeignHandUseTwoRefillsAndNextTurnExpiryCold()
    {
        var s = Scene.Start(shared: true); s.Play(); s.End(); s.Play();
        var grant = s.E<ShiLsHandGrantEvent>().Single();
        s.Need(grant.Source.OwnerSeat == 1 && grant.Beneficiary == 0, "An actual Lu Su ending phase grants another actor access until Lu Su's next actual turn.");
        var id = s.V(1).Hand.Single().Id;
        s.Need(s.G.CreateSnapshot(0).Players.Single(p => p.Seat == 1).Hand.Select(c => c.Id).SequenceEqual([id]) &&
            s.G.CreateSnapshot(2).Players.Single(p => p.Seat == 1).Hand.Count == 0 && s.V(0).Hand.All(c => c.Id != id),
            "Only the beneficiary privately sees the foreign real hand; the entity remains Lu Su's hand.");
        s.Cold(); s.Native(id); s.Play();
        s.Need(s.V(1).HandCount == 3 && s.E<ShiLsRefillIssuedEvent>().Length == 1, "The genuine last foreign Hand use refills Lu Su to exactly three.");
        var a = s.E<CardUseAppearanceCapturedEvent>().Select(e => e.Action).Single(a => a.PhysicalCards.Any(c => c.CardId == id));
        s.Need(a is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 1, PhysicalCards.Count: 1 } && a.PhysicalCards[0].From == CardLocation.Hand(1) &&
            s.G.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing), "Actual Use preserves the autonomous actor, real provider and original physical source.");
        foreach (var next in s.V(1).Hand.Select(c => c.Id).ToArray()) { s.Native(next); s.Play(); }
        s.Need(s.V(1).HandCount == 3 && s.E<ShiLsRefillIssuedEvent>().Length == 2, "The second authorized empty-Hand payment refills exactly once.");
        s.Cold(); foreach (var next in s.V(1).Hand.Select(c => c.Id).ToArray()) { s.Native(next); s.Play(); }
        s.Need(s.V(1).HandCount == 0 && s.E<ShiLsRefillIssuedEvent>().Length == 2, "A third empty-Hand payment cannot mint a third refill.");
        s.End(); s.Reach(() => s.E<ShiLsHandGrantExpiredEvent>().Any(e => e.GrantId == grant.GrantId));
        s.Need(s.G.CreateSnapshot(0).Players.Single(p => p.Seat == 1).Hand.Count == 0, "The original grant expires at Lu Su's next actual turn prelude."); s.Cold(); s.Replay();

        var converted = Scene.Start(shared: true, wusheng: true); converted.Play(); converted.End(); converted.Play();
        var red = converted.V(1).Hand.Single().Id; converted.Cold(); converted.ConvertedSlash(red); converted.Play();
        var slash = converted.E<CardActionAcceptedEvent>().Select(e => e.Action).Single(a => a.PhysicalCards.Any(c => c.CardId == red));
        converted.Need(slash is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 1, EffectiveKind: CardKind.Slash } &&
            slash.ConversionChain is [{ SkillId: "standard:wusheng", OwnerSeat: 0 }] && slash.PhysicalCards.Single().From == CardLocation.Hand(1),
            "The beneficiary converts Lu Su's real red non-Slash with the beneficiary's own Wusheng; conversion ownership and physical provider stay distinct."); converted.Replay();

        var marked = Scene.Start(shared: true, shanxi: true); marked.Play(); marked.End(); marked.Play();
        var material = marked.V(1).Hand.Single().Id;
        marked.Native(marked.V(0).Hand.Single(c => c.Kind == CardKind.BaguaFormation).Id); marked.MarkYingji(1, material); marked.Play();
        marked.Need(marked.E<ShiHqYingjiMarkedEvent>().Single(e => e.CardId == material) is { Holder: 1, Origin: ShiHqMarkOrigin.SelectedHand, Source.OwnerSeat: 0 } &&
            marked.V(1).Hand.Single(c => c.Id == material).SkillMarkLabel?.Contains("应机", StringComparison.Ordinal) == true && marked.V(0).Equipment.Any(c => c.Kind == CardKind.BaguaFormation),
            "A genuine equipment entry lets the beneficiary's Qizhou mark Lu Su's authorized physical hand card without moving or impersonating it.");
        marked.Cold(); marked.MarkedDuel(material, 2); marked.Play();
        var duel = marked.E<CardUseAppearanceCapturedEvent>().Select(e => e.Action).Single(a => a.PhysicalCards.Any(c => c.CardId == material));
        var spent = marked.E<ShiHqShanxiUsedEvent>().Single();
        marked.Need(duel is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 1, EffectiveKind: CardKind.Duel } &&
            duel.ConversionChain is [{ SkillId: Shanxi, OwnerSeat: 0 }] && duel.PhysicalCards.Single().From == CardLocation.Hand(1) &&
            spent is { Source.OwnerSeat: 0, Target: 2 } && spent.CardId == material && spent.ActionId == duel.ActionId &&
            marked.E<ProgramViewAsConvertedEvent>().Count(e => e.FrameId == spent.UseId && e.SkillId == Shanxi && e.OutputKind == CardKind.Duel && e.PhysicalCardIds.SequenceEqual([material])) == 1 &&
            marked.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1 &&
            marked.E<ShiLsUsePaymentCapturedEvent>().Count(e => e.UseId == spent.UseId && e.ActionId == duel.ActionId) == 1 && marked.E<ShiLsRefillIssuedEvent>().Length == 1 && marked.V(1).HandCount == 3,
            "The beneficiary's own Shanxi pays one real foreign marked material, preserves actor/provider/original Hand source, and completes the native Duel and Haoshi payment once.");
        marked.Need(marked.G.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Shanxi || !a.TargetSeats.Contains(2)) &&
            marked.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Shanxi && a.TargetSeats.SequenceEqual([3])),
            "The real foreign-material native Duel consumes only its original Play target; another living target remains legal."); marked.Cold(); marked.Replay();
    }
    public static void ActualForeignHandDodgeResponsePreservesActorProviderCold()
    {
        var s = Scene.Start(shared: true, response: true); s.Play(); s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var id = s.V(1).Hand.Single().Id; var hp = s.V(0).Hp;
        var originalSlash = s.G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Slash && f.CardAttack?.TargetSeat == 0);
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([id])), "The actual response query offers the authorized foreign Dodge.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([id]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Driver && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "completed-dodge"));
        var a = s.E<CardActionAcceptedEvent>().Select(e => e.Action).Single(a => a.PhysicalCards.Any(c => c.CardId == id));
        var completed = s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(f => f.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Dodge);
        s.Need(completed is { Continuation: null, CompletedResponseReturn.IsCommitted: false, Action: { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, ActorSeat: 0, ProviderSeat: 1, ResponderSeat: 0, RequesterSeat: null } } &&
            completed.Action.ActionId == a.ActionId && completed.Action.PhysicalCards.Single().CardId == id && completed.Action.PhysicalCards.Single().From == CardLocation.Hand(1) &&
            completed.ParentFrameId == originalSlash.Id && completed.AttackOwnerFrameId == originalSlash.Id && completed.Action.ParentActionId == originalSlash.Action!.ActionId && completed.Action.OpponentSeat == originalSlash.SourceSeat &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile && s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && s.V(0).Hp == hp,
            "The genuine foreign-Hand Dodge reaches its typed completed-use window after physical cleanup, with its exact accepted actor/provider/original Hand and original native Slash parent.");
        s.Cold(); s.Replay(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "completed-dodge");
        s.Reach(() => s.E<ShiLsRefillIssuedEvent>().Length == 1 && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == originalSlash.Id)); s.Play();
        s.Need(a is { Type: CardActionType.Response, ActorSeat: 0, ProviderSeat: 1, ResponderSeat: 0, RequesterSeat: null } &&
            a.PhysicalCards.Single().From == CardLocation.Hand(1) && s.V(0).Hp == hp && s.V(1).HandCount == 3,
            "Playing an authorized Dodge is the beneficiary's real autonomous response, pays Lu Su's last physical Hand card and refills its provider.");
        s.Need(s.E<ProgramCardTriggerResolvedEvent>().Count(e => e.SkillId == Driver && e.TriggerId == "after-shared-dodge" && e.Activated) == 1 &&
            s.E<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Driver && e.BindingId == "after-shared-dodge" && e.Activated && e.Completed) == 1 &&
            s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActionId == a.ActionId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == originalSlash.Id) == 1 &&
            s.G.ResolutionStack.All(f => f.Id != completed.Id) && s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && s.E<ShiLsRefillIssuedEvent>().Length == 1 && s.V(0).Hp == hp,
            "Cold completion resumes the original Slash once, resolves its one real use-completed observer, and neither repays the foreign response nor repeats cleanup or Haoshi refill."); s.Cold(); s.Replay();
    }
    public static void DimengSelfExchangeMultiDiscardAndPaidChildCold()
    {
        var s = Scene.Start(); s.Play(); var own = s.V(0).Hand.Select(c => c.Id).Order().ToArray(); var foreign = s.V(1).Hand.Select(c => c.Id).Order().ToArray();
        s.Skill(); s.Answer(c => Branch(c) == "pair" && c.Targets.SequenceEqual([0])); s.Cold(); s.Answer(c => Branch(c) == "pair" && c.Targets.SequenceEqual([1]));
        s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "discard") == true);
        s.Need(s.V(0).Hand.Select(c => c.Id).Order().SequenceEqual(foreign) && s.V(1).Hand.Select(c => c.Id).Order().SequenceEqual(own),
            "Dimeng may include Lu Su and exchanges the original physical hands before any benefit or cost.");
        s.Answer(c => Branch(c) == "discard"); var originalCount = s.V(0).HandCount;
        s.Need(s.P!.RequiredCardCount == 2 && s.P.MinimumCardCount == 2, "The projected unpaid HE collector advertises two simultaneous selections.");
        s.Answer(c => Branch(c) == "add"); s.Answer(c => Branch(c) == "add");
        s.Need(s.V(0).HandCount == originalCount && s.E<ShiLsDiscardPaidEvent>().Length == 0, "Individual selection clicks do not discard a material early.");
        s.Cold(); s.Answer(c => Branch(c) == "pay"); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.V(0).HandCount == originalCount - 2 && s.E<ShiLsDiscardPaidEvent>().Length == 1, "One real logical discard pays all selected materials before its observer child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.E<ShiLsDiscardPaidEvent>().Length == 1 && s.G.GetHumanLegalActions().All(a => a.SkillId != Dimeng), "Cold resuming the paid child neither pays again nor restores this phase's activation."); s.Replay();
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Skill() => Accept(new UseProgramSkillCommand(0, Dimeng, "exchange-hands", [], [], G.Revision, P!.PromptId));
        public void Native(int id)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.Kind != LegalActionKind.Recast && a.ConversionSource is null);
            Need(a is not null, "Missing native foreign card action."); Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void ConvertedSlash(int id)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is { SkillId: "standard:wusheng", OwnerSeat: 0 });
            Need(a is not null, "The beneficiary's own Wusheng must offer a real foreign red hand entity.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void MarkYingji(int target, int id)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Qizhou && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mobile-shi-he-qi" && Branch(c) == "target") == true);
            Answer(c => c.Targets.SequenceEqual([target]));
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mobile-shi-he-qi" && Branch(c) == "card") == true);
            var frame = G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.ShiHeQi is not null);
            var slot = frame.ShiHeQi!.Materials.ToList().FindIndex(c => c.CardId == id);
            Need(slot >= 0, "The original Qizhou hand-slot receipt contains the authorized foreign entity.");
            Answer(c => c.Parameters.GetValueOrDefault("slot") == slot.ToString());
        }
        public void MarkedDuel(int id, int target)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is { SkillId: Shanxi, OwnerSeat: 0 } && a.TargetSeats.SequenceEqual([target]));
            Need(a is not null, "The beneficiary's own Shanxi must offer the real authorized marked foreign hand entity.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact original native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 400; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Lu Su native prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "Bounded fixture remains alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missing bounded native boundary.");
        }
        public void Cold() { var expected = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(expected == State(), "Cold recovery preserves original hand authorization and paid native continuation."); }
        public void Replay() { var expected = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(expected == State(), "Accepted native commands reproduce physical actor/provider payments and grants."); }
        public void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why + " " + Dump()); }
        private void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Lu Su command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Grants = E<ShiLsHandGrantEvent>(), Refills = E<ShiLsRefillIssuedEvent>(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(bool shared = false, bool response = false, bool wusheng = false, bool shanxi = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(shanxi, response));
            ShanheStartingCard[] own = shanxi ? [new(CardKind.BaguaFormation, Suit.Club, 2)] : shared ? [] : [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Peach, Suit.Heart, 3)];
            var foes = new[] { new ShanheCombatantDefinition(shared ? Hero : Peer, Hp: 3, ExtraSkillIds: shared ? [Quiet] : [])
                { StartingHand = shared ? [new(response ? CardKind.Dodge : CardKind.DrawTwo, Suit.Heart, 7)] : [new(CardKind.Dodge), new(CardKind.Dodge), new(CardKind.Peach), new(CardKind.Alcohol)] },
                new ShanheCombatantDefinition(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach)] },
                new ShanheCombatantDefinition(response ? Foe : Peer, Hp: 8) { StartingHand = response ? [new(CardKind.Slash, Suit.Spade, 7)] : [] } };
            var setup = new ShanheBattleSetup(638, 1, "shi-lu-su-native", shared ? Foe : Hero, shared ? 8 : 1, 0, 0, 0, 0, wusheng ? [Driver, Watch, "standard:wusheng"] : shanxi ? [Driver, Watch, Qizhou, Shanxi] : [Driver, Watch], own, [], foes, [], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool shanxi, bool response) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-lu-su", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            if (shanxi) CharacterFixtureContent.Register(b, "mobile:shi-he-qi");
            var responseObserver = response ? """
                ,{"id":"after-shared-dodge","window":"cardUseCompleted","includeResponseUses":true,"ownerRelation":"actor","cardKinds":["dodge"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-dodge","options":[{"id":"completed-dodge"}]}]}
                """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}{{responseObserver}}]},
              {"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Aggressor}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-lu-su.dimeng.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "保留真实出牌", description = "固定实际摸牌。", optionLabels = response ? new Dictionary<string, string> { ["completed-dodge"] = "继续原生闪使用完成" } : new Dictionary<string, string>() }, [Quiet] = new { name = "安静同桌", description = "保留真实结束阶段。" },
                [Aggressor] = new { name = "原生进攻者", description = "实际AI出杀。" }, [Watch] = new { name = "付款观察", description = "真实弃牌子结算。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 8)); b.AddGeneral(new(Foe, "真实进攻者", "", Aggressor, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势鲁肃原生共享手牌与交换", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer, Foe], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
