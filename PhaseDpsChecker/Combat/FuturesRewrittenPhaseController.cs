using System;

namespace PhaseDpsChecker.Combat;

public enum FuturesRewrittenStage
{
	WaitingForPhase1,
	Phase1,
	WaitingForPhase2,
	Phase2,
	WaitingForPhase3,
	Phase3,
	WaitingForPhase4,
	Phase4,
	WaitingForPhase5Dialogue,
	WaitingForPhase5Targetable,
	Phase5,
	Completed,
}

public enum DedicatedPhaseCommand
{
	None,
	Start,
	End,
}

public readonly record struct DedicatedPhaseTransition(DedicatedPhaseCommand Command, int PhaseNumber)
{
	public static DedicatedPhaseTransition None => new(DedicatedPhaseCommand.None, 0);
}

public sealed class FuturesRewrittenPhaseController
{
	private const string Phase1EndDialogue = "お前たち、「初めて」じゃないな？ナルホド……さては……";
	private const string Phase2StartDialogue = "絶ッ！！再現したな、この私を……！";
	private const string Phase3StartDialogue = "ボクチンに不可能はなーい！";
	private const string Phase3EndBattleLog = "エクスデスは「メテオ」を中断した。";
	private const string Phase5StartDialogue = "私は破壊し続けよう！";

	private bool phase2SawEnemy;

	private int phase4DokiDokiUltimaCompletions;

	public FuturesRewrittenStage Stage { get; private set; } = FuturesRewrittenStage.WaitingForPhase1;

	public string StatusLabel => Stage switch
	{
		FuturesRewrittenStage.WaitingForPhase1 => "Phase 1 開始待ち",
		FuturesRewrittenStage.Phase1 => "Phase 1 計測中",
		FuturesRewrittenStage.WaitingForPhase2 => "Phase 2 開始台詞待ち",
		FuturesRewrittenStage.Phase2 => "Phase 2 計測中",
		FuturesRewrittenStage.WaitingForPhase3 => "Phase 3 開始台詞待ち",
		FuturesRewrittenStage.Phase3 => "Phase 3 計測中",
		FuturesRewrittenStage.WaitingForPhase4 => "Phase 4 初回攻撃待ち",
		FuturesRewrittenStage.Phase4 => $"Phase 4 計測中（どきどきアルテマ {phase4DokiDokiUltimaCompletions}/2）",
		FuturesRewrittenStage.WaitingForPhase5Dialogue => "Phase 5 開始台詞待ち",
		FuturesRewrittenStage.WaitingForPhase5Targetable => "Phase 5 ケフカ出現待ち",
		FuturesRewrittenStage.Phase5 => "Phase 5 計測中",
		_ => "計測完了",
	};

	public void Reset()
	{
		Stage = FuturesRewrittenStage.WaitingForPhase1;
		phase2SawEnemy = false;
		phase4DokiDokiUltimaCompletions = 0;
	}

	public DedicatedPhaseTransition OnCombatStarted()
	{
		if (Stage != FuturesRewrittenStage.WaitingForPhase1)
		{
			return DedicatedPhaseTransition.None;
		}

		Stage = FuturesRewrittenStage.Phase1;
		return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 1);
	}

	public DedicatedPhaseTransition OnFirstPartyAttack()
	{
		if (Stage == FuturesRewrittenStage.WaitingForPhase1)
		{
			Stage = FuturesRewrittenStage.Phase1;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 1);
		}

		if (Stage == FuturesRewrittenStage.WaitingForPhase4)
		{
			Stage = FuturesRewrittenStage.Phase4;
			phase4DokiDokiUltimaCompletions = 0;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 4);
		}

		return DedicatedPhaseTransition.None;
	}

	public DedicatedPhaseTransition OnDialogue(string message)
	{
		string normalized = Normalize(message);
		if (Stage == FuturesRewrittenStage.Phase1 && normalized.Contains(Normalize(Phase1EndDialogue), StringComparison.Ordinal))
		{
			Stage = FuturesRewrittenStage.WaitingForPhase2;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 1);
		}

		if (Stage == FuturesRewrittenStage.WaitingForPhase2 && normalized.Contains(Normalize(Phase2StartDialogue), StringComparison.Ordinal))
		{
			Stage = FuturesRewrittenStage.Phase2;
			phase2SawEnemy = false;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 2);
		}

		if (Stage == FuturesRewrittenStage.WaitingForPhase3 && normalized.Contains(Normalize(Phase3StartDialogue), StringComparison.Ordinal))
		{
			Stage = FuturesRewrittenStage.Phase3;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 3);
		}

		if (Stage == FuturesRewrittenStage.Phase3 && normalized.Contains(Normalize(Phase3EndBattleLog), StringComparison.Ordinal))
		{
			Stage = FuturesRewrittenStage.WaitingForPhase4;
			return new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 3);
		}

		if (Stage == FuturesRewrittenStage.WaitingForPhase5Dialogue && normalized.Contains(Normalize(Phase5StartDialogue), StringComparison.Ordinal))
		{
			Stage = FuturesRewrittenStage.WaitingForPhase5Targetable;
		}

		return DedicatedPhaseTransition.None;
	}

	public DedicatedPhaseTransition OnKefkaTargetable()
	{
		if (Stage != FuturesRewrittenStage.WaitingForPhase5Targetable)
		{
			return DedicatedPhaseTransition.None;
		}

		Stage = FuturesRewrittenStage.Phase5;
		return new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 5);
	}

	public DedicatedPhaseTransition OnEnemyListState(bool isEmpty)
	{
		if (Stage != FuturesRewrittenStage.Phase2)
		{
			return DedicatedPhaseTransition.None;
		}

		if (!isEmpty)
		{
			phase2SawEnemy = true;
			return DedicatedPhaseTransition.None;
		}

		if (!phase2SawEnemy)
		{
			return DedicatedPhaseTransition.None;
		}

		Stage = FuturesRewrittenStage.WaitingForPhase3;
		return new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 2);
	}

	public DedicatedPhaseTransition OnDokiDokiUltimaCompleted()
	{
		if (Stage != FuturesRewrittenStage.Phase4)
		{
			return DedicatedPhaseTransition.None;
		}

		phase4DokiDokiUltimaCompletions++;
		if (phase4DokiDokiUltimaCompletions < 2)
		{
			return DedicatedPhaseTransition.None;
		}

		Stage = FuturesRewrittenStage.WaitingForPhase5Dialogue;
		return new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 4);
	}

	public DedicatedPhaseTransition OnDutyCompleted()
	{
		if (Stage != FuturesRewrittenStage.Phase5)
		{
			return DedicatedPhaseTransition.None;
		}

		Stage = FuturesRewrittenStage.Completed;
		return new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 5);
	}

	private static string Normalize(string value) => value
		.Replace(" ", string.Empty, StringComparison.Ordinal)
		.Replace("　", string.Empty, StringComparison.Ordinal)
		.Replace("\r", string.Empty, StringComparison.Ordinal)
		.Replace("\n", string.Empty, StringComparison.Ordinal)
		.Trim('「', '」', '『', '』', '“', '”', '"');
}
