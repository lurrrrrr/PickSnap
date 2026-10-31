<p align="center">
  <img src="../../src/assets/PickSnap.png" width="72" height="72" alt="拾幀圖示">
</p>

<h1 align="center">拾幀 · PickSnap</h1>

<p align="center"><strong>晾衣繩式剪貼收納</strong>，把螢幕截圖和複製過的文字掛在螢幕頂邊，隨手取用，輕鬆留存、對照、編輯。</p>

![螢幕頂邊掛著的幾張截圖，右側是圖片籃子與文字信封](../../docs/assets/shelf-light.png)

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><strong>下載最新版本 ›</strong></a>
</p>


<p align="center">
  <a href="../../README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  繁體中文 ·
  <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><img src="https://img.shields.io/github/v/release/lurrrrrr/PickSnap?style=flat-square&label=release&color=2563eb" alt="最新版本"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4?style=flat-square" alt="Windows 10 和 11">
  <a href="../../LICENSE"><img src="https://img.shields.io/badge/license-MIT-475569?style=flat-square" alt="MIT 授權"></a>
</p>

---

## 一顆小夾子，三種用法

繩上的每張截圖，頭頂都別著一顆鋁夾。它不是裝飾，是這張圖唯一的把手——複製、釘到桌面、換位置，全在這一顆夾子上。

![按住圖片上方的小夾子左右拖動，自由調整它在繩上的位置](../../docs/assets/clip-drag.gif)

| 手勢 | 結果 |
| --- | --- |
| 按一下圖片本體 | 複製到剪貼簿 |
| 按住圖片拖到其他視窗 | 以圖片檔案投放，原圖仍留在繩上 |
| 按一下圖片上方的小夾子 | 脫離繩子、落到桌面，變成可縮放、可移動的貼圖 |
| **按住小夾子左右拖動** | **自由調整它在繩上的位置** |

拖動之後的新順序會被記住，下一次啟動還在原處。

圖片掛多了也看得過來：滾輪橫向瀏覽整條繩子，把圖片拖到繩子兩端時繩子會連續捲動，停下時自動對齊，圖片不會卡在邊緣只露出一半。

![滾輪橫向瀏覽整條繩子](../../docs/assets/rope-scroll.gif)

---

## 功能

**收集**

- 監聽 Windows 截圖資料夾，新截圖自動掛上繩子
- 剪貼簿裡的圖片和文字也能收集（圖片、文字分別開關）
- 手動「新增圖片」，本機已有的圖片也能掛上去

**掛在繩上**

- 按一下複製到剪貼簿；按住圖片拖到其他視窗，會以圖片檔案投放
- 長按進入標註：畫框、箭頭、塗畫、寫字、裁切、旋轉，全程本機處理
- 滾輪橫向瀏覽，繩子兩端的箭頭也可翻頁；把圖片拖到繩子兩端時繩子連續捲動
- 容量按螢幕寬度計算（最少 24 張），掛不下時最早的一張自動退回籃子
- 右鍵選單：開啟、編輯、儲存副本、開啟位置、取下放籃子

**釘在桌面上**

按一下圖片頂部的小夾子（按下即鬆），圖片脫離繩子、滑落為一個置頂視窗，可縮放、可移動。滑鼠移到貼圖頂部中央會浮現圖釘按鈕，按一下後掛回繩上原處。

**圖片籃子**

暫時用不到的截圖收在這裡，支援點選與框選，選取後一次掛回繩子。繩上的圖片拖到右端的籃子圖示，或直接拖進開啟中的籃子視窗，即可取下。

![把圖片拖到右側的籃子上，從繩上取下](../../docs/assets/drop-basket.gif)

籃子視窗頂部的「自動清理 7 天前的舊圖」預設關閉，開啟後超過 7 天的截圖會自動移入回收站（詳見下方常見問題）。

**文字信封**

剪貼簿裡的文字自動收進來，支援全文搜尋與一鍵複製，換行與縮排都會保留。刪除後 5 秒內可按「復原」放回原來的位置，只保留最近 100 則。

