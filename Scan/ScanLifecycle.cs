#nullable enable
using System;
using System.Collections.Generic;
using BTitles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Scan
{
    /*
    热重载生命周期管理
    运行时扫描热卸载、配色热重载、检测函数清理
    不包含扫描注册逻辑（BiomeRegistrar.Run）
    */
    internal static class ScanLifecycle
    {
        /*
        运行时配色热重载：响应配置变更事件
        开启时用群系返回色自动配色，关闭时统一白色+黑色描边
        */
        internal static void Restyle(bool enableStyling)
        {
            if (BiomeRegistrar.ScannedBiomes == null || BiomeRegistrar.ScannedBiomes.Count == 0)
                return;

            var instance = BTitlesLocalizationPatch.InstanceField?.GetValue(null);
            if (instance == null)
                return;

            var biomeDict =
                BTitlesLocalizationPatch.BiomeDictField?.GetValue(instance)
                as Dictionary<string, BiomeEntry>;
            if (biomeDict == null)
                return;

            int styled = 0;
            foreach (var (dictKey, modBiome) in BiomeRegistrar.ScannedBiomes)
            {
                // 跳过预存条目：不覆盖 BTitles 原有的配色
                if (
                    BiomeRegistrar.PreScanKeys != null
                    && BiomeRegistrar.PreScanKeys.Contains(dictKey)
                )
                    continue;

                if (!biomeDict.TryGetValue(dictKey, out var entry))
                    continue;

                // 扫描记录中的 modBiome 引用一般不会 null，但防御留一手
                if (modBiome == null)
                    continue;

                if (enableStyling)
                {
                    BiomeStyleHelper.GetTitleColors(
                        modBiome,
                        out Color titleColor,
                        out Color strokeColor
                    );
                    entry.TitleColor = titleColor;
                    entry.StrokeColor = strokeColor;
                }
                else
                {
                    // 关闭配色时统一用白色标题 + 黑色描边
                    entry.TitleColor = Color.White;
                    entry.StrokeColor = Color.Black;
                }
                styled++;
            }

            if (styled > 0)
            {
                string key = enableStyling ? "RestyledOn" : "RestyledOff";
                Diagnostics.DebugLog.Info(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.{key}",
                        styled
                    )
                );
            }
        }

        /*
        运行时扫描热卸载：从 BTitles 字典移除本次扫描新增的条目，清理检测函数
        */
        internal static void UnregisterAll(Mod mod)
        {
            if (BiomeRegistrar.ScannedBiomes == null || BiomeRegistrar.ScannedBiomes.Count == 0)
            {
                // 没有扫描记录，只清理检测函数防止残留
                Cleanup();
                return;
            }

            var instance = BTitlesLocalizationPatch.InstanceField?.GetValue(null);
            if (instance == null)
                return;

            var biomeDict =
                BTitlesLocalizationPatch.BiomeDictField?.GetValue(instance)
                as Dictionary<string, BiomeEntry>;
            if (biomeDict == null)
                return;

            // 移除本次扫描新增的字典条目（快照中不存在的键）
            if (BiomeRegistrar.PreScanKeys != null)
            {
                int removed = 0;
                foreach (var (dictKey, _) in BiomeRegistrar.ScannedBiomes)
                {
                    if (
                        dictKey != null
                        && !BiomeRegistrar.PreScanKeys.Contains(dictKey)
                        && biomeDict.Remove(dictKey)
                    )
                        removed++;
                }
                if (removed > 0)
                    mod.Logger.Info(
                        Language.GetTextValue(
                            $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.UnregisteredBiomes",
                            removed
                        )
                    );
            }

            // 移除检测函数
            Cleanup();
            BiomeRegistrar.ScannedBiomes = null;
            BiomeRegistrar.PreScanKeys = null;
        }

        /*
        清理注册的检测函数，在 Unload 时从 BTitles 的 BiomeCheckFunctions 列表中移除
        防止热重载时重复累积 lambda，导致无效的额外检测调用
        */
        public static void Cleanup()
        {
            if (BiomeRegistrar.RegisteredCheckFunc == null)
                return;

            try
            {
                var instance = BTitlesLocalizationPatch.InstanceField?.GetValue(null);
                if (instance == null)
                    return;

                var checkFuncs =
                    BTitlesLocalizationPatch.CheckFuncsField?.GetValue(instance)
                    as List<Func<Player, string>>;
                if (checkFuncs == null)
                    return;

                checkFuncs.Remove(BiomeRegistrar.RegisteredCheckFunc);
                // 只在成功移除后才释放引用；移除失败时保留引用，下次清理可重试
                BiomeRegistrar.RegisteredCheckFunc = null;
            }
            catch (Exception ex)
            {
                // 如果有一天 BTitles 突然复活更新了，这条日志会告诉我们需要改什么
                // 不移除 RegisteredCheckFunc 引用，保留以在下次清理时重试
                Diagnostics.DebugLog.Warn($"Cleanup: failed to remove check func - {ex.Message}");
            }
        }

        // 清理扫描记录和快照，供 Unload 或重新扫描时使用
        internal static void ClearScannedBiomes()
        {
            BiomeRegistrar.ScannedBiomes = null;
            BiomeRegistrar.PreScanKeys = null;
        }
    }
}
