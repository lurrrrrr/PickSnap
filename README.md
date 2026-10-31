<p align="center">
  <img src="src/assets/PickSnap.png" width="72" height="72" alt="拾帧图标">
</p>

<h1 align="center">拾帧 · PickSnap</h1>

<p align="center">把截图和复制过的文字挂在屏幕顶边的一条绳子上。<br>要用的时候让它滑下来，取走、换个位置，或者干脆摊在桌面上。</p>

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><strong>下载最新版本</strong></a>
  ·
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.zh-TW.md">繁體中文</a> ·
  <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><img src="https://img.shields.io/github/v/release/lurrrrrr/PickSnap?style=flat-square&label=release&color=2563eb" alt="最新版本"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4?style=flat-square" alt="Windows 10 和 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-475569?style=flat-square" alt="MIT 许可"></a>
</p>

![屏幕顶边挂着的几张截图，右侧是图片篮子与文字信封](docs/assets/shelf-light.png)

---

## 一颗小夹子，三种用法

绳上的每张截图，头顶都别着一颗铝夹。它不是装饰，是这张图唯一的把手——复制、钉到桌面、换位置，全在这一颗夹子上。

![按住图片上方的小夹子左右拖动，自由调整它在绳上的位置](docs/assets/clip-drag.gif)

| 手势 | 结果 |
| --- | --- |
| 单击图片本体 | 复制到剪贴板 |
| 按住图片拖到其他窗口 | 以图片文件投放，原图仍留在绳上 |
| 单击图片上方的小夹子 | 脱离绳子、落到桌面，变成一张可缩放、可移动的贴图 |
| **按住小夹子左右拖动** | **自由调整它在绳上的位置** |

拖动之后的新顺序会被记住，下一次启动还在原处。

图片挂多了也看得过来：滚轮横向浏览整条绳子，把图片拖到绳子两端时绳子会连续滚动，停下时自动对齐，图片不会卡在边缘只露出一半。

![滚轮横向浏览整条绳子](docs/assets/rope-scroll.gif)

---

## 功能

**收集**

- 监听 Windows 截图文件夹，新截图自动挂上绳子
- 剪贴板里的图片和文字也能收集（图片、文字分别开关）
- 手动「添加图片」，本机已有的图片也能挂上去

**挂在绳上**

- 单击复制到剪贴板；按住图片拖到其他窗口，会以图片文件投放
- 长按进入标注：画框、箭头、涂画、写字、裁剪、旋转，全程本机处理
- 滚轮横向浏览，绳子两端的箭头也可翻页；把图片拖到绳子两端时绳子连续滚动
- 容量按屏幕宽度计算（最少 24 张），挂不下时最早的一张自动退回篮子
- 右键菜单：打开、编辑、保存副本、打开位置、取下放篮子

**钉在桌面上**

单击图片顶部的小夹子（按下即松），图片脱离绳子、滑落为一个置顶窗口，可缩放、可移动。鼠标移到贴图顶部中央会浮现图钉按钮，单击后挂回绳上原处。

**图片篮子**

暂时不用的截图收在这里，支持点选与框选，选中后一次挂回绳子。绳上的图片拖到右端的篮子图标，或直接拖进打开着的篮子窗口，即可取下。

![把图片拖到右侧的篮子上，从绳上取下](docs/assets/drop-basket.gif)

篮子窗口顶部的「自动清理 7 天前的旧图」默认关闭，打开后超过 7 天的截图会自动移入回收站（详见下方常见问题）。

**文字信封**

剪贴板里的文字自动收进来，支持全文搜索与一键复制，换行与缩进都会保留。删除后 5 秒内可点「撤销」放回原来的位置，只保留最近 100 条。

<p align="center"><img src="docs/assets/text-window.png" width="330" alt="文字信封：搜索框、文字记录、复制与删除，删除后可撤销"></p>

**区域截图**

`Ctrl+Alt+S` 框选屏幕，就地画矩形、圆形、箭头、画笔、文字、马赛克，`Enter` 完成，`Esc` 取消。

**外观**

五套绳子（经典银灰、棉麻木夹、奶白轻盈、柔彩日常、石墨金属），配色可跟随系统，也可固定为浅色或深色。多显示器可固定某一屏幕，或跟随鼠标所在屏幕。

<details>
<summary>深色模式</summary>

![深色模式下的挂绳](docs/assets/shelf-dark.png)

</details>

---

## 安装

### 一、下载安装包（推荐）

