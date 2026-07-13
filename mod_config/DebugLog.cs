using System;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Diagnostics
{
    /* 调试日志工具，按级别分流到 tModLoader 的 Logger */
    internal static class DebugLog
    {
        // 缓存配置值，配合冷却时间避免 UI 渲染路径中高频查询 ModContent.GetInstance
        // 修改配置后最多 3 秒生效，无需重载
        private static bool _cachedEnabled;
        private static DateTime _lastConfigCheck = DateTime.MinValue;
        private static readonly TimeSpan ConfigCheckCooldown = TimeSpan.FromSeconds(3);

        public static bool Enabled
        {
            get
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastConfigCheck) < ConfigCheckCooldown)
                    return _cachedEnabled;

                _lastConfigCheck = now;
                try
                {
                    _cachedEnabled = ModContent.GetInstance<BTitlesConfig>()?.EnableDebugLog ?? false;
                }
                catch
                {
                    _cachedEnabled = false;
                }
                return _cachedEnabled;
            }
        }

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
