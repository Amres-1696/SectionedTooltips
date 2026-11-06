# 和物品详情（Item Details）的耦合清单

这张表用来决定：哪些代码是为物品详情的输出“擦屁股”，哪些是通用规则。
判断标准很简单——**把物品详情卸载，这段代码还有用吗？**

## 一、纯为它的输出打的补丁

| 位置 | 在补什么 | 触发条件 | 建议 |
|---|---|---|---|
| `Layout.cs:231` `DropContentLists` | 它把容器内容物列成文字（“内含：A×2、B”），和我们自己的内容物面板重复 | 内容物面板开着，且行里点到条目 | **结构**：接 `storage` 区块后整段删；接不上则**保底**（只在 Mods 节、只对“说明：清单”生效） |
| `TextRules.cs:252` `CaptionList` | 同上：认出“内含：”这类清单标题（原来有一张十语言表） | 同上 | 同上一行 |
| `Ticket.cs:1281` `Description` + `1313` `Joint` + `1306` `DescriptionText` | 它按**自己窗口的宽度**断行（“……金属锭。金” / “属锭的质量……”），我们得把行重新拼回原文 | 描述行 | **保底**（没办法从区块拿到未断行原文，只能留着） |
| `Ticket.cs:1324` `Trimmed` + `1345` `TrimLead` | 它给每行加了缩进，还有标签后面的空格 | 描述行 | 保底 |
| `TicketGrid.cs:166` `SplitBar` | 它把进度条画成 `█████` + `<alpha=#66>` 的文本，我们再解析回来 | 值里出现 `█` | **结构**：`NativeProgressBars` 已让它走 `AddProgressBar`，这条只在旧版本触发，可以降级删掉 |
| `Ticket.cs:235` `Badge()` + `BadgeGap` | 按物体名找到它的徽章，量高度给估价单留空 | 每帧 | 已注册 `tooltip_anchor`，位置由它自己摆；**只剩量高度**，可以简化为固定值 |

## 二、通用规则，但它触发得最多（保留，但要收紧）

| 位置 | 说明 | 建议 |
|---|---|---|
| `TextRules.cs:130` `PercentPairs` + `Layout.PercentRows` + `TicketGrid.cs:345` `ModifierRow` | “净水器 +0.91% 滤水器 +0.85%”这种列表，游戏自己也会写；物品详情写得更多 | 保留。已经加了“名字≤12 字、不带句子标点”的限制 |
| `TextRules.cs:192` `TrailingPercent` | “种子产量 +25%”没有冒号，借它变成标签/数值行 | 保留 |
| `TicketGrid.cs:511` `TabLayoutOf` / `GridCells` | 物品详情 0.7.7 的水质/种子表用全角空格对齐 | 保留（移植自上游 PR） |

## 三、不是它的锅（别误删）

- `Ticket.cs:753` `SplitNotOwned`、`774` `FeatureOf`：游戏自己把“未拥有”“[标签]”拼在行里，用的是 `LocHelper`。
- `Ticket.cs:1501` `ChipsOf` / `1528` `TypeNamed`：认游戏自己的 `[机器] [工具]` 类型行。
- `Ticket.cs:1542` `GroupOf`、`1443` `TagColorNames`：分组和配色**照抄物品详情的视觉**，是设计选择，不是解析。
- `LocTemplates`：反查的是游戏的 `mech_durability_display`。
- `Layout.SplitCustomName`：蓝色贴标机的“自定义名 (原名)”。

## 四、镜像：它也在给我们打补丁

`item-details/src/Compatibility/SectionedLayout.cs` 写死了 `amres.sectioned_tooltips`，直接读我们的 `Enabled` / `ContentsPanel` 两个设置项。
这是同一件事的另一半：A 模组的代码在猜 B 模组的配置。接上契约后这段可以删。

## 五、结论

- 真正“擦屁股”的是第一张表，约 160 行，占排版代码两成左右。
- 其中 `DropContentLists` / `CaptionList` / `SplitBar` 三项在接 `storage` 区块和 `NativeProgressBars` 后可以整段删除；
  `Description` 系列和 `Badge` 只能保留（或简化）。
- 规矩：以后不再加“靠文本形状推断”的特判，除非同时满足——有单测覆盖、只在明确的小节里生效、误判只影响观感不影响信息。
