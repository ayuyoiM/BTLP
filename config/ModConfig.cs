#nullable enable
using System;
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace BTitlesLocalizationPatch
{
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

        private bool? _lastAutoStyling;
        private bool? _lastScan;

        // tModLoader 确认保存后通知，直接执行配置变更
        // 主菜单时 BTitles 未加载，操作会静默跳过；进世界后 PostSetupContent 处理初始状态
        public override void OnChanged()
        {
            bool styleCurrent = EnableAutoStyling;
            if (_lastAutoStyling.HasValue && styleCurrent != _lastAutoStyling.Value)
                BTitlesLocalizationPatch.ApplyConfigChangeStyle(styleCurrent);
            _lastAutoStyling = styleCurrent;

            bool scanCurrent = EnableScan;
            if (_lastScan.HasValue && scanCurrent != _lastScan.Value)
                BTitlesLocalizationPatch.ApplyConfigChangeScan(scanCurrent);
            _lastScan = scanCurrent;
        }
    }
}
