#nullable enable
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
            BiomeEntry biomeEntry
        )
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
            string iconInfo =
                biomeEntry.Icon != null
                    ? $"Icon=✓({biomeEntry.Icon.Width}x{biomeEntry.Icon.Height})"
                    : "Icon=✗(null)";
            Diagnostics.DebugLog.Info(
                $"Biome: Key={biomeEntry.Key} Title={biomeEntry.Title} Scope={biomeEntry.LocalizationScope} {iconInfo}"
            );

            // 回退配色：无图标且 TitleColor 为透明（default）时用 HSL 色盘兜底
            // 防御：BiomeEntry 初始化后 TitleColor 默认为 Color.White，仅对显式设为透明的条目生效
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
                    $"Fallback: [{biomeEntry.Key}] → ({fallback.R},{fallback.G},{fallback.B})"
                );
            }

            /*
            翻译回退辅助：查询本地化键，非空时应用自定义改名
            三个回退步骤（源模组本地化/BTitles 翻译/补丁补充翻译）共用同一逻辑
            提取为局部函数集中一处：
            - 避免三份重复代码同步修改的维护风险
            - 非防御性编码，仅为结构化重构，零运行时开销
            */
            string? TryLocalized(string locKey)
            {
                if (Language.Exists(locKey))
                {
                    string translatedName = Language.GetTextValue(locKey);

                    // 防御：本地化键存在但值为空时继续回退，避免界面显示空白
                    if (!string.IsNullOrEmpty(translatedName))
                    {
                        // 对翻译结果再应用一次自定义改名（config 可能为 null）
                        return config
                                ?.CustomBiomeNames?.FirstOrDefault(n =>
                                    n.CurrentName == translatedName
                                )
                                ?.NewName
                            ?? translatedName;
                    }
                }
                return null;
            }

            // 1. 用户自定义生物群系名称（Config 可能为 null）
            string? firstHit = config
                ?.CustomBiomeNames?.FirstOrDefault(n => n.CurrentName == biomeEntry.Title)
                ?.NewName;
            if (firstHit != null)
                return firstHit;

            // 2. 读源模组本地化 Mods.{Scope}.Biomes.{Key}.DisplayName（跳过原版 Terraria）
            if (
                biomeEntry.LocalizationScope != "Terraria"
                && Language.ActiveCulture.Name != "en-US"
            )
            {
                string modLocKey =
                    $"Mods.{biomeEntry.LocalizationScope}" + $".Biomes.{sanitizedKey}.DisplayName";
                string? result = TryLocalized(modLocKey);
                /*
                防御：仅当翻译结果与 BTitles 已有标题不同时才采纳
                tModLoader 为每个 ModBiome 自动注册 DisplayName 键，Language.Exists 永远返回 true
                但值可能是自动生成的 PascalCase 转可读文本，并非模组显式翻译
                若翻译结果与 biomeEntry.Title 相同，说明标题已正确，继续回退给步骤 3/4 机会
                */
                if (result != null && result != biomeEntry.Title)
                    return result;
            }

            // 3. BTitles 自身翻译
            if (Language.ActiveCulture.Name != "en-US")
            {
                string btitlesKey =
                    $"Mods.BiomeTitles.Title." + $"{biomeEntry.LocalizationScope}.{sanitizedKey}";
                string? result = TryLocalized(btitlesKey);
                if (result != null)
                    return result;
            }

            // 4. 补丁模组补充翻译（不修改 BTitles 即可补充缺失翻译，如 Aether）
            if (Language.ActiveCulture.Name != "en-US")
            {
                string extraKey =
                    $"Mods.{nameof(BTitlesLocalizationPatch)}"
                    + $".ExtraTitles.{biomeEntry.LocalizationScope}.{sanitizedKey}";
                string? result = TryLocalized(extraKey);
                if (result != null)
                    return result;
            }

            // 5. 回退到原始方法（调用 orig 以兼容未来版本的新逻辑）
            return orig(self, biomeEntry);
        }
    }
}
