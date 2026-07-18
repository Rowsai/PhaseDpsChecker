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

	public static PhaseDetectionPreset Resolve(string contentName)
	{
		string normalized = contentName?.Trim() ?? string.Empty;
		return string.Equals(normalized, "絶妖星乱舞", System.StringComparison.Ordinal) ||
			string.Equals(normalized, "Futures Rewritten (Ultimate)", System.StringComparison.OrdinalIgnoreCase)
			? PhaseDetectionPreset.FuturesRewrittenUltimate
			: PhaseDetectionPreset.Normal;
	}
}
