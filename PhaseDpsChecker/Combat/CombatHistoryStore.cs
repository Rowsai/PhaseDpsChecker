using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhaseDpsChecker.Combat;

internal sealed class CombatHistoryStore
{
	private const string FileName = "PhaseDpsChecker.history.json";
	private readonly Func<string> customDirectoryProvider;
	private readonly string defaultDirectory;
	private readonly Action<Exception, string> logError;
	private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

	public string? LastError { get; private set; }

	public string DirectoryPath
	{
		get
		{
			string customDirectory = customDirectoryProvider();
			if (string.IsNullOrWhiteSpace(customDirectory))
			{
				return defaultDirectory;
			}
			try
			{
				return Path.GetFullPath(customDirectory);
			}
			catch
			{
				return defaultDirectory;
			}
		}
	}

	public string FilePath => Path.Combine(DirectoryPath, FileName);

	public CombatHistoryStore(Func<string> customDirectoryProvider, string defaultDirectory, Action<Exception, string> logError)
	{
		this.customDirectoryProvider = customDirectoryProvider;
		this.defaultDirectory = defaultDirectory;
		this.logError = logError;
	}

	public IReadOnlyList<CombatHistoryRecord> Load()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				LastError = null;
				return Array.Empty<CombatHistoryRecord>();
			}

			HistoryFileDto? file = JsonSerializer.Deserialize<HistoryFileDto>(File.ReadAllText(FilePath), jsonOptions);
			if (file == null || file.SchemaVersion != 1)
			{
				throw new InvalidDataException("未対応の履歴ファイル形式です。");
			}
			LastError = null;
			return file.Histories.Select(RestoreHistory).OrderBy(history => history.Number).ToArray();
		}
		catch (Exception ex)
		{
			LastError = $"履歴の読み込みに失敗しました: {ex.Message}";
			logError(ex, $"戦闘履歴ファイルを読み込めませんでした: {FilePath}");
			return Array.Empty<CombatHistoryRecord>();
		}
	}

	public bool Save(IReadOnlyList<CombatHistoryRecord> histories)
	{
		try
		{
			Directory.CreateDirectory(DirectoryPath);
			HistoryFileDto file = new()
			{
				Histories = histories.OrderBy(history => history.Number).Select(HistoryDto.From).ToList()
			};
			string temporaryPath = FilePath + ".tmp";
			File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, jsonOptions));
			File.Move(temporaryPath, FilePath, true);
			LastError = null;
			return true;
		}
		catch (Exception ex)
		{
			LastError = $"履歴の保存に失敗しました: {ex.Message}";
			logError(ex, $"戦闘履歴ファイルを保存できませんでした: {FilePath}");
			return false;
		}
	}

	private static CombatHistoryRecord RestoreHistory(HistoryDto history)
	{
		List<PhaseRecord> phases = new();
		foreach (PhaseDto savedPhase in history.Phases.OrderBy(phase => phase.Number))
		{
			PhaseRecord phase = new(savedPhase.Number, savedPhase.StartedAt, savedPhase.AnchorTargetId)
			{
				EndedAt = savedPhase.EndedAt
			};
			foreach (PlayerDto savedPlayer in savedPhase.Players)
			{
				PlayerPhaseStatistics player = phase.EnsurePlayer(savedPlayer.EntityId, savedPlayer.PlayerName);
				player.RestoreState(
					savedPlayer.TotalDamage,
					savedPlayer.TotalHealing,
					savedPlayer.DamageHitCount,
					savedPlayer.CriticalDamageHits,
					savedPlayer.DirectDamageHits,
					savedPlayer.CriticalDirectDamageHits,
					savedPlayer.MaximumDamage,
					savedPlayer.MaximumDamageAction,
					savedPlayer.GcdIntervals.Select(interval => (interval.Start, interval.End)),
					savedPlayer.DamageGcdIntervals.Select(interval => (interval.Start, interval.End)),
					savedPlayer.HealingGcdIntervals.Select(interval => (interval.Start, interval.End)));
				foreach (ActionDto savedAction in savedPlayer.Actions)
				{
					ActionStatistics action = new(savedAction.ActionId, savedAction.ActionName, savedAction.Kind);
					action.RestoreState(
						savedAction.UseCount,
						savedAction.TotalDamage,
						savedAction.TotalHealing,
						savedAction.EffectCount,
						savedAction.CriticalEffects,
						savedAction.DirectHitEffects,
						savedAction.CriticalDirectHitEffects,
						savedAction.MaximumAmount,
						savedAction.MinimumAmount);
					player.Actions[action.ActionId] = action;
				}
			}
			foreach (IncomingDamageDto incoming in savedPhase.IncomingDamageEvents)
			{
				phase.AddIncomingDamage(new IncomingDamageEvent(
					incoming.Timestamp,
					incoming.PlayerEntityId,
					incoming.PlayerName,
					incoming.SourceEntityId,
					incoming.EnemyName,
					incoming.ActionId,
					incoming.ActionName,
					incoming.Amount,
					incoming.Statuses.Select(status => new CombatStatusSnapshot(status.StatusId, status.Name, status.StackCount, status.RemainingSeconds)).ToArray()));
			}
			phases.Add(phase);
		}
		return new CombatHistoryRecord(history.Number, history.ArchivedAt, history.EndReason, phases);
	}

	private sealed class HistoryFileDto
	{
		public int SchemaVersion { get; set; } = 1;
		public List<HistoryDto> Histories { get; set; } = new();
	}

	private sealed class HistoryDto
	{
		public int Number { get; set; }
		public DateTime ArchivedAt { get; set; }
		public CombatHistoryEndReason EndReason { get; set; }
		public List<PhaseDto> Phases { get; set; } = new();

		public static HistoryDto From(CombatHistoryRecord history) => new()
		{
			Number = history.Number,
			ArchivedAt = history.ArchivedAt,
			EndReason = history.EndReason,
			Phases = history.Phases.Select(PhaseDto.From).ToList()
		};
	}

	private sealed class PhaseDto
	{
		public int Number { get; set; }
		public DateTime StartedAt { get; set; }
		public DateTime? EndedAt { get; set; }
		public uint AnchorTargetId { get; set; }
		public List<PlayerDto> Players { get; set; } = new();
		public List<IncomingDamageDto> IncomingDamageEvents { get; set; } = new();

		public static PhaseDto From(PhaseRecord phase) => new()
		{
			Number = phase.Number,
			StartedAt = phase.StartedAt,
			EndedAt = phase.EndedAt,
			AnchorTargetId = phase.AnchorTargetId,
			Players = phase.Players.Values.Select(PlayerDto.From).ToList(),
			IncomingDamageEvents = phase.IncomingDamageEvents.Select(IncomingDamageDto.From).ToList()
		};
	}

	private sealed class PlayerDto
	{
		public uint EntityId { get; set; }
		public string PlayerName { get; set; } = string.Empty;
		public long TotalDamage { get; set; }
		public long TotalHealing { get; set; }
		public int DamageHitCount { get; set; }
		public int CriticalDamageHits { get; set; }
		public int DirectDamageHits { get; set; }
		public int CriticalDirectDamageHits { get; set; }
		public uint MaximumDamage { get; set; }
		public string MaximumDamageAction { get; set; } = "-";
		public List<IntervalDto> GcdIntervals { get; set; } = new();
		public List<IntervalDto> DamageGcdIntervals { get; set; } = new();
		public List<IntervalDto> HealingGcdIntervals { get; set; } = new();
		public List<ActionDto> Actions { get; set; } = new();

		public static PlayerDto From(PlayerPhaseStatistics player) => new()
		{
			EntityId = player.EntityId,
			PlayerName = player.PlayerName,
			TotalDamage = player.TotalDamage,
			TotalHealing = player.TotalHealing,
			DamageHitCount = player.DamageHitCount,
			CriticalDamageHits = player.CriticalDamageHits,
			DirectDamageHits = player.DirectDamageHits,
			CriticalDirectDamageHits = player.CriticalDirectDamageHits,
			MaximumDamage = player.MaximumDamage,
			MaximumDamageAction = player.MaximumDamageAction,
			GcdIntervals = player.GcdIntervals.Select(IntervalDto.From).ToList(),
			DamageGcdIntervals = player.DamageGcdIntervals.Select(IntervalDto.From).ToList(),
			HealingGcdIntervals = player.HealingGcdIntervals.Select(IntervalDto.From).ToList(),
			Actions = player.Actions.Values.Select(ActionDto.From).ToList()
		};
	}

	private sealed class ActionDto
	{
		public uint ActionId { get; set; }
		public string ActionName { get; set; } = string.Empty;
		public ActionKind Kind { get; set; }
		public int UseCount { get; set; }
		public long TotalDamage { get; set; }
		public long TotalHealing { get; set; }
		public int EffectCount { get; set; }
		public int CriticalEffects { get; set; }
		public int DirectHitEffects { get; set; }
		public int CriticalDirectHitEffects { get; set; }
		public uint MaximumAmount { get; set; }
		public uint MinimumAmount { get; set; }

		public static ActionDto From(ActionStatistics action) => new()
		{
			ActionId = action.ActionId,
			ActionName = action.ActionName,
			Kind = action.Kind,
			UseCount = action.UseCount,
			TotalDamage = action.TotalDamage,
			TotalHealing = action.TotalHealing,
			EffectCount = action.EffectCount,
			CriticalEffects = action.CriticalEffects,
			DirectHitEffects = action.DirectHitEffects,
			CriticalDirectHitEffects = action.CriticalDirectHitEffects,
			MaximumAmount = action.MaximumAmount,
			MinimumAmount = action.MinimumAmount
		};
	}

	private sealed class IntervalDto
	{
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public static IntervalDto From((DateTime Start, DateTime End) interval) => new() { Start = interval.Start, End = interval.End };
	}

	private sealed class IncomingDamageDto
	{
		public DateTime Timestamp { get; set; }
		public uint PlayerEntityId { get; set; }
		public string PlayerName { get; set; } = string.Empty;
		public uint SourceEntityId { get; set; }
		public string EnemyName { get; set; } = string.Empty;
		public uint ActionId { get; set; }
		public string ActionName { get; set; } = string.Empty;
		public uint Amount { get; set; }
		public List<StatusDto> Statuses { get; set; } = new();

		public static IncomingDamageDto From(IncomingDamageEvent incoming) => new()
		{
			Timestamp = incoming.Timestamp,
			PlayerEntityId = incoming.PlayerEntityId,
			PlayerName = incoming.PlayerName,
			SourceEntityId = incoming.SourceEntityId,
			EnemyName = incoming.EnemyName,
			ActionId = incoming.ActionId,
			ActionName = incoming.ActionName,
			Amount = incoming.Amount,
			Statuses = incoming.Statuses.Select(StatusDto.From).ToList()
		};
	}

	private sealed class StatusDto
	{
		public uint StatusId { get; set; }
		public string Name { get; set; } = string.Empty;
		public ushort StackCount { get; set; }
		public float RemainingSeconds { get; set; }

		public static StatusDto From(CombatStatusSnapshot status) => new()
		{
			StatusId = status.StatusId,
			Name = status.Name,
			StackCount = status.Stacks,
			RemainingSeconds = status.RemainingSeconds
		};
	}
}
