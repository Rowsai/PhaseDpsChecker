using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using PhaseDpsChecker.Combat;

namespace PhaseDpsChecker.Windows;

public sealed class PartyOverlayWindow : Window
{
	private static readonly ImGuiTableFlags TableFlags = ImGuiTableFlags.Borders
		| ImGuiTableFlags.RowBg
		| ImGuiTableFlags.Resizable
		| ImGuiTableFlags.ScrollY
		| ImGuiTableFlags.SizingStretchProp;

	private readonly CombatTracker tracker;

	private sealed record OverlayRow(
		PhaseRecord Phase,
		PlayerPhaseStatistics Player,
		double Dps,
		double ActiveRate,
		double DamageActiveRate,
		double HealingActiveRate);

	public PartyOverlayWindow(CombatTracker tracker)
		: base("Phase DPS Checker Overlay###PhaseDpsCheckerPartyOverlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
	{
		this.tracker = tracker;
		ShowCloseButton = false;
		RespectCloseHotkey = false;
		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(1080f, 240f),
			MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
		};
		Size = new Vector2(1320f, 420f);
		SizeCondition = ImGuiCond.FirstUseEver;
	}

	public override void PreDraw()
	{
		MainWindow.PushBlueTheme();
	}

	public override void PostDraw()
	{
		ImGui.PopStyleColor(MainWindow.BlueThemeColorCount);
	}

	public override void Draw()
	{
		PhaseRecord? currentPhase = tracker.Aggregator.CurrentPhase;
		ImGui.TextColored(new Vector4(0.36f, 0.78f, 1f, 1f), "PHASE DPS CHECKER  ·  PARTY OVERLAY");
		ImGui.SameLine();
		ImGui.TextDisabled("現在計測中のPhaseのみ表示します");
		ImGui.Separator();

		if (currentPhase == null)
		{
			ImGui.TextDisabled("現在計測中のPhaseはありません。");
			return;
		}

		DateTime now = DateTime.UtcNow;
		DateTime encounterStart = currentPhase.StartedAt;
		List<OverlayRow> rows = currentPhase.Players.Values
			.Select(player => new OverlayRow(
				currentPhase,
				player,
				player.Dps(currentPhase.DurationSeconds(now)),
				player.ActiveRate(currentPhase.StartedAt, currentPhase.EffectiveEnd(now)),
				player.DamageActiveRate(currentPhase.StartedAt, currentPhase.EffectiveEnd(now)),
				player.HealingActiveRate(currentPhase.StartedAt, currentPhase.EffectiveEnd(now))))
			.OrderByDescending(row => row.Dps)
			.ThenBy(row => row.Player.PlayerName, StringComparer.CurrentCulture)
			.ToList();

		DrawMetrics(currentPhase, rows, now);
		ImGui.Spacing();
		if (!ImGui.BeginTable("##PartyOverlayTable", 12, TableFlags, new Vector2(0f, -1f)))
		{
			return;
		}

		SetupColumns();
		ImGui.TableHeadersRow();
		double maximumDps = rows.Count == 0 ? 0 : rows.Max(row => row.Dps);
		foreach (OverlayRow row in rows)
		{
			DrawRow(row, encounterStart, now, maximumDps);
		}
		ImGui.EndTable();
	}

	private void DrawRow(OverlayRow row, DateTime encounterStart, DateTime now, double maximumDps)
	{
		ImGui.TableNextRow();
		TextColumn(0, row.Phase.IsActive ? $"{row.Phase.Number} (計測中)" : row.Phase.Number.ToString());

		ImGui.TableSetColumnIndex(1);
		uint jobId = tracker.Roster.GetJobId(row.Player.EntityId);
		if (jobId != 0)
		{
			uint jobIconId = 62100u + jobId;
			var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(jobIconId)).GetWrapOrEmpty();
			float iconSize = Math.Max(18f, ImGui.GetTextLineHeight());
			ImGui.Image(iconTexture.Handle, new Vector2(iconSize, iconSize));
			ImGui.SameLine(0f, 5f);
		}
		ImGui.TextUnformatted(row.Player.PlayerName);

		TextColumn(2, FormatElapsed(row.Phase.StartedAt, encounterStart));
		TextColumn(3, row.Phase.EndedAt.HasValue
			? FormatElapsed(row.Phase.EndedAt.Value, encounterStart)
			: $"{FormatElapsed(now, encounterStart)} (計測中)");
		ProgressColumn(4, row.Dps, maximumDps, row.Dps.ToString("N1"), new Vector4(0.05f, 0.52f, 0.86f, 0.9f));
		TextColumn(5, Percent(row.Player.CriticalRate));
		TextColumn(6, Percent(row.Player.DirectHitRate));
		TextColumn(7, Percent(row.Player.CriticalDirectHitRate));
		TextColumn(8, row.Player.MaximumDamage == 0 ? "-" : $"{row.Player.MaximumDamage:N0} / {row.Player.MaximumDamageAction}");
		ProgressColumn(9, row.ActiveRate, 1.0, Percent(row.ActiveRate), new Vector4(0.06f, 0.72f, 0.68f, 0.9f));
		ProgressColumn(10, row.DamageActiveRate, 1.0, Percent(row.DamageActiveRate), new Vector4(0.08f, 0.55f, 0.95f, 0.9f));
		ProgressColumn(11, row.HealingActiveRate, 1.0, Percent(row.HealingActiveRate), new Vector4(0.2f, 0.82f, 0.55f, 0.9f));
	}

	private static void DrawMetrics(PhaseRecord currentPhase, IReadOnlyList<OverlayRow> rows, DateTime now)
	{
		long totalDamage = rows.Sum(row => row.Player.TotalDamage);
		OverlayRow? top = rows.OrderByDescending(row => row.Dps).FirstOrDefault();
		DateTime startedAt = currentPhase.StartedAt;
		DateTime endedAt = currentPhase.EffectiveEnd(now);
		(string Label, string Value, Vector4 Color)[] cards =
		[
			("PHASE", currentPhase.Number.ToString("N0"), new Vector4(0.3f, 0.72f, 1f, 1f)),
			("計測時間", FormatDuration(endedAt - startedAt), new Vector4(0.25f, 0.82f, 0.92f, 1f)),
			("総ダメージ", totalDamage.ToString("N0"), new Vector4(0.25f, 0.72f, 1f, 1f)),
			("最高DPS", top == null ? "-" : $"{top.Dps:N1}  {top.Player.PlayerName}", new Vector4(0.45f, 0.86f, 1f, 1f))
		];

		float width = Math.Max(150f, (ImGui.GetContentRegionAvail().X - 24f) / cards.Length);
		for (int index = 0; index < cards.Length; index++)
		{
			(string label, string value, Vector4 color) = cards[index];
			ImGui.PushID(index);
			ImGui.BeginChild("##OverlayMetric", new Vector2(width, 54f), true);
			ImGui.TextDisabled(label);
			ImGui.TextColored(color, value);
			ImGui.EndChild();
			ImGui.PopID();
			if (index < cards.Length - 1)
			{
				ImGui.SameLine();
			}
		}
	}

	private static void SetupColumns()
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
		ImGui.TableSetupColumn("D / Active %");
		ImGui.TableSetupColumn("H / Active %");
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

	private static string Percent(double rate)
	{
		return $"{rate * 100.0:0.00}%";
	}
}
