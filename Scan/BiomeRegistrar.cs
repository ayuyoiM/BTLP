using BTitles;
using BTitlesLocalizationPatch.Diagnostics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Scan
{
    /*
	PostSetupContent 自动发现与注册逻辑
	- 扫描所有已加载模组的 ModBiome
	- 已收录的只更新 DisplayName，不动颜色/图标（以内置为主）
	- 未收录的注册到 BTitles 字典，按开关决定配色+图标
	- 注册唯一检测函数（Insert(0)到 BiomeCheckFunctions 最前面）
	- 走 tModLoader 原生 IsBiomeActive，大小群系通吃
	*/
    internal static class BiomeRegistrar
    {
        // 保存注册的检测函数引用，供 Unload 时移除
        // volatile：PostSetupContent（TP Worker 线程）写入，Unload（主线程）读取
        private static volatile Func<Player, string> _registeredCheckFunc;
        // 双重保险：先清理上次可能残留的委托，再注册新的
        Cleanup();

        string L(string key) => Language.GetTextValue(
            $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.{key}");

        var instance = BTitlesLocalizationPatch.InstanceField?.GetValue(null);
            if (instance == null) { mod.Logger.Error(L("InstanceNull")); return; }

            var biomeDict = BTitlesLocalizationPatch.BiomeDictField?.GetValue(instance)
                as Dictionary<string, BiomeEntry>;
            if (biomeDict == null) { mod.Logger.Error(L("DictNull")); return; }

var checkFuncs = BTitlesLocalizationPatch.CheckFuncsField?.GetValue(instance)
    as List<Func<Player, string>>;
if (checkFuncs == null) { mod.Logger.Error(L("CheckerNull")); return; }

mod.Logger.Info(string.Format(L("DictStats"), biomeDict.Count, checkFuncs.Count));

// 记录扫描前的字典 Key 快照，用于异常回滚
// 防御：复制到新 HashSet，防止外部在扫描期间修改 biomeDict 导致快照失真
var preScanKeys = new HashSet<string>(biomeDict.Keys, biomeDict.Comparer);
int added = 0;          // 新增群系数
int updated = 0;        // 更新标题数
int skipped = 0;        // 占位群系跳过数

// 收集所有模组的 ModBiome 缓存，供注册字典和检测函数共用
var allModBiomes = new List<(Mod Mod, ModBiome Biome)>();

// 记录每个群系使用的字典 Key 格式，供检测函数返回正确的键名
// Key = "{Mod}.{BiomeName}"，Value = 字典中实际使用的 Key（命名空间键或短键）
var biomeToDictKey = new Dictionary<string, string>();

try
{
    foreach (Mod otherMod in ModLoader.Mods)
    {
        if (otherMod.Name == "BTitles" || otherMod.Name == "tModLoader" || otherMod.Name == "ModLoader")
            continue;

        var modBiomes = otherMod.GetContent<ModBiome>().ToArray();
        if (modBiomes.Length == 0) continue;

        mod.Logger.Info(string.Format(L("ScanningMod"), otherMod.Name, modBiomes.Length));

        foreach (var modBiome in modBiomes)
        {
            // 跳过没有重写 IsBiomeActive 的占位群系（如嘉登实验室），注册了也检测不到
            // 用 DeclaringType 判断：子类没重写时 DeclaringType 仍是 ModBiome
            // 注意：不用 GetBaseDefinition()，因 fgpt 等模组的 ILHook 会干扰其行为
            var isBiomeActiveMethod = modBiome.GetType().GetMethod(
                "IsBiomeActive", BindingFlags.Public | BindingFlags.Instance);
            if (isBiomeActiveMethod != null &&
                isBiomeActiveMethod.DeclaringType == typeof(ModBiome))
            {
                mod.Logger.Info(string.Format(L("SkippedPlaceholder"),
                    modBiome.Name, modBiome.DisplayName.Value));
                skipped++;
                continue;
            }

            allModBiomes.Add((otherMod, modBiome));

            // 使用 "{模组名}.{群系类名}" 作为命名空间键，防止不同模组的同名群系互相覆盖
            // 同时保留对 BTitles 短键（原版 modBiome.Name）的兼容查找
            string namespacedKey = $"{otherMod.Name}.{modBiome.Name}";

            if (biomeDict.TryGetValue(namespacedKey, out var existing))
            {
                // 已收录（命名空间键）：之前扫描注册的，更新标题和本地化作用域
                biomeToDictKey[namespacedKey] = namespacedKey;
                existing.Title = modBiome.DisplayName.Value;
                existing.LocalizationScope = otherMod.Name;
                updated++;
            }
            else if (biomeDict.TryGetValue(modBiome.Name, out existing))
            {
                // 已收录（BTitles 短键）：同样更新，检测函数返回短键供 BTitles 查找
                biomeToDictKey[namespacedKey] = modBiome.Name;
                existing.Title = modBiome.DisplayName.Value;
                existing.LocalizationScope = otherMod.Name;
                updated++;
            }
            else
            {
                var entry = new BiomeEntry
                {
                    Key = modBiome.Name,
                    Title = modBiome.DisplayName.Value,
                    SubTitle = otherMod.DisplayNameClean,
                    LocalizationScope = otherMod.Name
                };

                // 加载图标（供配色采样使用）
                entry.Icon = BiomeStyleHelper.TryLoadIcon(modBiome);

                // 自动上新群系配色（按开关）；关闭时若有图标也立即采样，避免运行时 GetData
                if (enableStyling)
                {
                    BiomeStyleHelper.GetTitleColors(modBiome, out Color tc, out Color sc);
                    entry.TitleColor = tc;
                    entry.StrokeColor = sc;
                }
                else if (entry.Icon != null)
                {
                    // 运行时惰性采样前置到加载阶段
                    Color sampled = BiomeStyleHelper.SampleDominantColor(entry.Icon);
                    entry.TitleColor = sampled;
                    entry.StrokeColor = new Color(
                        (int)(sampled.R * 0.35f),
                        (int)(sampled.G * 0.35f),
                        (int)(sampled.B * 0.35f));
                }

                biomeToDictKey[namespacedKey] = namespacedKey;
                biomeDict[namespacedKey] = entry;
                added++;
                mod.Logger.Info(string.Format(L("NewBiome"), modBiome.Name, modBiome.DisplayName.Value));
            }
        }
    }
}
catch (Exception ex)
{
    // 扫描中途异常：回滚本次新增的字典条目，防止残留脏数据
    mod.Logger.Error($"Biome scan failed with exception, rolling back: {ex.Message}");
#if DEBUG
    DebugLog.Warn($"Scan rollback stack trace: {ex.StackTrace}");
#endif

    var keysToRemove = biomeDict.Keys
        .Where(k => !preScanKeys.Contains(k))
        .ToList();
    foreach (var key in keysToRemove)
        biomeDict.Remove(key);

    // 确保不残留检测函数引用
    _registeredCheckFunc = null;

    // 已修改的条目（预存键）不做回滚——只改了 Title/Scope，无结构损坏
    return;
}

/*
注册一个统一的检测函数，插到 BiomeCheckFunctions 最前面
走 tModLoader 原生 ModBiome.IsBiomeActive，大小群系都能正确识别
*/
_registeredCheckFunc = player =>
{
    foreach (var (otherMod, mb) in allModBiomes)
    {
        try
        {
            if (mb.IsBiomeActive(player))
            {
                return biomeToDictKey.TryGetValue($"{otherMod.Name}.{mb.Name}", out var dictKey) ? dictKey : mb.Name;
            }
        }
        catch (Exception ex)
        {
            // 单个群系检测异常，不影响其他群系
            DebugLog.Warn(string.Format(L("DetectionEx"), otherMod.Name, mb.Name, ex.Message));
#if DEBUG
            DebugLog.Warn($"StackTrace: {ex.StackTrace}");
#endif
        }
    }
    return "";
};
checkFuncs.Insert(0, _registeredCheckFunc);

// 预热已有群系颜色：BTitles 已有条目若有图标但无色，也在此补上，避免运行时采样
int prewarmed = 0;
foreach (var kvp in biomeDict)
{
    if (kvp.Value.TitleColor == default && kvp.Value.Icon != null)
    {
        Color sampled = BiomeStyleHelper.SampleDominantColor(kvp.Value.Icon);
        kvp.Value.TitleColor = sampled;
        kvp.Value.StrokeColor = new Color(
            (int)(sampled.R * 0.35f),
            (int)(sampled.G * 0.35f),
            (int)(sampled.B * 0.35f));
        prewarmed++;
    }
}
if (prewarmed > 0)
    mod.Logger.Info(string.Format(L("Prewarmed"), prewarmed));

// 输出汇总：新增 + 更新 + 跳过 + 字典总数 + 函数数，一眼可验证总数匹配
mod.Logger.Info(string.Format(L("ScanSummary"),
    added, updated, skipped, biomeDict.Count, checkFuncs.Count));
        }

        /*
		清理注册的检测函数，在 Unload 时从 BTitles 的 BiomeCheckFunctions 列表中移除
		防止热重载时重复累积 lambda，导致无效的额外检测调用
		*/
        public static void Cleanup()
{
    if (_registeredCheckFunc == null) return;

    try
    {
        var instance = BTitlesLocalizationPatch.InstanceField?.GetValue(null);
        if (instance == null) return;

        var checkFuncs = BTitlesLocalizationPatch.CheckFuncsField?.GetValue(instance)
            as List<Func<Player, string>>;
        if (checkFuncs == null) return;

        checkFuncs.Remove(_registeredCheckFunc);
    }
    catch (Exception ex)
    {
        // 如果有一天 BTitles 突然复活更新了，这条日志会告诉我们需要改什么
        Diagnostics.DebugLog.Warn($"Cleanup: failed to remove check func - {ex.Message}");
    }
    finally
    {
        // 确保引用一定被释放，即使反射或移除操作抛异常
        _registeredCheckFunc = null;
    }
}
    }
}