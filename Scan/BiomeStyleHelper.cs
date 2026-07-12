using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Localization;
using Terraria.ModLoader;
using BTitlesLocalizationPatch.Diagnostics;

namespace BTitlesLocalizationPatch.Scan
{
	/* 自动配色辅助方法 */
	internal static class BiomeStyleHelper
	{
		// 自动取色策略：BackgroundColor → 哈希色 → 白色回退
		public static void GetTitleColors(ModBiome biome, out Color title, out Color stroke)
		{
			if (biome.BackgroundColor.HasValue)
			{
				title = biome.BackgroundColor.Value;
			}
			else
			{
				/*
				HSL 色盘兜底
				用 FNV-1a 取色相 (0-360)，固定饱和度 50%，亮度 60%
				比纯哈希颜色更饱满悦目，且确定不变
				*/
				uint hash = Fnv1aHash(biome.FullName);
				title = HslToRgb((hash % 3600) / 10f, 0.5f, 0.6f);
			}

			// 描边取标题的暗化版
			stroke = new Color(
				(int)(title.R * 0.35f),
				(int)(title.G * 0.35f),
				(int)(title.B * 0.35f));
		}

		/*
		尝试加载 BestiaryIcon
		加载失败时静默返回 null，不影响群系注册流程
		*/
		public static Texture2D TryLoadIcon(ModBiome biome)
		{
			try
			{
				string path = biome.BestiaryIcon;
				return !string.IsNullOrEmpty(path) && ModContent.HasAsset(path)
					? ModContent.Request<Texture2D>(path).Value
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
		从图标采样主色调
		取中心 1/2 区域的像素加权平均，忽略透明/过暗/过亮的像素
		*/
		public static Color SampleDominantColor(Texture2D icon)
		{
			if (icon == null || icon.IsDisposed)
				return Color.White;

			int w = icon.Width;
			int h = icon.Height;
			if (w <= 1 || h <= 1)
				return Color.White;

			// 取中心区域避免边缘背景干扰
			int startX = w / 4;
			int startY = h / 4;
			int sampleW = Math.Max(1, w / 2);
			int sampleH = Math.Max(1, h / 2);

			Color[] pixels = new Color[sampleW * sampleH];
			try
			{
				icon.GetData(0, new Rectangle(startX, startY, sampleW, sampleH), pixels, 0, pixels.Length);
			}
			catch
			{
				return Color.White;
			}

			long r = 0, g = 0, b = 0, count = 0;
			foreach (ref Color pixel in pixels.AsSpan())
			{
				if (pixel.A < 128) continue;
				int brightness = pixel.R + pixel.G + pixel.B;
				if (brightness < 30 || brightness > 720) continue;

				r += pixel.R;
				g += pixel.G;
				b += pixel.B;
				count++;
			}

			if (count == 0)
			{
				// 全被过滤了，取第一个不透明的像素
				foreach (ref Color pixel in pixels.AsSpan())
				{
					if (pixel.A >= 128)
						return pixel;
				}
				return Color.Gray;
			}

			return new Color(
				(byte)(r / count),
				(byte)(g / count),
				(byte)(b / count));
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
			int v = (int)Math.Round(value);
			return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
		}
	}
}
