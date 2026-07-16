using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

internal static class DefensiveStatusFilter
{
	private static readonly HashSet<string> AllowedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		// Tank role and tank job mitigation.
		"Rampart", "ランパート", "Reprisal", "リプライザル", "Arm's Length", "アームズレングス",
		"Sentinel", "センチネル", "Guardian", "ガーディアン", "Bulwark", "ブルワーク",
		"Sheltron", "シェルトロン", "Holy Sheltron", "ホーリーシェルトロン",
		"Knight's Resolve", "ナイトの堅守", "Knight's Benediction", "ナイトの加護",
		"Intervention", "インターベンション", "Divine Veil", "ディヴァインヴェール",
		"Passage of Arms", "パッセージ・オブ・アームズ", "Hallowed Ground", "インビンシブル",
		"Vengeance", "ヴェンジェンス", "Damnation", "ダムネーション", "Thrill of Battle", "スリル・オブ・バトル",
		"Raw Intuition", "原初の直感", "Bloodwhetting", "原初の血気", "Nascent Flash", "原初の猛り",
		"Shake It Off", "シェイクオフ", "Holmgang", "ホルムギャング",
		"Shadow Wall", "シャドウウォール", "Shadowed Vigil", "シャドウヴィジル", "Dark Mind", "ダークマインド",
		"The Blackest Night", "ブラックナイト", "Oblation", "オブレーション", "Dark Missionary", "ダークミッショナリー",
		"Living Dead", "リビングデッド", "Walking Dead", "ウォーキングデッド", "Undead Rebirth", "アンデッド・リバース",
		"Camouflage", "カモフラージュ", "Nebula", "ネビュラ", "Great Nebula", "グレートネビュラ",
		"Heart of Stone", "ハート・オブ・ストーン", "Heart of Corundum", "ハート・オブ・コランダム",
		"Clarity of Corundum", "クルーザーコルベット", "Catharsis of Corundum", "コランダムの清心",
		"Heart of Light", "ハート・オブ・ライト", "Superbolide", "ボーライド",

		// Party mitigation and personal defensive abilities.
		"Troubadour", "トルバドゥール", "Tactician", "タクティシャン", "Shield Samba", "守りのサンバ",
		"Magick Barrier", "マジックバリア", "Manaward", "マバリア", "Radiant Aegis", "守りの光",
		"Shade Shift", "残影", "Riddle of Earth", "金剛の極意", "Third Eye", "心眼", "Arcane Crest", "アルケインクレスト",

		// Healer mitigation, shields and the requested ground-healing effects.
		"Asylum", "アサイラム", "Sacred Soil", "野戦治療の陣",
		"Temperance", "テンパランス", "Aquaveil", "アクアヴェール", "Divine Benison", "ディヴァインベニゾン",
		"Divine Caress", "ディヴァインカレス",
		"Galvanize", "鼓舞", "Catalyze", "激励", "Seraphic Veil", "セラフィックヴェール",
		"Fey Illumination", "フェイイルミネーション", "Consolation", "コンソレイション", "Expedience", "疾風怒濤",
		"Protraction", "プロトラクション", "Collective Unconscious", "運命の輪", "Wheel of Fortune", "運命の輪",
		"Celestial Intersection", "星天交差", "Neutral Sect Shield", "ニュートラルセクト［障壁］", "Exaltation", "エグザルテーション",
		"Kerachole", "ケーラコレ", "Taurochole", "タウロコレ",
		"Holos", "ホーリズム", "Haima", "ハイマ", "Panhaima", "パンハイマ",
		"Eukrasian Diagnosis", "エウクラシア・ディアグノシス",
		"Eukrasian Prognosis", "エウクラシア・プログノシス", "Differential Diagnosis", "ディファレンシャル・ディアグノシス"
	};

	public static bool IsAllowed(string statusName)
	{
		return !string.IsNullOrWhiteSpace(statusName) && AllowedNames.Contains(statusName.Trim());
	}
}
