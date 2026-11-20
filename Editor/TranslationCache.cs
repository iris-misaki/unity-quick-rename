using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LevelNameTranslator
{
    /// <summary>
    /// 翻译结果缓存。存到项目根的 Library/ 下（Unity 的临时目录，不进版本管理）。
    /// Key = 模型 + 目标语言 + 原文 的 SHA256，所以换模型/换语言不会串。
    /// </summary>
    internal static class TranslationCache
    {
        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class EntryList
        {
            public List<Entry> items = new List<Entry>();
        }

        private static Dictionary<string, string> map;
        private static bool dirty;
        private static string cachePath;
        private static float lastSaveTime;

        private static string CacheFilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(cachePath))
                {
                    return cachePath;
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string dir = Path.Combine(projectRoot, "Library", "LevelNameTranslator");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                cachePath = Path.Combine(dir, "translation-cache.json");
                return cachePath;
            }
        }

        private static void EnsureLoaded()
        {
            if (map != null)
            {
                return;
            }

            map = new Dictionary<string, string>();

            try
            {
                string path = CacheFilePath;
                if (!File.Exists(path))
                {
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                var data = JsonUtility.FromJson<EntryList>(json);
                if (data == null || data.items == null)
                {
                    return;
                }

                for (int i = 0; i < data.items.Count; i++)
                {
                    Entry e = data.items[i];
                    if (e != null && !string.IsNullOrEmpty(e.key))
                    {
                        map[e.key] = e.value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[层级名翻译器] 读取缓存失败，本次不使用缓存：" + e.Message);
                map = new Dictionary<string, string>();
            }
        }

        private static string MakeKey(string model, string targetLanguage, string source)
        {
            string raw = (model ?? string.Empty) + "\u0001" + (targetLanguage ?? string.Empty) + "\u0001" + (source ?? string.Empty);

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public static bool TryGet(string model, string targetLanguage, string source, out string translation)
        {
            EnsureLoaded();
            translation = null;

            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            return map.TryGetValue(MakeKey(model, targetLanguage, source), out translation);
        }

        public static void Put(string model, string targetLanguage, string source, string translation)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            map[MakeKey(model, targetLanguage, source)] = translation;
            dirty = true;

            // 避免每翻一条就写盘，最多 5 秒落一次。
            if (Time.realtimeSinceStartup - lastSaveTime > 5f)
            {
                Save();
            }
        }

        public static void Save()
        {
            if (!dirty || map == null)
            {
                return;
            }

            try
            {
                var data = new EntryList();
                foreach (var pair in map)
                {
                    data.items.Add(new Entry { key = pair.Key, value = pair.Value });
                }

                File.WriteAllText(CacheFilePath, JsonUtility.ToJson(data), Encoding.UTF8);
                dirty = false;
                lastSaveTime = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[层级名翻译器] 写入缓存失败：" + e.Message);
            }
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return map.Count;
            }
        }
    }
}
