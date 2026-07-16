# Phase DPS Checker

Dalamud API 15 / .NET 10 向けの、フェーズ単位パーティ戦闘集計プラグインです。

![Phase DPS Checker icon](images/icon.png)

## インストール

Dalamudのカスタムプラグインリポジトリへ、次のURLを追加してください。

```text
https://raw.githubusercontent.com/Rowsai/Rowsai-Plugins/main/pluginmaster.json
```

## 機能

- パーティメンバーとペット（オーナーへ合算）の敵向けダメージを集計
- DoT / HoT の周期効果を取得し、付与元アクションへ合算
- 敵への最初のダメージからフェーズを開始
- アンカーとなる敵がターゲット不可になった時点、または戦闘終了時にフェーズを終了
- 全体表示: Phase、開始時間、終了時間、DPS、Crit%、DH%、Crit+DH%、最大ダメージ／アクション、Active%
- 個人表示: ウェポンスキル、アビリティ、魔法、回復魔法、回復アビリティ、その他の使用回数・ダメージ・回復量
- 全滅またはコンテンツクリア時に現在表示をクリアし、全フェーズを戦闘履歴へ自動保存
- 履歴表示タブで戦闘履歴と全体／個人を選択
- `/phasedps` でウィンドウを開閉
- PvP中は集計を自動停止

戦闘履歴はプラグインをアンロードするまでのメモリ内履歴です。

## 集計定義

- DPS: フェーズ内の敵向け総ダメージ ÷ フェーズ秒数
- Crit/DH/Crit+DH: 敵向けダメージ効果のヒット数を母数に算出
- 使用回数: ActionEffect 1件を1使用として算出（AoEの対象数では増えません）
- 回復量: パーティメンバーを対象とした回復効果の値を合算（オーバーヒール控除なし）
- Active%: `CooldownGroup == 58` の魔法／ウェポンスキルが占有する基本GCD区間を重複なく合算し、フェーズ時間で除算

## ビルド

前提は.NET 10 SDKとDalamud API 15です。

```powershell
dotnet build .\PhaseDpsChecker\PhaseDpsChecker.csproj -c Release
dotnet run --project .\PhaseDpsChecker.Tests\PhaseDpsChecker.Tests.csproj
```

既定以外の場所にDalamudがある場合は、`DALAMUD_HOME`へAPI 15のDLLフォルダを指定してください。

## 注意事項

戦闘データはチャット文字列ではなく、`FFXIVClientStructs`の`ActionEffectHandler.Receive`と`PacketDispatcher.HandleActorControlPacket`を読み取ります。ゲームパッチで内部構造やシグネチャが変わった場合は追随が必要です。

他メンバーの実スキルスピードは取得できないため、Active%はActionシートの基本リキャスト値を使う近似値です。

このプラグインはDPSパーサーに該当するため、Dalamud公式プラグインリポジトリの受け入れ対象外です。私有カスタムリポジトリでの利用を想定しています。

本プロジェクトの初期実装およびドキュメント作成には生成AIを使用しています。公開前にコードのレビュー、ビルド、集計ロジックの自動テストを実施しています。
