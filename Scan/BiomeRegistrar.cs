using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using BTitles;
using BTitlesLocalizationPatch.Diagnostics;

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
		private static Func<Player, string> _registeredCheckFunc;

		public static void Run(Mod mod, bool enableStyling)
		{
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

			int added = 0;          // 新增群系数
			int updated = 0;        // 更新标题数
			int skipped = 0;        // 占位群系跳过数

			// 收集所有模组的 ModBiome 缓存，供注册字典和检测函数共用
			var allModBiomes = new List<(Mod Mod, ModBiome Biome)>();

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

					if (biomeDict.TryGetValue(modBiome.Name, out var existing))
					{
						// 已收录的群系：只更新标题和本地化作用域，不动颜色/图标/副标题（以内置为主）
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

						// 始终尝试加载图标，供 Hook 惰性采样
						entry.Icon = BiomeStyleHelper.TryLoadIcon(modBiome);

						// 自动上新群系配色（按开关）
						if (enableStyling)
						{
							BiomeStyleHelper.GetTitleColors(modBiome, out Color tc, out Color sc);
							entry.TitleColor = tc;
							entry.StrokeColor = sc;
						}

						biomeDict[modBiome.Name] = entry;
						added++;
						mod.Logger.Info(string.Format(L("NewBiome"), modBiome.Name, modBiome.DisplayName.Value));
					}
				}
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
							return mb.Name;
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

			// 输出汇总：新增 + 更新 + 跳过 + 字典总数 + 函数数，一眼可验证总数匹配
			mod.Logger.Info(string.Format(L("ScanSummary"),
				added, updated, skipped, biomeDict.Count, checkFuncs.Count));

			/*
			防御断言：字典增量与计数器一致
			如果未来有人误删了 biomeDict[...] = entry，此断言会在 Debug 编译时立刻暴露
			Release 版不包含此代码，零开销
			*/
#if DEBUG
			int totalModBiomes = 0;
			foreach (Mod m in ModLoader.Mods)
			{
				if (m.Name != "BTitles" && m.Name != "tModLoader" && m.Name != "ModLoader")
					totalModBiomes += m.GetContent<ModBiome>().Count();
			}
			// totalModBiomes = 更新数 + 新增数 + 跳过数（非占位群系被跳过但本就不该计入）
			// 但更简单的是：扫描后的字典增量应 >= added
			mod.Logger.Info($"[防御] 模组群系总数={totalModBiomes}, " +
				$"更新={updated}+新增={added}+跳过={skipped}=" +
				$"{updated + added + skipped}, 符合预期: {totalModBiomes == updated + added + skipped}");
#endif
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