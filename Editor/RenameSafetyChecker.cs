using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LevelNameTranslator
{
    internal enum FindingLevel
    {
        Info = 0,
        Warning = 1,
        Danger = 2
    }

    internal class SafetyFinding
    {
        public FindingLevel Level;
        public string TargetName;
        public string Message;
        public string File;
        public int Line;

        /// <summary>true 表示这条危险可以通过「自动修正动画路径」消除。</summary>
        public bool FixableByPathRewrite;

        public string Describe()
        {
            var sb = new StringBuilder();

            switch (Level)
            {
                case FindingLevel.Danger: sb.Append("[危险] "); break;
                case FindingLevel.Warning: sb.Append("[注意] "); break;
                default: sb.Append("[提示] "); break;
            }

            sb.Append(TargetName).Append(" — ");

            if (FixableByPathRewrite)
            {
                sb.Append("[动画路径候选，需确认] ");
            }

            sb.Append(Message);

            if (!string.IsNullOrEmpty(File))
            {
                sb.Append("  (").Append(File);
                if (Line > 0)
                {
                    sb.Append(':').Append(Line);
                }

                sb.Append(')');
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 改名前的安全预检（A 方案：代码引用 + 动画路径 + 序列化路径字段）。
    ///
    /// 检查的是"层级名"有没有被别的东西按字符串/路径引用。
    /// 按对象引用（fileID）绑定的东西不会断，所以不报警。
    /// </summary>
    internal static class RenameSafetyChecker
    {
        private static readonly HashSet<string> ScannedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".anim", ".cs", ".prefab", ".unity", ".asset", ".controller", ".overrideController"
        };

        private static readonly Regex AnimPathRegex =
            new Regex(@"^[ \t]*(?:m_)?path:[ \t]*(.+?)[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex FindCallRegex =
            new Regex(@"\bFind\s*\(\s*""([^""]*)""", RegexOptions.Compiled);

        private const long MaxFileBytes = 16L * 1024L * 1024L;
        private const int MaxScannedFiles = 20000;

        public static List<SafetyFinding> Check(IList<GameObject> targets, out bool canceled)
        {
            canceled = false;
            var findings = new List<SafetyFinding>();

            if (targets == null || targets.Count == 0)
            {
                return findings;
            }

            // 收集要检查的名字，同时做掉空引用。
            var nameSet = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject go = targets[i];
                if (go == null)
                {
                    continue;
                }

                string name = go.name;
                if (string.IsNullOrEmpty(name) || nameSet.ContainsKey(name))
                {
                    continue;
                }

                nameSet.Add(name, name);
            }

            if (nameSet.Count == 0)
            {
                return findings;
            }

            // YAML 会把非 ASCII 名字转义成 \uXXXX，所以额外准备一份转义形态用于匹配。
            Dictionary<string, string> escapedSet = BuildEscapedForms(nameSet);

            AddAnimatorContextFindings(targets, findings);

            string assetsRoot = Application.dataPath;
            List<string> files = CollectFiles(assetsRoot);

            var dangerKey = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                for (int index = 0; index < files.Count; index++)
                {
                    if ((index & 31) == 0)
                    {
                        string shortPath = files[index].Substring(assetsRoot.Length).TrimStart('/', '\\');
                        float progress = files.Count <= 0 ? 0f : (float)index / files.Count;

                        if (EditorUtility.DisplayCancelableProgressBar(
                                "层级名翻译器 · 安全预检",
                                shortPath,
                                progress))
                        {
                            canceled = true;
                            break;
                        }
                    }

                    string file = files[index];
                    string extension = Path.GetExtension(file);

                    FileInfo info;
                    try
                    {
                        info = new FileInfo(file);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes)
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(file, Encoding.UTF8);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    // 廉价预筛：文件里一个待检查名字都没有就整份跳过。
                    // 注意：YAML 里非 ASCII 名字会写成 \uXXXX 转义，所以两种形式都要试。
                    if (!ContainsAny(text, nameSet, escapedSet))
                    {
                        continue;
                    }

                    string relative = file.Substring(assetsRoot.Length).TrimStart('/', '\\');

                    switch (extension.ToLowerInvariant())
                    {
                        case ".anim":
                            ScanAnimClip(text, relative, nameSet, findings, dangerKey);
                            break;
                        case ".cs":
                            ScanSourceCode(text, relative, nameSet, findings, dangerKey);
                            break;
                        default:
                            ScanSerializedYaml(text, relative, nameSet, findings, dangerKey);
                            break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            findings.Sort(CompareFindings);
            return findings;
        }

        private static int CompareFindings(SafetyFinding a, SafetyFinding b)
        {
            int levelCompare = ((int)b.Level).CompareTo((int)a.Level);
            if (levelCompare != 0)
            {
                return levelCompare;
            }

            return string.Compare(a.TargetName, b.TargetName, StringComparison.Ordinal);
        }

        private static List<string> CollectFiles(string assetsRoot)
        {
            var files = new List<string>();

            if (!Directory.Exists(assetsRoot))
            {
                return files;
            }

            try
            {
                foreach (string file in Directory.GetFiles(assetsRoot, "*.*", SearchOption.AllDirectories))
                {
                    if (!ScannedExtensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    files.Add(file);

                    if (files.Count >= MaxScannedFiles)
                    {
                        Debug.LogWarning("[层级名翻译器] 待扫描文件超过 " + MaxScannedFiles + " 个，后面的文件被跳过。");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[层级名翻译器] 收集文件时出错，预检结果可能不完整：" + e.Message);
            }

            return files;
        }

        private static bool ContainsAny(
            string text,
            Dictionary<string, string> nameSet,
            Dictionary<string, string> escapedSet)
        {
            foreach (var pair in nameSet)
            {
                if (text.IndexOf(pair.Key, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            if (escapedSet != null)
            {
                foreach (var pair in escapedSet)
                {
                    if (text.IndexOf(pair.Key, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>把名字转成 Unity YAML 里可能出现的形式（非 ASCII → \uXXXX，大写十六进制）。</summary>
        private static Dictionary<string, string> BuildEscapedForms(Dictionary<string, string> nameSet)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in nameSet)
            {
                string escaped = EscapeYamlName(pair.Key);
                if (!string.Equals(escaped, pair.Key, StringComparison.Ordinal))
                {
                    result[escaped] = pair.Key;
                }
            }

            return result;
        }

        private static string EscapeYamlName(string name)
        {
            var sb = new StringBuilder(name.Length + 8);

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                if (c > 127)
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>把 YAML 字符串里的 \uXXXX 等转义还原成真实字符。</summary>
        private static string UnescapeYamlString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            {
                return value;
            }

            var sb = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];

                    if ((next == 'u' || next == 'U') && i + 5 < value.Length)
                    {
                        int code;
                        if (int.TryParse(
                                value.Substring(i + 2, 4),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture,
                                out code))
                        {
                            sb.Append((char)code);
                            i += 5;
                            continue;
                        }
                    }
                    else if (next == 'n') { sb.Append('\n'); i++; continue; }
                    else if (next == 't') { sb.Append('\t'); i++; continue; }
                    else if (next == '"') { sb.Append('"'); i++; continue; }
                    else if (next == '\\') { sb.Append('\\'); i++; continue; }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------ 动画

        private static void ScanAnimClip(
            string text,
            string relative,
            Dictionary<string, string> nameSet,
            List<SafetyFinding> findings,
            HashSet<string> dangerKey)
        {
            MatchCollection matches = AnimPathRegex.Matches(text);

            for (int i = 0; i < matches.Count; i++)
            {
                string rawPath = matches[i].Groups[1].Value.Trim().Trim('"', '\'');

                // YAML 里非 ASCII 名字是 \uXXXX 转义，先还原再按 / 拆段比较
                rawPath = UnescapeYamlString(rawPath);

                if (rawPath.Length == 0)
                {
                    continue;
                }

                int line = LineOf(text, matches[i].Index);
                string[] segments = rawPath.Split('/');

                for (int s = 0; s < segments.Length; s++)
                {
                    string segment = segments[s].Trim();
                    if (segment.Length == 0 || !nameSet.ContainsKey(segment))
                    {
                        continue;
                    }

                    string key = "anim|" + segment + "|" + relative;
                    if (!dangerKey.Add(key))
                    {
                        continue;
                    }

                    findings.Add(new SafetyFinding
                    {
                        Level = FindingLevel.Danger,
                        TargetName = segment,
                        Message = "这个动画片段按层级路径绑定它（路径：" + rawPath + "），改名后动画会找不到对象",
                        File = relative,
                        Line = line,
                        FixableByPathRewrite = true
                    });
                }
            }
        }

        // ------------------------------------------------------------ 代码

        private static void ScanSourceCode(
            string text,
            string relative,
            Dictionary<string, string> nameSet,
            List<SafetyFinding> findings,
            HashSet<string> dangerKey)
        {
            MatchCollection matches = FindCallRegex.Matches(text);

            for (int i = 0; i < matches.Count; i++)
            {
                string literal = matches[i].Groups[1].Value;
                if (literal.Length == 0)
                {
                    continue;
                }

                string[] segments = literal.Split('/');
                string candidate = segments[segments.Length - 1].Trim();

                if (candidate.Length == 0 || !nameSet.ContainsKey(candidate))
                {
                    continue;
                }

                string key = "code|" + candidate + "|" + relative;
                if (!dangerKey.Add(key))
                {
                    continue;
                }

                findings.Add(new SafetyFinding
                {
                    Level = FindingLevel.Danger,
                    TargetName = candidate,
                    Message = "代码里用 Find(\"" + literal + "\") 按名字找它，改名后会找不到",
                    File = relative,
                    Line = LineOf(text, matches[i].Index)
                });
            }
        }

        // ------------------------------------------------------------ 序列化数据

        private static void ScanSerializedYaml(
            string text,
            string relative,
            Dictionary<string, string> nameSet,
            List<SafetyFinding> findings,
            HashSet<string> dangerKey)
        {
            string[] lines = text.Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                // YAML 里非 ASCII 名字是 \uXXXX 转义，还原后再匹配
                string line = lines[index];
                if (line.IndexOf('\\') >= 0)
                {
                    line = UnescapeYamlString(line);
                }

                if (line.Length == 0 || line.Length > 2000)
                {
                    continue;
                }

                // 只关心"看起来像路径"的行：带斜杠并且带引号或 路径字段名。
                if (line.IndexOf('/') < 0)
                {
                    continue;
                }

                if (line.IndexOf('"') < 0 && line.IndexOf('\'') < 0)
                {
                    continue;
                }

                bool isPathKey = line.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("path:", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var pair in nameSet)
                {
                    string name = pair.Key;
                    if (line.IndexOf(name, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    // 必须是以 "/名字" 或 "名字/" 的形式出现，才可能是路径引用。
                    bool pathLike = line.IndexOf("/" + name, StringComparison.Ordinal) >= 0 ||
                                    line.IndexOf(name + "/", StringComparison.Ordinal) >= 0;

                    if (!pathLike)
                    {
                        continue;
                    }

                    string key = "yaml|" + name + "|" + relative;
                    if (!dangerKey.Add(key))
                    {
                        break;
                    }

                    findings.Add(new SafetyFinding
                    {
                        Level = isPathKey ? FindingLevel.Warning : FindingLevel.Warning,
                        TargetName = name,
                        Message = "某个组件把它的层级路径存成了字符串，改名后这条配置可能失联",
                        File = relative,
                        Line = index + 1
                    });

                    break;
                }
            }
        }

        // ------------------------------------------------------------ 上下文提示

        private static void AddAnimatorContextFindings(IList<GameObject> targets, List<SafetyFinding> findings)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < targets.Count; i++)
            {
                GameObject go = targets[i];
                if (go == null)
                {
                    continue;
                }

                if (!reported.Add(go.name))
                {
                    continue;
                }

                bool hasAnimator = go.GetComponent<Animator>() != null;
                if (!hasAnimator)
                {
                    Transform current = go.transform.parent;
                    int depth = 0;
                    while (current != null && depth < 32)
                    {
                        if (current.GetComponent<Animator>() != null)
                        {
                            hasAnimator = true;
                            break;
                        }

                        current = current.parent;
                        depth++;
                    }
                }

                if (hasAnimator)
                {
                    findings.Add(new SafetyFinding
                    {
                        Level = FindingLevel.Info,
                        TargetName = go.name,
                        Message = "这个对象在 Animator 层级下。如果动画引用它，改名会影响动画路径（下面会列出具体片段）",
                        File = null,
                        Line = 0
                    });
                }
            }
        }

        private static int LineOf(string text, int charIndex)
        {
            int line = 1;
            int limit = Mathf.Min(charIndex, text.Length);

            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
