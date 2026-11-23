# 层级名翻译器 v1.0.1

Unity 编辑器插件：批量把拖入的日文、英文层级对象名翻成中文，译文可手动修改。已在 Unity **2022.3.22f1** 验证；其他版本未验证。

## 安装

1. 到 [v1.0.1 Releases](https://github.com/iris-misaki/unity-quick-rename/releases/tag/v1.0.1) 下载 `LevelNameTranslator_v1.0.1_Install.zip`。关闭 Unity，解压后将 `Assets` 合并到工程根目录（与项目原有 `Assets` 同级）。这是手动复制安装，不需要导入 `.unitypackage`。
2. 打开 Unity，等编译完成，从 **Tools → 层级名翻译器** 打开，确认版本为 **v1.0.1**。
3. 若旧版装在同一 `Assets/LevelNameTranslator` 路径，覆盖同名插件文件即可；若通过 UPM 或装在其他目录，先移除重复安装。不要删除模型、场景或其他插件。

开发者也可通过 Unity Package Manager 的 **Add package from git URL** 使用 `https://github.com/iris-misaki/unity-quick-rename.git#v1.0.1`；此方式需等标签发布，不要与 ZIP 同时安装。UPM 安装本次未实测。

## 使用

- 从 Hierarchy 或 Prefab 模式拖入对象，点「开始翻译」；可逐条修改译文，再点「应用改名」。也可用「只用术语表」离线试用。
- 在线翻译需自备 OpenAI 兼容接口的 API Key、模型名和完整 `/chat/completions` 地址；待翻译的对象名会发给你配置的服务。Key 保存在本机 Unity EditorPrefs，不在发布文件中。
- **动画同步默认关闭。** 勾选后，工具通过 Unity API 更新从相关 Animator 或可识别的 VRChat Avatar Descriptor 动画层找到的剪辑，应用前会显示计划。Ctrl+Z 可一起撤销本批对象名和动画绑定。
- 文本预检不保证覆盖所有引用。未加载的场景、共用剪辑、脚本和第三方换装工具可能有未纳入的引用。应用前看日志、先备份；确认表情和换装正常后，保存场景或 Prefab，再用 **File → Save Project** 保存动画资产。
- 旧版如果已经写坏动画，新版不会自动修复历史损坏。请恢复原始动画，并让对象名与绑定路径一致。

## 验证与许可

Unity 2022.3.22f1 独立工程测试验证了绑定哈希更新、数值与对象引用曲线保留，以及同组撤销。Rize 工程在 Gesture Manager 中已预览表情、衣服穿脱生成和大小调整，未发现异常；不代表其他工程或全部剪辑均已验证。

[MIT](LICENSE)。参考了 DigitalKabuki BulkRename 的工作流，未附带该插件。
