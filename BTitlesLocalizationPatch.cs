#nullable enable
using System;
using System.Linq;
using System.Reflection;
using BTitles;
using BTitlesLocalizationPatch.Diagnostics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch
{
    /*
    BTitles 本地化增强补丁
    1. 用 ModBiome.DisplayName 覆盖已注册群系的标题
    2. 自动发现各模组未收录的 ModBiome 并注册到 BTitles
    3. 增强 GetActualTitleName：源模组本地化、补丁补充翻译
    */
    public class BTitlesLocalizationPatch : Mod
    {
        private Hook? _getActualTitleNameHook;
        private volatile bool _loaded;

        // volatile: 后台线程（PostSetupContent）读 Load（主线程）写入的最新值
        private static volatile FieldInfo? _instanceField;
        private static volatile FieldInfo? _biomeDictField;
        private static volatile FieldInfo? _checkFuncsField;
        internal static FieldInfo? InstanceField => _instanceField;
        internal static FieldInfo? BiomeDictField => _biomeDictField;
        internal static FieldInfo? CheckFuncsField => _checkFuncsField;

        // volatile: BiomeNameHook（UI 线程）读，Load（主线程）写
        internal static volatile bool HookInstalled;

        private static readonly Type[] _expectedHookSignature = [typeof(BiomeEntry)];

        public override void Load()
        {
            if (Main.dedServ)
                return;

            if (_loaded)
            {
                Logger.Warn("Mod Load called again, skipped");
                return;
            }
            _loaded = true;

            InstallDebugLogger();
            CacheReflectionFields();
            InstallHookWithFingerprint();
        }

        /*
        DebugLog InfoWriter/WarnWriter/ErrorWriter 对应 tModLoader Logger 级别
        Unload 时清空防止热重载残留引用
        */
        private void InstallDebugLogger()
        {
            DebugLog.InfoWriter = s => Logger.Info(s!);
            DebugLog.WarnWriter = s => Logger.Warn(s!);
            DebugLog.ErrorWriter = s => Logger.Error(s!);
        }

        // 预缓存反射字段，失败时不影响功能只打 Warn
        private void CacheReflectionFields()
        {
            _instanceField = typeof(BiomeTitlesMod).GetField(
                "Instance",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (_instanceField == null)
                Logger.Warn("Failed to reflect Instance field");

            _biomeDictField = typeof(BiomeTitlesMod).GetField(
                "BiomeDictionary",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (_biomeDictField == null)
                Logger.Warn("Failed to reflect BiomeDictionary field");

            _checkFuncsField = typeof(BiomeTitlesMod).GetField(
                "BiomeCheckFunctions",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (_checkFuncsField == null)
                Logger.Warn("Failed to reflect BiomeCheckFunctions field");
        }

        // 签名指纹验证：防止 BTitles 更新改变方法签名导致 new Hook() 崩溃
        private void InstallHookWithFingerprint()
        {
            MethodInfo? getTitleMethod = typeof(BiomeTitlesUI).GetMethod(
                "GetActualTitleName",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (getTitleMethod == null)
            {
                HookInstalled = false;
                Logger.Error("Failed to find BiomeTitlesUI.GetActualTitleName");
                return;
            }

            if (!ValidateMethodSignature(getTitleMethod, _expectedHookSignature))
            {
                HookInstalled = false;
                Logger.Error(
                    $"Hook signature mismatch: actual {FormatMethodSignature(getTitleMethod)}, expected BiomeEntry"
                );
                return;
            }

            _getActualTitleNameHook = new Hook(
                getTitleMethod,
                new Func<
                    Func<BiomeTitlesUI, BiomeEntry, string>,
                    BiomeTitlesUI,
                    BiomeEntry,
                    string
                >(BiomeNameHook.GetActualTitleNamePrefixHook)
            );
            HookInstalled = true;
            Logger.Info("GetActualTitleName Detour patch applied successfully");
        }

        private static bool ValidateMethodSignature(MethodInfo method, Type[] expectedParamTypes)
        {
            var actualParams = method.GetParameters();
            if (actualParams.Length != expectedParamTypes.Length)
                return false;
            for (int i = 0; i < actualParams.Length; i++)
            {
                if (actualParams[i].ParameterType != expectedParamTypes[i])
                    return false;
            }
            return true;
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            var paramNames = method
                .GetParameters()
                .Select(p =>
                {
                    string name = p.ParameterType.IsByRef
                        ? p.ParameterType.GetElementType()?.Name ?? "?"
                        : p.ParameterType.Name;
                    string mod =
                        p.IsOut ? "out"
                        : p.ParameterType.IsByRef ? "ref"
                        : "";
                    return mod.Length > 0 ? $"{name} {mod}" : name;
                });
            return $"{method.Name}({string.Join(", ", paramNames)})";
        }

        public override void PostSetupContent()
        {
            if (Main.dedServ)
                return;

            // 防御：配置类可能为 null（极端情况下 ModContent.GetInstance 返回 null）
            var config = ModContent.GetInstance<BTitlesConfig>();
            if (config == null)
            {
                Logger.Error(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ConfigNull"
                    )
                );
                return;
            }

            if (config.EnableScan)
            {
                Logger.Info(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ScanEnabled"
                    )
                );
                Scan.BiomeRegistrar.Run(this, config.EnableAutoStyling);
            }
        }

        // tModLoader 通知配置变更后直接执行，由 ModConfig.OnChanged 调用
        // 主菜单时 BTitles Instance 为 null，操作会静默跳过
        internal static void ApplyConfigChangeStyle(bool enabled)
        {
            if (!HookInstalled)
                return;
            Scan.ScanLifecycle.Restyle(enabled);
        }

        internal static void ApplyConfigChangeScan(bool enabled)
        {
            if (!HookInstalled)
                return;

            BiomeNameHook.TranslationCache.Clear();

            if (enabled)
            {
                var config = ModContent.GetInstance<BTitlesConfig>();
                var mod = ModContent.GetInstance<BTitlesLocalizationPatch>();
                if (mod != null && config != null)
                {
                    mod.Logger.Info(
                        Language.GetTextValue(
                            $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ScanToggleOn"
                        )
                    );
                    Scan.BiomeRegistrar.Run(mod, config.EnableAutoStyling);
                }
            }
            else
            {
                var mod = ModContent.GetInstance<BTitlesLocalizationPatch>();
                if (mod != null)
                {
                    mod.Logger.Info(
                        Language.GetTextValue(
                            $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ScanToggleOff"
                        )
                    );
                    Scan.ScanLifecycle.UnregisterAll(mod);
                }
            }
        }

        public override void Unload()
        {
            // 清理翻译缓存
            BiomeNameHook.TranslationCache.Clear();

            // 单独 try-catch 包裹 Dispose，防止它抛异常阻断后续清理
            try
            {
                _getActualTitleNameHook?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Hook Dispose failed: {ex.Message}");
            }
            _getActualTitleNameHook = null;
            HookInstalled = false;
            _instanceField = null;
            _biomeDictField = null;
            _checkFuncsField = null;
            _loaded = false;

            // 从 BTitles 检测函数列表移除本模组注册的 lambda
            Scan.ScanLifecycle.Cleanup();
            // 清理扫描记录，避免热重载后残留旧引用
            Scan.ScanLifecycle.ClearScannedBiomes();

            // 清理调试日志写入器，防止卸载后残留引用
            Diagnostics.DebugLog.InfoWriter = s => { };
            Diagnostics.DebugLog.WarnWriter = s => { };
            Diagnostics.DebugLog.ErrorWriter = s => { };

            // 重置调试日志缓存，防止热重载后 3 秒内读到旧缓存值
            Diagnostics.DebugLog.ResetCache();
        }
    }
}
