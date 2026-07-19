#nullable enable
using BTitles;
using BTitlesLocalizationPatch.Scan;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.Localization;

namespace BTitlesLocalizationPatch
{
    internal static class BiomeNameHook
    {
        // 只缓存不受 BTitles 配置影响的翻译链结果（步骤 1-3/5）
        // 自定义覆盖（步骤 4）每次重新查配置，保证配置变更即时生效
        internal static readonly ConcurrentDictionary<
            string,
            (string Text, string Tag)
        > TranslationCache = new();
        private static volatile string _lastCultureName = "";

        public static string GetActualTitleNamePrefixHook(
            Func<BiomeTitlesUI, BiomeEntry, string> orig,
            BiomeTitlesUI self,
            BiomeEntry biomeEntry
        )
        {
            if (self == null || biomeEntry == null)
                return "";
            if (!BTitlesLocalizationPatch.HookInstalled)
                return biomeEntry.Title ?? "";

            var config = self.Config;
            string key = biomeEntry.Key ?? "";
            string sanitizedKey = key.Replace(" ", "_");
            string safeScope = (biomeEntry.LocalizationScope ?? "").Replace(".", "_");
            bool hasValidKey = !string.IsNullOrEmpty(sanitizedKey);

            string cacheKey = $"{safeScope}|{sanitizedKey}";
            string currentCulture = Language.ActiveCulture.Name;

            // 语言变更时清空缓存
            if (_lastCultureName != currentCulture)
            {
                _lastCultureName = currentCulture;
                TranslationCache.Clear();
            }

            // 缓存命中：从基础翻译查自定义覆盖
            if (hasValidKey && TranslationCache.TryGetValue(cacheKey, out var cached))
            {
                string displayName = ApplyCustomOverride(cached.Text, config) ?? cached.Text;
                string tag = displayName == cached.Text ? cached.Tag : "CustomName";
                LogBiomeEntry(biomeEntry.Key ?? "", displayName, biomeEntry, tag);
                return displayName;
            }

            // 回退配色：无图标且 TitleColor 透明时用 HSL 色盘
            if (biomeEntry.Icon == null && biomeEntry.TitleColor == default)
            {
                Color fallback = BiomeStyleHelper.GetFallbackColor(
                    biomeEntry.Key ?? biomeEntry.Title ?? ""
                );
                biomeEntry.TitleColor = fallback;
                biomeEntry.StrokeColor = new Color(
                    (int)(fallback.R * 0.35f),
                    (int)(fallback.G * 0.35f),
                    (int)(fallback.B * 0.35f)
                );
                Diagnostics.DebugLog.Info(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.FallbackColor",
                        biomeEntry.Key ?? "",
                        fallback.R,
                        fallback.G,
                        fallback.B
                    )
                );
            }

            // 翻译回退链：先命中者胜出
            string? baseText = null;
            string? baseTag = null;

            // 1. 源模组本地化
            if (hasValidKey && currentCulture != "en-US")
                (baseText, baseTag) = TryStep1(
                    sanitizedKey,
                    safeScope,
                    biomeEntry.LocalizationScope
                );

            // 2. BTitles 自身翻译
            if (baseText == null && hasValidKey && currentCulture != "en-US")
                (baseText, baseTag) = TryLocalizedSteps(
                    "BTitles",
                    $"Mods.BiomeTitles.Title.{safeScope}.{sanitizedKey}"
                );

            // 3. 补丁模组补充翻译
            if (baseText == null && hasValidKey && currentCulture != "en-US")
                (baseText, baseTag) = TryLocalizedSteps(
                    "ExtraTitles",
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.ExtraTitles.{safeScope}.{sanitizedKey}"
                );

            // 4. 自定义覆盖
            string? overridden = baseText != null ? ApplyCustomOverride(baseText, config) : null;
            if (overridden != null)
            {
                // 基础翻译仍缓存，下次重新检查配置
                if (hasValidKey && baseText != null)
                    TranslationCache[cacheKey] = (baseText, baseTag ?? "Unknown");
                LogBiomeEntry(biomeEntry.Key ?? "", overridden, biomeEntry, "CustomName");
                return overridden;
            }
            if (baseText != null)
            {
                LogAndCache(biomeEntry, cacheKey, hasValidKey, baseText, baseTag ?? "Unknown");
                return baseText;
            }

