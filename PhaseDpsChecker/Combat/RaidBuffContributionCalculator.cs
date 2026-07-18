using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PhaseDpsChecker.Combat;

public sealed record RaidBuffContribution(
	double ExternalDamageReceived,
	IReadOnlyDictionary<uint, double> DamageGrantedByProvider,
	bool HasExternalCriticalBuff,
	bool HasExternalDirectHitBuff)
{
	public static readonly RaidBuffContribution Empty = new(0.0, new Dictionary<uint, double>(), false, false);
}

internal enum RaidBuffKind
{
	Damage,
	Critical,
	DirectHit,
}

internal sealed record RaidBuffComponent(uint ProviderEntityId, RaidBuffKind Kind, double Rate);

internal sealed class RaidBuffContributionCalculator
{
	private readonly IDataManager dataManager;
	private readonly IObjectTable objectTable;
	private readonly PartyRoster roster;

	public RaidBuffContributionCalculator(IDataManager dataManager, IObjectTable objectTable, PartyRoster roster)
	{
		this.dataManager = dataManager;
		this.objectTable = objectTable;
		this.roster = roster;
	}

	public RaidBuffContribution Calculate(
		uint sourceEntityId,
		uint attackerEntityId,
		uint targetEntityId,
		EffectSample effect,
		PlayerPhaseStatistics player,
		IReadOnlyDictionary<uint, string> partyMembers)
	{
		if (effect.Damage == 0)
		{
			return RaidBuffContribution.Empty;
		}

		List<RaidBuffComponent> buffs = Capture(sourceEntityId, attackerEntityId, targetEntityId, partyMembers);
		if (buffs.Count == 0)
		{
			return RaidBuffContribution.Empty;
		}

		double damageMultiplier = buffs.Where(buff => buff.Kind == RaidBuffKind.Damage).Aggregate(1.0, (value, buff) => value * (1.0 + buff.Rate));
		double criticalBonus = buffs.Where(buff => buff.Kind == RaidBuffKind.Critical).Sum(buff => buff.Rate);
		double directHitBonus = buffs.Where(buff => buff.Kind == RaidBuffKind.DirectHit).Sum(buff => buff.Rate);
		double criticalChance = Math.Clamp(player.EstimatedUnbuffedCriticalChance + criticalBonus, 0.0, 1.0);
		double directHitChance = Math.Clamp(player.EstimatedUnbuffedDirectHitChance + directHitBonus, 0.0, 1.0);
		double externalCriticalProbability = effect.Critical && criticalChance > 0.0 ? Math.Clamp(criticalBonus / criticalChance, 0.0, 1.0) : 0.0;
		double externalDirectHitProbability = effect.DirectHit && directHitChance > 0.0 ? Math.Clamp(directHitBonus / directHitChance, 0.0, 1.0) : 0.0;
		double criticalMultiplier = 1.35 + player.EstimatedUnbuffedCriticalChance;

		Dictionary<uint, double> granted = new();
		double received = 0.0;
		foreach ((bool externalCritical, double criticalWeight) in Branches(effect.Critical, externalCriticalProbability))
		{
			foreach ((bool externalDirectHit, double directHitWeight) in Branches(effect.DirectHit, externalDirectHitProbability))
			{
				double branchWeight = criticalWeight * directHitWeight;
				if (branchWeight <= 0.0)
				{
					continue;
				}
				double combinedMultiplier = damageMultiplier * (externalCritical ? criticalMultiplier : 1.0) * (externalDirectHit ? 1.25 : 1.0);
				if (combinedMultiplier <= 1.0)
				{
					continue;
				}

				double branchPool = effect.Damage * (1.0 - 1.0 / combinedMultiplier) * branchWeight;
				double logTotal = Math.Log(combinedMultiplier);
				foreach (RaidBuffComponent buff in buffs.Where(buff => buff.Kind == RaidBuffKind.Damage))
				{
					AddGranted(granted, buff.ProviderEntityId, branchPool * Math.Log(1.0 + buff.Rate) / logTotal);
				}
				if (externalCritical && criticalBonus > 0.0)
				{
					double pool = branchPool * Math.Log(criticalMultiplier) / logTotal;
					foreach (RaidBuffComponent buff in buffs.Where(buff => buff.Kind == RaidBuffKind.Critical))
					{
						AddGranted(granted, buff.ProviderEntityId, pool * buff.Rate / criticalBonus);
					}
				}
				if (externalDirectHit && directHitBonus > 0.0)
				{
					double pool = branchPool * Math.Log(1.25) / logTotal;
					foreach (RaidBuffComponent buff in buffs.Where(buff => buff.Kind == RaidBuffKind.DirectHit))
					{
						AddGranted(granted, buff.ProviderEntityId, pool * buff.Rate / directHitBonus);
					}
				}
				received += branchPool;
			}
		}

		return new RaidBuffContribution(received, granted, criticalBonus > 0.0, directHitBonus > 0.0);
	}

