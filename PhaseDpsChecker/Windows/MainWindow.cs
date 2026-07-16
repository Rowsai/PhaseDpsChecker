using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using PhaseDpsChecker.Combat;

namespace PhaseDpsChecker.Windows;

public sealed class MainWindow : Window, IDisposable
{
	internal const int BlueThemeColorCount = 24;

	private static readonly ImGuiTableFlags TableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg;

	private readonly Configuration configuration;
	private readonly CombatTracker tracker;

	private int selectedCurrentPhaseNumber;
	private int selectedHistoryNumber;
	private uint selectedHistoryEntityId;
	private int selectedHistoryPhaseNumber;
	private int selectedIncomingHistoryNumber;
	private uint selectedIncomingEntityId;
	private SummarySortColumn summarySortColumn = SummarySortColumn.Phase;
	private bool summarySortDescending;
	private IncomingSortColumn incomingSortColumn = IncomingSortColumn.Phase;
	private bool incomingSortDescending;
	private readonly Dictionary<string, (ActionSortColumn Column, bool Descending)> actionSortStates = new Dictionary<string, (ActionSortColumn, bool)>();

	private enum SummarySortColumn
	{
		Phase,
		Player,
		Start,
		End,
		Dps,
		Critical,
		DirectHit,
		CriticalDirectHit,
		MaximumDamage,
		Active
	}

	private enum IncomingSortColumn
	{
		Phase,
		Player,
		Start,
		End,
		Amount,
		Action,
		Statuses
	}

	private enum ActionSortColumn
	{
		Number,
		Action,
		Count,
		TotalDamage,
		TotalHealing,
		Critical,
		DirectHit,
		CriticalDirectHit,
		Maximum,
		Minimum
	}

	private sealed record SummaryRowData(PhaseRecord Phase, PlayerPhaseStatistics Player, double Dps, double ActiveRate);

	private sealed record IncomingRowData(PhaseRecord Phase, IncomingDamageEvent DamageEvent);

