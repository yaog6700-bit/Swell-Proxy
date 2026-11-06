namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Strongly-typed localized string accessors for C# code-behind and ViewModels.
    /// XAML static elements use <c>x:Uid</c> backed by the same <c>Strings/{lang}/Resources.resw</c> files.
    /// </summary>
    public static class L
    {
        // ── Common / Dialogs ───────────────────────────────────────────────────
        public static string Dialog_OK                  => Loc.GetString("Dialog_OK");
        public static string Dialog_Cancel              => Loc.GetString("Dialog_Cancel");
        public static string Dialog_Confirm             => Loc.GetString("Dialog_Confirm");
        public static string Common_Connected           => Loc.GetString("Common_Connected");
        public static string Common_Disconnected        => Loc.GetString("Common_Disconnected");
        public static string Common_NoNodeSelected      => Loc.GetString("Common_NoNodeSelected");
        public static string Common_UnknownNode         => Loc.GetString("Common_UnknownNode");

        // ── Proxy & Routing Modes ──────────────────────────────────────────────
        public static string ProxyMode_System           => Loc.GetString("ProxyMode_System");
        public static string ProxyMode_Tun              => Loc.GetString("ProxyMode_Tun");
        public static string ProxyMode_Manual           => Loc.GetString("ProxyMode_Manual");
        public static string ProxyMode_Local            => Loc.GetString("ProxyMode_Local");
        public static string ProxyMode_TunSystem        => Loc.GetString("ProxyMode_TunSystem");

        public static string RouteMode_Smart            => Loc.GetString("RouteMode_Smart");
        public static string RouteMode_Global           => Loc.GetString("RouteMode_Global");
        public static string RouteMode_Direct           => Loc.GetString("RouteMode_Direct");
        public static string RouteMode_GlobalProxy      => Loc.GetString("RouteMode_GlobalProxy");
        public static string RouteMode_DirectAll        => Loc.GetString("RouteMode_DirectAll");

        // ── MainWindow & Tray ──────────────────────────────────────────────────
        public static string MainWindow_SwitchToLight   => Loc.GetString("MainWindow_SwitchToLight");
        public static string MainWindow_SwitchToDark    => Loc.GetString("MainWindow_SwitchToDark");
        public static string Tray_ConnectedTooltip      => Loc.GetString("Tray_ConnectedTooltip");
        public static string Tray_DisconnectedTooltip   => Loc.GetString("Tray_DisconnectedTooltip");
        public static string Tray_ShowMainWindow        => Loc.GetString("Tray_ShowMainWindow");
        public static string Tray_UnlockWindow          => Loc.GetString("Tray_UnlockWindow");
        public static string Tray_LockAndHide           => Loc.GetString("Tray_LockAndHide");
        public static string Tray_Exit                  => Loc.GetString("Tray_Exit");

        public static string Privacy_SetPwdTitle        => Loc.GetString("Privacy_SetPwdTitle");
        public static string Privacy_SetPwdHeader       => Loc.GetString("Privacy_SetPwdHeader");
        public static string Privacy_SetPwdPlaceholder  => Loc.GetString("Privacy_SetPwdPlaceholder");

        // ── UAC & Core Startup Dialogs ─────────────────────────────────────────
        public static string Tun_AdminRequiredTitle     => Loc.GetString("Tun_AdminRequiredTitle");
        public static string Tun_AdminRequiredMsg       => Loc.GetString("Tun_AdminRequiredMsg");
        public static string Tun_AdminConfirmBtn        => Loc.GetString("Tun_AdminConfirmBtn");
        public static string Core_StartFailedTitle      => Loc.GetString("Core_StartFailedTitle");
        public static string Core_StartFailedDashboard  => Loc.GetString("Core_StartFailedDashboard");
        public static string Core_StartFailedWithError  => Loc.GetString("Core_StartFailedWithError");
        public static string Core_ConfigBuildFailedTitle => Loc.GetString("Core_ConfigBuildFailedTitle");
        public static string Core_ConfigBuildFailedMsg  => Loc.GetString("Core_ConfigBuildFailedMsg");

        // ── Dashboard ──────────────────────────────────────────────────────────
        public static string Dashboard_InfoConnectedMsg => Loc.GetString("Dashboard_InfoConnectedMsg");
        public static string Dashboard_InfoStoppedTitle => Loc.GetString("Dashboard_InfoStoppedTitle");
        public static string Dashboard_InfoStoppedMsg   => Loc.GetString("Dashboard_InfoStoppedMsg");
        public static string Dashboard_RemainingDaysFmt => Loc.GetString("Dashboard_RemainingDaysFmt");

        // ── Servers & Subscriptions ────────────────────────────────────────────
        public static string Servers_PingUntested       => Loc.GetString("Servers_PingUntested");
        public static string Servers_PingTesting        => Loc.GetString("Servers_PingTesting");
        public static string Servers_PingTimeout        => Loc.GetString("Servers_PingTimeout");
        public static string Servers_PingFailed         => Loc.GetString("Servers_PingFailed");

        public static string Servers_FilterAll          => Loc.GetString("Servers_FilterAll");
        public static string Servers_FilterFavorites    => Loc.GetString("Servers_FilterFavorites");
        public static string Servers_FilterManual       => Loc.GetString("Servers_FilterManual");
        public static string Servers_UnnamedSub         => Loc.GetString("Servers_UnnamedSub");
        public static string Servers_AutoSubPrefix      => Loc.GetString("Servers_AutoSubPrefix");
        public static string Servers_FilterTooltipFmt   => Loc.GetString("Servers_FilterTooltipFmt");

        public static string Servers_SortDefault        => Loc.GetString("Servers_SortDefault");
        public static string Servers_SortActive         => Loc.GetString("Servers_SortActive");
        public static string Servers_SortProtocol       => Loc.GetString("Servers_SortProtocol");
        public static string Servers_SortLatency        => Loc.GetString("Servers_SortLatency");
        public static string Servers_SortTooltipFmt     => Loc.GetString("Servers_SortTooltipFmt");

        public static string Servers_CopyShareLink      => Loc.GetString("Servers_CopyShareLink");
        public static string Servers_Copied             => Loc.GetString("Servers_Copied");
        public static string Servers_MenuEdit           => Loc.GetString("Servers_MenuEdit");
        public static string Servers_MenuDelete         => Loc.GetString("Servers_MenuDelete");
        public static string Servers_MenuShare          => Loc.GetString("Servers_MenuShare");
        public static string Servers_MenuAddFav         => Loc.GetString("Servers_MenuAddFav");
        public static string Servers_MenuRemoveFav      => Loc.GetString("Servers_MenuRemoveFav");

        public static string Servers_AddManualTitle     => Loc.GetString("Servers_AddManualTitle");
        public static string Servers_EditNodeTitle      => Loc.GetString("Servers_EditNodeTitle");
        public static string Servers_ChainDirect        => Loc.GetString("Servers_ChainDirect");
        public static string Servers_HeaderUsername     => Loc.GetString("Servers_HeaderUsername");

        public static string Servers_ImportDialogTitle  => Loc.GetString("Servers_ImportDialogTitle");
        public static string Servers_ImportDialogHint   => Loc.GetString("Servers_ImportDialogHint");
        public static string Servers_ImportPlaceholder  => Loc.GetString("Servers_ImportPlaceholder");
        public static string Servers_ImportSuccessTitle => Loc.GetString("Servers_ImportSuccessTitle");
        public static string Servers_ImportBothFmt      => Loc.GetString("Servers_ImportBothFmt");
        public static string Servers_ImportSubsFmt      => Loc.GetString("Servers_ImportSubsFmt");
        public static string Servers_ImportNodesFmt     => Loc.GetString("Servers_ImportNodesFmt");
        public static string Servers_ImportFailTitle    => Loc.GetString("Servers_ImportFailTitle");
        public static string Servers_ImportFailMsg      => Loc.GetString("Servers_ImportFailMsg");
        public static string Servers_ImportErrorTitle   => Loc.GetString("Servers_ImportErrorTitle");

        public static string Servers_IpDatacenterTag    => Loc.GetString("Servers_IpDatacenterTag");
        public static string Servers_IpFetchFailed      => Loc.GetString("Servers_IpFetchFailed");
        public static string Servers_ShowFullIp         => Loc.GetString("Servers_ShowFullIp");
        public static string Servers_HideExitIp         => Loc.GetString("Servers_HideExitIp");
        public static string Servers_CopyIpSummaryFmt   => Loc.GetString("Servers_CopyIpSummaryFmt");

        public static string Servers_ShareDialogTitle   => Loc.GetString("Servers_ShareDialogTitle");
        public static string Servers_CopyLinkTooltip    => Loc.GetString("Servers_CopyLinkTooltip");
        public static string Servers_ShareFailTitle     => Loc.GetString("Servers_ShareFailTitle");
        public static string Servers_ShareFailMsg       => Loc.GetString("Servers_ShareFailMsg");

        public static string Servers_DeleteConfirmTitle => Loc.GetString("Servers_DeleteConfirmTitle");
        public static string Servers_DeleteConfirmFmt   => Loc.GetString("Servers_DeleteConfirmFmt");

        public static string Servers_SubSyncingBtn      => Loc.GetString("Servers_SubSyncingBtn");
        public static string Servers_SubImportSyncBtn   => Loc.GetString("Servers_SubImportSyncBtn");

        public static string SubDialog_AddTitle         => Loc.GetString("SubDialog_AddTitle");
        public static string SubDialog_ManageTitle      => Loc.GetString("SubDialog_ManageTitle");
        public static string SubDialog_AddBtn           => Loc.GetString("SubDialog_AddBtn");
        public static string SubDialog_DoneBtn          => Loc.GetString("SubDialog_DoneBtn");
        public static string SubDialog_AddFailTitle     => Loc.GetString("SubDialog_AddFailTitle");
        public static string SubDialog_UpdateFailTitle  => Loc.GetString("SubDialog_UpdateFailTitle");
        public static string SubDialog_RefreshTooltip   => Loc.GetString("SubDialog_RefreshTooltip");
        public static string SubDialog_DelConfirmTitle  => Loc.GetString("SubDialog_DelConfirmTitle");
        public static string SubDialog_DelConfirmDesc   => Loc.GetString("SubDialog_DelConfirmDesc");
        public static string SubDialog_DelTooltip       => Loc.GetString("SubDialog_DelTooltip");
        public static string SubDialog_AutoRefreshOff   => Loc.GetString("SubDialog_AutoRefreshOff");
        public static string SubDialog_AutoRefreshPending => Loc.GetString("SubDialog_AutoRefreshPending");

        // ── Settings Categories & Language Restart ─────────────────────────────
        public static string Settings_CatAppearanceTitle    => Loc.GetString("Settings_CatAppearanceTitle");
        public static string Settings_CatAppearanceSub      => Loc.GetString("Settings_CatAppearanceSub");
        public static string Settings_CatColorsTitle        => Loc.GetString("Settings_CatColorsTitle");
        public static string Settings_CatColorsSub          => Loc.GetString("Settings_CatColorsSub");
        public static string Settings_CatRoutingTitle       => Loc.GetString("Settings_CatRoutingTitle");
        public static string Settings_CatRoutingSub         => Loc.GetString("Settings_CatRoutingSub");
        public static string Settings_CatDnsTitle           => Loc.GetString("Settings_CatDnsTitle");
        public static string Settings_CatDnsSub             => Loc.GetString("Settings_CatDnsSub");
        public static string Settings_CatTailscaleTitle     => Loc.GetString("Settings_CatTailscaleTitle");
        public static string Settings_CatTailscaleSub       => Loc.GetString("Settings_CatTailscaleSub");
        public static string Settings_CatBackupTitle        => Loc.GetString("Settings_CatBackupTitle");
        public static string Settings_CatBackupSub          => Loc.GetString("Settings_CatBackupSub");
        public static string Settings_CatAutostartTitle     => Loc.GetString("Settings_CatAutostartTitle");
        public static string Settings_CatAutostartSub       => Loc.GetString("Settings_CatAutostartSub");
        public static string Settings_CatAboutTitle         => Loc.GetString("Settings_CatAboutTitle");
        public static string Settings_CatAboutSub           => Loc.GetString("Settings_CatAboutSub");

        // ── Phase 2: Settings, Updates, & File Picker ──────────────────────────
        public static string Settings_CoreVer115Fmt         => Loc.GetString("Settings_CoreVer115Fmt");
        public static string Settings_CoreVerFmt            => Loc.GetString("Settings_CoreVerFmt");
        public static string Settings_TunStackDesc115       => Loc.GetString("Settings_TunStackDesc115");
        public static string Settings_TunStackDescDefault   => Loc.GetString("Settings_TunStackDescDefault");
        public static string Settings_VmCoreVerFmt          => Loc.GetString("Settings_VmCoreVerFmt");
        public static string Settings_VmAppVerFmt           => Loc.GetString("Settings_VmAppVerFmt");
        public static string Settings_LanIpFallback         => Loc.GetString("Settings_LanIpFallback");
        public static string Settings_ExportingBtn          => Loc.GetString("Settings_ExportingBtn");
        public static string Settings_ExportBtnText         => Loc.GetString("Settings_ExportBtnText");
        public static string Settings_ExportSuccessTitle    => Loc.GetString("Settings_ExportSuccessTitle");
        public static string Settings_ExportSuccessFmt      => Loc.GetString("Settings_ExportSuccessFmt");
        public static string Settings_ExportFailTitle       => Loc.GetString("Settings_ExportFailTitle");
        public static string Settings_ImportConfirmTitle    => Loc.GetString("Settings_ImportConfirmTitle");
        public static string Settings_ImportConfirmMsg      => Loc.GetString("Settings_ImportConfirmMsg");
        public static string Settings_ImportConfirmPrimary  => Loc.GetString("Settings_ImportConfirmPrimary");
        public static string Settings_BackupFilterName      => Loc.GetString("Settings_BackupFilterName");
        public static string Settings_SelectBackupTitle     => Loc.GetString("Settings_SelectBackupTitle");
        public static string Settings_ImportingBtn          => Loc.GetString("Settings_ImportingBtn");
        public static string Settings_ImportBtnText         => Loc.GetString("Settings_ImportBtnText");
        public static string Settings_ImportSuccessTitle    => Loc.GetString("Settings_ImportSuccessTitle");
        public static string Settings_ImportSuccessMsg      => Loc.GetString("Settings_ImportSuccessMsg");
        public static string Settings_ImportFailTitle       => Loc.GetString("Settings_ImportFailTitle");
        public static string Settings_CheckingBtn           => Loc.GetString("Settings_CheckingBtn");
        public static string Settings_CheckUpdateBtnText    => Loc.GetString("Settings_CheckUpdateBtnText");
        public static string Settings_CoreCheckTitle        => Loc.GetString("Settings_CoreCheckTitle");
        public static string Settings_CoreUpToDateFmt       => Loc.GetString("Settings_CoreUpToDateFmt");
        public static string Settings_CoreStopWarning       => Loc.GetString("Settings_CoreStopWarning");
        public static string Settings_NewVerTitle           => Loc.GetString("Settings_NewVerTitle");
        public static string Settings_CoreConfirmUpdateFmt  => Loc.GetString("Settings_CoreConfirmUpdateFmt");
        public static string Settings_ConfirmUpdateBtn      => Loc.GetString("Settings_ConfirmUpdateBtn");
        public static string Settings_PreparingDownload     => Loc.GetString("Settings_PreparingDownload");
        public static string Settings_UpdatingCoreTitle     => Loc.GetString("Settings_UpdatingCoreTitle");
        public static string Settings_CoreReconnectNote     => Loc.GetString("Settings_CoreReconnectNote");
        public static string Settings_UpdateDoneTitle       => Loc.GetString("Settings_UpdateDoneTitle");
        public static string Settings_UpdateDoneFmt         => Loc.GetString("Settings_UpdateDoneFmt");
        public static string Settings_UpdateFailTitle       => Loc.GetString("Settings_UpdateFailTitle");
        public static string Settings_CheckFailTitle        => Loc.GetString("Settings_CheckFailTitle");
        public static string Settings_CheckFailFmt          => Loc.GetString("Settings_CheckFailFmt");
        public static string Settings_AppCheckTitle         => Loc.GetString("Settings_AppCheckTitle");
        public static string Settings_AppUpToDateMsg        => Loc.GetString("Settings_AppUpToDateMsg");
        public static string Settings_AppConfirmUpdateFmt   => Loc.GetString("Settings_AppConfirmUpdateFmt");
        public static string Settings_UpdatingAppTitle      => Loc.GetString("Settings_UpdatingAppTitle");
        public static string Settings_AppReadyTitle         => Loc.GetString("Settings_AppReadyTitle");
        public static string Settings_AppReadyFmt           => Loc.GetString("Settings_AppReadyFmt");
        public static string Settings_RestartNowBtn         => Loc.GetString("Settings_RestartNowBtn");
        public static string Settings_ExeFilterName         => Loc.GetString("Settings_ExeFilterName");
        public static string Settings_SelectCoreTitle       => Loc.GetString("Settings_SelectCoreTitle");
        public static string Settings_InvalidCoreTitle      => Loc.GetString("Settings_InvalidCoreTitle");
        public static string Settings_InvalidCoreFmt        => Loc.GetString("Settings_InvalidCoreFmt");
        public static string Settings_CoreReplace115Note    => Loc.GetString("Settings_CoreReplace115Note");
        public static string Settings_CoreReplaceOkTitle    => Loc.GetString("Settings_CoreReplaceOkTitle");
        public static string Settings_CoreReplaceOkFmt      => Loc.GetString("Settings_CoreReplaceOkFmt");
        public static string Settings_CoreReplaceFailTitle  => Loc.GetString("Settings_CoreReplaceFailTitle");
        public static string Settings_CoreReplaceFailFmt    => Loc.GetString("Settings_CoreReplaceFailFmt");
        public static string Settings_CoreRestoredTitle     => Loc.GetString("Settings_CoreRestoredTitle");
        public static string Settings_CoreRestoredFmt       => Loc.GetString("Settings_CoreRestoredFmt");
        public static string Settings_CoreRestoredReconnect => Loc.GetString("Settings_CoreRestoredReconnect");
        public static string Settings_CoreRestoreFailTitle  => Loc.GetString("Settings_CoreRestoreFailTitle");
        public static string Settings_ResetOnboardOkTitle   => Loc.GetString("Settings_ResetOnboardOkTitle");
        public static string Settings_ResetOnboardOkMsg     => Loc.GetString("Settings_ResetOnboardOkMsg");
        public static string Settings_ErrorTitle            => Loc.GetString("Settings_ErrorTitle");
        public static string Settings_GeoOkTitle            => Loc.GetString("Settings_GeoOkTitle");
        public static string Settings_GeoOkMsg              => Loc.GetString("Settings_GeoOkMsg");
        public static string Settings_GeoFailTitle          => Loc.GetString("Settings_GeoFailTitle");
        public static string Settings_GeoFailPrefix         => Loc.GetString("Settings_GeoFailPrefix");
        public static string Settings_GeoNoProxy            => Loc.GetString("Settings_GeoNoProxy");
        public static string Settings_GeoWithProxyFmt       => Loc.GetString("Settings_GeoWithProxyFmt");
        public static string Settings_GeoDownloadErrFmt     => Loc.GetString("Settings_GeoDownloadErrFmt");

        public static string Update_BannerFmt               => Loc.GetString("Update_BannerFmt");
        public static string Update_FetchingChecksum        => Loc.GetString("Update_FetchingChecksum");
        public static string Update_InvalidChecksumFmt      => Loc.GetString("Update_InvalidChecksumFmt");
        public static string Update_DownloadChecksumErr     => Loc.GetString("Update_DownloadChecksumErr");
        public static string Update_HashMismatch            => Loc.GetString("Update_HashMismatch");
        public static string Update_Extracting              => Loc.GetString("Update_Extracting");
        public static string Update_ExtractErr              => Loc.GetString("Update_ExtractErr");
        public static string Update_Verifying               => Loc.GetString("Update_Verifying");
        public static string Update_MissingFiles            => Loc.GetString("Update_MissingFiles");
        public static string Update_MissingUpdater          => Loc.GetString("Update_MissingUpdater");
        public static string Update_PreparingRestart        => Loc.GetString("Update_PreparingRestart");
        public static string Update_DownloadingWithTotalFmt => Loc.GetString("Update_DownloadingWithTotalFmt");
        public static string Update_DownloadingNoTotalFmt   => Loc.GetString("Update_DownloadingNoTotalFmt");

        public static string Core_NoChecksumSkip            => Loc.GetString("Core_NoChecksumSkip");
        public static string Core_PreparingEnv              => Loc.GetString("Core_PreparingEnv");
        public static string Core_InstallingFmt             => Loc.GetString("Core_InstallingFmt");
        public static string Core_MissingTargetFmt          => Loc.GetString("Core_MissingTargetFmt");
        public static string Core_UpdatedStatusFmt          => Loc.GetString("Core_UpdatedStatusFmt");
        public static string Core_NotInstalled              => Loc.GetString("Core_NotInstalled");
        public static string Core_CannotExec                => Loc.GetString("Core_CannotExec");
        public static string Core_InvalidBinary             => Loc.GetString("Core_InvalidBinary");
        public static string Core_InstalledUnknownVer       => Loc.GetString("Core_InstalledUnknownVer");
        public static string FilePicker_AllFiles            => Loc.GetString("FilePicker_AllFiles");

        // ── Phase 2: Routing & Custom Rules ────────────────────────────────────
        public static string Routing_RuleCountFmt           => Loc.GetString("Routing_RuleCountFmt");
        public static string Routing_EditRuleTooltip        => Loc.GetString("Routing_EditRuleTooltip");
        public static string Routing_DelRuleTooltip         => Loc.GetString("Routing_DelRuleTooltip");
        public static string Routing_ResetTitle             => Loc.GetString("Routing_ResetTitle");
        public static string Routing_ResetMsg               => Loc.GetString("Routing_ResetMsg");
        public static string Routing_ResetConfirmBtn        => Loc.GetString("Routing_ResetConfirmBtn");
        public static string Routing_ActionProxy            => Loc.GetString("Routing_ActionProxy");
        public static string Routing_ActionDirect           => Loc.GetString("Routing_ActionDirect");
        public static string Routing_ActionBlock            => Loc.GetString("Routing_ActionBlock");
        public static string Routing_ActionAuto             => Loc.GetString("Routing_ActionAuto");
        public static string Routing_BadgeProxy             => Loc.GetString("Routing_BadgeProxy");
        public static string Routing_BadgeAuto              => Loc.GetString("Routing_BadgeAuto");
        public static string Routing_TypeProcess            => Loc.GetString("Routing_TypeProcess");
        public static string Routing_TypeMixed              => Loc.GetString("Routing_TypeMixed");
        public static string Routing_TypeDomain             => Loc.GetString("Routing_TypeDomain");
        public static string Routing_BuiltInYoutubeName     => Loc.GetString("Routing_BuiltInYoutubeName");
        public static string Routing_BuiltInYoutubeDesc     => Loc.GetString("Routing_BuiltInYoutubeDesc");
        public static string Routing_BuiltInGoogleName      => Loc.GetString("Routing_BuiltInGoogleName");
        public static string Routing_BuiltInGoogleDesc      => Loc.GetString("Routing_BuiltInGoogleDesc");
        public static string Routing_BuiltInTelegramName    => Loc.GetString("Routing_BuiltInTelegramName");
        public static string Routing_BuiltInTelegramDesc    => Loc.GetString("Routing_BuiltInTelegramDesc");
        public static string Routing_BuiltInNetflixName     => Loc.GetString("Routing_BuiltInNetflixName");
        public static string Routing_BuiltInNetflixDesc     => Loc.GetString("Routing_BuiltInNetflixDesc");
        public static string Routing_BuiltInTiktokDesc      => Loc.GetString("Routing_BuiltInTiktokDesc");
        public static string Routing_BuiltInChatgptDesc     => Loc.GetString("Routing_BuiltInChatgptDesc");
        public static string Routing_BuiltInClaudeDesc      => Loc.GetString("Routing_BuiltInClaudeDesc");

        public static string CustomRules_DefaultName        => Loc.GetString("CustomRules_DefaultName");
        public static string CustomRules_DefaultDesc        => Loc.GetString("CustomRules_DefaultDesc");
        public static string CustomRules_Unnamed            => Loc.GetString("CustomRules_Unnamed");
        public static string CustomRules_GlobalWarn         => Loc.GetString("CustomRules_GlobalWarn");
        public static string CustomRules_DirectWarn         => Loc.GetString("CustomRules_DirectWarn");
        public static string CustomRules_NodePrefixFmt      => Loc.GetString("CustomRules_NodePrefixFmt");
        public static string CustomRules_NodeFallback       => Loc.GetString("CustomRules_NodeFallback");
        public static string CustomRules_CountFmt           => Loc.GetString("CustomRules_CountFmt");
        public static string CustomRules_CountFilteredFmt   => Loc.GetString("CustomRules_CountFilteredFmt");
        public static string CustomRules_EditTooltip        => Loc.GetString("CustomRules_EditTooltip");
        public static string CustomRules_DelTooltip         => Loc.GetString("CustomRules_DelTooltip");
        public static string CustomRules_EditTitle          => Loc.GetString("CustomRules_EditTitle");
        public static string CustomRules_AddTitle           => Loc.GetString("CustomRules_AddTitle");
        public static string CustomRules_SaveBtn            => Loc.GetString("CustomRules_SaveBtn");
        public static string CustomRules_SelectTplErr       => Loc.GetString("CustomRules_SelectTplErr");
        public static string CustomRules_DupTplErr          => Loc.GetString("CustomRules_DupTplErr");
        public static string CustomRules_EmptyMatchErr      => Loc.GetString("CustomRules_EmptyMatchErr");
        public static string CustomRules_InvalidIpFmt       => Loc.GetString("CustomRules_InvalidIpFmt");
        public static string CustomRules_InvalidCidrFmt     => Loc.GetString("CustomRules_InvalidCidrFmt");
        public static string CustomRules_Ipv4MaskErrFmt     => Loc.GetString("CustomRules_Ipv4MaskErrFmt");
        public static string CustomRules_Ipv6MaskErrFmt     => Loc.GetString("CustomRules_Ipv6MaskErrFmt");
        public static string CustomRules_PhDomain           => Loc.GetString("CustomRules_PhDomain");
        public static string CustomRules_HintDomain         => Loc.GetString("CustomRules_HintDomain");
        public static string CustomRules_PhIp               => Loc.GetString("CustomRules_PhIp");
        public static string CustomRules_HintIp             => Loc.GetString("CustomRules_HintIp");
        public static string CustomRules_PhProcess          => Loc.GetString("CustomRules_PhProcess");
        public static string CustomRules_HintProcess        => Loc.GetString("CustomRules_HintProcess");
        public static string CustomRules_HintMixed          => Loc.GetString("CustomRules_HintMixed");
        public static string CustomRules_TplMsDesc          => Loc.GetString("CustomRules_TplMsDesc");
        public static string CustomRules_TplAppleDesc       => Loc.GetString("CustomRules_TplAppleDesc");
        public static string CustomRules_TplGithubDesc      => Loc.GetString("CustomRules_TplGithubDesc");
        public static string CustomRules_TplSteamDesc       => Loc.GetString("CustomRules_TplSteamDesc");
        public static string CustomRules_TplDiscordDesc     => Loc.GetString("CustomRules_TplDiscordDesc");
        public static string CustomRules_TplSpotifyDesc     => Loc.GetString("CustomRules_TplSpotifyDesc");
        public static string CustomRules_TplBiliDesc        => Loc.GetString("CustomRules_TplBiliDesc");
        public static string CustomRules_TplLanName         => Loc.GetString("CustomRules_TplLanName");
        public static string CustomRules_TplLanDesc         => Loc.GetString("CustomRules_TplLanDesc");

        // ── Phase 2: Traffic, Topology, Connections, & Logs ────────────────────
        public static string Traffic_StatusActive           => Loc.GetString("Traffic_StatusActive");
        public static string Traffic_StatusInactive         => Loc.GetString("Traffic_StatusInactive");
        public static string Traffic_UpTooltipFmt           => Loc.GetString("Traffic_UpTooltipFmt");
        public static string Traffic_DownTooltipFmt         => Loc.GetString("Traffic_DownTooltipFmt");
        public static string Traffic_NoTrafficTipFmt        => Loc.GetString("Traffic_NoTrafficTipFmt");
        public static string Traffic_DaysFmt                => Loc.GetString("Traffic_DaysFmt");

        public static string Topology_LocalDevice           => Loc.GetString("Topology_LocalDevice");
        public static string Topology_Others                => Loc.GetString("Topology_Others");

        public static string Conn_StopCapture               => Loc.GetString("Conn_StopCapture");
        public static string Conn_StartCapture              => Loc.GetString("Conn_StartCapture");
        public static string Conn_StatusActive              => Loc.GetString("Conn_StatusActive");

        public static string Logs_LineCountFmt              => Loc.GetString("Logs_LineCountFmt");
        public static string Logs_StatusRunning             => Loc.GetString("Logs_StatusRunning");
        public static string Logs_StatusStopped             => Loc.GetString("Logs_StatusStopped");

        // ── Phase 2: Plugins, Onboarding, & Clipboard Toast ────────────────────
        public static string Plugins_TypeNetwork            => Loc.GetString("Plugins_TypeNetwork");
        public static string Plugins_TypeLocal              => Loc.GetString("Plugins_TypeLocal");
        public static string Plugins_RunManualTip           => Loc.GetString("Plugins_RunManualTip");
        public static string Plugins_DelTip                 => Loc.GetString("Plugins_DelTip");
        public static string Plugins_ImportLocalRadio       => Loc.GetString("Plugins_ImportLocalRadio");
        public static string Plugins_InstallUrlRadio        => Loc.GetString("Plugins_InstallUrlRadio");
        public static string Plugins_AddDialogTitle         => Loc.GetString("Plugins_AddDialogTitle");
        public static string Plugins_ContinueBtn            => Loc.GetString("Plugins_ContinueBtn");
        public static string Plugins_AddedTitle             => Loc.GetString("Plugins_AddedTitle");
        public static string Plugins_AddedFmt               => Loc.GetString("Plugins_AddedFmt");
        public static string Plugins_DownloadingTitle       => Loc.GetString("Plugins_DownloadingTitle");
        public static string Plugins_InstallOkTitle         => Loc.GetString("Plugins_InstallOkTitle");
        public static string Plugins_InstallOkFmt           => Loc.GetString("Plugins_InstallOkFmt");
        public static string Plugins_InstallFailTitle       => Loc.GetString("Plugins_InstallFailTitle");
        public static string Plugins_ExecOkTitle            => Loc.GetString("Plugins_ExecOkTitle");
        public static string Plugins_ExecOkMsg              => Loc.GetString("Plugins_ExecOkMsg");
        public static string Plugins_ExecFailTitle          => Loc.GetString("Plugins_ExecFailTitle");
        public static string Plugins_DelTitle               => Loc.GetString("Plugins_DelTitle");
        public static string Plugins_DelConfirmFmt          => Loc.GetString("Plugins_DelConfirmFmt");
        public static string Plugins_DelConfirmBtn          => Loc.GetString("Plugins_DelConfirmBtn");
        public static string Plugins_DeletedTitle           => Loc.GetString("Plugins_DeletedTitle");

        public static string Onboard_DetectingRegion        => Loc.GetString("Onboard_DetectingRegion");
        public static string Onboard_RegionCn               => Loc.GetString("Onboard_RegionCn");
        public static string Onboard_RegionOverseas         => Loc.GetString("Onboard_RegionOverseas");
        public static string Onboard_RegionFallback         => Loc.GetString("Onboard_RegionFallback");
        public static string Onboard_StartBtn               => Loc.GetString("Onboard_StartBtn");
        public static string Onboard_NextBtnText            => Loc.GetString("Onboard_NextBtnText");
        public static string Onboard_ImportingSub           => Loc.GetString("Onboard_ImportingSub");
        public static string Onboard_ImportOkFmt            => Loc.GetString("Onboard_ImportOkFmt");
        public static string Onboard_ImportFailFmt          => Loc.GetString("Onboard_ImportFailFmt");

        public static string ClipToast_NodeInfoFmt          => Loc.GetString("ClipToast_NodeInfoFmt");

        // ── Custom Native sing-box Config ──────────────────────────────────────
        public static string Servers_DetailAddrLabelText    => Loc.GetString("Servers_DetailAddrLabelText");
        public static string Servers_DetailPortLabelText    => Loc.GetString("Servers_DetailPortLabelText");
        public static string Servers_DetailTransportLabelText => Loc.GetString("Servers_DetailTransportLabelText");
        public static string Servers_DetailLatencyLabelText => Loc.GetString("Servers_DetailLatencyLabelText");

        public static string CustomCfg_ListBadge            => Loc.GetString("CustomCfg_ListBadge");
        public static string CustomCfg_DetailSubtitle       => Loc.GetString("CustomCfg_DetailSubtitle");
        public static string CustomCfg_DetailFileLabel      => Loc.GetString("CustomCfg_DetailFileLabel");
        public static string CustomCfg_DetailSummaryLabel   => Loc.GetString("CustomCfg_DetailSummaryLabel");
        public static string CustomCfg_DetailBridgeLabel    => Loc.GetString("CustomCfg_DetailBridgeLabel");
        public static string CustomCfg_DetailModeLabel      => Loc.GetString("CustomCfg_DetailModeLabel");
        public static string CustomCfg_BridgeEnabled        => Loc.GetString("CustomCfg_BridgeEnabled");
        public static string CustomCfg_BridgeRawOnly        => Loc.GetString("CustomCfg_BridgeRawOnly");
        public static string CustomCfg_RouteSelfManaged     => Loc.GetString("CustomCfg_RouteSelfManaged");
        public static string CustomCfg_AddTitle             => Loc.GetString("CustomCfg_AddTitle");
        public static string CustomCfg_EditTitle            => Loc.GetString("CustomCfg_EditTitle");
        public static string CustomCfg_DefaultName          => Loc.GetString("CustomCfg_DefaultName");
        public static string CustomCfg_PickFileTitle        => Loc.GetString("CustomCfg_PickFileTitle");
        public static string CustomCfg_ImportedTitle        => Loc.GetString("CustomCfg_ImportedTitle");
        public static string CustomCfg_ImportedWithWarnTitle => Loc.GetString("CustomCfg_ImportedWithWarnTitle");
        public static string CustomCfg_OverwriteTemplateTitle => Loc.GetString("CustomCfg_OverwriteTemplateTitle");
        public static string CustomCfg_OverwriteTemplateMsg => Loc.GetString("CustomCfg_OverwriteTemplateMsg");
        public static string CustomCfg_TemplateInsertedTitle => Loc.GetString("CustomCfg_TemplateInsertedTitle");
        public static string CustomCfg_FormattedOkTitle     => Loc.GetString("CustomCfg_FormattedOkTitle");
        public static string CustomCfg_FormatFailTitle      => Loc.GetString("CustomCfg_FormatFailTitle");
        public static string CustomCfg_ValidateOkTitle      => Loc.GetString("CustomCfg_ValidateOkTitle");
        public static string CustomCfg_ValidateFailTitle    => Loc.GetString("CustomCfg_ValidateFailTitle");
        public static string CustomCfg_OpenExtFailTitle     => Loc.GetString("CustomCfg_OpenExtFailTitle");
        public static string CustomCfg_ReloadedTitle        => Loc.GetString("CustomCfg_ReloadedTitle");
        public static string CustomCfg_SaveBlockedInvalidTitle => Loc.GetString("CustomCfg_SaveBlockedInvalidTitle");
        public static string CustomCfg_CopiedJsonTitle      => Loc.GetString("CustomCfg_CopiedJsonTitle");
        public static string CustomCfg_CopiedJsonMsg        => Loc.GetString("CustomCfg_CopiedJsonMsg");
        public static string CustomCfg_ErrEmptyJson         => Loc.GetString("CustomCfg_ErrEmptyJson");
        public static string CustomCfg_ErrRootNotObject     => Loc.GetString("CustomCfg_ErrRootNotObject");
        public static string CustomCfg_ErrInvalidJson       => Loc.GetString("CustomCfg_ErrInvalidJson");
        public static string CustomCfg_ErrFileMissingOrEmpty => Loc.GetString("CustomCfg_ErrFileMissingOrEmpty");
        public static string CustomCfg_CoreMissingError     => Loc.GetString("CustomCfg_CoreMissingError");
        public static string CustomCfg_CheckTimeout         => Loc.GetString("CustomCfg_CheckTimeout");
    }
}
