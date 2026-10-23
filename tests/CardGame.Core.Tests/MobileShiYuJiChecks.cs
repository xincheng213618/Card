using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class MobileShiYuJiChecks
{
    private const string Hero = "mobile:shi-yu-ji", Fuji = "mobile:shi-fuji", Daozhuan = "mobile:shi-daozhuan",
        Calm = "fixture:shi-yu-ji-calm", Quiet = "fixture:shi-yu-ji-quiet",
        SlashOwner = "fixture:shi-yu-ji-slash-owner", DuelOwner = "fixture:shi-yu-ji-duel-owner",
        HighestSlash = "fixture:shi-yu-ji-highest-slash", DoubleSlash = "fixture:shi-yu-ji-double-slash",
        Peer = "fixture:shi-yu-ji-peer", Deck = "fixture:shi-yu-ji-deck";

    public static void FujiGiftMarksEmpoweredFirstSlashAndMarkedDodge()
    {
        var s = Scene.Start("fuji-first");
        s.Play();
        var realSlash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Club).Id;
        var dodge = s.V(0).Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.ActivateFuji([dodge], [3]); s.Play();
        s.Need(s.E<MobileShiFujiCardMarkedEvent>() is [{ RecipientSeat: 3, CardId: var markedId, Kind: CardKind.Dodge, Suit: Suit.Club }] &&
                markedId == dodge && s.Zone(dodge) == CardLocation.Hand(3),
            "The gifted physical Dodge is marked as a Fuji card and reaches the single adjacent recipient's hand.");
        s.Need(s.E<MobileShiFujiEmpowerIssuedEvent>().Length == 1 && FujiEmpowered(s) && s.V(0).HandCount == 3,
            "Ending with the fewest hand draws one card and arms the empowered first-Slash/Dodge window.");

        var peerHp = s.V(1).Hp;
        s.NativeCard(realSlash, 1); s.Play();
        s.Need(s.V(1).Hp == peerHp - 2 && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2),
            "The empowered first real Slash gains exactly one base damage.");
        s.Need(s.E<MobileShiFujiCardUsedEvent>().Any(e => e.Empowered && e.UserSeat == 0 && e.Name == CardKind.Slash) &&
                s.E<MobileShiFujiSuitGainIssuedEvent>().Any(e => e.UserSeat == 0 && e.Suit == Suit.Club && s.Zone(e.GainedCardId) == CardLocation.Hand(0)),
            "The empowered Slash also grants its user one same-suit card from the draw pile.");

        var before = s.V(3).HandCount;
        s.End();
        s.Reach(() => s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 3 && e.Action.EffectiveKind == CardKind.Dodge &&
            e.Action.PhysicalCards.Any(cost => cost.CardId == dodge)));
        s.Need(s.E<MobileShiFujiCardUsedEvent>().Any(e => !e.Empowered && e.UserSeat == 3 && e.CardId == dodge && e.Name == CardKind.Dodge) &&
                s.E<MobileShiFujiSuitGainIssuedEvent>().Any(e => e.UserSeat == 3 && e.Suit == Suit.Club) &&
                s.E<MobileShiFujiDodgeDrawIssuedEvent>().Any(e => e.UserSeat == 3 && e.CardId == dodge) && s.V(3).HandCount == before + 1,
            "Answering the highest-hand attack with the marked Dodge grants one same-suit card and one post-completion draw.");

        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.Count == 1 && !c.Parameters.ContainsKey("conversion-skill-id"));
        s.Reach(() => s.E<MobileShiFujiDodgeDrawIssuedEvent>().Any(e => e.UserSeat == 0));
        s.Need(s.E<MobileShiFujiCardUsedEvent>().Any(e => e.Empowered && e.UserSeat == 0 && e.Name == CardKind.Dodge) &&
                s.E<MobileShiFujiSuitGainIssuedEvent>().Any(e => e.UserSeat == 0 && e.Suit == Suit.Club),
            "The owner's first Dodge while armed carries the empowered effects too.");
        s.Need(FujiEmpowered(s), "The armed window survives until the owner's next turn starts.");
        s.Replay();
    }

    public static void FujiMarkedSlashDuelResponseAndEmpowerExpiry()
    {
        var s = Scene.Start("fuji-expiry");
        s.Play();
        var marked = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        s.ActivateFuji([marked], [1]); s.Play();
        s.Need(s.E<MobileShiFujiCardMarkedEvent>() is [{ RecipientSeat: 1, CardId: var markedId }] && markedId == marked && FujiEmpowered(s),
            "A second scene marks the gifted Slash and arms the empowered window again from the fewest hand.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber > turn && !FujiEmpowered(s) && s.E<ProgramBooleanStateChangedEvent>().Any(e =>
                e.OwnerSeat == 0 && e.SkillId == Fuji && e.StateId == "fuji-empowered" && !e.Value),
            "The owner's next turn start expires the empowered window.");

        var before = s.V(1).HandCount;
        s.Native(CardKind.Duel, 1);
        s.Play();
        s.Need(s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response &&
                e.Action.PhysicalCards.Any(cost => cost.CardId == marked)) &&
            s.V(1).HandCount == before - 1 && s.E<MobileShiFujiSuitGainIssuedEvent>().Length == 0,
            "The marked Slash paid to a Duel is played out, not used, so it earns no Fuji suit gain.");
        s.Need(s.V(0).Hp == 2 && s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) &&
                s.E<MobileShiFujiCardUsedEvent>().Length == 0,
            "The expired window no longer empowers anyone while the declined Duel costs one health.");
        s.Replay();
    }

    public static void DaozhuanOwnTurnExhaustBlocksResponsesUntilRoundRestore()
    {
        var s = Scene.Start("daozhuan-exhaust");
        s.Play();
        s.Need(s.DaozhuanActions().Any(), "Daozhuan offers its one-card basic conversions on the owner's turn.");
        var material = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        var peerHp = s.V(1).Hp;
        s.DaozhuanPlay(material, 1); s.Play();
        s.Need(s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash &&
                e.Action.ConversionChain.Any(source => source.SkillId == Daozhuan) && e.Action.PhysicalCards.Any(cost => cost.CardId == material)) &&
                s.Zone(material) == CardLocation.DiscardPile && s.V(1).Hp == peerHp - 1,
            "The paid hand card is discarded to use an actual converted Slash for one damage.");
        s.Need(s.E<MobileShiDaozhuanUseRecordedEvent>() is [{ OwnerSeat: 0, Name: CardKind.Slash, TurnNumber: 1, RoundNumber: 1, OwnerIsTurnPlayer: true }] &&
                !s.DaozhuanActions().Any(),
            "The own-turn use records once and exhausts every further Daozhuan conversion this turn.");
        s.Native(CardKind.Duel, 1); s.Play();
        s.End();
        var prompted = 0;
        while (true)
        {
            s.Reach(() => s.G.State.TurnNumber >= 5 ||
                s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash });
            if (s.G.State.TurnNumber >= 5 &&
                s.P is not { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash }) break;
            s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != Daozhuan),
                "The exhausted state also removes Daozhuan from every foreign-turn response window.");
            s.AnswerRealOrDecline(); prompted++;
        }
        s.Need(prompted >= 2, "The scripted foreign turns produced their expected response windows.");
        s.Need(s.E<MobileShiDaozhuanUseRecordedEvent>().Length == 1,
            "The next round's start restores the exhausted conversions without inventing a second recorded use.");
        s.Play();
        var restored = s.V(0).Hand.Single(c => c.Kind == CardKind.Peach).Id;
        s.Need(s.DaozhuanActions().Any(a => a.CardId == restored), "The restored round offers the Slash name once more.");
        s.DaozhuanPlay(restored, 3); s.Play();
        s.Need(s.E<MobileShiDaozhuanUseRecordedEvent>() is [{ Name: CardKind.Slash }, { Name: CardKind.Slash, TurnNumber: 5, RoundNumber: 2 }] &&
                !s.DaozhuanActions().Any(),
            "The restored round allows the Slash name once more and re-exhausts after the own-turn use.");
        s.Replay();
    }

    public static void DaozhuanRoundNameLedgerAndPerTurnTotalOffTurn()
    {
        var s = Scene.Start("daozhuan-ledger");
        s.Play();
        s.Need(s.DaozhuanActions().Any(), "Daozhuan stays available when the owner has not used it this round.");
        s.End();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var firstMaterial = s.V(0).Hand.First(c => c.Kind == CardKind.Alcohol).Id;
        s.Answer(c => c.Cards.Count == 1 && c.Cards[0] == firstMaterial &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == Daozhuan &&
            c.Parameters.GetValueOrDefault("conversion-binding-id") == "daozhuan-dodge");
        s.Need(s.E<MobileShiDaozhuanUseRecordedEvent>() is [{ OwnerSeat: 0, Name: CardKind.Dodge, TurnNumber: 3, RoundNumber: 1, OwnerIsTurnPlayer: false }] &&
                s.Zone(firstMaterial) == CardLocation.DiscardPile,
            "An off-turn converted Dodge records its name in its own turn without exhausting the skill.");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != Daozhuan),
            "A second Daozhuan use inside the same foreign turn is banned by the shared once-per-turn ceiling.");
        s.Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("conversion-skill-id"));
        s.Play();
        s.Need(s.DaozhuanActions().Any(), "The next round frees the spent names again.");
        s.End();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var secondMaterial = s.V(0).Hand.Single(c => c.Kind == CardKind.Alcohol).Id;
        s.Answer(c => c.Cards.Count == 1 && c.Cards[0] == secondMaterial &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == Daozhuan &&
            c.Parameters.GetValueOrDefault("conversion-binding-id") == "daozhuan-dodge");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != Daozhuan) &&
                s.E<MobileShiDaozhuanUseRecordedEvent>() is
                    [{ Name: CardKind.Dodge, TurnNumber: 3 }, { Name: CardKind.Dodge, TurnNumber: 7, RoundNumber: 2 }] &&
                s.Zone(secondMaterial) == CardLocation.DiscardPile,
            "The new round frees the Dodge name once while the per-turn and per-round ceilings still bind.");
        s.Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("conversion-skill-id"));
        s.Replay();
    }

    private static bool FujiEmpowered(Scene s) => s.V(0).SkillRuntimeStates!.Single(skill => skill.SkillId == Fuji)
        .BooleanStates!.Single(state => state.StateId == "fuji-empowered").Value;

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public IEnumerable<LegalAction> DaozhuanActions() => G.GetHumanLegalActions()
            .Where(a => a.ConversionSource?.SkillId == Daozhuan);
        public void ActivateFuji(int[] cards, int[] targets) =>
            Accept(new UseProgramSkillCommand(0, Fuji, "gift", cards, targets, G.Revision, P!.PromptId));
        public void DaozhuanPlay(int material, int target)
        {
            var action = DaozhuanActions().First(a => a.CardId == material && a.TargetSeats.SequenceEqual([target]) &&
                a.ConversionSource!.BindingId == "daozhuan-slash");
            Accept(new PlayCardCommand(0, material, [target], G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        }
        public void Native(CardKind kind, int? target = null)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is > 0 && a.ConversionSource is null &&
                V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(action is not null, "The expected actual hand-card action must be legal: " + kind);
            PlayAction(action!);
        }
        public void NativeCard(int cardId, int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId == cardId && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            PlayAction(action);
        }
        private void PlayAction(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected native prompt choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void AnswerRealOrDecline()
        {
            var p = P!;
            var real = p.Choices.FirstOrDefault(c => c.Cards.Count == 1 && !c.Parameters.ContainsKey("conversion-skill-id"));
            if (real is null) { Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("conversion-skill-id")); return; }
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, real.Id, G.Revision));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 300; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("conversion-skill-id")); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Yu Ji boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Yu Ji did not reach the expected bounded native boundary.");
        }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the marked cards, quotas and every state exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Yu Ji command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Actions = G.GetHumanLegalActions().Select(a => new { a.CardId, a.Kind, a.TargetSeats, Skill = a.ConversionSource?.SkillId }).ToArray(),
            Marked = E<MobileShiFujiCardMarkedEvent>(), Used = E<MobileShiFujiCardUsedEvent>(),
            Recorded = E<MobileShiDaozhuanUseRecordedEvent>(), States = E<ProgramBooleanStateChangedEvent>(),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = nodeId switch
            {
                "fuji-first" => [new(CardKind.Slash, Suit.Club, 4), new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 9)],
                "fuji-expiry" => [new(CardKind.Slash, Suit.Club, 8), new(CardKind.Duel, Suit.Spade, 1), new(CardKind.Peach, Suit.Heart, 12)],
                "daozhuan-exhaust" => [new(CardKind.Peach, Suit.Heart, 2), new(CardKind.Duel, Suit.Spade, 3),
                    new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Peach, Suit.Heart, 6)],
                _ => [new(CardKind.Alcohol, Suit.Heart, 2), new(CardKind.Alcohol, Suit.Spade, 3),
                    new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Peach, Suit.Heart, 5)]
            };
            var peerCards = nodeId is "fuji-first" ? 4 : 5;
            ShanheStartingCard[] PeerHand(int seat) =>
                Enumerable.Range(0, peerCards).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3 + i)).ToArray();
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: 8,
                SlashBonus: seat == 2 && nodeId is "daozhuan-ledger" ? 1 : 0,
                ExtraSkillIds: nodeId switch
                {
                    "fuji-first" => seat == 1 ? [Quiet, HighestSlash] : seat == 2 ? [Quiet, SlashOwner] : [Quiet],
                    "fuji-expiry" => [Quiet],
                    "daozhuan-exhaust" => [Quiet, SlashOwner],
                    _ => seat == 2 ? [Quiet, DoubleSlash] : [Quiet, DuelOwner]
                }) { StartingHand = PeerHand(seat) };
            var ownerHp = nodeId is "daozhuan-ledger" ? 6 : 3;
            var ownerMaxHpBonus = nodeId is "daozhuan-ledger" ? 3 : 0;
            var setup = new ShanheBattleSetup(91, 1, nodeId, Hero, ownerHp, ownerMaxHpBonus, 0, 0, 0, [Calm], opening, [],
                [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-yu-ji", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Calm}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{SlashOwner}}","revision":1,"triggers":[{"id":"slash-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{HighestSlash}}","revision":1,"triggers":[{"id":"slash-highest","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{DoubleSlash}}","revision":1,"triggers":[
                {"id":"slash-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]},
                {"id":"slash-least-again","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{DuelOwner}}","revision":1,"triggers":[{"id":"duel-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},
                  {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Calm] = new { name = Calm, description = "固定手牌的安静主公。" },
                [Quiet] = new { name = Quiet, description = "保留真实回合并跳过普通摸牌出牌。" },
                [SlashOwner] = new { name = SlashOwner, description = "回合开始对最少手牌角色使用虚拟杀。" },
                [HighestSlash] = new { name = HighestSlash, description = "回合开始对最多手牌其他角色使用虚拟杀。" },
                [DoubleSlash] = new { name = DoubleSlash, description = "回合开始对最少手牌角色使用两次虚拟杀。" },
                [DuelOwner] = new { name = DuelOwner, description = "回合开始对最少手牌角色使用虚拟决斗。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定真实基本牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势于吉符济道转实际检查", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
