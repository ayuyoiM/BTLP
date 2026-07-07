using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using BTitles;
using BTitlesLocalizationPatch.Scan;

namespace BTitlesLocalizationPatch
{
	/*
	GetActualTitleName 的替换逻辑
	5 步回退链，命中即返回
	1. 用户自定义改名          → Config.CustomBiomeNames
	2. 源模组本地化            → Mods.{Scope}.Biomes.{Key}.DisplayName（跳过原版 Terraria）
	3. BTitles 自身翻译        → Mods.BiomeTitles.Title.{Scope}.{Key}
	4. 补丁模组补充翻译        → Mods.BTitlesLocalizationPatch.ExtraTitles.{Scope}.{Key}
	5. 回退原始 Title          → biomeEntry.Title
	*/
	internal static class BiomeNameHook
	{
		public static string GetActualTitleNamePrefixHook(
			Func<BiomeTitlesUI, BiomeEntry, string> orig,
			BiomeTitlesUI self,
			BiomeEntry biomeEntry)
		{
			// 防止空引用（外部模组调用时参数可能异常）
			if (self == null || biomeEntry == null)
				return "";

			// 进出群系时日志输出，方便排查注册/显示问题
			Diagnostics.DebugLog.Info(
				$"群系: Key={biomeEntry.Key} Title={biomeEntry.Title} Scope={biomeEntry.LocalizationScope}");

			// 惰性采样：有图标但颜色未设时从图标取色
			if (biomeEntry.TitleColor == default && biomeEntry.Icon != null)
			{
				Color sampled = BiomeStyleHelper.SampleDominantColor(biomeEntry.Icon);
				biomeEntry.TitleColor = sampled;
				biomeEntry.StrokeColor = new Color(
					(int)(sampled.R * 0.35f),
					(int)(sampled.G * 0.35f),
					(int)(sampled.B * 0.35f));
				Diagnostics.DebugLog.Info(
					$"惰性采样: [{biomeEntry.Key}] → RGB({sampled.R},{sampled.G},{sampled.B})");
			}
			else if (biomeEntry.TitleColor == default)
			{
				// 无图标也无颜色 → HSL 色盘兜底
				Color fallback = BiomeStyleHelper.GetFallbackColor(
					biomeEntry.Key ?? biomeEntry.Title ?? "");
				biomeEntry.TitleColor = fallback;
				biomeEntry.StrokeColor = new Color(
					(int)(fallback.R * 0.35f),
					(int)(fallback.G * 0.35f),
					(int)(fallback.B * 0.35f));
				Diagnostics.DebugLog.Info(
					$"色盘兜底: [{biomeEntry.Key}] → ({fallback.R},{fallback.G},{fallback.B})");
			}

			// 1. 用户自定义生物群系名称
			string customName = self.Config?.CustomBiomeNames?
				.FirstOrDefault(n => n.CurrentName == biomeEntry.Title)?.NewName;
			if (customName != null) return customName;

			// 2. 读源模组本地化 Mods.{Scope}.Biomes.{Key}.DisplayName（跳过原版 Terraria）
			if (biomeEntry.LocalizationScope != "Terraria"
				&& Language.ActiveCulture.Name != "en-US")
			{
				string modLocKey = $"Mods.{biomeEntry.LocalizationScope}" +
					$".Biomes.{biomeEntry.Key.Replace(" ", "_")}.DisplayName";

				if (Language.Exists(modLocKey))
				{
					string translatedName = Language.GetTextValue(modLocKey);

					// 对翻译结果再应用一次自定义改名
					customName = self.Config?.CustomBiomeNames?
						.FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

					return customName ?? translatedName;
				}
			}

			// 3. BTitles 自身翻译
			if (Language.ActiveCulture.Name != "en-US")
			{
				string btitlesKey = $"Mods.BiomeTitles.Title." +
					$"{biomeEntry.LocalizationScope}.{biomeEntry.Key.Replace(" ", "_")}";

				if (Language.Exists(btitlesKey))
				{
					string translatedName = Language.GetTextValue(btitlesKey);

					customName = self.Config?.CustomBiomeNames?
						.FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

					return customName ?? translatedName;
				}
			}

			// 4. 补丁模组补充翻译（不修改 BTitles 即可补充缺失翻译，如 Aether）
			if (Language.ActiveCulture.Name != "en-US")
			{
				string extraKey = $"Mods.{nameof(BTitlesLocalizationPatch)}" +
					$".ExtraTitles.{biomeEntry.LocalizationScope}.{biomeEntry.Key.Replace(" ", "_")}";

				if (Language.Exists(extraKey))
				{
					string translatedName = Language.GetTextValue(extraKey);

					customName = self.Config?.CustomBiomeNames?
						.FirstOrDefault(n => n.CurrentName == translatedName)?.NewName;

					return customName ?? translatedName;
				}
			}

			// 5. 回退到原始 Title
			return biomeEntry.Title;
		}
	}
}
