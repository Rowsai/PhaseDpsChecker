using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace PhaseDpsChecker.Combat;

internal static unsafe class EnemyListReader
{
	public static bool TryIsEmpty(out bool isEmpty)
	{
		UIState* uiState = UIState.Instance();
		if (uiState == null)
		{
			isEmpty = false;
			return false;
		}

		isEmpty = uiState->Hater.HaterCount <= 0;
		return true;
	}
}