1. 打开 [Releases](https://github.com/lurrrrrr/PickSnap/releases/latest)，下载 `PickSnap-版本号-windows-x64.zip`
2. 解压到任意位置，双击 `Install.cmd`
3. 安装程序会把文件放到 `%LOCALAPPDATA%\Programs\PickSnap`，创建桌面快捷方式并直接启动

不需要管理员权限，不安装系统服务。卸载时从 Windows「设置 → 应用」里移除，卸载会一并清理已收集的截图与设置。

不使用 `Install.cmd` 也可以直接运行：双击 `app\PickSnap.exe`，首次启动会在桌面创建快捷方式，之后可在托盘「更多操作 → 在桌面创建快捷方式」随时重建。

**系统要求**：Windows 10 / 11 64 位；.NET Framework 4.8（Win10 1809 之后的系统自带）；内置编辑器依赖 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win11 自带，部分 Win10 需单独安装，未安装时区域截图内的标注仍可使用）。

### 二、从源码编译

```powershell
git clone https://github.com/lurrrrrr/PickSnap.git
cd PickSnap
./scripts/build.ps1
```

编译产物在 `build\`，运行 `build\PickSnap.exe` 即可；生成发布压缩包用 `./scripts/build.ps1 -Package`。

---

## 上手三步

1. **截一张**：`Win+Shift+S`、`PrtScn` 或微信 `Alt+A`，截完无需另存
2. **滑下来**：鼠标移到屏幕最上沿，绳子带着截图滑下来
3. **取走**：单击复制，或直接拖进聊天框、文档、文件夹

<details>
<summary>快捷键与常用操作</summary>

| 操作 | 方式 |
| --- | --- |
| 显示 / 收起挂绳 | `Ctrl+Alt+T`，或单击托盘图标 |
| 区域截图 | `Ctrl+Alt+S`，或点托盘面板顶部的「区域截图」 |
| 修改截图快捷键 | 托盘「更多操作」→「截图快捷键」 |
| 复制图片 | 单击图片 |
| 打开原图 | 双击图片 |
| 调整在绳上的位置 | 按住图片顶部的小夹子左右拖动 |
| 钉到桌面 / 挂回绳子 | 单击夹子 / 单击贴图顶部的图钉 |
| 标注 | 长按图片，或右键 →「编辑」 |
| 取下放篮子 | 右键 →「取下放篮子」，或拖到绳子右端的篮子图标 |
| 打开图片篮子 | 单击绳子右端的篮子 |
| 打开文字信封 | 单击绳子右端的信封 |
| 选择屏幕 | 托盘右键 →「显示屏幕」 |
| 外观、开机启动、检查更新 | 托盘右键 |

</details>

---

## 数据与隐私

所有内容都保存在本机：图片存放在程序目录下的 `data\Captures`，设置与文字记录在 `data\state.json`。不上传任何内容；仅在手动触发「检查更新」时读取 GitHub Releases 的版本信息。

图片不会自动过期：绳子上的展示位置有限，挂不下的会自动退回篮子，文件仍在本地，何时清理由用户决定（可选的「旧图自动清理」也只是把过期的副本送进回收站）。文字信封只保留最近 100 条，更早的文字会被新内容顶掉。

---

## 常见问题

<details>
<summary>截图没有自动出现？</summary>

默认只监听 Windows 的截图文件夹。`Win+Shift+S`、`PrtScn`、微信截图这类把图片放进剪贴板的方式，需要先在托盘面板 →「剪贴板收集」里打开「收集图片」。

如果关闭了「新图片自动上绳」，新截图会先进入图片篮子，选中后点「挂上绳子」即可。

</details>

<details>
<summary>「取下」和「删除」有什么区别？</summary>

「取下放篮子」只是不挂在绳子上，文件与记录都还在，随时能挂回来。删除会让收集的副本进入回收站；对于截图文件夹里的原始截图，只移除拾帧的记录，不改动源文件。

</details>

<details>
<summary>拖动到两端停不下来怎么办？</summary>

把光标拖到绳子左右边缘，绳子会连续横向滚动；图片回到视野内后松手即可，滚动随即停止，并自动对齐到一张图片的边缘。

</details>

<details>
<summary>图片会自动清理吗？</summary>

默认**不会**自动删除任何图片。

如果你想让它替你打扫：打开图片篮子，标题栏右侧有一个开关「自动清理 7 天前的旧图」（默认关闭）。开启时会立即检查一次，之后每次启动、每 6 小时各检查一次，超过 7 天的图片进入回收站——先到回收站，需要时还能捞回来；手动添加、不在收集文件夹里的原图只移除记录，不删文件。是否新旧看文件当前的修改时间，标注过的图不会被误清。随时回到同一个开关即可关掉。

</details>

<details>
<summary>支持哪些图片格式？</summary>

PNG、JPG、BMP、GIF、TIF。GIF 与 TIF 只取第一帧用于编辑，裁剪、旋转、马赛克作用于整张图。

</details>

<details>
<summary>容量是多大？满了之后会怎样？</summary>

绳子的容量按屏幕宽度计算，最少 24 张，挂不下时多余的图留在篮子里等待，不会被丢弃。

文字信封只保留最近 100 条，更早的文字会被新内容自动顶掉——文字是零碎的剪贴板内容，滚动清理即可。

图片篮子超过 **100 张**后，每收到一张新截图会提示一次；拾帧**不会自动删除任何图片**——删哪些、什么时候删完全由用户决定：打开图片篮子多选删除即可，删除的副本都会进入回收站。

</details>

<details>
<summary>怎么更新？</summary>

托盘右键 →「更多操作」→「检查更新」，下载完成后重启即可。图片、设置与开机启动项都会保留。

</details>

---

## 开发

C# + WPF（.NET Framework 4.8），使用 `csc` 直接编译，无 MSBuild 依赖。

```powershell
./scripts/build.ps1          # 编译
./scripts/build.ps1 -Package # 编译并生成发布用 zip
```

源码在 `src\`，第三方组件在 `vendor\`，构建与打包脚本在 `scripts\`。

---

## 致谢与许可

本项目基于以下开源项目修改而来，感谢原作者：

- [Tendedero](https://github.com/alejandrobujan/tendedero)：macOS 原始项目，作者 [Alejandro Buján](https://github.com/alejandrobujan)
- [PicRope](https://github.com/Renly1994/PicRope)：Windows 移植版，作者 [Renly1994](https://github.com/Renly1994)

原始版权归 Alejandro Buján 所有，以 MIT 许可发布，本仓库的修改部分同样使用 MIT。按上游许可中的「名称与图标」条款，本项目使用独立名称「拾帧 / PickSnap」与自行设计的图标。完整的来源链条与第三方组件声明见 [NOTICE](NOTICE)，许可条款见 [LICENSE](LICENSE)。
