using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
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
				确定性 FNV-1a 哈希（避免 string.GetHashCode 跨 .NET 版本不一致的问题）
				保证最低亮度 0x40 让文字可见
				*/
				uint hash = 2166136261;
				foreach (char c in biome.FullName)
				{
					hash ^= c;
					hash *= 16777619;
				}
				title = new Color(
					(byte)(((hash >> 16) & 0xFF) | 0x40),
					(byte)(((hash >> 8)  & 0xFF) | 0x40),
					(byte)(( hash        & 0xFF) | 0x40));
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
	}
}
