using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 下面这些数据类靠 JsonUtility 反射填充，编译器看不到赋值。
#pragma warning disable 0649

namespace LevelNameTranslator
{
    /// <summary>
    /// OpenAI 兼容的 chat/completions 客户端。
    /// 所有请求都走编辑器协程，主线程不被阻塞。
    /// </summary>
    internal class TranslatorClient
    {
        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChatChoice
        {
            public ChatMessage message;
        }

        [Serializable]
        private class ErrorBody
        {
            public string message;
        }

        [Serializable]
        private class ChatResponse
        {
            public ChatChoice[] choices;
            public ErrorBody error;
        }

        [Serializable]
        private class TranslationPayload
        {
            public string[] translations;
        }

        private readonly TranslatorSettings settings;

        public TranslatorClient(TranslatorSettings settings)
        {
            this.settings = settings;
        }

        // ---------------------------------------------------------------- 公共入口

        /// <summary>
        /// 批量翻译。会依次处理所有批次，逐条回调结果。
        /// onEach 的第二参为 null 表示该条翻译失败。
        /// </summary>
        public IEnumerator TranslateAll(
            List<string> sources,
            Action<int, string> onEach,
            Action<string> onError,
            Action<int, int> onProgress)
        {
            if (sources == null || sources.Count == 0)
            {
                yield break;
            }

            if (!settings.HasApiKey)
            {
                if (onError != null)
                {
                    onError("还没有填 API Key。请在上方设置里填入后点「保存设置」。");
                }

                yield break;
            }

            int total = sources.Count;
            int done = 0;
            int batchSize = Mathf.Clamp(settings.BatchSize, 1, 64);

            for (int start = 0; start < total; start += batchSize)
            {
                int end = Mathf.Min(start + batchSize, total);
                int count = end - start;

                var batch = new List<string>(count);
                var indices = new List<int>(count);
                for (int i = start; i < end; i++)
                {
                    batch.Add(sources[i]);
                    indices.Add(i);
                }

                string[] results = null;
                string lastError = null;

                // 整批请求，最多 3 次
                for (int attempt = 0; attempt < 3 && results == null; attempt++)
                {
                    results = null;
                    lastError = null;

                    yield return RequestBatch(batch, r => results = r, e => lastError = e);

                    if (results == null && attempt < 2)
                    {
                        yield return 1f + attempt * 2f;
                    }
                }

                if (results != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (onEach != null)
                        {
                            onEach(indices[i], results[i]);
                        }
                    }

                    done += count;
                    if (onProgress != null)
                    {
                        onProgress(done, total);
                    }
                }
                else
                {
                    // 整批失败：改为逐条再试一次，避免一条坏数据拖垮整批
                    Debug.LogWarning("[层级名翻译器] 整批请求失败，改为逐条重试。原因：" + lastError);

                    for (int i = 0; i < count; i++)
                    {
                        string[] single = null;
                        string singleError = null;

                        for (int attempt = 0; attempt < 2 && single == null; attempt++)
                        {
                            single = null;
                            yield return RequestBatch(
                                new List<string> { batch[i] },
                                r => single = r,
                                e => singleError = e);

                            if (single == null && attempt < 1)
                            {
                                yield return 0.6f;
                            }
                        }

                        string translation = null;
                        if (single != null && single.Length > 0)
                        {
                            translation = single[0];
                        }

                        if (onEach != null)
                        {
                            onEach(indices[i], translation);
                        }

                        if (translation == null && onError != null && i == 0)
                        {
                            onError(singleError ?? lastError ?? "请求失败");
                        }

                        done++;
                        if (onProgress != null)
                        {
                            onProgress(done, total);
                        }
                    }
                }
            }

            TranslationCache.Save();
        }

        /// <summary>测试连接：发一条最简单的请求。</summary>
        public IEnumerator TestConnection(Action<bool, string> onDone)
        {
            if (!settings.HasApiKey)
            {
                if (onDone != null)
                {
                    onDone(false, "还没有填 API Key。");
                }

                yield break;
            }

            string[] result = null;
            string error = null;

            yield return RequestBatch(
                new List<string> { "Body" },
                r => result = r,
                e => error = e);

            if (onDone != null)
            {
                if (result != null && result.Length > 0)
                {
                    onDone(true, "连接成功，返回：" + result[0]);
                }
                else
                {
                    onDone(false, error ?? "未知错误");
                }
            }
        }

