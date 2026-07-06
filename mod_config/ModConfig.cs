using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace BTitlesLocalizationPatch
{
	/* 玩家配置类，可在 Mod 设置中调整 */
	public class BTitlesConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[Header("Scan")]

		[DefaultValue(false)]
		[LabelKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableScan.Label")]
		[TooltipKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableScan.Tooltip")]
		public bool EnableScan { get; set; }

		[DefaultValue(false)]
		[LabelKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableAutoStyling.Label")]
		[TooltipKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableAutoStyling.Tooltip")]
		public bool EnableAutoStyling { get; set; }

		[Header("Debug")]

		[DefaultValue(false)]
		[LabelKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableDebugLog.Label")]
		[TooltipKey("$Mods.BTitlesLocalizationPatch.Configs.BTitlesConfig.EnableDebugLog.Tooltip")]
		public bool EnableDebugLog { get; set; }
	}
}
