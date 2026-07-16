using PhaseDpsChecker.Combat;

var tests = new (string Name, Action Run)[]
{
    ("DPS と Crit/DH 率", DamageRatesAndDps),
    ("パーティ対象の回復のみ集計", HealingTargets),
    ("GCD 区間から Active% を算出", GcdActiveRate),
    ("DoT tick は使用回数を増やさない", PeriodicTickDoesNotCountAsUse),
    ("履歴削除後も Phase 番号を維持", PhaseNumberAfterTrim),
    ("全滅時の履歴保存と現在表示クリア", ArchiveCombatHistory),
	("被ダメージとステータスを履歴へ保存", ArchiveIncomingDamage),
	("撃破したアンカーへの最終攻撃を判定", DefeatingAnchorHit),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
}

Console.WriteLine($"{tests.Length} tests passed.");

static void DamageRatesAndDps()
{
    var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var party = new Dictionary<uint, string> { [1] = "Player One", [2] = "Player Two" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 10, "Strike", new EffectSample(900, 1000, 0, true, true)), partyIds);
    aggregator.RecordAction(Event(t0.AddSeconds(5), 1, 10, "Strike", new EffectSample(900, 500, 0, false, true)), partyIds);
    aggregator.EndCurrentPhase(t0.AddSeconds(10));
    var phase = aggregator.Phases.Single();
    var player = phase.Players[1];
    Equal(1500L, player.TotalDamage, "total damage");
    Near(150, player.Dps(phase.DurationSeconds(t0.AddSeconds(10))), 0.001, "DPS");
    Near(0.5, player.CriticalRate, 0.001, "critical rate");
    Near(1.0, player.DirectHitRate, 0.001, "direct-hit rate");
    Near(0.5, player.CriticalDirectHitRate, 0.001, "critical direct-hit rate");
    Equal(1000u, player.MaximumDamage, "maximum damage");
    Equal(2, player.Actions[10].UseCount, "use count");
}

static void HealingTargets()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Healer", [2] = "Tank" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    var effects = new[]
    {
        new EffectSample(2, 0, 3000, true, false),
        new EffectSample(900, 0, 9999, false, false),
        new EffectSample(2, 1234, 0, false, false),
    };
    aggregator.RecordAction(new CombatActionEvent(t0, 1, "Healer", 20, "Heal", ActionKind.Magic, true, true, 2.5, effects), partyIds);
    var player = aggregator.CurrentPhase!.Players[1];
    Equal(3000L, player.TotalHealing, "party healing");
    Equal(0L, player.TotalDamage, "friendly damage excluded");
}

static void GcdActiveRate()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 10, "GCD", new EffectSample(900, 100, 0, false, false), true, 2.5), partyIds);
    aggregator.RecordAction(Event(t0.AddSeconds(2.5), 1, 10, "GCD", new EffectSample(900, 100, 0, false, false), true, 2.5), partyIds);
    aggregator.EndCurrentPhase(t0.AddSeconds(10));
    var phase = aggregator.Phases.Single();
    Near(0.5, phase.Players[1].ActiveRate(phase.StartedAt, phase.EndedAt!.Value), 0.001, "active rate");
}

static void PhaseNumberAfterTrim()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var aggregator = new CombatAggregator();
    for (var index = 0; index < 3; index++)
    {
        aggregator.BeginPhase(t0.AddSeconds(index * 10), party, 900);
        aggregator.EndCurrentPhase(t0.AddSeconds(index * 10 + 5));
    }
    aggregator.TrimCurrentPhases(2);
    var fourth = aggregator.BeginPhase(t0.AddSeconds(30), party, 900);
    Equal(4, fourth.Number, "phase number");
}

static void PeriodicTickDoesNotCountAsUse()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 30, "Damage over time", new EffectSample(900, 100, 0, false, false)), partyIds);
    aggregator.RecordAction(new CombatActionEvent(t0.AddSeconds(3), 1, "Player", 30, "Damage over time", ActionKind.Magic, false, false, 0, [new EffectSample(900, 50, 0, false, false)]), partyIds);
    var action = aggregator.CurrentPhase!.Players[1].Actions[30];
    Equal(1, action.UseCount, "use count after tick");
    Equal(150L, action.TotalDamage, "damage including tick");
}

