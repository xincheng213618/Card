using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouLuBuMobileChecks
{
    public static void ActualDoubleResponseMultiObtainAndVictimBranchesCold()
    {
        foreach (var grant in new[] { false, true })
        {
            var s = Scene.Start(grant); s.Play();
            s.Need(s.V().Hp == 5 && s.V().MaxHp == 5, "The current formal mobile general starts at five of five, overriding the stale four-HP hero API.");
            s.Use(CardKind.Alcohol); s.Play(); s.Use(CardKind.Slash, 3);
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == Liyu) == true);
            var damage = s.E<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 3);
            s.Need(damage.Amount == (grant ? 2 : 3) && s.E<MouLuBuWushuangBonusEvent>().Length == (grant ? 0 : 1),
                "A real unresponded Slash gets the turn's mandatory bonus, while a genuinely paid partial double-Dodge response blocks that bonus even though Alcohol damage still lands.");
            if (grant) s.Need(s.E<RequiredResponseProgressEvent>().Any(e => e.ResponderSeat == 3 && e.RequiredCardKind == CardKind.Dodge && e.ResponseCount == 1 && e.RequiredResponseCount == 2),
                "The partial native response is one actual Dodge of the required two, not an invented response counter.");
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            for (var i = 0; i < damage.Amount; i++)
            {
                s.Reach(() => s.Branch("take"));
                if (i == 1) s.Cold();
                s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take" && c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.GetValueOrDefault("slot-index") == "0");
            }
            s.Play();
            var started = s.E<MouLuBuLiyuStartedEvent>().Single();
            var obtained = s.E<MouLuBuLiyuObtainedEvent>().Where(e => e.FrameId == started.FrameId).ToArray();
            var draw = s.E<MouLuBuLiyuDrawReturnedEvent>().Single(e => e.FrameId == started.FrameId);
            s.Need(obtained.Length == damage.Amount && obtained.All(e => e.ActualGain && e.From == CardLocation.Hand(3) && e.To == CardLocation.Hand(0) &&
                s.G.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To) == 1) &&
                draw is { RecipientSeat: 3, CategoryMask: 7 } && draw.Requested == damage.Amount && draw.Actual == damage.Amount &&
                s.E<MouLuBuLiyuCompletedEvent>() is [{ Selections: var selections, ActualObtained: var gained, CategoryMask: 7 }] && selections == damage.Amount && gained == damage.Amount,
                "Liyu pays each actual concealed slot once, obtains up to real damage count, then the original victim truly draws the actual obtained count. Both real gain steps supply the three-category union.");
            var branch = s.E<MouLuBuLiyuBranchEvent>().Single();
            s.Need(branch.ChooserSeat == 3 && branch.Branch == (grant ? "wushuang" : "duel"), "The genuine NPC victim independently selects its offered consequence.");
            if (grant)
                s.Need(s.E<MouLuBuLiyuGrantEvent>() is [{ RecipientSeat: 3 }] && s.V(3).Skills?.Any(x => x.ContentId == Wushuang) == true && s.E<MouLuBuLiyuDuelIssuedEvent>().Length == 0,
                    "With no lawful third-party Duel target, the victim receives the complete mobile Wushuang, including its damage bonus, rather than Classic's passive subset.");
            else
            {
                var duel = s.E<MouLuBuLiyuDuelIssuedEvent>().Single();
                var nativeUse = s.NativeUses.GetValueOrDefault(duel.UseId);
                s.Need(duel.ActorSeat == 0 && duel.TargetSeat == 1 && s.E<MouLuBuLiyuDuelReturnedEvent>().Count(e => e.FrameId == started.FrameId && e.UseId == duel.UseId && e.DamageApplied) == 1 &&
                    s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) &&
                    nativeUse is { CardId: 0, CardKind: CardKind.Duel, SourceSeat: 0, PhysicalCardIds.Count: 0,
                        Action: { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, ActorSeat: 0, ProviderSeat: 0, PhysicalCards.Count: 0,
                            EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } } &&
                    s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == duel.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel && e.SourceSeat == 0) == 1 &&
                    s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == duel.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) == 1 &&
                    s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == started.OuterUseId && e.CardKind == CardKind.Slash) == 1,
                    "The victim's real target choice issues and completely returns a neutral zero-material native Duel, then resumes the original paid physical Slash exactly once.");
            }
            var nextVictim = grant ? 3 : 2;
            s.Use(CardKind.Duel, nextVictim); s.Play();
            s.Need(s.E<MouLuBuWushuangBonusEvent>().Length == 1 && s.E<DamageAppliedEvent>().Last(e => e.SourceSeat == 0 && e.TargetSeat == nextVictim).Amount == (grant ? 2 : 1),
                "The partial Dodge did not spend the once-per-turn bonus; the next unresponded Duel can spend it. A bonus already spent by Slash cannot be gained again by another Duel.");
            if (grant)
            {
                s.End(); s.Reach(() => s.G.State.TurnNumber >= 5 && s.IsPlay());
                var issued = s.E<MouLuBuLiyuGrantEvent>().Single();
                s.Need(s.E<MouLuBuLiyuGrantExpiredEvent>().Single().GrantId == issued.GrantId && s.E<MouLuBuLiyuGrantExpiredEvent>().Single().ActualTurn == 4 &&
                    s.V(3).Skills?.All(x => x.ContentId != Wushuang) != false,
                    "The issued complete skill lasts through the recipient's next actual turn, then is removed at its genuine native cursor return without deleting another source.");
            }
        }
    }
    private const string Hero = "mou:lu-bu-mobile", Wushuang = "mou-mobile:wushuang", Liyu = "mou-mobile:liyu";
    private static ShanheStartingCard C(CardKind k) => new(k, Suit.Heart, 7);
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public readonly Dictionary<long, CardUseFrame> NativeUses = new();
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public bool Branch(string b) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == b);
        public void Use(CardKind kind, int? target = null)
        {
            var hand = V().Hand.ToDictionary(c => c.Id);
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && id > 0 &&
                (a.PlayedCardKind ?? hand.GetValueOrDefault(id)?.Kind) == kind && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null, $"No published native {kind} action for target {target}.");
            Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        public void Play() => Reach(IsPlay);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Need(p.PlayerSeat == 0, "Only actual human-owned published choices are submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected human prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real mobile Lu Bu flow not reached at " + line + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try
            {
                var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Command rejected.");
                foreach (var use in G.ResolutionStack.OfType<CardUseFrame>()) NativeUses[use.Id] = use;
            }
            catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); }
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0));
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "One actual accepted multi-obtain continuation survives cold restore without repaying its first entity.");
        }
        public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P,
            OwnHand = V().Hand.Select(c => new { c.Id, c.Kind, c.Suit, c.Rank }),
            LegalActions = (IsPlay() ? G.GetHumanLegalActions().ToArray() : Array.Empty<LegalAction>()).Select(a => new
            { a.Kind, a.CardId, a.PlayedCardKind, NativeEffectiveKind = a.PlayedCardKind ?? V().Hand.FirstOrDefault(c => c.Id == a.CardId)?.Kind, a.TargetSeats, a.ConversionSource }),
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouLuBu", StringComparison.Ordinal) || e.Payload is DamageAppliedEvent or RequiredResponseProgressEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start(bool grant)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(grant));
            var victimHand = grant ? new[] { C(CardKind.Dodge), C(CardKind.Crossbow), C(CardKind.Duel) } : new[] { C(CardKind.Peach), C(CardKind.Crossbow), C(CardKind.Duel) };
            // A real optional counterspell frontier preserves the native zero-material Use for inspection.
            var ownHand = grant ? new[] { C(CardKind.Alcohol), C(CardKind.Slash) } : new[] { C(CardKind.Alcohol), C(CardKind.Slash), C(CardKind.Nullification) };
            var setup = new ShanheBattleSetup(7, 1, "moulb-real", Hero, 5, 0, 0, 0, 0, [], ownHand, [],
                [new("fixture:moulb-enemy-a", Hp: 12) { StartingHand = [] }, new("fixture:moulb-enemy-b", Hp: 12) { StartingHand = victimHand }],
                [new("fixture:moulb-ally", Hp: 12) { StartingHand = [] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool grant) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-lu-bu-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Alcohol, CardKind.Dodge, CardKind.Peach, CardKind.Crossbow, CardKind.Duel })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:moulb-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new object[] {
                new { id = "fixture:moulb-quiet", revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = "fixture:moulb-no-duel", revision = 1, cardPolicies = new[] { new { id = "forbid-duel", kind = "prohibitTarget", cardKinds = new[] { "duel" } } } }
            } });
            var labels = new[] { "fixture:moulb-quiet", "fixture:moulb-no-duel" }.ToDictionary(id => id, id => new { name = id, description = "真实原生谋吕布小局" });
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in programs.Programs) b.AddSkill(new(id, id, labels[id].description) { Program = p, ProgramPresentation = programs.Presentations[id] });
            foreach (var id in new[] { "fixture:moulb-ally", "fixture:moulb-enemy-a", "fixture:moulb-enemy-b" })
                b.AddGeneral(new(id, id, "", "standard:none", id.EndsWith("ally", StringComparison.Ordinal) ? "shu" : "wei", 12,
                    grant && !id.EndsWith("enemy-b", StringComparison.Ordinal) ? ["fixture:moulb-quiet", "fixture:moulb-no-duel"] : ["fixture:moulb-quiet"]));
            b.AddDeck(new("fixture:moulb-deck", "真实固定补牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:moulb-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋吕布完整原生小局", 4, 4, new Dictionary<string, int>(), "fixture:moulb-deck",
                GeneralPoolIds: [Hero, "fixture:moulb-ally", "fixture:moulb-enemy-a", "fixture:moulb-enemy-b"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
