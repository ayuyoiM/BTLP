using BTitles;
using BTitlesLocalizationPatch.Scan;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Terraria.Localization;

namespace BTitlesLocalizationPatch
{
    /*
	GetActualTitleName 的替换逻辑
	5 步回退链，命中即返回
	1. 用户自定义改名          → Config.CustomBiomeNames
	2. 源模组本地化            → Mods.{Scope}.Biomes.{Key}.DisplayName（跳过原版 Terraria）
	3. BTitles 自身翻译        → Mods.BiomeTitles.Title.{Scope}.{Key}
	4. 补丁模组补充翻译        → Mods.BTitlesLocalizationPatch.ExtraTitles.{Scope}.{Key}
	5. 回退原始 Title          → biomeEntry.Title
	*/
    internal static class BiomeNameHook
    {   
        public static string GetActualTitleNamePrefixHook(
            Func<BiomeTitlesUI, BiomeEntry, string> orig,
            BiomeTitlesUI self,
            BiomeEntry biomeEntry)
        {
            // 防止空引用（外部模组调用时参数可能异常）
            if (self == null || biomeEntry == null)
                return "";

            // 防御：Hook 未正确安装时静默降级，不抛异常
            if (!BTitlesLocalizationPatch.HookInstalled)
                return biomeEntry.Title;

            // 防御：Config 可能为 null（BTitles 版本变更或未初始化）
            var config = self.Config;

            // 预清理 Key 中的空格，供本地化键名拼接使用
            string key = biomeEntry.Key ?? "";
            string sanitizedKey = key.Replace(" ", "_");

            // 进出群系时日志输出，方便排查注册/显示问题
            Diagnostics.DebugLog.Info(
                $"Biome: Key={biomeEntry.Key} Title={biomeEntry.Title} Scope={biomeEntry.LocalizationScope}");

            // 回退配色：无图标也无颜色时用 HSL 色盘兜底（极少数边缘情况）
            if (biomeEntry.TitleColor == default)
            {
                Color fallback = BiomeStyleHelper.GetFallbackColor(
                    biomeEntry.Key ?? biomeEntry.Title ?? "");
                biomeEntry.TitleColor = fallback;
                biomeEntry.StrokeColor = new Color(
                    (int)(fallback.R * 0.35f),
                    (int)(fallback.G * 0.35f),
                    (int)(fallback.B * 0.35f));
                Diagnostics.DebugLog.Info(
                    $"Fallback: [{biomeEntry.Key}] → ({fallback.R},{fallback.G},{fallback.B})");
            }

            // 1. 用户自定义生物群系名称（Config 可能为 null）
            string customName = config?.CustomBiomeNames?.FirstOrDefault(
                n => n.CurrentName == biomeEntry.Title)?.NewName;
            if (customName != null) return customName;

            // 2. 读源模组本地化 Mods.{Scope}.Biomes.{Key}.DisplayName（跳过原版 Terraria）
            if (biomeEntry.LocalizationScope != "Terraria"
                && Language.ActiveCulture.Name != "en-US")
            {
                string modLocKey = $"Mods.{biomeEntry.LocalizationScope}" +
                    $".Biomes.{sanitizedKey}.DisplayName";

                if (Language.Exists(modLocKey))
                {
                    string translatedName = Language.GetTextValue(modLocKey);

                    // 防御：本地化键存在但值为空时继续回退，避免界面显示空白
                    if (!string.IsNullOrEmpty(translatedName))
                    {
                        // 对翻译结果再应用一次自定义改名（config 可能为 null）
                        customName = config?.CustomBiomeNames?
                            .FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

                        return customName ?? translatedName;
                    }
                }
            }

            // 3. BTitles 自身翻译
            if (Language.ActiveCulture.Name != "en-US")
            {
                string btitlesKey = $"Mods.BiomeTitles.Title." +
                    $"{biomeEntry.LocalizationScope}.{sanitizedKey}";

                if (Language.Exists(btitlesKey))
                {
                    string translatedName = Language.GetTextValue(btitlesKey);

                    // 防御：本地化键存在但值为空时继续回退，避免界面显示空白
                    if (!string.IsNullOrEmpty(translatedName))
                    {
                        // 对翻译结果再应用一次自定义改名（config 可能为 null）
                        customName = config?.CustomBiomeNames?
                            .FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

                        return customName ?? translatedName;
                    }
                }
            }

            // 4. 补丁模组补充翻译（不修改 BTitles 即可补充缺失翻译，如 Aether）
            if (Language.ActiveCulture.Name != "en-US")
            {
                string extraKey = $"Mods.{nameof(BTitlesLocalizationPatch)}" +
                    $".ExtraTitles.{biomeEntry.LocalizationScope}.{sanitizedKey}";

                if (Language.Exists(extraKey))
                {
                    string translatedName = Language.GetTextValue(extraKey);

                    // 防御：本地化键存在但值为空时继续回退，避免界面显示空白
                    if (!string.IsNullOrEmpty(translatedName))
                    {
                        // 对翻译结果再应用一次自定义改名（config 可能为 null）
                        customName = config?.CustomBiomeNames?
                            .FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

                        return customName ?? translatedName;
                    }
                }
            }

            // 5. 回退到原始方法（调用 orig 以兼容未来版本的新逻辑）
            return orig(self, biomeEntry);
        }
    }
}
