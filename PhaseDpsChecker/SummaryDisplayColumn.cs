namespace PhaseDpsChecker;

public enum SummaryDisplayColumn
{
	Phase,
	Player,
	Start,
	End,
	Dps,
	Rdps,
	Critical,
	DirectHit,
	CriticalDirectHit,
	MaximumDamage,
	Active,
	DamageActive,
	HealingActive,
}

public static class SummaryDisplayColumnCatalog
{
	public static readonly SummaryDisplayColumn[] All =
	[
		SummaryDisplayColumn.Phase,
		SummaryDisplayColumn.Player,
		SummaryDisplayColumn.Start,
		SummaryDisplayColumn.End,
		SummaryDisplayColumn.Dps,
		SummaryDisplayColumn.Rdps,
		SummaryDisplayColumn.Critical,
		SummaryDisplayColumn.DirectHit,
		SummaryDisplayColumn.CriticalDirectHit,
		SummaryDisplayColumn.MaximumDamage,
		SummaryDisplayColumn.Active,
		SummaryDisplayColumn.DamageActive,
		SummaryDisplayColumn.HealingActive,
	];

	public static string DisplayName(this SummaryDisplayColumn column) => column switch
	{
		SummaryDisplayColumn.Phase => "Phase",
		SummaryDisplayColumn.Player => "プレイヤー名",
		SummaryDisplayColumn.Start => "開始時間",
		SummaryDisplayColumn.End => "終了時間",
		SummaryDisplayColumn.Dps => "DPS",
		SummaryDisplayColumn.Rdps => "rDPS",
		SummaryDisplayColumn.Critical => "Crit %",
		SummaryDisplayColumn.DirectHit => "DH %",
		SummaryDisplayColumn.CriticalDirectHit => "Crit + DH %",
		SummaryDisplayColumn.MaximumDamage => "最大ダメージ / アクション",
		SummaryDisplayColumn.Active => "Active %",
		SummaryDisplayColumn.DamageActive => "D / Active %",
		SummaryDisplayColumn.HealingActive => "H / Active %",
		_ => column.ToString(),
	};

	public static bool DefaultDescending(this SummaryDisplayColumn column) => column is
		SummaryDisplayColumn.Dps or SummaryDisplayColumn.Rdps or SummaryDisplayColumn.Critical or
		SummaryDisplayColumn.DirectHit or SummaryDisplayColumn.CriticalDirectHit or
		SummaryDisplayColumn.MaximumDamage or SummaryDisplayColumn.Active or
		SummaryDisplayColumn.DamageActive or SummaryDisplayColumn.HealingActive;
}
