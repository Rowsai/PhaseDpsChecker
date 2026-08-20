using System;
using System.Collections.Generic;
using System.Linq;

namespace PhaseDpsChecker.Combat;

public sealed record IinactCombatantSnapshot(
	string Name,
	long Damage,
	long Healing,
	long DamageTaken,
	int Hits,
	int CriticalHits,
	int? DirectHits,
	int? CriticalDirectHits,
	double Dps = 0,
	double Hps = 0);

public sealed record IinactCombatSnapshot(
	long Sequence,
	DateTime ReceivedAt,
	string EncounterId,
	bool IsActive,
	IReadOnlyDictionary<string, IinactCombatantSnapshot> Combatants,
	double DurationSeconds = 0)
{
	public static IinactCombatSnapshot Empty(DateTime receivedAt) =>
		new(0, receivedAt, string.Empty, false, new Dictionary<string, IinactCombatantSnapshot>(StringComparer.OrdinalIgnoreCase));
}

internal sealed class IinactPhaseSynchronizer
{
	private readonly Dictionary<PhaseRecord, IinactCombatSnapshot> baselines = new();

	public void Begin(PhaseRecord phase, IinactCombatSnapshot? latest)
	{
		IinactCombatSnapshot baseline = latest ?? IinactCombatSnapshot.Empty(phase.StartedAt);
		baselines[phase] = baseline;
		phase.MarkIinactSynchronized(baseline.Sequence, hasData: false);
	}

	public bool Apply(PhaseRecord phase, IinactCombatSnapshot current, uint localPlayerEntityId = 0, bool allowInactiveFinal = false)
	{
		if ((!current.IsActive && !allowInactiveFinal)
			|| !baselines.TryGetValue(phase, out IinactCombatSnapshot? baseline)
			|| current.Combatants.Count == 0)
		{
			return false;
		}

		if (localPlayerEntityId == 0 && phase.Players.Count == 1)
		{
			localPlayerEntityId = phase.Players.Keys.Single();
		}
		foreach (PlayerPhaseStatistics player in phase.Players.Values)
		{
			bool isLocalPlayer = player.EntityId == localPlayerEntityId;
			IinactCombatantSnapshot currentValue = SumForPlayer(current.Combatants.Values, player.PlayerName, isLocalPlayer);
			IinactCombatantSnapshot baselineValue = SumForPlayer(baseline.Combatants.Values, player.PlayerName, isLocalPlayer);
			long damage = PositiveDelta(currentValue.Damage, baselineValue.Damage);
			long healing = PositiveDelta(currentValue.Healing, baselineValue.Healing);
			long damageTaken = PositiveDelta(currentValue.DamageTaken, baselineValue.DamageTaken);
			int hits = PositiveDelta(currentValue.Hits, baselineValue.Hits);
			int criticalHits = PositiveDelta(currentValue.CriticalHits, baselineValue.CriticalHits);
			int? directHits = DeltaNullable(currentValue.DirectHits, baselineValue.DirectHits);
			int? criticalDirectHits = DeltaNullable(currentValue.CriticalDirectHits, baselineValue.CriticalDirectHits);

			player.ApplyIinactTotals(damage, healing, hits, criticalHits, directHits, criticalDirectHits);
			phase.SetIinactIncomingDamage(player.EntityId, damageTaken);
		}
		phase.MarkIinactSynchronized(current.Sequence);
		return true;
	}

	public void Forget(PhaseRecord phase) => baselines.Remove(phase);

	public void Clear() => baselines.Clear();

	private static IinactCombatantSnapshot SumForPlayer(IEnumerable<IinactCombatantSnapshot> combatants, string playerName, bool isLocalPlayer)
	{
		string normalizedPlayer = NormalizeName(playerName);
		List<IinactCombatantSnapshot> matches = combatants
			.Where(combatant => BelongsToPlayer(combatant.Name, normalizedPlayer, isLocalPlayer))
			.ToList();
		if (matches.Count == 0)
		{
			return Zero(playerName);
		}

		return new IinactCombatantSnapshot(
			playerName,
			matches.Sum(value => value.Damage),
			matches.Sum(value => value.Healing),
			matches.Sum(value => value.DamageTaken),
			matches.Sum(value => value.Hits),
			matches.Sum(value => value.CriticalHits),
			SumNullable(matches.Select(value => value.DirectHits)),
			SumNullable(matches.Select(value => value.CriticalDirectHits)),
			matches.Sum(value => value.Dps),
			matches.Sum(value => value.Hps));
	}

	private static bool BelongsToPlayer(string combatantName, string normalizedPlayer, bool isLocalPlayer)
	{
		string normalizedCombatant = NormalizeName(combatantName);
		return string.Equals(normalizedCombatant, normalizedPlayer, StringComparison.OrdinalIgnoreCase)
			|| normalizedCombatant.EndsWith($"({normalizedPlayer})", StringComparison.OrdinalIgnoreCase)
			|| isLocalPlayer && (string.Equals(normalizedCombatant, "YOU", StringComparison.OrdinalIgnoreCase)
				|| normalizedCombatant.EndsWith("(YOU)", StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeName(string value) =>
		string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

	private static IinactCombatantSnapshot Zero(string name) => new(name, 0, 0, 0, 0, 0, 0, 0);

	private static long PositiveDelta(long current, long baseline) => Math.Max(0, current - baseline);

	private static int PositiveDelta(int current, int baseline) => Math.Max(0, current - baseline);

	private static int? DeltaNullable(int? current, int? baseline) =>
		current.HasValue ? Math.Max(0, current.Value - (baseline ?? 0)) : null;

	private static int? SumNullable(IEnumerable<int?> values)
	{
		int? sum = null;
		foreach (int? value in values)
		{
			if (value.HasValue)
			{
				sum = (sum ?? 0) + value.Value;
			}
		}
		return sum;
	}
}
