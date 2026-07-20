#nullable enable
using System;
using System.Collections.Generic;
using BTitlesLocalizationPatch.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.Localization;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Scan
{
    /* 自动配色辅助方法 */
    internal static class BiomeStyleHelper
    {
        // 取色链：群系返回色 → 图标主色（最多色）→ 白色回退
        public static void GetTitleColors(ModBiome? biome, out Color titleColor)
        {
            if (biome == null)
            {
                titleColor = Color.White;
                return;
            }

            if (biome.BackgroundColor.HasValue)
            {
                titleColor = biome.BackgroundColor.Value;
                return;
            }

            // 尝试图标采样（取出现最多的颜色）
            Texture2D? icon = TryLoadIcon(biome);
            if (icon != null)
            {
                titleColor = SampleDominantColor(icon);
                return;
            }

            // 没返回色也没图标时日志提示
            // 后台线程（PostSetupContent）不设颜色，Hook 会在主线程补色
            Diagnostics.DebugLog.Info(
                Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.NoBiomeColor",
                    biome.FullName
                )
            );
            // 留 default 让 BiomeNameHook 在主线程图标采样补色
            titleColor = default;
        }

        /*
        尝试加载 BestiaryIcon
        加载失败时静默返回 null，不影响群系注册流程
        */
        public static Texture2D? TryLoadIcon(ModBiome? biome)
        {
            if (biome == null)
                return null;

            try
            {
                string iconPath = biome.BestiaryIcon;
                return !string.IsNullOrEmpty(iconPath) && ModContent.HasAsset(iconPath)
                    ? ModContent.Request<Texture2D>(iconPath, AssetRequestMode.ImmediateLoad).Value
                    : null;
            }
            catch (Exception ex)
            {
                DebugLog.Warn(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.IconLoadFailed",
                        biome.FullName,
                        ex.Message
                    )
                );
                return null;
            }
        }

        /*
        从整个图标采样，返回出现次数最多的颜色（众数）
        像素量化到 32 级色块避免噪点干扰
        */
        public static Color SampleDominantColor(Texture2D icon)
        {
            if (icon == null || icon.IsDisposed || icon.Width <= 1 || icon.Height <= 1)
                return Color.White;

            int sampleWidth = Math.Min(icon.Width, 256);
            int sampleHeight = Math.Min(icon.Height, 256);

            Color[] pixels = new Color[sampleWidth * sampleHeight];
            try
            {
                icon.GetData(
                    0,
                    new Rectangle(0, 0, sampleWidth, sampleHeight),
                    pixels,
                    0,
                    pixels.Length
                );
            }
            catch
            {
                return Color.White;
            }

            var colorFrequency = new Dictionary<int, int>();
            int highestFrequency = 0;
            Color mostCommonColor = Color.Gray;

            foreach (ref Color pixel in pixels.AsSpan())
            {
                if (pixel.A < 128)
                    continue;
                int brightness = pixel.R + pixel.G + pixel.B;
                if (brightness < 30 || brightness > 720)
                    continue;

                int colorKey = (pixel.R >> 3) << 10 | (pixel.G >> 3) << 5 | (pixel.B >> 3);
                colorFrequency.TryGetValue(colorKey, out int currentCount);
                currentCount++;
                colorFrequency[colorKey] = currentCount;

                if (currentCount > highestFrequency)
                {
                    highestFrequency = currentCount;
                    mostCommonColor = new Color(
                        (byte)(((colorKey >> 10) & 0x1F) << 3 | 4),
                        (byte)(((colorKey >> 5) & 0x1F) << 3 | 4),
                        (byte)((colorKey & 0x1F) << 3 | 4)
                    );
                }
            }

            return highestFrequency > 0 ? mostCommonColor : Color.Gray;
        }
    }
}
