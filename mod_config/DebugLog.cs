using System;
using Terraria.ModLoader;
using BTitlesLocalizationPatch;

namespace BTitlesLocalizationPatch.Diagnostics
{
	/* 调试日志工具，按级别分流到 tModLoader 的 Logger */
	internal static class DebugLog
	{
		// 实时读取配置，修改后立即生效，无需重载
		public static bool Enabled => ModContent.GetInstance<BTitlesConfig>().EnableDebugLog;

		// 三个级别的日志写入器，由 Mod 主类在 Load 时注入对应的 Logger 方法
		public static Action<string> InfoWriter { get; set; } = s => { };
		public static Action<string> WarnWriter { get; set; } = s => { };
		public static Action<string> ErrorWriter { get; set; } = s => { };

		// 输出调试日志（仅 Enabled 时生效）
		public static void Info(string message)
		{
			if (Enabled)
				InfoWriter?.Invoke(message);
		}

		public static void Warn(string message)
		{
			WarnWriter?.Invoke(message);
		}

		public static void Error(string message)
		{
			ErrorWriter?.Invoke(message);
		}
	}
}
