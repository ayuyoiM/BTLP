using System;
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
        // 取色策略：群系返回的颜色（BackgroundColor）→ 白色回退
        public static void GetTitleColors(
            ModBiome biome,
            out Color titleColor,
            out Color strokeColor
        )
        {
            if (biome == null)
            {
                titleColor = Color.White;
                strokeColor = Color.Black;
                return;
            }

            if (biome.BackgroundColor.HasValue)
            {
                titleColor = biome.BackgroundColor.Value;
            }
            else
            {
                // 群系没有返回颜色，日志提示
                Diagnostics.DebugLog.Info(
                    Language.GetTextValue(
                        $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.NoBiomeColor",
                        biome.FullName
                    )
                );
                titleColor = Color.White;
            }

            strokeColor = new Color(
                (int)(titleColor.R * 0.35f),
                (int)(titleColor.G * 0.35f),
                (int)(titleColor.B * 0.35f)
            );
        }

        /*
        尝试加载 BestiaryIcon
        加载失败时静默返回 null，不影响群系注册流程
        */
        public static Texture2D TryLoadIcon(ModBiome biome)
        {
            if (biome == null)
                return null;

            try
            {
                string path = biome.BestiaryIcon;
                return !string.IsNullOrEmpty(path) && ModContent.HasAsset(path)
                    ? ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value
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
        兜底颜色：无群系返回色时使用白色
        */
        public static Color GetFallbackColor(string key)
        {
            return string.IsNullOrEmpty(key) ? Color.Gray : Color.White;
        }
    }
}
