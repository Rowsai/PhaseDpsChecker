namespace PhaseDpsChecker;

public enum SummaryDisplayColumn
{
	Phase,
	Player,
	Start,
	End,
	Dps,
	Rdps,
	TotalDamage,
	Hps,
	TotalHealing,
	DamageTaken,
	HitCount,
	CriticalHitCount,
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
		SummaryDisplayColumn.TotalDamage,
		SummaryDisplayColumn.Hps,
		SummaryDisplayColumn.TotalHealing,
		SummaryDisplayColumn.DamageTaken,
		SummaryDisplayColumn.HitCount,
		SummaryDisplayColumn.CriticalHitCount,
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
		SummaryDisplayColumn.TotalDamage => "総ダメージ",
		SummaryDisplayColumn.Hps => "HPS",
		SummaryDisplayColumn.TotalHealing => "総回復量",
		SummaryDisplayColumn.DamageTaken => "被ダメージ",
		SummaryDisplayColumn.HitCount => "ヒット数",
		SummaryDisplayColumn.CriticalHitCount => "Crit数",
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
		SummaryDisplayColumn.Dps or SummaryDisplayColumn.Rdps or SummaryDisplayColumn.TotalDamage or
		SummaryDisplayColumn.Hps or SummaryDisplayColumn.TotalHealing or SummaryDisplayColumn.DamageTaken or
		SummaryDisplayColumn.HitCount or SummaryDisplayColumn.CriticalHitCount or SummaryDisplayColumn.Critical or
		SummaryDisplayColumn.DirectHit or SummaryDisplayColumn.CriticalDirectHit or
		SummaryDisplayColumn.MaximumDamage or SummaryDisplayColumn.Active or
		SummaryDisplayColumn.DamageActive or SummaryDisplayColumn.HealingActive;
}
