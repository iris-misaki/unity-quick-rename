# v1.0.1

- 不再直接替换 `.anim` 文件的 YAML。可选动画同步改用 Unity 编辑 API，更新剪辑路径和绑定数据。
- 应用前显示拟更新的剪辑和未纳入的潜在引用；对象改名和动画更新归入同一个撤销组。
- 动画同步默认关闭。旧版造成的动画损坏需从备份恢复，新版不会自动修复历史改动。

在 Unity 2022.3.22f1 验证。安装请下载 Assets 中的 `LevelNameTranslator_v1.0.1_Install.zip`，关闭 Unity，将包内 `Assets` 合并到工程根目录，然后打开 **Tools → 层级名翻译器**。不要误下载 GitHub 自动生成的 `Source code (zip)` 当作安装包。