            // 英文环境 / 链全未命中：用原始标题匹配自定义覆盖
            if (config != null)
            {
                string title = biomeEntry.Title ?? "";
                var match = config.CustomBiomeNames?.FirstOrDefault(n => n.CurrentName == title);
                if (match != null)
                {
                    if (hasValidKey)
                        TranslationCache[cacheKey] = (title, "Fallback");
                    LogBiomeEntry(biomeEntry.Key ?? "", match.NewName, biomeEntry, "CustomName");
                    return match.NewName;
                }
            }

            // 5. 回退原始标题
            return LogAndCache(
                biomeEntry,
                cacheKey,
                hasValidKey,
                biomeEntry.Title ?? "",
                "Fallback"
            );
        }

        // ── 翻译链各步骤 ───────────────────────────────────

        private static (string? Text, string? Tag) TryStep1(
            string sanitizedKey,
            string safeScope,
            string? scope
        )
        {
            string modLocKey =
                scope == "Terraria"
                    ? $"Mods.BiomeTitles.Title.Terraria.{sanitizedKey}"
                    : $"Mods.{safeScope}.Biomes.{sanitizedKey}.DisplayName";

            if (TryLocalized(modLocKey) is { } localized)
            {
                if (scope == "Terraria")
                    return (localized, "BTitles");

                string? englishValue = GetEnglishTranslation(modLocKey);
                if (englishValue != null && englishValue != localized)
                    return (localized, "ModLocalization");
            }
            return (null, null);
        }

        private static (string? Text, string? Tag) TryLocalizedSteps(string tag, string locKey)
        {
            if (TryLocalized(locKey) is { } localized)
                return (localized, tag);
            return (null, null);
        }

        // ── 自定义覆盖 ─────────────────────────────────────

        // BTitles 的 GeneralConfig 已有 ProjectReference，直接类型访问
        private static string? ApplyCustomOverride(string currentName, object? configObj)
        {
            if (configObj is not GeneralConfig config || config.CustomBiomeNames == null)
                return null;

            var match = config.CustomBiomeNames.FirstOrDefault(n => n.CurrentName == currentName);
            if (match == null)
                return null;

            Diagnostics.DebugLog.Info(
                Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.CustomOverride",
                    currentName,
                    match.NewName
                )
            );
            return match.NewName;
        }

        // ── 日志与缓存 ─────────────────────────────────────

        private static string LogAndCache(
            BiomeEntry entry,
            string cacheKey,
            bool hasValidKey,
            string text,
            string tag
        )
        {
            if (hasValidKey && !string.IsNullOrEmpty(text))
                TranslationCache[cacheKey] = (text, tag);
            LogBiomeEntry(entry.Key ?? "", text, entry, tag);
            return text;
        }

        private static void LogBiomeEntry(string key, string text, BiomeEntry entry, string tag)
        {
            Diagnostics.DebugLog.Info(
                Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.BiomeEntry",
                    key,
                    text
                )
            );
            string iconInfo =
                entry.Icon != null
                    ? Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.IconPresent",
                        entry.Icon.Width,
                        entry.Icon.Height
                    )
                    : Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.IconNull"
                    );
            Diagnostics.DebugLog.Info(
                Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.BiomeDetail",
                    entry.LocalizationScope ?? "",
                    iconInfo,
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.SourceTags.{tag}"
                    )
                )
            );
            Diagnostics.DebugLog.Info(
                Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.BiomeColor",
                    entry.TitleColor.R,
                    entry.TitleColor.G,
                    entry.TitleColor.B,
                    entry.StrokeColor.R,
                    entry.StrokeColor.G,
                    entry.StrokeColor.B
                )
            );
        }

        // ── 本地化辅助 ─────────────────────────────────────

        private static string? TryLocalized(string locKey)
        {
            if (Language.Exists(locKey))
            {
                string value = Language.GetTextValue(locKey);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            return null;
        }

        // 反射访问 LocalizedText._translations，取英文值对比判断是否有真实翻译
        private static string? GetEnglishTranslation(string locKey)
        {
            var localizedText = LanguageManager.Instance.GetText(locKey);
            if (localizedText == null)
                return null;

            try
            {
                var field = typeof(LocalizedText).GetField(
                    "_translations",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                if (
                    field?.GetValue(localizedText) is Dictionary<string, string> translations
                    && translations.TryGetValue("en-US", out var englishValue)
                    && !string.IsNullOrEmpty(englishValue)
                )
                    return englishValue;
            }
            catch { }
            return null;
        }
    }
}
