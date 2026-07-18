using System;
using System.Threading;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Diagnostics
{
    /* 调试日志工具，按级别分流到 tModLoader 的 Logger */
    internal static class DebugLog
    {
        // 缓存配置值，配合冷却时间避免 UI 渲染路径中高频查询 ModContent.GetInstance
        // 修改配置后最多 3 秒生效，无需重载
        // volatile: 确保不同线程（UI 渲染 / PostSetupContent）看到缓存值的一致性
        private static volatile bool _cachedEnabled;

        // long 不能 volatile（CS0677），用 Volatile.Read/Write 保证原子可见性
        private static long _lastConfigCheckTicks;
        private static readonly long ConfigCheckCooldownTicks = TimeSpan.FromSeconds(3).Ticks;

        // 重置缓存（Unload 时调用），防止热重载后残留旧值
        internal static void ResetCache()
        {
            _cachedEnabled = false;
            Volatile.Write(ref _lastConfigCheckTicks, 0L);
        }

        public static bool Enabled
        {
            get
            {
                long now = DateTime.UtcNow.Ticks;
                long lastTicks = Volatile.Read(ref _lastConfigCheckTicks);
                if ((now - lastTicks) < ConfigCheckCooldownTicks)
                    return _cachedEnabled;

                Volatile.Write(ref _lastConfigCheckTicks, now);
                try
                {
                    _cachedEnabled =
                        ModContent.GetInstance<BTitlesConfig>()?.EnableDebugLog ?? false;
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

        // 输出调试日志
        public static void Info(string message)
        {
            if (Enabled)
                InfoWriter?.Invoke(message);
        }

        // 警告日志：不受调试开关控制，始终输出，用于记录不影响功能的异常
        public static void Warn(string message)
        {
            WarnWriter?.Invoke(message);
        }

        // 错误日志：不受调试开关控制，始终输出，用于记录严重异常
        public static void Error(string message)
        {
            ErrorWriter?.Invoke(message);
        }
    }
}
