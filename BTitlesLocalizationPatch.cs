using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using BTitles;
using BTitlesLocalizationPatch.Diagnostics;

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
		private Hook _getActualTitleNameHook;
		private bool _loaded;           // 防止 Load 被重复调用

		// 反射字段缓存（供 BiomeRegistrar 使用）
		internal static FieldInfo InstanceField { get; private set; }
		internal static FieldInfo BiomeDictField { get; private set; }
		internal static FieldInfo CheckFuncsField { get; private set; }

		// 反映 Hook 是否成功安装，Unload 时验证
		internal static bool HookInstalled { get; private set; }

		public override void Load()
		{
			if (Main.dedServ) return;

			// 防御：防止意外重复加载
			if (_loaded)
			{
				Logger.Warn(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.LoadReentry"));
				return;
			}
			_loaded = true;

			// 初始化调试日志写入器（按级别对应 tModLoader Logger 的 Info/Warn/Error）
			DebugLog.InfoWriter = s => Logger.Info(s);
			DebugLog.WarnWriter = s => Logger.Warn(s);
			DebugLog.ErrorWriter = s => Logger.Error(s);

			// 预缓存反射字段，失败时打 Warn 方便排查
			InstanceField = typeof(BiomeTitlesMod).GetField("Instance",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			if (InstanceField == null)
				Logger.Warn(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ReflectInstanceNull"));

			BiomeDictField = typeof(BiomeTitlesMod).GetField("BiomeDictionary",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (BiomeDictField == null)
				Logger.Warn(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ReflectDictNull"));

			CheckFuncsField = typeof(BiomeTitlesMod).GetField("BiomeCheckFunctions",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (CheckFuncsField == null)
				Logger.Warn(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ReflectCheckerNull"));

			// 注册 GetActualTitleName Hook
			MethodInfo getTitleMethod = typeof(BiomeTitlesUI)
				.GetMethod("GetActualTitleName", BindingFlags.NonPublic | BindingFlags.Instance);

			if (getTitleMethod != null)
			{
				_getActualTitleNameHook = new Hook(
					getTitleMethod,
					new Func<Func<BiomeTitlesUI, BiomeEntry, string>, BiomeTitlesUI, BiomeEntry, string>(
						BiomeNameHook.GetActualTitleNamePrefixHook)
				);
				HookInstalled = true;
				Diagnostics.DebugLog.Info(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.HookApplied"));
			}
			else
			{
				HookInstalled = false;
				Logger.Error(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.HookGetTitleNameFailed"));
			}
		}

		public override void PostSetupContent()
		{
			if (Main.dedServ) return;

			var config = ModContent.GetInstance<BTitlesConfig>();

			if (config.EnableScan)
			{
				Logger.Info(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.ScanEnabled"));
				Scan.BiomeRegistrar.Run(this, config.EnableAutoStyling);
			}
		}

		public override void Unload()
		{
			_getActualTitleNameHook?.Dispose();
			_getActualTitleNameHook = null;
			HookInstalled = false;
			InstanceField = null;
			BiomeDictField = null;
			CheckFuncsField = null;
			_loaded = false;

			// 清理颜色采样缓存
			BiomeNameHook.ClearColorCache();
		}
	}
}
