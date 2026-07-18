using BTitlesLocalizationPatch.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria.Localization;
using Terraria.ModLoader;

namespace BTitlesLocalizationPatch.Scan
{
    /* 自动配色辅助方法 */
    internal static class BiomeStyleHelper
    {
        // 自动取色策略：BackgroundColor → 哈希色 → 白色回退
        public static void GetTitleColors(ModBiome biome, out Color titleColor, out Color strokeColor)
        {
            // 外部调用可能传来 null，防御到结果级别
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
                // HSL 色盘兜底：FNV-1a 取色相 (0-360)，固定饱和度 50%，亮度 60%
                uint hash = Fnv1aHash(biome.FullName);
                titleColor = HslToRgb((hash % 3600) / 10f, 0.5f, 0.6f);
            }

            strokeColor = new Color(
                (int)(titleColor.R * 0.35f),
                (int)(titleColor.G * 0.35f),
                (int)(titleColor.B * 0.35f));
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
                DebugLog.Warn(Language.GetTextValue(
                    $"Mods.{nameof(BTitlesLocalizationPatch)}.Logs.IconLoadFailed",
                    biome.FullName, ex.Message));
                return null;
            }
        }

        /*
		基于键名的 HSL 色盘兜底
		用 FNV-1a 决定色相，固定饱和度与亮度
		*/
        public static Color GetFallbackColor(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Color.Gray;

            uint hash = Fnv1aHash(key);
            return HslToRgb((hash % 3600) / 10f, 0.5f, 0.6f);
        }

        /* HSL → RGB 转换 */
        private static uint Fnv1aHash(string input)
        {
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            // 防御：NaN/Infinity 兜底，防止哈希异常值破坏颜色计算
            if (float.IsNaN(h) || float.IsInfinity(h)) h = 0f;
            if (float.IsNaN(s) || float.IsInfinity(s)) s = 0.5f;
            if (float.IsNaN(l) || float.IsInfinity(l)) l = 0.6f;

            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = l - c / 2f;

            float r, g, b;
            if (h < 60f) { r = c; g = x; b = 0f; }
            else if (h < 120f) { r = x; g = c; b = 0f; }
            else if (h < 180f) { r = 0f; g = c; b = x; }
            else if (h < 240f) { r = 0f; g = x; b = c; }
            else if (h < 300f) { r = x; g = 0f; b = c; }
            else { r = c; g = 0f; b = x; }

            return new Color(
                ClampByte((r + m) * 255f),
                ClampByte((g + m) * 255f),
                ClampByte((b + m) * 255f));
        }

        private static byte ClampByte(float value)
        {
            int v = (int)(value + 0.5f);  // 算术舍入，避免银行家舍入的不一致
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
    }
}
