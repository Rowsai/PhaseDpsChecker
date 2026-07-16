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
	private const int BlueThemeColorCount = 18;

	private static readonly ImGuiTableFlags TableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.RowBg;

	private readonly Configuration configuration;

	private readonly CombatTracker tracker;

	private int selectedCurrentPhaseNumber;

	private int selectedHistoryNumber;

	private uint selectedHistoryEntityId;

	private int selectedHistoryPhaseNumber;

	public MainWindow(Configuration configuration, CombatTracker tracker)
		: base("Phase DPS Checker ver 0.2.1###PhaseDpsCheckerMain")
	{
		this.configuration = configuration;
		this.tracker = tracker;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(860f, 480f),
			MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
		};
		base.Size = new Vector2(1320f, 760f);
		base.SizeCondition = ImGuiCond.FirstUseEver;
	}

	public void Dispose()
	{
	}

	public override void PreDraw()
	{
		PushBlueTheme();
	}

	public override void PostDraw()
	{
		ImGui.PopStyleColor(18);
	}

	public override void Draw()
	{
		DrawWarnings();
		if (ImGui.BeginTabBar("##PhaseDpsCheckerTabs"))
		{
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
			ImGui.EndTabBar();
		}
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
		Dictionary<uint, string> currentMembers = tracker.Roster.GetCurrentMembers();
		string value;
		string text = ((configuration.SelectedEntityId == 0) ? "パーティメンバー全体" : (currentMembers.TryGetValue(configuration.SelectedEntityId, out value) ? value : $"不在のメンバー ({configuration.SelectedEntityId:X8})"));
		ImGui.TextUnformatted("表示対象を選択してください");
		ImGui.SetNextItemWidth(360f);
		if (ImGui.BeginCombo("##DisplayTarget", text))
		{
			if (ImGui.Selectable("パーティメンバー全体", configuration.SelectedEntityId == 0))
			{
				configuration.SelectedEntityId = 0u;
				configuration.Save();
			}
			foreach (KeyValuePair<uint, string> item in currentMembers.OrderBy<KeyValuePair<uint, string>, string>((KeyValuePair<uint, string> member) => member.Value, StringComparer.CurrentCulture))
			{
				bool flag = configuration.SelectedEntityId == item.Key;
				if (ImGui.Selectable(item.Value, flag))
				{
					configuration.SelectedEntityId = item.Key;
					configuration.Save();
				}
				if (flag)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();
		float v = configuration.TargetLossGraceSeconds;
		ImGui.SetNextItemWidth(260f);
		if (ImGui.SliderFloat("ターゲット不可の猶予（秒）", ref v, 0.1f, 3f, "%.2f"))
		{
			configuration.TargetLossGraceSeconds = v;
			configuration.Save();
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("一時的なオブジェクト更新をフェーズ終了と誤判定しないための猶予です。");
		}
		int v2 = configuration.MaxPhaseHistory;
		ImGui.SetNextItemWidth(260f);
		if (ImGui.SliderInt("1戦闘で保持するフェーズ数", ref v2, 5, 100))
		{
			configuration.MaxPhaseHistory = v2;
			configuration.Save();
		}
		int v3 = configuration.MaxEncounterHistory;
		ImGui.SetNextItemWidth(260f);
		if (ImGui.SliderInt("保持する戦闘履歴数", ref v3, 1, 50))
		{
			configuration.MaxEncounterHistory = v3;
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
			selectedHistoryEntityId = 0u;
			selectedHistoryPhaseNumber = 0;
		}
		ImGui.Spacing();
		PhaseRecord currentPhase = tracker.Aggregator.CurrentPhase;
		ImGui.TextDisabled((currentPhase == null) ? $"状態: フェーズ待機中 / 保存済み履歴 {tracker.Aggregator.Histories.Count}件" : $"状態: Phase {currentPhase.Number} を計測中 / 保存済み履歴 {tracker.Aggregator.Histories.Count}件");
	}

	private void DrawDisplay()
	{
		IReadOnlyList<PhaseRecord> phases = tracker.Aggregator.Phases;
		if (configuration.SelectedEntityId == 0)
		{
			DrawPartyOverview(phases, "パーティメンバー全体", "LivePartyOverview", "敵へのダメージを検出すると、ここにフェーズ結果が表示されます。");
			return;
		}
		string value;
		string playerName = (tracker.Roster.GetCurrentMembers().TryGetValue(configuration.SelectedEntityId, out value) ? value : FindPlayerName(phases, configuration.SelectedEntityId));
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
		if (selectedHistoryNumber == 0 || histories.All((CombatHistoryRecord history) => history.Number != selectedHistoryNumber))
		{
			selectedHistoryNumber = histories[histories.Count - 1].Number;
			selectedHistoryEntityId = 0u;
			selectedHistoryPhaseNumber = 0;
		}
		ImGui.TextUnformatted("履歴を選択してください");
		if (ImGui.BeginListBox("##HistoryList", new Vector2(-1f, 125f)))
		{
			foreach (CombatHistoryRecord item in histories.Reverse())
			{
				bool flag = item.Number == selectedHistoryNumber;
				if (ImGui.Selectable(HistoryLabel(item), flag))
				{
					selectedHistoryNumber = item.Number;
					selectedHistoryEntityId = 0u;
					selectedHistoryPhaseNumber = 0;
				}
				if (flag)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndListBox();
		}
		CombatHistoryRecord combatHistoryRecord = histories.First((CombatHistoryRecord history) => history.Number == selectedHistoryNumber);
		Dictionary<uint, string> historyMembers = GetHistoryMembers(combatHistoryRecord);
		if (selectedHistoryEntityId != 0 && !historyMembers.ContainsKey(selectedHistoryEntityId))
		{
			selectedHistoryEntityId = 0u;
			selectedHistoryPhaseNumber = 0;
		}
		string value;
		string text = ((selectedHistoryEntityId == 0) ? "パーティメンバー全体" : (historyMembers.TryGetValue(selectedHistoryEntityId, out value) ? value : "不明なメンバー"));
		ImGui.Spacing();
		ImGui.TextUnformatted("履歴の表示対象");
		ImGui.SetNextItemWidth(360f);
		if (ImGui.BeginCombo("##HistoryDisplayTarget", text))
		{
			if (ImGui.Selectable("パーティメンバー全体", selectedHistoryEntityId == 0))
			{
				selectedHistoryEntityId = 0u;
				selectedHistoryPhaseNumber = 0;
			}
			foreach (KeyValuePair<uint, string> item2 in historyMembers.OrderBy<KeyValuePair<uint, string>, string>((KeyValuePair<uint, string> member) => member.Value, StringComparer.CurrentCulture))
			{
				bool flag2 = item2.Key == selectedHistoryEntityId;
				if (ImGui.Selectable(item2.Value, flag2))
				{
					selectedHistoryEntityId = item2.Key;
					selectedHistoryPhaseNumber = 0;
				}
				if (flag2)
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
			DrawPartyOverview(combatHistoryRecord.Phases, $"履歴 #{combatHistoryRecord.Number} / パーティメンバー全体", "HistoryPartyOverview", "この履歴には表示可能なフェーズがありません。");
		}
		else
		{
			DrawPlayerDetail(combatHistoryRecord.Phases, selectedHistoryEntityId, text, ref selectedHistoryPhaseNumber, "HistoryPlayer");
		}
	}

	private static void DrawPartyOverview(IReadOnlyList<PhaseRecord> phases, string displayLabel, string tableId, string emptyText)
	{
		ImU8String text = new ImU8String(5, 1);
		text.AppendLiteral("選択対象：");
		text.AppendFormatted(displayLabel);
		ImGui.TextUnformatted(text);
		ImGui.Spacing();
		if (phases.Count == 0)
		{
			ImGui.TextDisabled(emptyText);
			return;
		}
		ImU8String strId = new ImU8String(2, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(tableId);
		if (!ImGui.BeginTable(strId, 10, TableFlags | ImGuiTableFlags.ScrollY, new Vector2(0f, -1f)))
		{
			return;
		}
		SetupSummaryColumns();
		ImGui.TableHeadersRow();
		DateTime utcNow = DateTime.UtcNow;
		foreach (PhaseRecord item in phases.Reverse())
		{
			foreach (PlayerPhaseStatistics item2 in item.Players.Values.OrderBy<PlayerPhaseStatistics, string>((PlayerPhaseStatistics player) => player.PlayerName, StringComparer.CurrentCulture))
			{
				DrawSummaryRow(item, item2, utcNow);
			}
		}
		ImGui.EndTable();
	}

	private static void DrawPlayerDetail(IReadOnlyList<PhaseRecord> sourcePhases, uint entityId, string playerName, ref int selectedPhaseNumber, string idPrefix)
	{
		List<PhaseRecord> list = sourcePhases.Where((PhaseRecord phaseRecord) => phaseRecord.Players.ContainsKey(entityId)).ToList();
		ImU8String text = new ImU8String(5, 1);
		text.AppendLiteral("選択対象：");
		text.AppendFormatted(playerName);
		ImGui.TextUnformatted(text);
		if (list.Count == 0)
		{
			ImGui.TextDisabled("このメンバーのフェーズデータはありません。");
			return;
		}
		int phaseNumber = selectedPhaseNumber;
		if (phaseNumber == 0 || list.All((PhaseRecord phaseRecord) => phaseRecord.Number != phaseNumber))
		{
			phaseNumber = list[list.Count - 1].Number;
		}
		PhaseRecord phase = list.First((PhaseRecord phaseRecord) => phaseRecord.Number == phaseNumber);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(180f);
		ImU8String label = new ImU8String(15, 1);
		label.AppendLiteral("##");
		label.AppendFormatted(idPrefix);
		label.AppendLiteral("SelectedPhase");
		if (ImGui.BeginCombo(label, PhaseLabel(phase)))
		{
			foreach (PhaseRecord item in list.AsEnumerable().Reverse())
			{
				bool flag = item.Number == phaseNumber;
				if (ImGui.Selectable(PhaseLabel(item), flag))
				{
					phaseNumber = item.Number;
				}
				if (flag)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		selectedPhaseNumber = phaseNumber;
		phase = list.First((PhaseRecord phaseRecord) => phaseRecord.Number == phaseNumber);
		ImGui.Spacing();
		ImU8String strId = new ImU8String(15, 1);
		strId.AppendLiteral("##");
		strId.AppendFormatted(idPrefix);
		strId.AppendLiteral("PlayerSummary");
		if (ImGui.BeginTable(strId, 10, TableFlags))
		{
			SetupSummaryColumns();
			ImGui.TableHeadersRow();
			DrawSummaryRow(phase, phase.Players[entityId], DateTime.UtcNow);
			ImGui.EndTable();
		}
		ImGui.Spacing();
		ImGui.TextUnformatted("ActionDetail");
		ImGui.Separator();
		List<ActionStatistics> source = phase.Players[entityId].Actions.Values.ToList();
		DrawActionGroup("ウェポンスキル", source.Where((ActionStatistics action) => action.Kind == ActionKind.WeaponSkill), idPrefix);
		DrawActionGroup("アビリティ", source.Where((ActionStatistics action) => action.Kind == ActionKind.Ability && (action.TotalDamage > 0 || action.TotalHealing == 0)), idPrefix);
		DrawActionGroup("魔法", source.Where((ActionStatistics action) => action.Kind == ActionKind.Magic && (action.TotalDamage > 0 || action.TotalHealing == 0)), idPrefix);
		DrawActionGroup("回復魔法", source.Where((ActionStatistics action) => action.Kind == ActionKind.Magic && action.TotalHealing > 0), idPrefix);
		DrawActionGroup("回復アビリティ", source.Where((ActionStatistics action) => action.Kind == ActionKind.Ability && action.TotalHealing > 0), idPrefix);
		DrawActionGroup("その他／オートアタック", source.Where((ActionStatistics action) => action.Kind == ActionKind.Other), idPrefix);
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

	private static void DrawSummaryRow(PhaseRecord phase, PlayerPhaseStatistics player, DateTime now)
	{
		DateTime phaseEnd = phase.EffectiveEnd(now);
		double phaseDurationSeconds = phase.DurationSeconds(now);
		ImGui.TableNextRow();
		TextColumn(0, PhaseLabel(phase));
		TextColumn(1, player.PlayerName);
		TextColumn(2, FormatClock(phase.StartedAt));
		DateTime? endedAt = phase.EndedAt;
		object text;
		if (endedAt.HasValue)
		{
			DateTime valueOrDefault = endedAt.GetValueOrDefault();
			text = FormatClock(valueOrDefault);
		}
		else
		{
			text = "計測中";
		}
		TextColumn(3, (string)text);
		TextColumn(4, player.Dps(phaseDurationSeconds).ToString("N1"));
		TextColumn(5, Percent(player.CriticalRate));
		TextColumn(6, Percent(player.DirectHitRate));
		TextColumn(7, Percent(player.CriticalDirectHitRate));
		TextColumn(8, (player.MaximumDamage == 0) ? "-" : $"{player.MaximumDamage:N0} / {player.MaximumDamageAction}");
		TextColumn(9, Percent(player.ActiveRate(phase.StartedAt, phaseEnd)));
	}

	private static void DrawActionGroup(string label, IEnumerable<ActionStatistics> source, string idPrefix)
	{
		List<ActionStatistics> list = source.OrderByDescending((ActionStatistics action) => action.TotalDamage + action.TotalHealing).ThenBy<ActionStatistics, string>((ActionStatistics action) => action.ActionName, StringComparer.CurrentCulture).ToList();
		ImGui.Spacing();
		ImGui.TextUnformatted(label);
		ImGui.PushID(idPrefix);
		ImGui.PushID(label);
		if (ImGui.BeginTable("##Actions", 10, TableFlags))
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
			ImGui.TableHeadersRow();
			for (int num = 0; num < list.Count; num++)
			{
				ActionStatistics actionStatistics = list[num];
				ImGui.TableNextRow();
				TextColumn(0, (num + 1).ToString());
				TextColumn(1, actionStatistics.ActionName);
				TextColumn(2, actionStatistics.UseCount.ToString("N0"));
				TextColumn(3, actionStatistics.TotalDamage.ToString("N0"));
				TextColumn(4, actionStatistics.TotalHealing.ToString("N0"));
				TextColumn(5, Percent(actionStatistics.CriticalRate));
				TextColumn(6, Percent(actionStatistics.DirectHitRate));
				TextColumn(7, Percent(actionStatistics.CriticalDirectHitRate));
				TextColumn(8, actionStatistics.MaximumAmount.ToString("N0"));
				TextColumn(9, actionStatistics.DisplayMinimumAmount.ToString("N0"));
			}
			ImGui.EndTable();
		}
		if (list.Count == 0)
		{
			ImGui.TextDisabled("データなし");
		}
		ImGui.PopID();
		ImGui.PopID();
	}

	private static Dictionary<uint, string> GetHistoryMembers(CombatHistoryRecord history)
	{
		return (from player in history.Phases.SelectMany((PhaseRecord phase) => phase.Players)
			group player by player.Key).ToDictionary((IGrouping<uint, KeyValuePair<uint, PlayerPhaseStatistics>> group) => group.Key, (IGrouping<uint, KeyValuePair<uint, PlayerPhaseStatistics>> group) => group.Last().Value.PlayerName);
	}

	private static string FindPlayerName(IReadOnlyList<PhaseRecord> phases, uint entityId)
	{
		return (from phase in phases.Reverse()
			where phase.Players.ContainsKey(entityId)
			select phase.Players[entityId].PlayerName).FirstOrDefault() ?? $"不明なメンバー ({entityId:X8})";
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
			_ => reason.ToString(), 
		};
	}

	private static void TextColumn(int column, string text)
	{
		ImGui.TableSetColumnIndex(column);
		ImGui.TextUnformatted(text);
	}

	private static string PhaseLabel(PhaseRecord phase)
	{
		if (!phase.IsActive)
		{
			return phase.Number.ToString();
		}
		return $"{phase.Number} (計測中)";
	}

	private static string FormatClock(DateTime timestamp)
	{
		return timestamp.ToLocalTime().ToString("HH:mm:ss");
	}

	private static string Percent(double rate)
	{
		return $"{rate * 100.0:0.00}%";
	}

	private static void PushBlueTheme()
	{
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.018f, 0.045f, 0.075f, 0.98f));
		ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.015f, 0.22f, 0.34f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.02f, 0.4f, 0.62f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.015f, 0.16f, 0.25f, 1f));
		ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.035f, 0.12f, 0.2f, 0.95f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.04f, 0.25f, 0.4f, 1f));
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.03f, 0.34f, 0.54f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.02f, 0.32f, 0.5f, 0.85f));
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.02f, 0.46f, 0.7f, 1f));
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.02f, 0.55f, 0.82f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.02f, 0.35f, 0.55f, 0.9f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.02f, 0.48f, 0.73f, 1f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.01f, 0.58f, 0.88f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.02f, 0.22f, 0.36f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.02f, 0.48f, 0.73f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.02f, 0.4f, 0.62f, 1f));
		ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.02f, 0.36f, 0.56f, 1f));
		ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(0.06f, 0.48f, 0.72f, 1f));
	}
}
