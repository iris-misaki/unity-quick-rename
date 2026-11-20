# 层级名翻译器 (Level Name Translator)

把 Unity 层级里那些日文 / 英文的对象名批量翻成中文，改模找部件时直观很多。

只处理你拖进来的对象，不做全工程扫描，绝不动别的东西。

![Unity](https://img.shields.io/badge/Unity-2019.3%2B-black)
![License](https://img.shields.io/badge/License-MIT-blue)
![Version](https://img.shields.io/badge/version-1.0.0-green)

---

## 它解决什么

VRChat 模型、日系素材的层级里经常是一堆 `アウター`、`スカートベルト`、`ヘッドドレス`、`backunder_long`。
改模的时候要在里面找一件件衣服，很费眼。

这个工具让你**把对象拖进窗口，一键翻成中文**，确认后再写回场景 / Prefab。

- **只处理你拖的** —— 不做任何扫描
- **先预览再应用** —— 每行译文都能手动改
- **中文界面** —— 菜单、按钮、日志、报错提示全中文
- **安全预检** —— 改名之前检查会不会破坏动画、代码引用
- **可撤销** —— 改错了一键还原

---

## 安装

### 方式一：下载发行包（最简单）

去 [Releases](https://github.com/iris-misaki/unity-quick-rename/releases) 下载最新的 `LevelNameTranslator_vX.X.X.unitypackage`，双击或在 Unity 里
`Assets → Import Package → Custom Package...` 导入。

### 方式二：Clone 到工程的 Assets

```bash
cd 你的Unity工程/Assets
git clone https://github.com/iris-misaki/unity-quick-rename.git
```

`Editor` 目录会被 Unity 自动识别为编辑器脚本，无需额外配置。

### 方式三：UPM（Unity Package Manager）

在工程的 `Packages/manifest.json` 里加：

```json
{
  "dependencies": {
    "com.iris-misaki.quick-rename": "https://github.com/iris-misaki/unity-quick-rename.git"
  }
}
```

> 三种方式选一种即可，别重复导入（会报重复类型错误）。UPM 方式依赖仓库根目录的 `package.json`，已提供。

导入后菜单里会出现**两个入口**，点哪个都一样：

- **Tools → Level Name Translator (层级名翻译器)** ← 英文名排在 L 段，好找
- **Tools → 层级名翻译器** ← 中文名会被 Unity 排到菜单最下面

---

## 配置接口

打开窗口，展开顶部 **「▶ 接口设置（API Key / 模型）」**：

| 字段 | 填什么 |
|---|---|
| 接口地址 | `https://api.deepseek.com/chat/completions` |
| API Key | 你的 Key |
| 模型名 | `deepseek-flash`（或 `deepseek-v4-pro`） |

点 **「保存设置」** → **「测试连接」**，日志出现「测试成功」即可。

> Key 存在本机的 Unity 设置（EditorPrefs）里，**不写进工程目录**，不会被 git 提交。

### 最容易填错的两件事

1. **接口地址不是网页地址。**
   `https://platform.deepseek.com/api_keys` 是 Key 管理网页，填进去必然失败。
   必须是以 `/chat/completions` 结尾的 API 路径。
2. **模型名大小写敏感。**
   官方文档里是全小写的 `deepseek-flash` / `deepseek-v4-pro`。
   写成 `DeepSeek-V4.1-Flash` 这种会报模型不存在。

地址填错时工具会自动识别并改对，同时在日志里说明。也可以点 **「恢复默认地址」**（Key 不受影响）。

---

## 使用流程

```
拖对象进窗口  →  开始翻译  →  逐条检查/手改  →  应用改名
```

### 1. 添加对象

在 Hierarchy 里选中要翻的节点，**直接拖进窗口**，或点「添加选中对象」。
可多选，也可分几次加。

### 2. 翻译

点 **「开始翻译」**，工具按顺序处理：

1. **内置术语表** —— `アウター=外套`、`スカート本体=裙子本体` 这类常见词直接命中，不花钱
2. **本机缓存** —— 同样的词不会重复请求
3. **调用模型** —— 剩下的才发给 API

译文出现在每行输入框里，**可以直接改**。改完不用额外保存。

### 3. 安全预检（重要）

点 **「安全预检」**，会扫一遍工程，找出这些名字有没有被别的东西**按字符串**引用：

| 检查项 | 改名的后果 | 严重度 |
|---|---|---|
| 动画片段（`.anim`）的绑定路径 | 动画找不到对象，**动作直接失效** | 危险 |
| 代码里的 `Find("名字")` | 运行时找不到，功能静默坏掉 | 危险 |
| 序列化里的路径字段（MA / 换装组件等） | 相关配置可能失联 | 注意 |

**不点预检直接「应用改名」，工具也会自动先跑一次**，发现危险引用会弹窗拦截确认。

> 按对象引用（fileID）绑定的东西不会断，所以不报警——绝大多数 Unity 组件都属于这一类。

### 4. 应用改名

- 场景里的对象 → 标记为 dirty，**按 Ctrl+S 保存场景**才落盘
- Prefab 资源里的对象 → 自动保存到磁盘

改错了点 **「撤销改名」** 还原，或直接 Ctrl+Z。

---

## 其它功能

| 功能 | 作用 |
|---|---|
| **只用术语表** | 完全不联网，只用内置词表翻译。常见服装部件够用，不花额度。 |
| **导出对照表 CSV** | 导出「原名,译名」，UTF-8 带 BOM，Excel 打开不乱码。 |
| **恢复默认地址** | 把接口地址和模型名恢复成默认值，Key 不受影响。 |
| 每行的 **「清」** | 清空该行译文，下次重新翻译。 |
| 每行的 **「×」** | 从列表移除，**不会**改名。 |

---

## 支持的翻译服务

接口是 **OpenAI 兼容格式**，只要支持 `/v1/chat/completions` 就能用：

- **DeepSeek**：`https://api.deepseek.com/chat/completions` + `deepseek-flash`
- **OpenAI**：`https://api.openai.com/v1/chat/completions` + `gpt-4o-mini`
- **各类中转站**：填它给的完整地址和模型名

百度、有道、DeepL 的原生 API 不支持（签名和报文格式各不相同）。

---

## 已知问题与限制

- **「测试连接」的连通性修复尚未在真实网络环境下完整验证。** 如果报
  「连不上服务器（没有收到任何响应）」，日志会带 `[结果=...]` 标记，请附上该行提交 issue。
  如开着代理软件，试着把 `api.deepseek.com` 设为直连。
- **不会改文件名。** 只改 GameObject 的层级名（`target.name`），绝不调用 `AssetDatabase.RenameAsset`。
- **撤销范围**：「撤销改名」只记录本窗口点过「应用改名」的操作，手动改的名字它不管。
- **专有名词可能翻得怪。** 模型名、骨骼代号这类词建议应用前扫一眼，不对的直接在输入框改。
- **限流**：每批数量默认 24，经常碰到 HTTP 429 就调到 10。

---

## 文件结构

```
unity-quick-rename/
├── Editor/
│   ├── IrisMisaki.LevelNameTranslator.asmdef
│   ├── LevelNameTranslatorWindow.cs   主窗口（界面全中文）
│   ├── TranslatorClient.cs            OpenAI 兼容调用 + 分批 + 重试 + 解析
│   ├── TranslatorSettings.cs          API Key / 模型设置 + 地址自动纠错
│   ├── TranslationCache.cs            译文缓存（Library/ 下）
│   ├── RenameSafetyChecker.cs         安全预检：动画路径 / Find / 序列化路径
│   ├── EditorCoroutineHost.cs         编辑器协程驱动器（主线程，不卡界面）
│   └── Glossary.cs                    内置日文 / 英文术语表
├── package.json                       UPM 包描述
├── LICENSE
└── README.md
```

纯 C#，无第三方 DLL，无运行时依赖。

---

## 常见问题

**Q：测试连接报 401？**
Key 不对，或这个地址的服务不认这个 Key。检查地址和 Key 是否同一家。

**Q：报「model not found」？**
模型名写错了，大小写敏感。照服务商文档里的 id 原样填。

**Q：报 404？**
接口地址填错了，必须是 `/chat/completions` 结尾的完整 API 地址。

**Q：报 429？**
限流或额度用尽。调小「每批数量」，或换时间段。

**Q：翻译部分是空的？**
模型没按格式返回时会自动逐条重试。仍失败的显示「翻译失败」，再点一次「开始翻译」即可——已成功的不重翻。

**Q：改了名之后动画不动了？**
说明该层级被动画路径绑定了，预检本该拦你。用「撤销改名」还原，然后只改不影响动画的部分。

**Q：找不到菜单？**
看 Console 有没有红色编译错误；或确认 `Editor` 目录已被导入。

---

## 许可证

[MIT](LICENSE)

## 致谢

开发过程中参考了 DigitalKabuki 的 BulkRename 在 UIElements 窗口与拖拽投递方面的思路，代码为本项目独立实现。