	public MainWindow(Configuration configuration, CombatTracker tracker)
		: base($"Phase DPS Checker ver {GetDisplayVersion()}###PhaseDpsCheckerMain")
	{
		this.configuration = configuration;
		this.tracker = tracker;
		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(900f, 540f),
			MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
		};
		Size = new Vector2(1380f, 800f);
		SizeCondition = ImGuiCond.FirstUseEver;
	}

	private static string GetDisplayVersion() =>
		typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

	public void Dispose()
	{
	}

	public override void PreDraw()
	{
		PushBlueTheme();
	}

	public override void PostDraw()
	{
		ImGui.PopStyleColor(BlueThemeColorCount);
	}

	public override void Draw()
	{
		DrawWarnings();
		if (!ImGui.BeginTabBar("##PhaseDpsCheckerTabs"))
		{
			return;
		}

		if (ImGui.BeginTabItem("設定"))
		{
			DrawSettings();
			ImGui.EndTabItem();
		}
		if (ImGui.BeginTabItem("表示"))
		{
			DrawDisplay();
			ImGui.EndTabItem();
		}
		if (ImGui.BeginTabItem("履歴表示"))
		{
			DrawHistory();
			ImGui.EndTabItem();
		}
		if (ImGui.BeginTabItem("被ダメージ"))
		{
			DrawIncomingDamage();
			ImGui.EndTabItem();
		}
		ImGui.EndTabBar();
	}

	private void DrawWarnings()
	{
		if (!tracker.CaptureAvailable)
		{
			ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "戦闘イベントの取得を開始できませんでした。");
			if (!string.IsNullOrWhiteSpace(tracker.CaptureError))
			{
				ImGui.TextWrapped(tracker.CaptureError);
			}
			ImGui.Separator();
		}
		if (tracker.IsDisabledForPvP)
		{
			ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), "PvP中は戦闘集計を無効化しています。");
			ImGui.Separator();
		}
	}

	private void DrawSettings()
	{
		DrawSectionTitle("表示設定", "ライブ表示の対象と計測条件を設定します。");
		Dictionary<uint, string> currentMembers = tracker.Roster.GetCurrentMembers();

		DrawSectionTitle("専用フェーズ判定", "絶コンテンツ用のフェーズ条件を選択します。未選択時は通常計測です。");
		ImGui.TextUnformatted("コンテンツ情報");
		ImGui.SetNextItemWidth(380f);
		if (ImGui.BeginListBox("##PhaseDetectionPreset", new Vector2(380f, 82f)))
		{
			bool normalSelected = configuration.PhaseDetectionPreset == PhaseDetectionPreset.Normal;
			if (ImGui.Selectable(PhaseDetectionPreset.Normal.DisplayName(), normalSelected))
			{
				tracker.SetPhaseDetectionPreset(PhaseDetectionPreset.Normal);
			}
			if (normalSelected)
			{
				ImGui.SetItemDefaultFocus();
			}

			foreach (PhaseDetectionPreset preset in PhaseDetectionPresetCatalog.All)
			{
				bool selected = configuration.PhaseDetectionPreset == preset;
				if (ImGui.Selectable(preset.DisplayName(), selected))
				{
					tracker.SetPhaseDetectionPreset(preset);
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndListBox();
		}
		ImGui.TextColored(new Vector4(0.35f, 0.75f, 1f, 1f), tracker.PhaseDetectionStatus);
		ImGui.Spacing();

		string selectedLabel = configuration.SelectedEntityId == 0
			? "パーティメンバー全体"
			: currentMembers.TryGetValue(configuration.SelectedEntityId, out string? selectedName)
				? selectedName
				: $"不在のメンバー ({configuration.SelectedEntityId:X8})";

		ImGui.TextUnformatted("表示対象");
		ImGui.SetNextItemWidth(380f);
		if (ImGui.BeginCombo("##DisplayTarget", selectedLabel))
		{
			if (ImGui.Selectable("パーティメンバー全体", configuration.SelectedEntityId == 0))
			{
				configuration.SelectedEntityId = 0;
				configuration.Save();
			}
			foreach (KeyValuePair<uint, string> member in currentMembers.OrderBy(member => member.Value, StringComparer.CurrentCulture))
			{
				bool selected = configuration.SelectedEntityId == member.Key;
				if (ImGui.Selectable(member.Value, selected))
				{
					configuration.SelectedEntityId = member.Key;
					configuration.Save();
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}

		ImGui.Spacing();
		bool showPartyOverlay = configuration.ShowPartyOverlay;
		if (ImGui.Checkbox("パーティメンバー全体をオーバーレイ表示", ref showPartyOverlay))
		{
			configuration.ShowPartyOverlay = showPartyOverlay;
			configuration.Save();
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("表示対象の選択に関係なく、ライブのパーティ全体集計を画面上へ表示します。");
		}

		ImGui.Spacing();
		DrawSectionTitle("計測", "フェーズ判定とメモリ内履歴の保持数を調整します。");
		int maxPhaseHistory = configuration.MaxPhaseHistory;
		ImGui.SetNextItemWidth(280f);
		if (ImGui.SliderInt("1戦闘で保持するフェーズ数", ref maxPhaseHistory, 5, 100))
		{
			configuration.MaxPhaseHistory = maxPhaseHistory;
			configuration.Save();
		}

		int maxEncounterHistory = configuration.MaxEncounterHistory;
		ImGui.SetNextItemWidth(280f);
		if (ImGui.SliderInt("保持する戦闘履歴数", ref maxEncounterHistory, 1, 50))
		{
			configuration.MaxEncounterHistory = maxEncounterHistory;
			configuration.Save();
		}

		ImGui.Spacing();
		if (ImGui.Button("現在のフェーズを終了"))
		{
			tracker.ForceEndCurrentPhase();
		}
		ImGui.SameLine();
		if (ImGui.Button("現在のデータを履歴へ移動"))
		{
			tracker.ArchiveCurrentForHistory();
		}
		ImGui.SameLine();
		if (ImGui.Button("保存済み履歴を削除"))
		{
			tracker.ClearArchivedHistory();
			selectedHistoryNumber = 0;
			selectedHistoryEntityId = 0;
			selectedHistoryPhaseNumber = 0;
			selectedIncomingHistoryNumber = 0;
			selectedIncomingEntityId = 0;
		}

		ImGui.Spacing();
		PhaseRecord? currentPhase = tracker.Aggregator.CurrentPhase;
		ImGui.TextDisabled(currentPhase == null
			? $"状態: フェーズ待機中 / 保存済み履歴 {tracker.Aggregator.Histories.Count}件"
			: $"状態: Phase {currentPhase.Number} を計測中 / 保存済み履歴 {tracker.Aggregator.Histories.Count}件");
	}

	private void DrawDisplay()
	{
		IReadOnlyList<PhaseRecord> phases = tracker.Aggregator.Phases;
		if (configuration.SelectedEntityId == 0)
		{
			DrawPartyOverview(phases, "パーティメンバー全体", "LivePartyOverview", "敵へのダメージを検出すると、ここにフェーズ結果が表示されます。");
			return;
		}

		string playerName = tracker.Roster.GetCurrentMembers().TryGetValue(configuration.SelectedEntityId, out string? currentName)
			? currentName
			: FindPlayerName(phases, configuration.SelectedEntityId);
		DrawPlayerDetail(phases, configuration.SelectedEntityId, playerName, ref selectedCurrentPhaseNumber, "LivePlayer");
	}

	private void DrawHistory()
	{
		IReadOnlyList<CombatHistoryRecord> histories = tracker.Aggregator.Histories;
		if (histories.Count == 0)
		{
			ImGui.TextDisabled("全滅、コンテンツクリア、または手動保存後の戦闘履歴がここに表示されます。");
			return;
		}

		CombatHistoryRecord history = DrawHistorySelector(histories, ref selectedHistoryNumber, "HistoryList", () =>
		{
			selectedHistoryEntityId = 0;
			selectedHistoryPhaseNumber = 0;
		});

		Dictionary<uint, string> historyMembers = GetHistoryMembers(history);
		if (selectedHistoryEntityId != 0 && !historyMembers.ContainsKey(selectedHistoryEntityId))
		{
			selectedHistoryEntityId = 0;
			selectedHistoryPhaseNumber = 0;
		}

		string selectedLabel = selectedHistoryEntityId == 0
			? "パーティメンバー全体"
			: historyMembers.TryGetValue(selectedHistoryEntityId, out string? historyName) ? historyName : "不明なメンバー";
		ImGui.TextUnformatted("履歴の表示対象");
		ImGui.SetNextItemWidth(380f);
		if (ImGui.BeginCombo("##HistoryDisplayTarget", selectedLabel))
		{
			if (ImGui.Selectable("パーティメンバー全体", selectedHistoryEntityId == 0))
			{
				selectedHistoryEntityId = 0;
				selectedHistoryPhaseNumber = 0;
			}
			foreach (KeyValuePair<uint, string> member in historyMembers.OrderBy(member => member.Value, StringComparer.CurrentCulture))
			{
				bool selected = member.Key == selectedHistoryEntityId;
				if (ImGui.Selectable(member.Value, selected))
				{
					selectedHistoryEntityId = member.Key;
					selectedHistoryPhaseNumber = 0;
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		if (selectedHistoryEntityId == 0)
		{
			DrawPartyOverview(history.Phases, $"履歴 #{history.Number} / パーティメンバー全体", "HistoryPartyOverview", "この履歴には表示可能なフェーズがありません。");
		}
		else
		{
			DrawPlayerDetail(history.Phases, selectedHistoryEntityId, selectedLabel, ref selectedHistoryPhaseNumber, "HistoryPlayer");
		}
	}

	private void DrawIncomingDamage()
	{
		IReadOnlyList<CombatHistoryRecord> histories = tracker.Aggregator.Histories;
		if (histories.Count == 0)
		{
			ImGui.TextDisabled("保存された戦闘履歴の被ダメージがここに表示されます。");
			return;
		}

		CombatHistoryRecord history = DrawHistorySelector(histories, ref selectedIncomingHistoryNumber, "IncomingHistoryList", () => selectedIncomingEntityId = 0);
		Dictionary<uint, string> members = GetHistoryMembers(history);
		List<KeyValuePair<uint, string>> orderedMembers = members.OrderBy(member => member.Value, StringComparer.CurrentCulture).ToList();
		if (selectedIncomingEntityId == 0 || !members.ContainsKey(selectedIncomingEntityId))
		{
			selectedIncomingEntityId = orderedMembers.FirstOrDefault().Key;
		}

		ImGui.TextUnformatted("メンバー個別表示");
		ImGui.SetNextItemWidth(380f);
		string selectedPlayerName = members.TryGetValue(selectedIncomingEntityId, out string? memberName) ? memberName : "メンバーなし";
		if (ImGui.BeginCombo("##IncomingMember", selectedPlayerName))
		{
			foreach (KeyValuePair<uint, string> member in orderedMembers)
			{
				bool selected = member.Key == selectedIncomingEntityId;
				if (ImGui.Selectable(member.Value, selected))
				{
					selectedIncomingEntityId = member.Key;
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}

		List<IncomingRowData> rows = history.Phases
			.OrderBy(phase => phase.Number)
			.SelectMany(phase => phase.IncomingDamageEvents
				.Where(damageEvent => damageEvent.PlayerEntityId == selectedIncomingEntityId)
				.OrderBy(damageEvent => damageEvent.Timestamp)
				.Select(damageEvent => new IncomingRowData(phase, damageEvent)))
			.ToList();
		rows.Sort(CompareIncomingRows);

		ImGui.Spacing();
		DrawIncomingCards(rows);
		ImGui.Spacing();
		if (rows.Count == 0)
		{
			ImGui.TextDisabled("このメンバーの被ダメージ情報はありません。");
			return;
		}

		DateTime encounterStart = history.StartedAt;
		uint maximumAmount = rows.Max(row => row.DamageEvent.Amount);
		if (ImGui.BeginTable("##IncomingDamageTable", 7, TableFlags | ImGuiTableFlags.ScrollY, new Vector2(0f, -1f)))
		{
			ImGui.TableSetupColumn("Phase");
			ImGui.TableSetupColumn("プレイヤー名");
			ImGui.TableSetupColumn("開始時間");
			ImGui.TableSetupColumn("終了時間");
			ImGui.TableSetupColumn("被ダメージ量");
			ImGui.TableSetupColumn("エネミー / 攻撃名");
			ImGui.TableSetupColumn("被ダメージ時のバフ / デバフ");
			DrawSortableIncomingHeader();

			foreach (IncomingRowData row in rows)
			{
				DrawIncomingDamageRow(row, encounterStart, maximumAmount);
			}
			ImGui.EndTable();
		}
	}

	private CombatHistoryRecord DrawHistorySelector(IReadOnlyList<CombatHistoryRecord> histories, ref int selectedNumber, string id, Action onSelectionChanged)
	{
		int currentSelection = selectedNumber;
		if (currentSelection == 0 || histories.All(history => history.Number != currentSelection))
		{
			currentSelection = histories.OrderBy(history => history.Number).First().Number;
			onSelectionChanged();
		}

		ImGui.TextUnformatted("履歴を選択してください（昇順）");
		if (ImGui.BeginListBox($"##{id}", new Vector2(-1f, 108f)))
		{
			foreach (CombatHistoryRecord history in histories.OrderBy(history => history.Number))
			{
				bool selected = history.Number == currentSelection;
				if (ImGui.Selectable(HistoryLabel(history), selected))
				{
					currentSelection = history.Number;
					onSelectionChanged();
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndListBox();
		}
		selectedNumber = currentSelection;
		return histories.First(history => history.Number == currentSelection);
	}

	private void DrawPartyOverview(IReadOnlyList<PhaseRecord> phases, string displayLabel, string tableId, string emptyText)
	{
		DrawSectionTitle(displayLabel, "ヘッダをクリックすると並び替えできます。初期順は Phase 昇順、同一 Phase 内は DPS 降順です。");
		if (phases.Count == 0)
		{
			ImGui.TextDisabled(emptyText);
			return;
		}

		DateTime now = DateTime.UtcNow;
		DateTime encounterStart = phases.Min(phase => phase.StartedAt);
		List<SummaryRowData> rows = phases
			.SelectMany(phase => phase.Players.Values.Select(player => new SummaryRowData(
				phase,
				player,
				player.Dps(phase.DurationSeconds(now)),
				player.ActiveRate(phase.StartedAt, phase.EffectiveEnd(now)))))
			.ToList();
		rows.Sort(CompareSummaryRows);
		DrawPartyCards(phases, rows, now);
		ImGui.Spacing();

		if (!ImGui.BeginTable($"##{tableId}", 10, TableFlags | ImGuiTableFlags.ScrollY, new Vector2(0f, -1f)))
		{
			return;
		}
		SetupSummaryColumns();
		DrawSortableSummaryHeader(tableId);
		double maximumDps = rows.Count == 0 ? 0 : rows.Max(row => row.Dps);
		foreach (SummaryRowData row in rows)
		{
			DrawSummaryRow(row, encounterStart, now, maximumDps);
		}
		ImGui.EndTable();
	}

	private void DrawPlayerDetail(IReadOnlyList<PhaseRecord> sourcePhases, uint entityId, string playerName, ref int selectedPhaseNumber, string idPrefix)
	{
		List<PhaseRecord> phases = sourcePhases.Where(phase => phase.Players.ContainsKey(entityId)).OrderBy(phase => phase.Number).ToList();
		DrawSectionTitle(playerName, "フェーズ概要とアクション別の内訳です。");
		if (phases.Count == 0)
		{
			ImGui.TextDisabled("このメンバーのフェーズデータはありません。");
			return;
		}

		int phaseNumber = selectedPhaseNumber;
		if (phaseNumber == 0 || phases.All(phase => phase.Number != phaseNumber))
		{
			phaseNumber = phases[^1].Number;
		}
		PhaseRecord phase = phases.First(item => item.Number == phaseNumber);
		ImGui.TextUnformatted("表示フェーズ");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(190f);
		if (ImGui.BeginCombo($"##{idPrefix}SelectedPhase", PhaseLabel(phase)))
		{
			foreach (PhaseRecord item in phases)
			{
				bool selected = item.Number == phaseNumber;
				if (ImGui.Selectable(PhaseLabel(item), selected))
				{
					phaseNumber = item.Number;
				}
				if (selected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		selectedPhaseNumber = phaseNumber;
		phase = phases.First(item => item.Number == phaseNumber);

		DateTime now = DateTime.UtcNow;
		DateTime encounterStart = sourcePhases.Min(item => item.StartedAt);
		PlayerPhaseStatistics player = phase.Players[entityId];
		double dps = player.Dps(phase.DurationSeconds(now));
		double activeRate = player.ActiveRate(phase.StartedAt, phase.EffectiveEnd(now));
		DrawPlayerCards(player, dps, activeRate);
		ImGui.Spacing();

		if (ImGui.BeginTable($"##{idPrefix}PlayerSummary", 10, TableFlags))
		{
			SetupSummaryColumns();
			ImGui.TableHeadersRow();
			DrawSummaryRow(new SummaryRowData(phase, player, dps, activeRate), encounterStart, now, dps);
			ImGui.EndTable();
		}

		ImGui.Spacing();
		DrawSectionTitle("アクション内訳", "カテゴリ別に使用回数、ダメージ、回復量を表示します。各ヘッダをクリックすると並び替えできます。");
		List<ActionStatistics> actions = player.Actions.Values.ToList();
		DrawActionGroup("ウェポンスキル", actions.Where(action => action.Kind == ActionKind.WeaponSkill), idPrefix);
		DrawActionGroup("アビリティ", actions.Where(action => action.Kind == ActionKind.Ability && (action.TotalDamage > 0 || action.TotalHealing == 0)), idPrefix);
		DrawActionGroup("魔法", actions.Where(action => action.Kind == ActionKind.Magic && (action.TotalDamage > 0 || action.TotalHealing == 0)), idPrefix);
		DrawActionGroup("回復魔法", actions.Where(action => action.Kind == ActionKind.Magic && action.TotalHealing > 0), idPrefix);
		DrawActionGroup("回復アビリティ", actions.Where(action => action.Kind == ActionKind.Ability && action.TotalHealing > 0), idPrefix);
		DrawActionGroup("その他 / オートアタック", actions.Where(action => action.Kind == ActionKind.Other), idPrefix);
	}

	private void DrawSortableSummaryHeader(string tableId)
	{
		string[] labels = ["Phase", "プレイヤー名", "開始時間", "終了時間", "DPS", "Crit %", "DH %", "Crit + DH %", "最大ダメージ / アクション", "Active %"];
		ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
		for (int index = 0; index < labels.Length; index++)
		{
			ImGui.TableSetColumnIndex(index);
			SummarySortColumn column = (SummarySortColumn)index;
			string indicator = column == summarySortColumn ? (summarySortDescending ? " ▼" : " ▲") : string.Empty;
			if (ImGui.Selectable($"{labels[index]}{indicator}##{tableId}Sort{index}", false))
			{
				if (summarySortColumn == column)
				{
					summarySortDescending = !summarySortDescending;
				}
				else
				{
					summarySortColumn = column;
					summarySortDescending = column is SummarySortColumn.Dps or SummarySortColumn.Critical or SummarySortColumn.DirectHit or SummarySortColumn.CriticalDirectHit or SummarySortColumn.MaximumDamage or SummarySortColumn.Active;
				}
			}
		}
	}

	private int CompareSummaryRows(SummaryRowData left, SummaryRowData right)
	{
		int comparison = summarySortColumn switch
		{
			SummarySortColumn.Phase => left.Phase.Number.CompareTo(right.Phase.Number),
			SummarySortColumn.Player => StringComparer.CurrentCulture.Compare(left.Player.PlayerName, right.Player.PlayerName),
			SummarySortColumn.Start => left.Phase.StartedAt.CompareTo(right.Phase.StartedAt),
			SummarySortColumn.End => left.Phase.EffectiveEnd(DateTime.UtcNow).CompareTo(right.Phase.EffectiveEnd(DateTime.UtcNow)),
			SummarySortColumn.Dps => left.Dps.CompareTo(right.Dps),
			SummarySortColumn.Critical => left.Player.CriticalRate.CompareTo(right.Player.CriticalRate),
			SummarySortColumn.DirectHit => left.Player.DirectHitRate.CompareTo(right.Player.DirectHitRate),
			SummarySortColumn.CriticalDirectHit => left.Player.CriticalDirectHitRate.CompareTo(right.Player.CriticalDirectHitRate),
			SummarySortColumn.MaximumDamage => left.Player.MaximumDamage.CompareTo(right.Player.MaximumDamage),
			SummarySortColumn.Active => left.ActiveRate.CompareTo(right.ActiveRate),
			_ => 0
		};
		if (comparison != 0)
		{
			return summarySortDescending ? -comparison : comparison;
		}
		comparison = left.Phase.Number.CompareTo(right.Phase.Number);
		if (comparison != 0)
		{
			return comparison;
		}
		comparison = right.Dps.CompareTo(left.Dps);
		return comparison != 0 ? comparison : StringComparer.CurrentCulture.Compare(left.Player.PlayerName, right.Player.PlayerName);
	}

	private void DrawSortableIncomingHeader()
	{
		string[] labels = ["Phase", "プレイヤー名", "開始時間", "終了時間", "被ダメージ量", "エネミー / 攻撃名", "被ダメージ時のバフ / デバフ"];
		ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
		for (int index = 0; index < labels.Length; index++)
		{
			ImGui.TableSetColumnIndex(index);
			IncomingSortColumn column = (IncomingSortColumn)index;
			string indicator = column == incomingSortColumn ? (incomingSortDescending ? " ▼" : " ▲") : string.Empty;
			if (ImGui.Selectable($"{labels[index]}{indicator}##IncomingSort{index}", false))
			{
				if (incomingSortColumn == column)
				{
					incomingSortDescending = !incomingSortDescending;
				}
				else
				{
					incomingSortColumn = column;
					incomingSortDescending = column == IncomingSortColumn.Amount;
				}
			}
		}
	}

	private int CompareIncomingRows(IncomingRowData left, IncomingRowData right)
	{
		int comparison = incomingSortColumn switch
		{
			IncomingSortColumn.Phase => left.Phase.Number.CompareTo(right.Phase.Number),
			IncomingSortColumn.Player => StringComparer.CurrentCulture.Compare(left.DamageEvent.PlayerName, right.DamageEvent.PlayerName),
			IncomingSortColumn.Start => left.Phase.StartedAt.CompareTo(right.Phase.StartedAt),
			IncomingSortColumn.End => (left.Phase.EndedAt ?? left.DamageEvent.Timestamp).CompareTo(right.Phase.EndedAt ?? right.DamageEvent.Timestamp),
			IncomingSortColumn.Amount => left.DamageEvent.Amount.CompareTo(right.DamageEvent.Amount),
			IncomingSortColumn.Action => StringComparer.CurrentCulture.Compare($"{left.DamageEvent.EnemyName} / {left.DamageEvent.ActionName}", $"{right.DamageEvent.EnemyName} / {right.DamageEvent.ActionName}"),
			IncomingSortColumn.Statuses => StringComparer.CurrentCulture.Compare(FormatStatuses(left.DamageEvent.Statuses), FormatStatuses(right.DamageEvent.Statuses)),
			_ => 0
		};
		if (comparison != 0)
		{
			return incomingSortDescending ? -comparison : comparison;
		}
		comparison = left.Phase.Number.CompareTo(right.Phase.Number);
		return comparison != 0 ? comparison : left.DamageEvent.Timestamp.CompareTo(right.DamageEvent.Timestamp);
	}

	private static void SetupSummaryColumns()
	{
		ImGui.TableSetupColumn("Phase");
		ImGui.TableSetupColumn("プレイヤー名");
		ImGui.TableSetupColumn("開始時間");
		ImGui.TableSetupColumn("終了時間");
		ImGui.TableSetupColumn("DPS");
		ImGui.TableSetupColumn("Crit %");
		ImGui.TableSetupColumn("DH %");
		ImGui.TableSetupColumn("Crit + DH %");
		ImGui.TableSetupColumn("最大ダメージ / アクション");
		ImGui.TableSetupColumn("Active %");
	}

	private static void DrawSummaryRow(SummaryRowData row, DateTime encounterStart, DateTime now, double maximumDps)
	{
		PhaseRecord phase = row.Phase;
		PlayerPhaseStatistics player = row.Player;
		ImGui.TableNextRow();
		TextColumn(0, PhaseLabel(phase));
		TextColumn(1, player.PlayerName);
		TextColumn(2, FormatElapsed(phase.StartedAt, encounterStart));
		TextColumn(3, phase.EndedAt.HasValue
			? FormatElapsed(phase.EndedAt.Value, encounterStart)
			: $"{FormatElapsed(now, encounterStart)} (計測中)");
		ProgressColumn(4, row.Dps, maximumDps, row.Dps.ToString("N1"), new Vector4(0.05f, 0.52f, 0.86f, 0.9f));
		TextColumn(5, Percent(player.CriticalRate));
		TextColumn(6, Percent(player.DirectHitRate));
		TextColumn(7, Percent(player.CriticalDirectHitRate));
		TextColumn(8, player.MaximumDamage == 0 ? "-" : $"{player.MaximumDamage:N0} / {player.MaximumDamageAction}");
		ProgressColumn(9, row.ActiveRate, 1.0, Percent(row.ActiveRate), new Vector4(0.06f, 0.72f, 0.68f, 0.9f));
	}

	private static void DrawIncomingDamageRow(IncomingRowData row, DateTime encounterStart, uint maximumAmount)
	{
		PhaseRecord phase = row.Phase;
		IncomingDamageEvent damageEvent = row.DamageEvent;
		ImGui.TableNextRow();
		TextColumn(0, phase.Number.ToString());
		TextColumn(1, damageEvent.PlayerName);
		TextColumn(2, FormatElapsed(phase.StartedAt, encounterStart));
		TextColumn(3, FormatElapsed(phase.EndedAt ?? damageEvent.Timestamp, encounterStart));
		ProgressColumn(4, damageEvent.Amount, maximumAmount, damageEvent.Amount.ToString("N0"), new Vector4(0.92f, 0.29f, 0.22f, 0.9f));
		TextColumn(5, $"{damageEvent.EnemyName} / {damageEvent.ActionName}");
		TextColumn(6, FormatStatuses(damageEvent.Statuses));
	}

	private void DrawActionGroup(string label, IEnumerable<ActionStatistics> source, string idPrefix)
	{
		string sortKey = $"{idPrefix}:{label}";
		ActionSortColumn defaultColumn = label.StartsWith("回復", StringComparison.Ordinal)
			? ActionSortColumn.TotalHealing
			: ActionSortColumn.TotalDamage;
		(ActionSortColumn column, bool descending) = actionSortStates.TryGetValue(sortKey, out var state)
			? state
			: (defaultColumn, true);
		List<ActionStatistics> actions = source.ToList();
		actions.Sort((left, right) => CompareActions(left, right, column, descending));
		ImGui.Spacing();
		ImGui.TextColored(new Vector4(0.35f, 0.76f, 1f, 1f), $"{label}  {actions.Count}種");
		ImGui.PushID(idPrefix);
		ImGui.PushID(label);
		if (actions.Count != 0 && ImGui.BeginTable("##Actions", 10, TableFlags))
		{
			ImGui.TableSetupColumn("No");
			ImGui.TableSetupColumn("アクション");
			ImGui.TableSetupColumn("回数");
			ImGui.TableSetupColumn("総ダメージ");
			ImGui.TableSetupColumn("総回復量");
			ImGui.TableSetupColumn("Crit %");
			ImGui.TableSetupColumn("DH %");
			ImGui.TableSetupColumn("Crit + DH %");
			ImGui.TableSetupColumn("最大");
			ImGui.TableSetupColumn("最小");
			DrawSortableActionHeader(sortKey, column, descending);
			for (int index = 0; index < actions.Count; index++)
			{
				ActionStatistics action = actions[index];
				ImGui.TableNextRow();
				TextColumn(0, (index + 1).ToString());
				TextColumn(1, action.ActionName);
				TextColumn(2, action.UseCount.ToString("N0"));
				TextColumn(3, action.TotalDamage.ToString("N0"));
				TextColumn(4, action.TotalHealing.ToString("N0"));
				TextColumn(5, Percent(action.CriticalRate));
				TextColumn(6, Percent(action.DirectHitRate));
				TextColumn(7, Percent(action.CriticalDirectHitRate));
				TextColumn(8, action.MaximumAmount.ToString("N0"));
				TextColumn(9, action.DisplayMinimumAmount.ToString("N0"));
			}
			ImGui.EndTable();
		}
		if (actions.Count == 0)
		{
			ImGui.TextDisabled("データなし");
		}
		ImGui.PopID();
		ImGui.PopID();
	}

	private void DrawSortableActionHeader(string sortKey, ActionSortColumn selectedColumn, bool descending)
	{
		string[] labels = ["No", "アクション", "回数", "総ダメージ", "総回復量", "Crit %", "DH %", "Crit + DH %", "最大", "最小"];
		ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
		for (int index = 0; index < labels.Length; index++)
		{
			ImGui.TableSetColumnIndex(index);
			ActionSortColumn column = (ActionSortColumn)index;
			string indicator = column == selectedColumn ? (descending ? " ▼" : " ▲") : string.Empty;
			if (ImGui.Selectable($"{labels[index]}{indicator}##ActionSort{index}", false))
			{
				bool nextDescending = column == selectedColumn
					? !descending
					: column is not ActionSortColumn.Number and not ActionSortColumn.Action;
				actionSortStates[sortKey] = (column, nextDescending);
			}
		}
	}

	private static int CompareActions(ActionStatistics left, ActionStatistics right, ActionSortColumn column, bool descending)
	{
		int comparison = column switch
		{
			ActionSortColumn.Number => left.ActionId.CompareTo(right.ActionId),
			ActionSortColumn.Action => StringComparer.CurrentCulture.Compare(left.ActionName, right.ActionName),
			ActionSortColumn.Count => left.UseCount.CompareTo(right.UseCount),
			ActionSortColumn.TotalDamage => left.TotalDamage.CompareTo(right.TotalDamage),
			ActionSortColumn.TotalHealing => left.TotalHealing.CompareTo(right.TotalHealing),
			ActionSortColumn.Critical => left.CriticalRate.CompareTo(right.CriticalRate),
			ActionSortColumn.DirectHit => left.DirectHitRate.CompareTo(right.DirectHitRate),
			ActionSortColumn.CriticalDirectHit => left.CriticalDirectHitRate.CompareTo(right.CriticalDirectHitRate),
			ActionSortColumn.Maximum => left.MaximumAmount.CompareTo(right.MaximumAmount),
			ActionSortColumn.Minimum => left.DisplayMinimumAmount.CompareTo(right.DisplayMinimumAmount),
			_ => 0
		};
		if (comparison != 0)
		{
			return descending ? -comparison : comparison;
		}
		return StringComparer.CurrentCulture.Compare(left.ActionName, right.ActionName);
	}

	private static void DrawPartyCards(IReadOnlyList<PhaseRecord> phases, IReadOnlyList<SummaryRowData> rows, DateTime now)
	{
		long totalDamage = rows.Sum(row => row.Player.TotalDamage);
		SummaryRowData? top = rows.OrderByDescending(row => row.Dps).FirstOrDefault();
		DateTime startedAt = phases.Min(phase => phase.StartedAt);
		DateTime endedAt = phases.Max(phase => phase.EffectiveEnd(now));
		DrawMetricCards([
			("PHASE", phases.Count.ToString("N0"), new Vector4(0.3f, 0.72f, 1f, 1f)),
			("計測時間", FormatDuration(endedAt - startedAt), new Vector4(0.25f, 0.82f, 0.92f, 1f)),
			("総ダメージ", totalDamage.ToString("N0"), new Vector4(0.25f, 0.72f, 1f, 1f)),
			("最高 DPS", top == null ? "-" : $"{top.Dps:N1}  {top.Player.PlayerName}", new Vector4(0.45f, 0.86f, 1f, 1f))
		]);
	}

	private static void DrawPlayerCards(PlayerPhaseStatistics player, double dps, double activeRate)
	{
		DrawMetricCards([
			("DPS", dps.ToString("N1"), new Vector4(0.3f, 0.75f, 1f, 1f)),
			("総ダメージ", player.TotalDamage.ToString("N0"), new Vector4(0.25f, 0.72f, 1f, 1f)),
			("総回復量", player.TotalHealing.ToString("N0"), new Vector4(0.2f, 0.82f, 0.72f, 1f)),
			("ACTIVE", Percent(activeRate), new Vector4(0.25f, 0.86f, 0.8f, 1f))
		]);
	}

	private static void DrawIncomingCards(IReadOnlyList<IncomingRowData> rows)
	{
		long total = rows.Sum(row => (long)row.DamageEvent.Amount);
		IncomingRowData? maximum = rows.OrderByDescending(row => row.DamageEvent.Amount).FirstOrDefault();
		string commonAction = rows
			.GroupBy(row => row.DamageEvent.ActionName)
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.CurrentCulture)
			.Select(group => group.Key)
			.FirstOrDefault() ?? "-";
		DrawMetricCards([
			("被ダメージ合計", total.ToString("N0"), new Vector4(1f, 0.42f, 0.32f, 1f)),
			("ヒット数", rows.Count.ToString("N0"), new Vector4(1f, 0.58f, 0.3f, 1f)),
			("最大被ダメージ", maximum == null ? "-" : maximum.DamageEvent.Amount.ToString("N0"), new Vector4(1f, 0.35f, 0.28f, 1f)),
			("最多攻撃", commonAction, new Vector4(0.95f, 0.55f, 0.35f, 1f))
		]);
	}

	private static void DrawMetricCards(IReadOnlyList<(string Label, string Value, Vector4 Color)> cards)
	{
		float width = Math.Max(150f, (ImGui.GetContentRegionAvail().X - 24f) / cards.Count);
		for (int index = 0; index < cards.Count; index++)
		{
			(string label, string value, Vector4 color) = cards[index];
			ImGui.PushID(index);
			ImGui.BeginChild("##MetricCard", new Vector2(width, 62f), true);
			ImGui.TextDisabled(label);
			ImGui.TextColored(color, value);
			ImGui.EndChild();
			ImGui.PopID();
			if (index < cards.Count - 1)
			{
				ImGui.SameLine();
			}
		}
	}

	private static void DrawSectionTitle(string title, string description)
	{
		ImGui.TextColored(new Vector4(0.36f, 0.78f, 1f, 1f), title);
		if (!string.IsNullOrWhiteSpace(description))
		{
			ImGui.TextDisabled(description);
		}
		ImGui.Separator();
		ImGui.Spacing();
	}

	private static Dictionary<uint, string> GetHistoryMembers(CombatHistoryRecord history)
	{
		return history.Phases
			.SelectMany(phase => phase.Players)
			.GroupBy(player => player.Key)
			.ToDictionary(group => group.Key, group => group.Last().Value.PlayerName);
	}

	private static string FindPlayerName(IReadOnlyList<PhaseRecord> phases, uint entityId)
	{
		return phases.Reverse()
			.Where(phase => phase.Players.ContainsKey(entityId))
			.Select(phase => phase.Players[entityId].PlayerName)
			.FirstOrDefault() ?? $"不明なメンバー ({entityId:X8})";
	}

	private static string HistoryLabel(CombatHistoryRecord history)
	{
		return $"#{history.Number}  {history.StartedAt.ToLocalTime():yyyy/MM/dd HH:mm:ss} - {history.EndedAt.ToLocalTime():HH:mm:ss}  {HistoryReasonLabel(history.EndReason)}  ({history.Phases.Count}フェーズ)";
	}

	private static string HistoryReasonLabel(CombatHistoryEndReason reason)
	{
		return reason switch
		{
			CombatHistoryEndReason.Wipe => "全滅",
			CombatHistoryEndReason.DutyCompleted => "コンテンツクリア",
			CombatHistoryEndReason.Manual => "手動保存",
			_ => reason.ToString()
		};
	}

	private static void TextColumn(int column, string text)
	{
		ImGui.TableSetColumnIndex(column);
		ImGui.TextUnformatted(text);
	}

	private static void ProgressColumn(int column, double value, double maximum, string overlay, Vector4 color)
	{
		ImGui.TableSetColumnIndex(column);
		float ratio = maximum <= 0 ? 0 : (float)Math.Clamp(value / maximum, 0, 1);
		ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
		ImGui.ProgressBar(ratio, new Vector2(-1f, 0f), overlay);
		ImGui.PopStyleColor();
	}

	private static string PhaseLabel(PhaseRecord phase)
	{
		return phase.IsActive ? $"{phase.Number} (計測中)" : phase.Number.ToString();
	}

	private static string FormatElapsed(DateTime timestamp, DateTime encounterStart)
	{
		TimeSpan elapsed = timestamp > encounterStart ? timestamp - encounterStart : TimeSpan.Zero;
		return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
	}

	private static string FormatDuration(TimeSpan duration)
	{
		if (duration < TimeSpan.Zero)
		{
			duration = TimeSpan.Zero;
		}
		return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
	}

	private static string FormatStatuses(IReadOnlyList<CombatStatusSnapshot> statuses)
	{
		if (statuses.Count == 0)
		{
			return "なし";
		}
		return string.Join(", ", statuses.Select(status => status.Stacks > 1
			? $"{status.Name} x{status.Stacks} ({status.RemainingSeconds:0.0}s)"
			: $"{status.Name} ({status.RemainingSeconds:0.0}s)"));
	}

	private static string Percent(double rate)
	{
		return $"{rate * 100.0:0.00}%";
	}

	internal static void PushBlueTheme()
	{
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.012f, 0.032f, 0.058f, 0.98f));
		ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.025f, 0.085f, 0.14f, 0.92f));
		ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.015f, 0.22f, 0.34f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.02f, 0.4f, 0.62f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.015f, 0.16f, 0.25f, 1f));
		ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.03f, 0.11f, 0.19f, 0.95f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.04f, 0.25f, 0.4f, 1f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.03f, 0.34f, 0.54f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.02f, 0.32f, 0.5f, 0.85f));
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.02f, 0.46f, 0.7f, 1f));
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.02f, 0.55f, 0.82f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.02f, 0.35f, 0.55f, 0.9f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.02f, 0.48f, 0.73f, 1f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.01f, 0.58f, 0.88f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.02f, 0.2f, 0.34f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.02f, 0.48f, 0.73f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.02f, 0.4f, 0.62f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.02f, 0.36f, 0.56f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.015f, 0.055f, 0.09f, 0.72f));
		ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.025f, 0.095f, 0.15f, 0.72f));
		ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.06f, 0.48f, 0.72f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.08f, 0.36f, 0.56f, 0.82f));
		ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.05f, 0.52f, 0.86f, 0.9f));
		ImGui.PushStyleColor(ImGuiCol.PlotHistogramHovered, new Vector4(0.12f, 0.7f, 1f, 1f));
	}
}
