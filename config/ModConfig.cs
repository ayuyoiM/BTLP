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

        // tModLoader 配置有确认保存弹窗，OnChanged 只在保存时触发一次，不需要防抖
        internal static event Action<bool>? OnScanChanged;
        internal static event Action<bool>? OnAutoStylingChanged;

        private bool? _lastAutoStyling;
        private bool? _lastScan;

        public override void OnChanged()
        {
            bool styleCurrent = EnableAutoStyling;
            if (_lastAutoStyling.HasValue && styleCurrent != _lastAutoStyling.Value)
                OnAutoStylingChanged?.Invoke(styleCurrent);
            _lastAutoStyling = styleCurrent;

            bool scanCurrent = EnableScan;
            if (_lastScan.HasValue && scanCurrent != _lastScan.Value)
                OnScanChanged?.Invoke(scanCurrent);
            _lastScan = scanCurrent;
        }
    }
}
