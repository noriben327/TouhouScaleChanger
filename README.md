# TouhouScalePad

![TouhouScalePad running with Touhou](Assets/README/touhou-scalepad-demo.jpg)

TouhouScalePadは、東方原作などのWindows用ゲームを今の環境で遊びやすくするため補助ツールです。

固定サイズのゲームウィンドウを見やすい大きさへ自動拡大し、Xboxコントローラーの十字キー入力をゲームが受け取りやすいカーソルキー入力へ変換できます。

## ダウンロード

- [TouhouScalePad v0.2.2 / Windows x64 zip](https://github.com/noriben327/TouhouScalePad/releases/download/v0.2.2/TouhouScalePad-v0.2.2-win-x64.zip)

zipを展開して、`TouhouScalePad.exe` を起動してください。

## できること

- 東方原作などの固定サイズゲームウィンドウを指定したクライアントサイズへ自動拡大
- 4:3 / 16:9の拡大プリセットを内蔵
- ユーザー定義サイズの追加・削除
- Xboxコントローラーの十字キーをカーソルキーへ変換
- ゲーム名・拡大サイズ・D-pad変換ON/OFFをプロファイルとして保存
- 対象ゲームの起動を検出して、自動で拡大とD-pad変換を開始
- 対象ゲームが終了したら処理を停止
- 「タスクトレイに常駐させる」ボタンでタスクトレイへ格納

## 想定用途

- 東方原作をちょうどいい大きさの画面で遊びたい
- Xboxコントローラーの十字キーを使いたい
- ゲームごとに、ウィンドウサイズと入力設定を覚えさせたい

## 使い方

1. ゲームを起動します。
2. TouhouScalePadで「新規」→「起動中のウィンドウから選択」を押し、対象ゲームを選びます。
3. 拡大サイズと「Xboxコントローラーの十字キーをカーソルキーへ変換」のON/OFFを選びます。
4. 「プロファイルを保存」を押します。
5. 以後、TouhouScalePadを起動したまま対象ゲームを起動すると、自動で設定が適用されます。

`.exe` ファイルを直接参照して登録することもできます。

常駐させたい場合は、画面上部の「タスクトレイに常駐させる」ボタンを押してください。

## 配布形態

TouhouScalePadはzip配布を想定したポータブルアプリです。

設定は `TouhouScalePad.exe` と同じフォルダの `TouhouScalePad.settings.json` に保存されます。
アンインストールするときは、展開したTouhouScalePadフォルダを削除してください。

## ライセンス

TouhouScalePadのソースコードはMIT Licenseで公開しています。

このリポジトリ内の東方Projectゲーム画面を含むスクリーンショットは、動作例を示すためのものであり、MIT Licenseの対象外です。東方Projectおよび各作品の権利は、それぞれの権利者に帰属します。TouhouScalePadは東方Project公式ツールではありません。

## 製作
Codex(GPT-5.5)を使用して製作しました
