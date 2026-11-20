using System;
using UnityEditor;
using UnityEngine;

namespace LevelNameTranslator
{
    /// <summary>
    /// 插件设置。存在 EditorPrefs（本机注册表），不写进工程目录，
    /// 所以不会被打包、也不会被 git 提交。
    /// </summary>
    internal class TranslatorSettings
    {
        private const string KeyEndpoint = "LevelNameTranslator.Endpoint";
        private const string KeyApiKey = "LevelNameTranslator.ApiKey";
        private const string KeyModel = "LevelNameTranslator.Model";
        private const string KeyBatch = "LevelNameTranslator.BatchSize";
        private const string KeyTarget = "LevelNameTranslator.TargetLanguage";

        public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";
        public const string DefaultModel = "deepseek-flash";

        /// <summary>完整的 chat/completions 地址。</summary>
        public string Endpoint = DefaultEndpoint;

        /// <summary>API Key（Bearer）。</summary>
        public string ApiKey = string.Empty;

        /// <summary>模型名。</summary>
        public string Model = DefaultModel;

        /// <summary>每次请求带多少条名字。</summary>
        public int BatchSize = 24;

        /// <summary>目标语言。</summary>
        public string TargetLanguage = "简体中文";

        public static TranslatorSettings Load()
        {
            var s = new TranslatorSettings();
            s.Endpoint = EditorPrefs.GetString(KeyEndpoint, DefaultEndpoint);
            s.ApiKey = EditorPrefs.GetString(KeyApiKey, string.Empty);
            s.Model = EditorPrefs.GetString(KeyModel, DefaultModel);
            s.BatchSize = EditorPrefs.GetInt(KeyBatch, 24);
            s.TargetLanguage = EditorPrefs.GetString(KeyTarget, "简体中文");
            s.Sanitize();
            return s;
        }

        public void Save()
        {
            Sanitize();
            EditorPrefs.SetString(KeyEndpoint, Endpoint);
            EditorPrefs.SetString(KeyApiKey, ApiKey);
            EditorPrefs.SetString(KeyModel, Model);
            EditorPrefs.SetInt(KeyBatch, BatchSize);
            EditorPrefs.SetString(KeyTarget, TargetLanguage);
        }

        private void Sanitize()
        {
            ApiKey = (ApiKey ?? string.Empty).Trim();
            Model = (Model ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(Model))
            {
                Model = DefaultModel;
            }

            if (string.IsNullOrEmpty(TargetLanguage))
            {
                TargetLanguage = "简体中文";
            }

            BatchSize = Mathf.Clamp(BatchSize, 1, 64);

            string note;
            NormalizeEndpoint(out note);
        }

        /// <summary>
        /// 把用户填的接口地址修成真正的 chat/completions 地址。
        /// 最常见的错误是粘了 Key 管理网页（platform.deepseek.com/api_keys）或者只填了 base url。
        /// 返回 true 表示改过地址，note 里是说明。
        /// </summary>
        public bool NormalizeEndpoint(out string note)
        {
            note = null;

            string raw = (Endpoint ?? string.Empty).Trim().Trim('"', '\'').Trim();

            if (raw.Length == 0)
            {
                Endpoint = DefaultEndpoint;
                note = "接口地址是空的，已填入默认地址。";
                return true;
            }

            // 已经是完整地址就原样保留
            if (raw.IndexOf("chat/completions", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Endpoint = raw;
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Endpoint = DefaultEndpoint;
                note = "接口地址不是合法网址，已改成默认地址：" + DefaultEndpoint;
                return true;
            }

            string host = uri.Host.ToLowerInvariant();

            if (host.Contains("deepseek"))
            {
                Endpoint = DefaultEndpoint;
                note = "这是 DeepSeek 的网页地址，不是 API 接口。已改成：" + DefaultEndpoint;
                return true;
            }

            string path = uri.AbsolutePath.TrimEnd('/');

            if (path.Length == 0 || path == "/")
            {
                Endpoint = uri.GetLeftPart(UriPartial.Authority) + "/v1/chat/completions";
            }
            else
            {
                Endpoint = uri.GetLeftPart(UriPartial.Authority) + path + "/chat/completions";
            }

            note = "接口地址缺少 /chat/completions，已补成：" + Endpoint;
            return true;
        }

        public bool HasApiKey
        {
            get { return !string.IsNullOrEmpty(ApiKey); }
        }
    }
}