        // ---------------------------------------------------------------- 请求

        private IEnumerator RequestBatch(List<string> names, Action<string[]> onSuccess, Action<string> onError)
        {
            string body = BuildRequestBody(names);
            byte[] payload = Encoding.UTF8.GetBytes(body);

            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(settings.Endpoint, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + settings.ApiKey);
                request.timeout = 90;
            }
            catch (Exception e)
            {
                if (request != null)
                {
                    request.Dispose();
                }

                if (onError != null)
                {
                    onError("请求创建失败：" + e.Message);
                }

                yield break;
            }

            // 显式轮询，不依赖驱动器对 AsyncOperation 的支持
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                yield return null;
            }

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : null;
            long status = request.responseCode;
            UnityWebRequest.Result result = request.result;
            string transportError = request.error;
            request.Dispose();

            if (result != UnityWebRequest.Result.Success)
            {
                if (onError != null)
                {
                    onError(DescribeFailure(result, status, transportError, responseText));
                }

                yield break;
            }

            string[] translations = ExtractTranslations(responseText, names.Count);

            if (translations == null)
            {
                if (onError != null)
                {
                    onError("返回内容无法解析（可能模型没按 JSON 返回）。原始内容片段：" +
                            Truncate(responseText, 200));
                }

                yield break;
            }

            if (onSuccess != null)
            {
                onSuccess(translations);
            }
        }

        private static string DescribeFailure(
            UnityWebRequest.Result result,
            long status,
            string transportError,
            string body)
        {
            if (status == 401 || status == 403)
            {
                return "认证失败（HTTP " + status + "）：Key 无效、没权限，或填的地址不是这个 Key 对应的服务。";
            }

            if (status == 404)
            {
                return "接口不存在（HTTP 404）：地址可能填错了，应该形如 https://api.xxx.com/v1/chat/completions";
            }

            if (status == 429)
            {
                return "请求太频繁或额度用尽（HTTP 429）。可以调小「每批数量」或稍后再试。";
            }

            if (status >= 500)
            {
                return "服务端错误（HTTP " + status + "）。稍后重试一般能恢复。";
            }

            var sb = new StringBuilder();
            sb.Append("请求失败");

            if (status > 0)
            {
                sb.Append("（HTTP ").Append(status).Append("）");
            }

            if (!string.IsNullOrEmpty(transportError))
            {
                sb.Append("：").Append(transportError);
            }

            if (status <= 0 && string.IsNullOrEmpty(transportError) && string.IsNullOrEmpty(body))
            {
                sb.Append("：连不上服务器（没有收到任何响应）。");
                sb.Append("可能是断网、代理/VPN 拦截、防火墙，或证书校验失败。");
                sb.Append("如果开着代理软件，试着把 api.deepseek.com 设为直连，或者临时关掉代理再测。");
            }

            sb.Append("  [结果=").Append(result).Append(']');

            if (!string.IsNullOrEmpty(body))
            {
                sb.Append(" | ").Append(Truncate(body, 200));

                if (body.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.Append("  提示：模型名可能不对。model 取值大小写敏感，要照服务商文档里的 id 原样填，");
                    sb.Append("DeepSeek 目前是 deepseek-flash 或 deepseek-v4-pro。");
                }
            }

            return sb.ToString();
        }

        private string BuildRequestBody(List<string> names)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"model\":\"").Append(JsonEscape(settings.Model)).Append("\",");
            sb.Append("\"temperature\":0.1,");
            sb.Append("\"stream\":false,");
            sb.Append("\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":\"").Append(JsonEscape(BuildSystemPrompt())).Append("\"},");
            sb.Append("{\"role\":\"user\",\"content\":\"").Append(JsonEscape(BuildUserPrompt(names))).Append("\"}");
            sb.Append("]}");
            return sb.ToString();
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("You localize Unity GameObject names into ");
            sb.Append(settings.TargetLanguage);
            sb.Append(" for a VRChat avatar modding project.\n");
            sb.Append("Hard rules:\n");
            sb.Append("1. Output must be a short object name: no spaces, no punctuation, no quotes, no markdown.\n");
            sb.Append("2. If the name ends with a numbering suffix such as \".002\" or \" (1)\", keep that exact suffix at the end.\n");
            sb.Append("3. If the name is already ");
            sb.Append(settings.TargetLanguage);
            sb.Append(", return it unchanged.\n");
            sb.Append("4. If the name has no translatable meaning (pure technical identifier such as \"PB\", \"N001\", \"skinned\"), return it unchanged.\n");
            sb.Append("5. The same source term must always map to the same target word inside your reply.\n");
            sb.Append("6. Return JSON only. No explanation, no code fence.");
            return sb.ToString();
        }

        private string BuildUserPrompt(List<string> names)
        {
            var sb = new StringBuilder();
            sb.Append("Reference glossary (prefer these renderings): ");
            sb.Append(Glossary.BuildPromptHint());
            sb.Append("\n\n");
            sb.Append("Translate the following ");
            sb.Append(names.Count);
            sb.Append(" names.\n");
            sb.Append("Return exactly this JSON shape with exactly ");
            sb.Append(names.Count);
            sb.Append(" strings, in the same order:\n");
            sb.Append("{\"translations\": [\"...\"]}\n\n");
            sb.Append("Input:\n[\n");

            for (int i = 0; i < names.Count; i++)
            {
                sb.Append("  \"").Append(JsonEscape(names[i])).Append('"');
                if (i < names.Count - 1)
                {
                    sb.Append(',');
                }

                sb.Append('\n');
            }

            sb.Append(']');
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 解析

        private static string[] ExtractTranslations(string responseText, int expected)
        {
            if (string.IsNullOrEmpty(responseText))
            {
                return null;
            }

            string content = null;

            try
            {
                ChatResponse response = JsonUtility.FromJson<ChatResponse>(responseText);
                if (response != null && response.choices != null && response.choices.Length > 0 &&
                    response.choices[0] != null && response.choices[0].message != null)
                {
                    content = response.choices[0].message.content;
                }
            }
            catch (Exception)
            {
                // 下面直接尝试整段当内容解析
            }

            if (string.IsNullOrEmpty(content))
            {
                content = responseText;
            }

            content = StripCodeFence(content).Trim();

            return ParseTranslationArray(content, expected);
        }

        private static string[] ParseTranslationArray(string content, int expected)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            // 1) 标准形状 {"translations": [...]}
            TranslationPayload payload = TryParseObject(content);
            if (payload != null && payload.translations != null)
            {
                return AcceptIfValid(payload.translations, expected);
            }

            // 2) 裸数组 [...]
            int left = content.IndexOf('[');
            int right = content.LastIndexOf(']');
            if (left >= 0 && right > left)
            {
                string array = content.Substring(left, right - left + 1);
                TranslationPayload wrapped = TryParseObject("{\"translations\":" + array + "}");
                if (wrapped != null && wrapped.translations != null)
                {
                    return AcceptIfValid(wrapped.translations, expected);
                }
            }

            // 3) 逐行兜底：模型没给 JSON，但每行一个结果
            string[] lines = content.Split('\n');
            var collected = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('-', '*', '"', ',', ' ', '\t', '\r');
                if (line.Length > 0 && line.IndexOf(':') < 0)
                {
                    collected.Add(line);
                }
            }

            if (collected.Count == expected)
            {
                return collected.ToArray();
            }

            return null;
        }

        private static TranslationPayload TryParseObject(string json)
        {
            try
            {
                return JsonUtility.FromJson<TranslationPayload>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string[] AcceptIfValid(string[] values, int expected)
        {
            if (values == null)
            {
                return null;
            }

            if (values.Length != expected)
            {
                return null;
            }

            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (value == null)
                {
                    return null;
                }

                value = value.Trim();
                if (value.Length == 0)
                {
                    return null;
                }

                result[i] = value;
            }

            return result;
        }

        private static string StripCodeFence(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0)
            {
                return trimmed;
            }

            string inner = trimmed.Substring(firstNewline + 1);
            int closing = inner.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
            {
                inner = inner.Substring(0, closing);
            }

            return inner.Trim();
        }

        // ---------------------------------------------------------------- 工具

        internal static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string single = text.Replace('\n', ' ').Replace('\r', ' ');
            return single.Length <= max ? single : single.Substring(0, max) + "...";
        }
    }
}
