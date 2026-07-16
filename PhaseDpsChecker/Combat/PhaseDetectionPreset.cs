namespace PhaseDpsChecker.Combat;

public enum PhaseDetectionPreset
{
	Normal = 0,
	FuturesRewrittenUltimate = 1,
}

public static class PhaseDetectionPresetCatalog
{
	public static readonly PhaseDetectionPreset[] All =
	[
		PhaseDetectionPreset.FuturesRewrittenUltimate,
	];

	public static string DisplayName(this PhaseDetectionPreset preset) => preset switch
	{
		PhaseDetectionPreset.FuturesRewrittenUltimate => "絶妖星乱舞",
		_ => "未選択（通常コンテンツ）",
	};
}
