using System;
using System.Linq;
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

		// 签名指纹缓存：GetActualTitleName 当前预期参数类型列表
		private static readonly Type[] _expectedHookSignature = new[] { typeof(BiomeEntry) };

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

			// 注册 GetActualTitleName Hook（含签名指纹验证）
			MethodInfo getTitleMethod = typeof(BiomeTitlesUI)
				.GetMethod("GetActualTitleName", BindingFlags.NonPublic | BindingFlags.Instance);

			if (getTitleMethod != null)
			{
				// 签名指纹验证：确认参数类型列表与预期一致
				// 防止 BTitles 未来更新改变方法签名时，new Hook(...) 直接崩溃
				if (!ValidateMethodSignature(getTitleMethod, _expectedHookSignature))
				{
					HookInstalled = false;
					string actualSig = FormatMethodSignature(getTitleMethod);
					Logger.Error(Language.GetTextValue(
						$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.HookSignatureMismatch",
						actualSig, "BiomeEntry"));
				}
				else
				{
					_getActualTitleNameHook = new Hook(
						getTitleMethod,
						new Func<Func<BiomeTitlesUI, BiomeEntry, string>, BiomeTitlesUI, BiomeEntry, string>(
							BiomeNameHook.GetActualTitleNamePrefixHook)
					);
					HookInstalled = true;
					DebugLog.Info(Language.GetTextValue(
						$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.HookApplied"));
				}
			}
			else
			{
				HookInstalled = false;
				Logger.Error(Language.GetTextValue(
					$"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.HookGetTitleNameFailed"));
			}
		} // ── Load 结束 ──

		/*
		签名指纹验证：比较方法参数类型列表
		参数不匹配时返回 false，避免因上游 API 变更导致 new Hook() 抛出异常
		*/
		private static bool ValidateMethodSignature(MethodInfo method, Type[] expectedParamTypes)
		{
			var actualParams = method.GetParameters();
			if (actualParams.Length != expectedParamTypes.Length) return false;
			for (int i = 0; i < actualParams.Length; i++)
			{
				if (actualParams[i].ParameterType != expectedParamTypes[i])
					return false;
			}
			return true;
		}

		/*
		格式化方法签名为可读字符串，用于签名不匹配时的日志输出
		如：GetActualTitleName(BiomeEntry, Boolean)
		*/
		private static string FormatMethodSignature(MethodInfo method)
		{
			var paramNames = method.GetParameters().Select(p =>
			{
				string name = p.ParameterType.IsByRef
					? p.ParameterType.GetElementType()?.Name ?? "?"
					: p.ParameterType.Name;
				string mod = p.IsOut ? "out" : p.ParameterType.IsByRef ? "ref" : "";
				return mod.Length > 0 ? $"{name} {mod}" : name;
			});
			return $"{method.Name}({string.Join(", ", paramNames)})";
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
			// 单独 try-catch 包裹 Dispose，防止它抛异常阻断后续清理
			try { _getActualTitleNameHook?.Dispose(); }
			catch (Exception ex) { Logger.Warn($"Hook Dispose failed: {ex.Message}"); }
			_getActualTitleNameHook = null;
			HookInstalled = false;
			InstanceField = null;
			BiomeDictField = null;
			CheckFuncsField = null;
			_loaded = false;

			// 从 BTitles 检测函数列表移除本模组注册的 lambda
			Scan.BiomeRegistrar.Cleanup();

			// 清理调试日志写入器，防止卸载后残留引用
			Diagnostics.DebugLog.InfoWriter = s => { };
			Diagnostics.DebugLog.WarnWriter = s => { };
			Diagnostics.DebugLog.ErrorWriter = s => { };
		}
	}
}