<p align="center"><img src="../../docs/assets/text-window.png" width="330" alt="文字信封：搜尋框、文字記錄、複製與刪除，刪除後可復原"></p>

**區域截圖**

`Ctrl+Alt+S` 框選螢幕，就地畫矩形、圓形、箭頭、畫筆、文字、馬賽克，`Enter` 完成，`Esc` 取消。

**外觀**

五套繩子（經典銀灰、棉麻木夾、奶白輕盈、柔彩日常、石墨金屬），配色可跟隨系統，也可固定為淺色或深色。多螢幕可固定某一螢幕，或跟隨滑鼠所在螢幕。

<details>
<summary>深色模式</summary>

![深色模式下的掛繩](../../docs/assets/shelf-dark.png)

</details>

---

## 安裝

### 一、下載安裝包（建議）

1. 開啟 [Releases](https://github.com/lurrrrrr/PickSnap/releases/latest)，下載 `PickSnap-版本號-windows-x64.zip`
2. 解壓到任意位置，雙擊 `Install.cmd`
3. 安裝程式會把檔案放到 `%LOCALAPPDATA%\Programs\PickSnap`，建立桌面捷徑並直接啟動

不需要管理員權限，不安裝系統服務。解除安裝時從 Windows「設定 → 應用程式」移除，解除安裝會一併清理已收集的截圖與設定。

不使用 `Install.cmd` 也可以直接執行：雙擊 `app\PickSnap.exe`，首次啟動會在桌面建立捷徑，之後可在匣「更多操作 → 在桌面建立捷徑」隨時重建。

**系統需求**：Windows 10 / 11 64 位元；.NET Framework 4.8（Win10 1809 之後的系統內建）；內建編輯器依賴 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win11 內建，部分 Win10 需另外安裝，未安裝時區域截圖內的標註仍可使用）。

### 二、從原始碼編譯

```powershell
git clone https://github.com/lurrrrrr/PickSnap.git
cd PickSnap
./scripts/build.ps1
```

原始碼在 `src\`，第三方元件在 `vendor\`，建置與打包指令碼在 `scripts\`。編譯產物在 `build\`，執行 `build\PickSnap.exe` 即可；產生發布壓縮包用 `./scripts/build.ps1 -Package`。

---

## 上手三步

1. **截一張**：`Win+Shift+S`、`PrtScn` 或微信 `Alt+A`，截完無須另存
2. **滑下來**：滑鼠移到螢幕最上沿，繩子帶著截圖滑下來
3. **取走**：按一下複製，或直接拖進聊天視窗、文件、資料夾

<details>
<summary>快捷鍵與常用操作</summary>

| 操作 | 方式 |
| --- | --- |
| 顯示 / 收起掛繩 | `Ctrl+Alt+T`，或按一下匣圖示 |
| 區域截圖 | `Ctrl+Alt+S`，或點匣面板頂部的「區域截圖」 |
| 修改截圖快捷鍵 | 匣「更多操作」→「截圖快捷鍵」 |
| 複製圖片 | 按一下圖片 |
| 開啟原圖 | 按兩下圖片 |
| 調整在繩上的位置 | 按住圖片頂部的小夾子左右拖動 |
| 釘到桌面 / 掛回繩子 | 按一下夾子 / 按一下貼圖頂部的圖釘 |
| 標註 | 長按圖片，或右鍵 →「編輯」 |
| 取下放籃子 | 右鍵 →「取下放籃子」，或拖到繩子右端的籃子圖示 |
| 開啟圖片籃子 | 按一下繩子右端的籃子 |
| 開啟文字信封 | 按一下繩子右端的信封 |
| 選擇螢幕 | 匣右鍵 →「顯示螢幕」 |
| 外觀、開機啟動、檢查更新 | 匣右鍵 |

</details>

---

## 資料與隱私

所有內容都儲存在本機：圖片存放在程式目錄下的 `data\Captures`，設定與文字記錄在 `data\state.json`。不上傳任何內容；僅在手動觸發「檢查更新」時讀取 GitHub Releases 的版本資訊。

圖片不會自動過期：繩子上的展示位置有限，掛不下的會自動退回籃子，檔案仍在本機，何時清理由使用者決定（選用的「舊圖自動清理」也只是把過期的副本送進回收站）。文字信封只保留最近 100 則，更早的文字會被新內容擠掉。

---

## 常見問題

<details>
<summary>截圖沒有自動出現？</summary>

預設只監聽 Windows 的截圖資料夾。`Win+Shift+S`、`PrtScn`、微信截圖這類把圖片放進剪貼簿的方式，需要先在匣面板 →「剪貼簿收集」裡開啟「收集圖片」。

如果關閉了「新圖片自動上繩」，新截圖會先進入圖片籃子，選取後點「掛上繩子」即可。

</details>

<details>
<summary>「取下」和「刪除」有什麼差別？</summary>

「取下放籃子」只是不掛在繩子上，檔案與記錄都還在，隨時能掛回來。刪除會讓收集的副本進入回收站；對於截圖資料夾裡的原始截圖，只移除拾幀的記錄，不改動來源檔案。

</details>

<details>
<summary>拖動到兩端停不下來怎麼辦？</summary>

把游標拖到繩子左右邊緣，繩子會連續橫向捲動；圖片回到視野內後鬆手即可，捲動隨即停止，並自動對齊到一張圖片的邊緣。

</details>

<details>
<summary>圖片會自動清理嗎？</summary>

預設**不會**自動刪除任何圖片。

如果你想讓它替你打掃：開啟圖片籃子，標題列右側有一個開關「自動清理 7 天前的舊圖」（預設關閉）。開啟時會立即檢查一次，之後每次啟動、每 6 小時各檢查一次，超過 7 天的圖片進入回收站——先到回收站，需要時還能撈回來；手動新增、不在收集資料夾裡的原圖只移除記錄，不刪檔案。是否新舊看檔案目前的修改時間，標註過的圖不會被誤清。隨時回到同一個開關即可關閉。

</details>

<details>
<summary>支援哪些圖片格式？</summary>

PNG、JPG、BMP、GIF、TIF。GIF 與 TIF 只取第一幀用於編輯，裁切、旋轉、馬賽克作用於整張圖。

</details>

<details>
<summary>容量多大？滿了之後會怎樣？</summary>

繩子的容量按螢幕寬度計算，最少 24 張，掛不下時多餘的圖留在籃子裡等待，不會被丟棄。

文字信封只保留最近 100 則，更早的文字會被新內容自動擠掉——文字是零碎的剪貼簿內容，滾動清理即可。

圖片籃子超過 **100 張**後，每收到一張新截圖會提示一次；拾幀**不會自動刪除任何圖片**——刪哪些、什麼時候刪完全由使用者決定：開啟圖片籃子多選刪除即可，刪除的副本都會進入回收站。

</details>

<details>
<summary>怎麼更新？</summary>

匣右鍵 →「更多操作」→「檢查更新」，下載完成後重新啟動即可。圖片、設定與開機啟動項都會保留。

</details>

---

## 致謝與授權

本專案基於以下開源專案修改而來，感謝原作者：

- [Tendedero](https://github.com/alejandrobujan/tendedero)：macOS 原始專案，作者 [Alejandro Buján](https://github.com/alejandrobujan)
- [PicRope](https://github.com/Renly1994/PicRope)：Windows 移植版，作者 [Renly1994](https://github.com/Renly1994)

原始版權歸 Alejandro Buján 所有，以 MIT 授權發布，本儲存庫的修改部分同樣使用 MIT。依上游授權中的「名稱與圖示」條款，本專案使用獨立名稱「拾幀 / PickSnap」與自行設計的圖示。完整的來源脈絡與第三方元件聲明見 [NOTICE](../../NOTICE)，授權條款見 [LICENSE](../../LICENSE)。
