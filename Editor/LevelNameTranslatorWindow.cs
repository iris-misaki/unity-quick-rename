using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LevelNameTranslator
{
    /// <summary>
    /// 层级名翻译器：把拖进来的层级对象名翻成中文，确认后写回场景 / Prefab。
    /// </summary>
    public class LevelNameTranslatorWindow : EditorWindow
    {
        private const string Version = "1.0.0";
        private const string MenuPath = "Tools/层级名翻译器";
        private const string MenuPathEnglish = "Tools/Level Name Translator (层级名翻译器)";

        private const int ColorDanger = 0;
        private const int ColorWarning = 1;
        private const int ColorOk = 2;

        [Serializable]
        private class Entry
        {
            public GameObject Target;
            public string OriginalName;
            public string NewName;
            public string StatusText;
            public int StatusKind;
        }

        [Serializable]
        private class UndoRecord
        {
            public GameObject Target;
            public string OldName;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private List<UndoRecord> undoRecords = new List<UndoRecord>();
        [SerializeField] private string logText = string.Empty;
        [SerializeField] private bool settingsExpanded;

        private TranslatorSettings settings;
        private List<SafetyFinding> lastFindings;
        private bool safetyCheckStale = true;
        private bool busy;

        private ScrollView listContainer;
        private TextField logField;
        private Label countLabel;
        private VisualElement dropZone;
        private Label dropLabel;
        private VisualElement settingsBody;
        private Button settingsToggle;
        private TextField endpointField;
        private TextField keyField;
        private TextField modelField;
        private IntegerField batchField;
        private TextField targetLangField;
        private Toggle showKeyToggle;
        private Button translateButton;
        private Button glossaryButton;
        private Button checkButton;
        private Button applyButton;
        private Button revertButton;

        // ------------------------------------------------------------------ 生命周期

        // 两个菜单项指向同一个窗口：中文名在 Unity 菜单里会被排到最后，
        // 留一个英文名（排序在 L 段）方便找。
        [MenuItem(MenuPath)]
        [MenuItem(MenuPathEnglish)]
        public static void Open()
        {
            LevelNameTranslatorWindow window = GetWindow<LevelNameTranslatorWindow>();
            window.titleContent = new GUIContent("层级名翻译器");
            window.minSize = new Vector2(560, 460);
            window.Show();
        }

        private void OnEnable()
        {
            settings = TranslatorSettings.Load();

            if (entries == null)
            {
                entries = new List<Entry>();
            }

            if (undoRecords == null)
            {
                undoRecords = new List<UndoRecord>();
            }
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 6;
            rootVisualElement.style.paddingRight = 6;
            rootVisualElement.style.paddingTop = 6;
            rootVisualElement.style.paddingBottom = 6;

            BuildHeader();
            BuildSettings();
            BuildDropZone();
            BuildToolbar();
            BuildList();
            BuildLog();

            RegisterDragAndDrop();
            RebuildList();
            RefreshLog();
            UpdateToolbarState();
        }

        // ------------------------------------------------------------------ 构建界面

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;

            var title = new Label("层级名翻译器");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.flexGrow = 1;
            header.Add(title);

            var version = new Label("v" + Version);
            version.style.fontSize = 10;
            version.style.color = EditorGUIUtility.isProSkin
                ? new Color(0.6f, 0.6f, 0.6f)
                : new Color(0.4f, 0.4f, 0.4f);
            header.Add(version);

            rootVisualElement.Add(header);
        }

        private void BuildSettings()
        {
            var section = new VisualElement();
            section.style.marginBottom = 6;
            section.style.borderTopWidth = 1;
            section.style.borderBottomWidth = 1;
            section.style.borderLeftWidth = 1;
            section.style.borderRightWidth = 1;
            section.style.borderTopColor = Divider();
            section.style.borderBottomColor = Divider();
            section.style.borderLeftColor = Divider();
            section.style.borderRightColor = Divider();
            section.style.paddingTop = 4;
            section.style.paddingBottom = 4;
            section.style.paddingLeft = 6;
            section.style.paddingRight = 6;

            settingsToggle = new Button(ToggleSettings);
            settingsToggle.text = (settingsExpanded ? "▼" : "▶") + " 接口设置（API Key / 模型）";
            settingsToggle.style.unityTextAlign = TextAnchor.MiddleLeft;
            settingsToggle.style.borderTopWidth = 0;
            settingsToggle.style.borderBottomWidth = 0;
            settingsToggle.style.borderLeftWidth = 0;
            settingsToggle.style.borderRightWidth = 0;
            settingsToggle.style.backgroundColor = Color.clear;
            settingsToggle.style.paddingLeft = 0;
            section.Add(settingsToggle);

            settingsBody = new VisualElement();
            settingsBody.style.marginTop = 4;
            settingsBody.style.display = settingsExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            endpointField = new TextField("接口地址");
            endpointField.value = settings.Endpoint;
            endpointField.tooltip = "必须是 API 接口地址，不是网页地址。DeepSeek 填：https://api.deepseek.com/chat/completions";
            endpointField.style.marginBottom = 2;
            settingsBody.Add(endpointField);

            var keyRow = new VisualElement();
            keyRow.style.flexDirection = FlexDirection.Row;
            keyRow.style.alignItems = Align.Center;

            keyField = new TextField("API Key");
            keyField.value = settings.ApiKey;
            keyField.isPasswordField = true;
            keyField.tooltip = "存在本机 EditorPrefs，不会写进工程目录，也不会被 git 提交。";
            keyField.style.flexGrow = 1;
            keyRow.Add(keyField);

            showKeyToggle = new Toggle("显示");
            showKeyToggle.value = false;
            showKeyToggle.style.marginLeft = 6;
            showKeyToggle.RegisterValueChangedCallback(evt =>
            {
                if (keyField != null)
                {
                    keyField.isPasswordField = !evt.newValue;
                }
            });
            keyRow.Add(showKeyToggle);
            settingsBody.Add(keyRow);

            modelField = new TextField("模型名");
            modelField.value = settings.Model;
            modelField.tooltip = "服务商文档里的 model id，大小写敏感。DeepSeek 用 deepseek-flash 或 deepseek-v4-pro。";
            modelField.style.marginBottom = 2;
            settingsBody.Add(modelField);

            var batchRow = new VisualElement();
            batchRow.style.flexDirection = FlexDirection.Row;
            batchRow.style.alignItems = Align.Center;

            batchField = new IntegerField("每批数量");
            batchField.value = settings.BatchSize;
            batchField.tooltip = "一次请求带多少条名字。数量越大越快，但更容易触发限流。";
            batchField.style.flexGrow = 1;
            batchRow.Add(batchField);

            targetLangField = new TextField("目标语言");
            targetLangField.value = settings.TargetLanguage;
            targetLangField.style.flexGrow = 1;
            targetLangField.style.marginLeft = 6;
            batchRow.Add(targetLangField);

            settingsBody.Add(batchRow);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4;

            var saveButton = new Button(SaveSettings) { text = "保存设置" };
            saveButton.style.flexGrow = 1;
            buttonRow.Add(saveButton);

            var testButton = new Button(TestConnection) { text = "测试连接" };
            testButton.style.flexGrow = 1;
            testButton.style.marginLeft = 4;
            buttonRow.Add(testButton);

            var resetButton = new Button(ResetEndpointAndModel) { text = "恢复默认地址" };
            resetButton.style.flexGrow = 1;
            resetButton.style.marginLeft = 4;
            resetButton.tooltip = "把接口地址和模型名恢复成 DeepSeek 官方默认值。Key 不动。";
            buttonRow.Add(resetButton);

            settingsBody.Add(buttonRow);

            var hint = new Label("Key 只保存在这台机器的 Unity 设置里。换 Key 改上面再点「保存设置」。填错了地址工具会自动修正。");
            hint.style.fontSize = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 4;
            hint.style.color = Dim();
            settingsBody.Add(hint);

            section.Add(settingsBody);
            rootVisualElement.Add(section);
        }

        private void BuildDropZone()
        {
            dropZone = new VisualElement();
            dropZone.style.height = 52;
            dropZone.style.marginBottom = 6;
            dropZone.style.alignItems = Align.Center;
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.borderTopWidth = 1;
            dropZone.style.borderBottomWidth = 1;
            dropZone.style.borderLeftWidth = 1;
            dropZone.style.borderRightWidth = 1;
            dropZone.style.borderTopColor = Divider();
            dropZone.style.borderBottomColor = Divider();
            dropZone.style.borderLeftColor = Divider();
            dropZone.style.borderRightColor = Divider();
            dropZone.style.borderTopLeftRadius = 4;
            dropZone.style.borderTopRightRadius = 4;
            dropZone.style.borderBottomLeftRadius = 4;
            dropZone.style.borderBottomRightRadius = 4;

            dropLabel = new Label("把层级里的对象拖到这里（可以多选），或点下面的「添加选中对象」");
            dropLabel.style.fontSize = 11;
            dropLabel.style.whiteSpace = WhiteSpace.Normal;
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.color = Dim();
            dropZone.Add(dropLabel);

            rootVisualElement.Add(dropZone);
        }

        private void BuildToolbar()
        {
            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom = 3;

            var addButton = new Button(AddFromSelection) { text = "添加选中对象" };
            addButton.style.flexGrow = 1;
            row1.Add(addButton);

            var clearButton = new Button(ClearEntries) { text = "清空列表" };
            clearButton.style.flexGrow = 1;
            clearButton.style.marginLeft = 4;
            row1.Add(clearButton);

            translateButton = new Button(StartTranslate) { text = "开始翻译" };
            translateButton.style.flexGrow = 2;
            translateButton.style.marginLeft = 4;
            row1.Add(translateButton);

            rootVisualElement.Add(row1);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginBottom = 3;

            glossaryButton = new Button(TranslateWithGlossaryOnly) { text = "只用术语表" };
            glossaryButton.style.flexGrow = 1;
            glossaryButton.tooltip = "不联网，只用内置日文/英文术语表翻译。适合常见服装部件，不花额度。";
            row2.Add(glossaryButton);

            checkButton = new Button(RunSafetyCheck) { text = "安全预检" };
            checkButton.style.flexGrow = 1;
            checkButton.style.marginLeft = 4;
            checkButton.tooltip = "检查这些名字有没有被动画路径、代码 Find(...)、或序列化路径字段引用。";
            row2.Add(checkButton);

            applyButton = new Button(ApplyRename) { text = "应用改名" };
            applyButton.style.flexGrow = 1;
            applyButton.style.marginLeft = 4;
            row2.Add(applyButton);

            revertButton = new Button(RevertRename) { text = "撤销改名" };
            revertButton.style.flexGrow = 1;
            revertButton.style.marginLeft = 4;
            row2.Add(revertButton);

            rootVisualElement.Add(row2);

            var row3 = new VisualElement();
            row3.style.flexDirection = FlexDirection.Row;
            row3.style.marginBottom = 4;

            var exportButton = new Button(ExportCsv) { text = "导出对照表 CSV" };
            exportButton.style.flexGrow = 1;
            row3.Add(exportButton);

            countLabel = new Label();
            countLabel.style.flexGrow = 1;
            countLabel.style.fontSize = 10;
            countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            countLabel.style.color = Dim();
            row3.Add(countLabel);

            rootVisualElement.Add(row3);
        }

        private void BuildList()
        {
            listContainer = new ScrollView(ScrollViewMode.Vertical);
            listContainer.style.flexGrow = 1;
            listContainer.style.minHeight = 120;
            listContainer.style.marginBottom = 4;
            listContainer.style.borderTopWidth = 1;
            listContainer.style.borderBottomWidth = 1;
            listContainer.style.borderLeftWidth = 1;
            listContainer.style.borderRightWidth = 1;
            listContainer.style.borderTopColor = Divider();
            listContainer.style.borderBottomColor = Divider();
            listContainer.style.borderLeftColor = Divider();
            listContainer.style.borderRightColor = Divider();
            rootVisualElement.Add(listContainer);
        }

        private void BuildLog()
        {
            var label = new Label("日志");
            label.style.fontSize = 10;
            label.style.color = Dim();
            rootVisualElement.Add(label);

            logField = new TextField();
            logField.multiline = true;
            logField.isReadOnly = true;
            logField.style.height = 96;
            logField.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(logField);
        }

        private void RegisterDragAndDrop()
        {
            rootVisualElement.RegisterCallback<DragEnterEvent>(_ => SetDropHover(true));
            rootVisualElement.RegisterCallback<DragLeaveEvent>(_ => SetDropHover(false));
            rootVisualElement.RegisterCallback<DragExitedEvent>(_ => SetDropHover(false));

            rootVisualElement.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    SetDropHover(true);
                }
            });

            rootVisualElement.RegisterCallback<DragPerformEvent>(_ =>
            {
                SetDropHover(false);

                if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                {
                    return;
                }

                UnityEngine.Object[] objects = DragAndDrop.objectReferences;
                DragAndDrop.AcceptDrag();
                AddObjects(objects);
            });
        }

        private void SetDropHover(bool hover)
        {
            if (dropZone == null)
            {
                return;
            }

            Color color = hover
                ? (EditorGUIUtility.isProSkin ? new Color(0.24f, 0.49f, 0.9f) : new Color(0.3f, 0.55f, 0.95f))
                : Divider();

            dropZone.style.borderTopColor = color;
            dropZone.style.borderBottomColor = color;
            dropZone.style.borderLeftColor = color;
            dropZone.style.borderRightColor = color;
        }

        // ------------------------------------------------------------------ 列表

        private void AddFromSelection()
        {
            UnityEngine.Object[] selection = Selection.objects;
            if (selection == null || selection.Length == 0)
            {
                Log("没有选中任何对象。");
                return;
            }

            AddObjects(selection);
        }

        private void AddObjects(UnityEngine.Object[] objects)
        {
            if (objects == null || objects.Length == 0)
            {
                return;
            }

            int added = 0;
            int skipped = 0;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = ResolveGameObject(objects[i]);
                if (go == null)
                {
                    skipped++;
                    continue;
                }

                if (ContainsTarget(go))
                {
                    skipped++;
                    continue;
                }

                entries.Add(new Entry
                {
                    Target = go,
                    OriginalName = go.name,
                    NewName = string.Empty,
                    StatusText = "待翻译",
                    StatusKind = ColorOk
                });

                added++;
            }

            safetyCheckStale = true;
            RebuildList();
            UpdateToolbarState();

            var sb = new StringBuilder();
            sb.Append("添加 ").Append(added).Append(" 个对象");
            if (skipped > 0)
            {
                sb.Append("，跳过 ").Append(skipped).Append(" 个（不是层级对象，或已经在列表里）");
            }

            Log(sb.ToString());
        }

        private static GameObject ResolveGameObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            GameObject go = obj as GameObject;
            if (go != null)
            {
                return go;
            }

            Component component = obj as Component;
            if (component != null)
            {
                return component.gameObject;
            }

            return null;
        }

        private bool ContainsTarget(GameObject go)
        {
            int id = go.GetInstanceID();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.Target != null && entry.Target.GetInstanceID() == id)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearEntries()
        {
            entries.Clear();
            lastFindings = null;
            safetyCheckStale = true;
            RebuildList();
            UpdateToolbarState();
        }

        private void RebuildList()
        {
            if (listContainer == null)
            {
                return;
            }

            listContainer.Clear();

            if (entries.Count == 0)
            {
                var empty = new Label("列表是空的。把对象拖进上面的框，或点「添加选中对象」。");
                empty.style.fontSize = 11;
                empty.style.color = Dim();
                empty.style.paddingTop = 8;
                empty.style.paddingBottom = 8;
                empty.style.paddingLeft = 8;
                empty.style.whiteSpace = WhiteSpace.Normal;
                listContainer.Add(empty);
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    listContainer.Add(BuildRow(entries[i]));
                }
            }

            if (countLabel != null)
            {
                int ready = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && !string.IsNullOrEmpty(entries[i].NewName))
                    {
                        ready++;
                    }
                }

                countLabel.text = "共 " + entries.Count + " 项，已有译文 " + ready + " 项";
            }
        }

        private VisualElement BuildRow(Entry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;

            var original = new Label(entry.OriginalName);
            original.style.width = Length.Percent(34);
            original.style.fontSize = 11;
            original.style.overflow = Overflow.Hidden;
            original.tooltip = entry.OriginalName;
            row.Add(original);

            var arrow = new Label("→");
            arrow.style.width = 18;
            arrow.style.fontSize = 11;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.color = Dim();
            row.Add(arrow);

            var field = new TextField();
            field.value = entry.NewName ?? string.Empty;
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 80;
            Entry captured = entry;
            field.RegisterValueChangedCallback(evt =>
            {
                if (captured == null)
                {
                    return;
                }

                captured.NewName = evt.newValue;
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    captured.StatusText = "已填写";
                    captured.StatusKind = ColorOk;
                }

                UpdateToolbarState();
            });
            row.Add(field);

            var reset = new Button(() =>
            {
                if (captured == null)
                {
                    return;
                }

                captured.NewName = string.Empty;
                captured.StatusText = "待翻译";
                captured.StatusKind = ColorOk;
                RebuildList();
                UpdateToolbarState();
            });
            reset.text = "清";
            reset.tooltip = "清空译文，下次翻译会重新处理";
            reset.style.width = 22;
            reset.style.marginLeft = 3;
            reset.style.marginRight = 3;
            row.Add(reset);

            var status = new Label(string.IsNullOrEmpty(entry.StatusText) ? "-" : entry.StatusText);
            status.style.width = 78;
            status.style.fontSize = 10;
            status.style.overflow = Overflow.Hidden;
            status.style.color = StatusColor(entry.StatusKind);
            status.tooltip = entry.StatusText;
            row.Add(status);

            var remove = new Button(() =>
            {
                entries.Remove(captured);
                safetyCheckStale = true;
                RebuildList();
                UpdateToolbarState();
            });
            remove.text = "×";
            remove.tooltip = "从列表移除（不会改名）";
            remove.style.width = 20;
            row.Add(remove);

            return row;
        }

        private void UpdateToolbarState()
        {
            if (translateButton == null)
            {
                return;
            }

            bool hasEntries = entries.Count > 0;
            translateButton.SetEnabled(hasEntries && !busy);
            glossaryButton.SetEnabled(hasEntries && !busy);
            checkButton.SetEnabled(hasEntries && !busy);
            applyButton.SetEnabled(hasEntries && !busy);
            revertButton.SetEnabled(hasEntries && undoRecords.Count > 0 && !busy);

            var caption = new StringBuilder("开始翻译");
            if (hasEntries)
            {
                int pending = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e != null && string.IsNullOrEmpty(e.NewName))
                    {
                        pending++;
                    }
                }

                caption.Append("（待译 ").Append(pending).Append("）");
            }

            if (busy)
            {
                caption.Append(" 进行中…");
            }

            translateButton.text = caption.ToString();
        }

        // ------------------------------------------------------------------ 翻译

        private void StartTranslate()
        {
            if (busy)
            {
                return;
            }

            List<Entry> pending = CollectPendingEntries();
            if (pending.Count == 0)
            {
                Log("没有待翻译的条目。已翻译过的条目会被跳过；要重翻请先点行里的「清」。");
                return;
            }

            if (!settings.HasApiKey)
            {
                Log("还没有填 API Key。展开「接口设置」填入后点「保存设置」。也可以先用「只用术语表」试试。");
                settingsExpanded = true;
                RefreshSettingsVisibility();
                return;
            }

            int fromGlossary = 0;
            int fromCache = 0;
            var needApi = new List<Entry>();

            for (int i = 0; i < pending.Count; i++)
            {
                Entry entry = pending[i];
                string source = entry.Target != null ? entry.Target.name : entry.OriginalName;

                string hit = Glossary.LookupExact(source);
                if (!string.IsNullOrEmpty(hit))
                {
                    entry.NewName = hit;
                    entry.StatusText = "术语表";
                    entry.StatusKind = ColorOk;
                    fromGlossary++;
                    continue;
                }

                string cached;
                if (TranslationCache.TryGet(settings.Model, settings.TargetLanguage, source, out cached) &&
                    !string.IsNullOrEmpty(cached))
                {
                    entry.NewName = cached;
                    entry.StatusText = "缓存";
                    entry.StatusKind = ColorOk;
                    fromCache++;
                    continue;
                }

                needApi.Add(entry);
            }

            RebuildList();

            if (needApi.Count == 0)
            {
                Log("全部命中：术语表 " + fromGlossary + " 条，缓存 " + fromCache + " 条，没有发起请求。");
                UpdateToolbarState();
                return;
            }

            Log("开始翻译：" + needApi.Count + " 条（术语表命中 " + fromGlossary +
                "，缓存命中 " + fromCache + "），模型 " + settings.Model + "。");

            busy = true;
            UpdateToolbarState();

            var client = new TranslatorClient(settings);
            EditorCoroutineHost.Start(RunTranslateRoutine(client, needApi, fromGlossary, fromCache));
        }

        private IEnumerator RunTranslateRoutine(
            TranslatorClient client,
            List<Entry> targets,
            int fromGlossary,
            int fromCache)
        {
            var sources = new List<string>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                sources.Add(targets[i].Target != null ? targets[i].Target.name : targets[i].OriginalName);
            }

            string lastError = null;
            int success = 0;
            int failed = 0;
            int done = 0;
            int total = sources.Count;

            yield return client.TranslateAll(
                sources,
                (index, translation) =>
                {
                    if (index < 0 || index >= targets.Count)
                    {
                        return;
                    }

                    Entry entry = targets[index];
                    if (string.IsNullOrEmpty(translation))
                    {
                        entry.StatusText = "翻译失败";
                        entry.StatusKind = ColorDanger;
                        failed++;
                        return;
                    }

                    entry.NewName = translation;
                    entry.StatusText = "已翻译";
                    entry.StatusKind = ColorOk;
                    success++;

                    string source = sources[index];
                    TranslationCache.Put(settings.Model, settings.TargetLanguage, source, translation);
                },
                error => { lastError = error; },
                (finished, all) =>
                {
                    done = finished;
                    total = all;
                });

            TranslationCache.Save();

            if (!string.IsNullOrEmpty(lastError))
            {
                Log("出错：" + lastError);
            }

            Log("翻译结束：新翻 " + success + " 条，失败 " + failed + " 条，术语表 " + fromGlossary +
                " 条，缓存 " + fromCache + " 条。请逐条检查译文，可以直接在列表里改。");

            busy = false;
            RebuildList();
            UpdateToolbarState();
        }

        private void TranslateWithGlossaryOnly()
        {
            if (busy)
            {
                return;
            }

            List<Entry> pending = CollectPendingEntries();
            if (pending.Count == 0)
            {
                Log("没有待翻译的条目。");
                return;
            }

            int hit = 0;
            int miss = 0;

            for (int i = 0; i < pending.Count; i++)
            {
                Entry entry = pending[i];
                string source = entry.Target != null ? entry.Target.name : entry.OriginalName;
                string value = Glossary.LookupExact(source);

                if (!string.IsNullOrEmpty(value))
                {
                    entry.NewName = value;
                    entry.StatusText = "术语表";
                    entry.StatusKind = ColorOk;
                    hit++;
                }
                else
                {
                    entry.StatusText = "术语表无匹配";
                    entry.StatusKind = ColorWarning;
                    miss++;
                }
            }

            RebuildList();
            UpdateToolbarState();
            Log("只用术语表：命中 " + hit + " 条，未命中 " + miss + " 条（未命中的需要联网翻译，或自己填）。");
        }

        private List<Entry> CollectPendingEntries()
        {
            var pending = new List<Entry>();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.Target == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.NewName))
                {
                    pending.Add(entry);
                }
            }

            return pending;
        }

        private void TestConnection()
        {
            if (busy)
            {
                Log("正在翻译中，等结束再测。");
                return;
            }

            ApplyFieldsToSettings();
            settings.Save();

            busy = true;
            UpdateToolbarState();
            Log("正在测试连接：" + settings.Endpoint + " / " + settings.Model);

            var client = new TranslatorClient(settings);
            EditorCoroutineHost.Start(RunTestRoutine(client));
        }

        private IEnumerator RunTestRoutine(TranslatorClient client)
        {
            bool ok = false;
            string message = null;

            yield return client.TestConnection((result, text) =>
            {
                ok = result;
                message = text;
            });

            busy = false;
            UpdateToolbarState();
            Log((ok ? "测试成功：" : "测试失败：") + message);
        }

        // ------------------------------------------------------------------ 安全预检

        private void RunSafetyCheck()
        {
            if (busy)
            {
                return;
            }

            var targets = new List<GameObject>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Target != null)
                {
                    targets.Add(entries[i].Target);
                }
            }

            if (targets.Count == 0)
            {
                Log("列表里没有可检查的对象。");
                return;
            }

            busy = true;
            UpdateToolbarState();

            bool canceled;
            List<SafetyFinding> findings = RenameSafetyChecker.Check(targets, out canceled);

            busy = false;
            lastFindings = findings;

            // 取消了就不能算检查过，否则后面会拿着半份结果去改名。
            if (!canceled)
            {
                safetyCheckStale = false;
            }

            UpdateToolbarState();

            if (canceled)
            {
                Log("安全预检被取消，结果不完整。");
                return;
            }

            ReportFindings(findings, true);
        }

        private void ReportFindings(List<SafetyFinding> findings, bool showDialog)
        {
            int danger = 0;
            int warning = 0;
            int info = 0;
            StringBuilder details = new StringBuilder();

            for (int i = 0; i < findings.Count; i++)
            {
                SafetyFinding finding = findings[i];
                switch (finding.Level)
                {
                    case FindingLevel.Danger: danger++; break;
                    case FindingLevel.Warning: warning++; break;
                    default: info++; break;
                }

                if (details.Length < 6000)
                {
                    details.AppendLine("  " + finding.Describe());
                }
            }

            if (findings.Count == 0)
            {
                Log("安全预检通过：没有发现按名字或路径引用这些对象的地方。");
            }
            else
            {
                Log("安全预检完成：危险 " + danger + " 条，注意 " + warning + " 条，提示 " + info + " 条。");
                Log(details.ToString().TrimEnd());
            }

            if (!showDialog)
            {
                return;
            }

            if (danger > 0)
            {
                EditorUtility.DisplayDialog(
                    "安全预检：发现危险引用",
                    "有 " + danger + " 处是按名字/路径引用这些对象的：\n\n" +
                    "动画剪辑会找不到对象，代码里的 Find(...) 会失效。\n\n" +
                    "详细清单已经写到下面日志里。\n" +
                    "如果这些引用确实存在，改名前请先想清楚；确认要改就点「应用改名」，会再问一次。",
                    "知道了");
            }
            else if (warning > 0)
            {
                EditorUtility.DisplayDialog(
                    "安全预检：有需要注意的地方",
                    "有 " + warning + " 处序列化路径字段可能引用这些对象（比如 MA / 换装组件的路径配置）。\n\n" +
                    "详细清单在下面日志里。这些不一定会坏，但建议看一眼。",
                    "知道了");
            }
        }

        // ------------------------------------------------------------------ 应用改名

        private void ApplyRename()
        {
            if (busy)
            {
                return;
            }

            var ready = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || entry.Target == null)
                {
                    continue;
                }

                string newName = (entry.NewName ?? string.Empty).Trim();
                if (newName.Length == 0)
                {
                    continue;
                }

                if (newName == entry.Target.name)
                {
                    continue;
                }

                entry.NewName = newName;
                ready.Add(entry);
            }

            if (ready.Count == 0)
            {
                Log("没有需要改名的条目。译文为空或和原名一样。");
                return;
            }

            if (safetyCheckStale && !RunSafetyCheckBeforeApply())
            {
                Log("安全预检被取消，已停止应用。要改名请重新点「安全预检」并等它跑完。");
                return;
            }

            int danger = 0;
            if (lastFindings != null)
            {
                for (int i = 0; i < lastFindings.Count; i++)
                {
                    if (lastFindings[i].Level == FindingLevel.Danger)
                    {
                        danger++;
                    }
                }
            }

            if (danger > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "仍然要改名吗？",
                    "安全预检发现了 " + danger + " 处危险引用（动画路径或代码 Find）。\n\n" +
                    "改名后这些地方会失效。\n\n" +
                    "确定要继续的话点「继续改名」，否则点「取消」。",
                    "继续改名",
                    "取消");

                if (!proceed)
                {
                    Log("已取消应用。");
                    return;
                }
            }

            int applied = 0;
            int assetTouched = 0;
            StringBuilder report = new StringBuilder();

            undoRecords.Clear();

            for (int i = 0; i < ready.Count; i++)
            {
                Entry entry = ready[i];
                GameObject go = entry.Target;

                if (go == null)
                {
                    continue;
                }

                string oldName = go.name;
                string newName = entry.NewName;

                if (oldName == newName)
                {
                    continue;
                }

                undoRecords.Add(new UndoRecord { Target = go, OldName = oldName });

                Undo.RecordObject(go, "层级名翻译");
                go.name = newName;

                bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(go) &&
                                     !PrefabUtility.IsPartOfPrefabInstance(go);

                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go);
                }
                else
                {
                    EditorUtility.SetDirty(go);

                    if (isPrefabAsset)
                    {
                        assetTouched++;
                    }
                }

                if (go.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }

                entry.OriginalName = oldName;
                entry.StatusText = "已改名";
                entry.StatusKind = ColorOk;

                applied++;
                report.AppendLine("  " + oldName + " → " + newName);
            }

            if (applied == 0)
            {
                Log("实际没有改动。");
                return;
            }

            if (assetTouched > 0)
            {
                AssetDatabase.SaveAssets();
            }

            safetyCheckStale = true;
            RebuildList();
            UpdateToolbarState();

            Log("已应用 " + applied + " 项改名：");
            Log(report.ToString().TrimEnd());

            if (assetTouched > 0)
            {
                Log("其中 " + assetTouched + " 项属于 Prefab 资源，已经保存到磁盘。");
            }

            Log("场景里的改名需要按 Ctrl+S 保存。改错了点「撤销改名」可以还原（Ctrl+Z 也能撤）。");
        }

        private bool RunSafetyCheckBeforeApply()
        {
            var targets = new List<GameObject>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Target != null)
                {
                    targets.Add(entries[i].Target);
                }
            }

            if (targets.Count == 0)
            {
                lastFindings = new List<SafetyFinding>();
                safetyCheckStale = false;
                return true;
            }

            bool canceled;
            lastFindings = RenameSafetyChecker.Check(targets, out canceled);

            if (canceled)
            {
                return false;
            }

            safetyCheckStale = false;
            ReportFindings(lastFindings, false);
            return true;
        }

        private void RevertRename()
        {
            if (busy)
            {
                return;
            }

            if (undoRecords.Count == 0)
            {
                Log("没有可撤销的记录。只有用「应用改名」改过的才记录在这里。");
                return;
            }

            int reverted = 0;
            StringBuilder report = new StringBuilder();

            for (int i = undoRecords.Count - 1; i >= 0; i--)
            {
                UndoRecord record = undoRecords[i];
                if (record == null || record.Target == null)
                {
                    continue;
                }

                string currentName = record.Target.name;

                Undo.RecordObject(record.Target, "撤销层级名翻译");
                record.Target.name = record.OldName;

                if (PrefabUtility.IsPartOfPrefabInstance(record.Target))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(record.Target);
                }
                else
                {
                    EditorUtility.SetDirty(record.Target);
                }

                if (record.Target.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(record.Target.scene);
                }

                report.AppendLine("  " + currentName + " → " + record.OldName);
                reverted++;
            }

            undoRecords.Clear();
            AssetDatabase.SaveAssets();
            safetyCheckStale = true;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.Target != null)
                {
                    entry.OriginalName = entry.Target.name;
                    entry.NewName = string.Empty;
                    entry.StatusText = "待翻译";
                    entry.StatusKind = ColorOk;
                }
            }

            RebuildList();
            UpdateToolbarState();

            Log("已撤销 " + reverted + " 项改名：");
            Log(report.ToString().TrimEnd());
        }

        // ------------------------------------------------------------------ 导出

        private void ExportCsv()
        {
            if (entries.Count == 0)
            {
                Log("列表是空的，没有可导出的内容。");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "导出对照表",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "层级名对照表",
                "csv");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("原名,译名");

                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    sb.Append(CsvCell(entry.OriginalName)).Append(',');
                    sb.AppendLine(CsvCell(entry.NewName));
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Log("已导出对照表：" + path + "（UTF-8 带 BOM，Excel 直接打开不乱码）");
            }
            catch (Exception e)
            {
                Log("导出失败：" + e.Message);
            }
        }

        private static string CsvCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool needQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 ||
                             value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;

            if (!needQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ------------------------------------------------------------------ 设置

        private void ToggleSettings()
        {
            settingsExpanded = !settingsExpanded;
            RefreshSettingsVisibility();
        }

        private void RefreshSettingsVisibility()
        {
            if (settingsBody != null)
            {
                settingsBody.style.display = settingsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (settingsToggle != null)
            {
                settingsToggle.text = (settingsExpanded ? "▼" : "▶") + " 接口设置（API Key / 模型）";
            }
        }

        private void ApplyFieldsToSettings()
        {
            if (endpointField != null)
            {
                settings.Endpoint = endpointField.value;
            }

            if (keyField != null)
            {
                settings.ApiKey = keyField.value;
            }

            if (modelField != null)
            {
                settings.Model = modelField.value;
            }

            if (batchField != null)
            {
                settings.BatchSize = batchField.value;
            }

            if (targetLangField != null)
            {
                settings.TargetLanguage = targetLangField.value;
            }

            // 自动修正常见填错（粘了 Key 管理网页、只填了 base url 等）
            string note;
            if (settings.NormalizeEndpoint(out note) && !string.IsNullOrEmpty(note))
            {
                Log(note);

                if (endpointField != null)
                {
                    endpointField.SetValueWithoutNotify(settings.Endpoint);
                }
            }
        }

        private void ResetEndpointAndModel()
        {
            if (endpointField != null)
            {
                endpointField.SetValueWithoutNotify(TranslatorSettings.DefaultEndpoint);
            }

            if (modelField != null)
            {
                modelField.SetValueWithoutNotify(TranslatorSettings.DefaultModel);
            }

            ApplyFieldsToSettings();
            settings.Save();

            Log("已恢复默认接口地址和模型名（Key 保持不动）：" +
                settings.Endpoint + " / " + settings.Model);
        }

        private void SaveSettings()
        {
            ApplyFieldsToSettings();
            settings.Save();
            Log("设置已保存。接口：" + settings.Endpoint + " / 模型：" + settings.Model);
        }

        // ------------------------------------------------------------------ 日志与工具

        private void Log(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string stamp = DateTime.Now.ToString("HH:mm:ss");
            logText += "[" + stamp + "] " + message + "\n";

            if (logText.Length > 40000)
            {
                logText = logText.Substring(logText.Length - 30000);
            }

            RefreshLog();
        }

        private void RefreshLog()
        {
            if (logField != null)
            {
                logField.SetValueWithoutNotify(logText);
                logField.style.height = 96;
            }
        }

        private static Color Divider()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.25f, 0.25f, 1f)
                : new Color(0.7f, 0.7f, 0.7f, 1f);
        }

        private static Color Dim()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.62f, 0.62f)
                : new Color(0.42f, 0.42f, 0.42f);
        }

        private static Color StatusColor(int kind)
        {
            switch (kind)
            {
                case ColorDanger:
                    return new Color(0.95f, 0.35f, 0.32f);
                case ColorWarning:
                    return new Color(0.95f, 0.68f, 0.25f);
                default:
                    return EditorGUIUtility.isProSkin
                        ? new Color(0.6f, 0.8f, 0.6f)
                        : new Color(0.2f, 0.5f, 0.2f);
            }
        }
    }
}
