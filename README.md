# 物品详情美化 / Better Item Tooltips

Probably Stolen 的物品 tooltip 重排版。把原版那一段文字换成一张估价单：售价放大在顶部，其余信息按小节分组、标签和数值分两列对齐。

只改排版，不改数据，也不显示游戏本来隐藏的信息。所有内容仍然来自游戏和其他模组。

## 功能

- 售价放大在顶部，其余价格作为一行明细放在下面
- 按小节分组：价值、鉴定、电力、机器、状态、液体、武器、装载、附加信息……
- 标签和数值分两列对齐，长行自动折行，放不下的数值换到下一行
- 分类标签显示为彩色小标签，每组颜色可单独设置
- 违禁品盖印章，赃物显示红色标签和热度
- 容器和机器的内容物画成图标格，电池和液体画成量条
- 价格明细里形如 `311/400` 的数值自动补一条进度条
- 武器词条按正负加浅绿/浅红底色
- 未拥有的物品用另一种纸色（默认粉色）
- 另有 Classic 风格：不画纸单，只在原版窗口里把文字分组

## 构建

需要 Windows 自带的 .NET Framework 4.x 编译器 `csc.exe`，不需要安装 .NET SDK。源码固定在 **C# 5**，不要用 C# 6 以后的语法。

```bat
set MANAGED=..\..\Probably Stolen_Data\Managed
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

"%CSC%" -nologo -target:library -optimize+ -nostdlib+ -noconfig -warn:4 -nowarn:1701,1702 ^
  -out:bin\SectionedTooltips.dll ^
  -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\netstandard.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
  -r:"%MANAGED%\Assembly-CSharp.dll" -r:"%MANAGED%\0Harmony.dll" ^
  -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -r:"%MANAGED%\UnityEngine.UI.dll" -r:"%MANAGED%\UnityEngine.UIModule.dll" -r:"%MANAGED%\Unity.TextMeshPro.dll" ^
  -r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" -r:"%MANAGED%\UnityEngine.TextCoreFontEngineModule.dll" ^
  src\*.cs
```

编译完成后，把 `SectionedTooltips.dll`、`manifest.xml`、`Localization\*.csv`、`preview.png` 一起放进游戏的 `Mods\SectionedTooltips\`。游戏运行时 DLL 被占用，改完要关掉游戏再复制。

## 目录

| 路径 | 内容 |
|---|---|
| `src/Capture.cs` | 记 `GetTooltipBasic` 期间各 helper 写过的 slice 区间 |
| `src/Helpers.cs` | 90 多个 tooltip helper 的 patch 表，按调用顺序编号 |
| `src/Layout.cs` | 排版的流程：读 builder、每行归到某一节、筛选、缓存 |
| `src/LayoutModel.cs` | 排版的数据：行、块、排版结果 |
| `src/LayoutRows.cs` | 把行并成块、拆成标签/数值/表格 |
| `src/LayoutClassic.cs` | Classic 风格的文字输出 |
| `src/Ticket.cs` | 估价单的纸、印章、标题、价格、页脚、文字上色 |
| `src/TicketGrid.cs` | 估价单的小节网格：标签列、数值列、进度条、升级、模组效果 |
| `src/Contents.cs` | 容器和机器里装了什么、电池和液体的量条 |
| `src/Panels.cs` | Classic 风格的侧边面板（液位计、内容物格子） |
| `src/Overlay.cs` | 画在 tooltip 上的图标和进度条 |
| `src/Icons.cs` | 物品图标的副本：裁掉透明边，按方框大小放大 |
| `src/SettingsPanel.cs` | 设置面板补丁：滑块数值、色块、示例单 |
| `src/TicketSample.cs` | 示例单和 Classic 示例 |
| `src/Compat.cs` | 和其他模组兼容的统一入口：核心流程只调它的钩子，每个兼容件各有熔断 |
| `src/ItemDetailsBridge.cs` | 和 Item Details 的对接：排版声明、tooltip 锚点 |
| `src/ItemDetailsSections.cs` | 读 Item Details 的区块协议，把它的行归到对应小节（区块 id 到小节的映射也在这里） |
| `src/AquaSense.cs` | 读《更好的水商操作体验》的成分接口，把它的成分行画成进度条 |
| `src/ReadableFonts.cs` | 和《字体大小优化》的兼容：它开着时估价单自己放大，字号抬到它不改的范围 |
| `src/Expand.cs` | 平时收起、按住展开键看完整估价单 |
| `src/DumpAll.cs` | dev 构建：导出全部物品的分节结果（`dumpall.flag`） |
| `src/Stress.cs` | dev 构建：压力测试，用假数据建极端单子写进日志（`stress.flag`） |
| `src/Shots.cs` | dev 构建：把显示过的估价单截图存进模组数据目录（`shots.flag`） |
| `src/TextRules.cs` | 纯文本规则（分数、模板、括号、清单），不引用游戏程序集 |
| `src/Fuse.cs` | 连续失败熔断 |
| `tests/` | 无头自检：`tests\test.bat` |
| `Localization/*.csv` | 文案，目前中英两份 |

`workshop/`（发布素材）、`build.bat`（本地构建脚本）和 `manifest.dev.xml`（dev 构建的清单）不进仓库。

## 兼容

可以和 Item Details、更好的水商操作体验、字体大小优化、Appraisal Assistant 等模组一起用。Item Details 的内容按它声明的归属放进对应小节，水商的成分进度条在估价单上照样画出来；开着字体大小优化时估价单跟着放大，排版不会重叠；其他模组追加的内容归到「附加信息」一节。

不需要任何权限，不修改存档，可以随时添加或移除。

## 许可

以 [GNU AGPL-3.0](LICENSE) 发布：可自由使用、修改、分发，衍生作品须以同一许可公开源码。游戏本体、Unity 与官方 SDK 不在本仓库授权范围内。

---

## English

A *Probably Stolen* mod that re-lays-out the item tooltip as an appraisal slip: the price large at the top, everything else grouped into sections with labels and values in two aligned columns.

Layout only — no data is added, and nothing the game hides is revealed. Works alongside Item Details; what those mods add goes into a "Mods" section.

Build with the `csc.exe` that ships with Windows (.NET Framework 4.x, C# 5); see the command above. No .NET SDK needed. Licensed under AGPL-3.0.
