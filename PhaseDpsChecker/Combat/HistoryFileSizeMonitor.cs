using System.IO;

namespace PhaseDpsChecker.Combat;

public enum HistoryFileSizeLevel
{
	Normal,
	Warning,
	Danger
}

public readonly record struct HistoryFileSizeStatus(long SizeBytes, HistoryFileSizeLevel Level);

internal static class HistoryFileSizeMonitor
{
	public const long WarningThresholdBytes = 500L * 1024 * 1024;
	public const long DangerThresholdBytes = 1024L * 1024 * 1024;

	public static HistoryFileSizeLevel GetLevel(long sizeBytes)
	{
		if (sizeBytes > DangerThresholdBytes)
		{
			return HistoryFileSizeLevel.Danger;
		}
		if (sizeBytes > WarningThresholdBytes)
		{
			return HistoryFileSizeLevel.Warning;
		}
		return HistoryFileSizeLevel.Normal;
	}

	public static HistoryFileSizeStatus Read(string filePath)
	{
		try
		{
			long sizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
			return new HistoryFileSizeStatus(sizeBytes, GetLevel(sizeBytes));
		}
		catch
		{
			return new HistoryFileSizeStatus(0, HistoryFileSizeLevel.Normal);
		}
	}
}