	private List<RaidBuffComponent> Capture(uint sourceEntityId, uint attackerEntityId, uint targetEntityId, IReadOnlyDictionary<uint, string> partyMembers)
	{
		List<RaidBuffComponent> result = new();
		HashSet<(uint StatusId, uint ProviderId, RaidBuffKind Kind)> seen = new();
		AddActorStatuses(sourceEntityId, attackerEntityId, partyMembers, result, seen);
		if (sourceEntityId != attackerEntityId)
		{
			AddActorStatuses(attackerEntityId, attackerEntityId, partyMembers, result, seen);
		}
		AddActorStatuses(targetEntityId, attackerEntityId, partyMembers, result, seen);
		return result;
	}

	private void AddActorStatuses(
		uint entityId,
		uint attackerEntityId,
		IReadOnlyDictionary<uint, string> partyMembers,
		List<RaidBuffComponent> output,
		HashSet<(uint StatusId, uint ProviderId, RaidBuffKind Kind)> seen)
	{
		if (objectTable.SearchByEntityId(entityId) is not IBattleChara actor)
		{
			return;
		}
		foreach (var status in actor.StatusList)
		{
			uint providerId = roster.ResolvePartyOwner(status.SourceId, partyMembers);
			if (providerId == 0 || providerId == attackerEntityId || !TryResolve(status.StatusId, out List<(RaidBuffKind Kind, double Rate)>? components))
			{
				continue;
			}
			foreach ((RaidBuffKind kind, double rate) in components)
			{
				if (seen.Add((status.StatusId, providerId, kind)))
				{
					output.Add(new RaidBuffComponent(providerId, kind, rate));
				}
			}
		}
	}

	private bool TryResolve(uint statusId, out List<(RaidBuffKind Kind, double Rate)> components)
	{
		List<(RaidBuffKind Kind, double Rate)> resolved = new();
		components = resolved;
		if (!dataManager.GetExcelSheet<Status>().TryGetRow(statusId, out Status status))
		{
			return false;
		}
		string name = status.Name.ToString();
		if (Matches(name, "Divination", "ディヴィネーション")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Technical Finish", "テクニカルフィニッシュ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Standard Finish", "スタンダードフィニッシュ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Brotherhood", "桃園結義")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Embolden", "エンボルデン")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Arcane Circle", "アルケインサークル")) Add(RaidBuffKind.Damage, 0.03);
		else if (Matches(name, "Searing Light", "シアリングライト")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Radiant Finale", "光神のフィナーレ")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Starry Muse", "星空のモチーフ", "星空のミューズ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Kunai's Bane", "Dokumori", "くないの毒", "毒盛の術")) Add(RaidBuffKind.Damage, 0.10);
		else if (Matches(name, "The Balance", "The Spear", "アーゼマの均衡", "ハルオーネの槍")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Battle Litany", "バトルリタニー")) Add(RaidBuffKind.Critical, 0.10);
		else if (Matches(name, "Chain Stratagem", "連環計")) Add(RaidBuffKind.Critical, 0.10);
		else if (Matches(name, "Battle Voice", "バトルボイス")) Add(RaidBuffKind.DirectHit, 0.20);
		else if (Matches(name, "Devilment", "攻めのタンゴ"))
		{
			Add(RaidBuffKind.Critical, 0.20);
			Add(RaidBuffKind.DirectHit, 0.20);
		}
		return resolved.Count != 0;

		void Add(RaidBuffKind kind, double rate) => resolved.Add((kind, rate));
	}

	private static IEnumerable<(bool External, double Weight)> Branches(bool occurred, double externalProbability)
	{
		if (!occurred || externalProbability <= 0.0)
		{
			yield return (false, 1.0);
			yield break;
		}
		if (externalProbability < 1.0)
		{
			yield return (false, 1.0 - externalProbability);
		}
		yield return (true, externalProbability);
	}

	private static bool Matches(string actual, params string[] names) => names.Any(name => string.Equals(actual, name, StringComparison.OrdinalIgnoreCase));

	private static void AddGranted(Dictionary<uint, double> values, uint providerId, double amount)
	{
		values[providerId] = values.TryGetValue(providerId, out double current) ? current + amount : amount;
	}
}
