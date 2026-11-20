using System.Collections.Generic;
using System.Text;

namespace LevelNameTranslator
{
    /// <summary>
    /// 内置术语表。既用于给模型提示（保证同一批翻法一致），
    /// 也用于"离线兜底"（没有 Key 或请求失败时仍然有可用结果）。
    /// </summary>
    internal static class Glossary
    {
        /// <summary>日文服装/身体部位 → 简体中文。</summary>
        internal static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
        {
            { "アウター",       "外套" },
            { "インナー",       "内衬" },
            { "ガーター",       "袜圈" },
            { "スカートベルト", "裙腰带" },
            { "スカート本体",   "裙子本体" },
            { "ピアス",         "耳环" },
            { "ヘッドドレス",   "头饰" },
            { "下着",           "内衣" },
            { "影",             "影子" },
            { "指輪",           "戒指" },
            { "羽",             "羽毛" },
            { "靴",             "靴子" },
            { "髪",             "头发" },
            { "頭",             "头部" },
            { "目",             "眼睛" },
            { "顔",             "脸" },
            { "体",             "身体" },
            { "手",             "手" },
            { "足",             "脚" },
            { "胸",             "胸部" },
            { "尻尾",           "尾巴" },
            { "耳",             "耳朵" },
            { "首",             "脖子" },
            { "肩",             "肩" },
            { "腰",             "腰" },
            { "裾",             "下摆" },
            { "袖",             "袖子" },
            { "襟",             "领子" },
            { "紐",             "系带" },
            { "留め具",         "扣具" },
            { "ベルト",         "腰带" },
            { "リストバンド",   "腕带" },
            { "ネックレス",     "项链" },
            { "チョーカー",     "颈环" },
            { "リボン",         "蝴蝶结" },
            { "フリル",         "荷叶边" },
            { "ボタン",         "纽扣" },
            { "ポケット",       "口袋" },
            { "ファスナー",     "拉链" },
            { "靴下",           "袜子" },
            { "手袋",           "手套" },
            { "帽子",           "帽子" },
            { "もふもふ",       "毛绒" },
        };

        /// <summary>常见英文部件名 → 简体中文。</summary>
        internal static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "body",       "身体" },
            { "hair",       "头发" },
            { "head",       "头部" },
            { "face",       "脸" },
            { "eye",        "眼睛" },
            { "eyes",       "眼睛" },
            { "mouth",      "嘴" },
            { "ear",        "耳朵" },
            { "ears",       "耳朵" },
            { "tail",       "尾巴" },
            { "arm",        "手臂" },
            { "arms",       "手臂" },
            { "leg",        "腿" },
            { "legs",       "腿" },
            { "hand",       "手" },
            { "hands",      "手" },
            { "foot",       "脚" },
            { "feet",       "脚" },
            { "neck",       "脖子" },
            { "shoulder",   "肩" },
            { "chest",      "胸部" },
            { "back",       "后片" },
            { "front",      "前片" },
            { "side",       "侧面" },
            { "skirt",      "裙子" },
            { "belt",       "腰带" },
            { "inner",      "内衬" },
            { "outer",      "外套" },
            { "shoe",       "鞋子" },
            { "shoes",      "鞋子" },
            { "boots",      "靴子" },
            { "sock",       "袜子" },
            { "socks",      "袜子" },
            { "glove",      "手套" },
            { "dress",      "连衣裙" },
            { "hat",        "帽子" },
            { "ribbon",     "蝴蝶结" },
            { "accessory",  "饰品" },
            { "kemono",     "兽形" },
            { "airintake",  "进气口" },
        };

        /// <summary>整段精确匹配优先（避免 "スカート本体" 被拆成 "裙子" + "本体"）。</summary>
        internal static string LookupExact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            string value;
            if (Japanese.TryGetValue(text, out value))
            {
                return value;
            }

            string lower = text.ToLowerInvariant();
            if (English.TryGetValue(lower, out value))
            {
                return value;
            }

            return null;
        }

        /// <summary>生成给模型的术语提示块。</summary>
        internal static string BuildPromptHint(int maxTerms = 40)
        {
            var sb = new StringBuilder();
            int count = 0;

            foreach (var pair in Japanese)
            {
                if (count >= maxTerms)
                {
                    break;
                }

                if (count > 0)
                {
                    sb.Append('、');
                }

                sb.Append(pair.Key).Append('=').Append(pair.Value);
                count++;
            }

            return sb.ToString();
        }
    }
}