static void ArchiveCombatHistory()
{
    var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0.AddSeconds(1), 1, 10, "Strike", new EffectSample(900, 1200, 0, false, false)), partyIds);
    var history = aggregator.ArchiveCurrent(t0.AddSeconds(10), CombatHistoryEndReason.Wipe);
    Equal(0, aggregator.Phases.Count, "current phase count after archive");
    Equal(1, aggregator.Histories.Count, "history count");
    Equal(CombatHistoryEndReason.Wipe, history!.EndReason, "history reason");
    Equal(t0, history.StartedAt, "history start");
    Equal(t0.AddSeconds(10), history.EndedAt, "history end");
    Equal(1200L, history.Phases.Single().Players[1].TotalDamage, "archived total damage");
    var next = aggregator.BeginPhase(t0.AddSeconds(20), party, 901);
    Equal(1, next.Number, "phase number reset for next combat");
}

static void ArchiveIncomingDamage()
{
	var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
	var party = new Dictionary<uint, string> { [1] = "Tank", [2] = "Healer" };
	var partyIds = party.Keys.ToHashSet();
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordIncomingDamage(new IncomingDamageEvent(
		t0.AddSeconds(2),
		1,
		"Tank",
		900,
		"Enemy",
		7,
		"Auto Attack",
		4321,
		[new CombatStatusSnapshot(100, "Mitigation", 1, 8.5f)]), partyIds);
	aggregator.RecordIncomingDamage(new IncomingDamageEvent(
		t0.AddSeconds(3),
		999,
		"Not Party",
		900,
		"Enemy",
		8,
		"Ignored",
		9999,
		[]), partyIds);
	var history = aggregator.ArchiveCurrent(t0.AddSeconds(10), CombatHistoryEndReason.Wipe)!;
	var incoming = history.Phases.Single().IncomingDamageEvents.Single();
	Equal(4321u, incoming.Amount, "incoming amount");
	Equal("Auto Attack", incoming.ActionName, "incoming action");
	Equal("Mitigation", incoming.Statuses.Single().Name, "status snapshot");
}

static void DefeatingAnchorHit()
{
	var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
	var finalHitAt = t0.AddSeconds(8.25);
	EffectSample[] effects =
	[
		new EffectSample(900, 1234, 0, false, false),
		new EffectSample(901, 567, 0, false, false),
	];
	Equal(true, PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: true), "defeated anchor hit");
	Equal(false, PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: false), "living anchor");
	Equal(false, PhaseEndDetection.IsDefeatingHit(999, effects, anchorIsDefeated: true), "different target");
	Equal(false, PhaseEndDetection.IsDefeatingHit(0, effects, anchorIsDefeated: true), "missing anchor");

	var party = new Dictionary<uint, string> { [1] = "Player" };
	var partyIds = party.Keys.ToHashSet();
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordAction(new CombatActionEvent(finalHitAt, 1, "Player", 10, "Final Strike", ActionKind.WeaponSkill, true, true, 2.5, effects), partyIds);
	if (PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: true))
	{
		aggregator.EndCurrentPhase(finalHitAt);
	}
	Equal(1801L, aggregator.Phases.Single().Players[1].TotalDamage, "final hit damage included");
	Equal(finalHitAt, aggregator.Phases.Single().EndedAt, "phase ends at final hit timestamp");
}

static CombatActionEvent Event(DateTime timestamp, uint source, uint actionId, string actionName, EffectSample effect, bool gcd = false, double gcdSeconds = 2.5) =>
    new(timestamp, source, $"Player {source}", actionId, actionName, ActionKind.WeaponSkill, true, gcd, gcdSeconds, [effect]);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}

static void Near(double expected, double actual, double epsilon, string label)
{
    if (Math.Abs(expected - actual) > epsilon)
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}
