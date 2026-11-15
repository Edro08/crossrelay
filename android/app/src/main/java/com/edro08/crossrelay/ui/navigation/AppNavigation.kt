package com.edro08.crossrelay.ui.navigation

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Devices
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.edro08.crossrelay.R
import com.edro08.crossrelay.core.CoreBridge
import com.edro08.crossrelay.core.LocalCoreClient
import com.edro08.crossrelay.storage.StorageAccessHelper
import com.edro08.crossrelay.ui.component.RelayModalSheets
import com.edro08.crossrelay.ui.screen.devices.DevicesScreen
import com.edro08.crossrelay.ui.screen.history.HistoryScreen
import com.edro08.crossrelay.ui.screen.receive.ReceiveScreen
import com.edro08.crossrelay.ui.screen.send.SendScreen
import com.edro08.crossrelay.ui.screen.settings.SettingsScreen
import com.edro08.crossrelay.ui.state.CrossRelayStateHolder
import com.edro08.crossrelay.ui.theme.*
import kotlinx.coroutines.launch

enum class RelayTab(@param:StringRes val labelRes: Int, val icon: ImageVector) {
    Send(R.string.nav_send, Icons.AutoMirrored.Filled.Send),
    Receive(R.string.nav_receive, Icons.Default.Download),
    Devices(R.string.nav_devices, Icons.Default.Devices),
    History(R.string.nav_history, Icons.Default.History),
    Settings(R.string.nav_settings, Icons.Default.Settings)
}

@Composable
fun AppNavigation(
    sharedPaths: List<String> = emptyList(),
    onManageAllFilesAccess: () -> Unit,
    onManageNotifications: () -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val scope = rememberCoroutineScope()
    val state = remember { CrossRelayStateHolder(scope, LocalCoreClient { CoreBridge.controlPort }) }
    var currentTab by remember { mutableStateOf(RelayTab.Send) }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                state.hasAllFilesAccess = StorageAccessHelper.hasAllFilesAccess()
                state.hasNotificationPermission = ContextCompat.checkSelfPermission(
                    context,
                    Manifest.permission.POST_NOTIFICATIONS
                ) == PackageManager.PERMISSION_GRANTED
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    val filesPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris: List<Uri> ->
        uris.forEach { uri ->
            StorageAccessHelper.resolveRealPath(context, uri)?.let { state.addFilePath(it) }
        }
    }

    val folderPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri: Uri? ->
        uri?.let { StorageAccessHelper.resolveTreePath(it) }?.let { state.addFolderPath(it) }
    }

    LaunchedEffect(sharedPaths) { state.addSharedPaths(sharedPaths) }
    LaunchedEffect(Unit) { state.startEventLoop() }

    Scaffold(
        containerColor = PitchBlack,
        topBar = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(PitchBlack)
                    .statusBarsPadding()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column {
                        Text(stringResource(R.string.app_name), color = TextPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                        Text(
                            text = if (state.controlPort > 0) {
                                stringResource(R.string.status_online_ports, state.activePort)
                            } else {
                                stringResource(R.string.status_starting_core)
                            },
                            color = if (state.controlPort > 0) EmeraldOnline else RelayPurpleLight,
                            fontSize = 11.sp,
                            fontFamily = FontFamily.Monospace
                        )
                    }
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(99.dp))
                            .background(SurfaceContainer)
                            .border(1.dp, BorderInput, RoundedCornerShape(99.dp))
                            .clickable { currentTab = RelayTab.Settings }
                            .padding(horizontal = 12.dp, vertical = 6.dp)
                    ) {
                        val speedRes = when (state.speedLimit) {
                            100 -> R.string.settings_speed_100
                            50 -> R.string.settings_speed_50
                            10 -> R.string.settings_speed_10
                            else -> R.string.settings_speed_unlimited
                        }
                        Text(stringResource(speedRes), color = RelayPurpleLight, fontSize = 11.sp, fontWeight = FontWeight.SemiBold, fontFamily = FontFamily.Monospace)
                    }
                }
                HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
            }
        },
        bottomBar = {
            Column(modifier = Modifier.background(ObsidianNav)) {
                HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
                NavigationBar(containerColor = ObsidianNav, tonalElevation = 0.dp) {
                    RelayTab.entries.forEach { tab ->
                        val selected = currentTab == tab
                        val label = stringResource(tab.labelRes)
                        NavigationBarItem(
                            selected = selected,
                            onClick = {
                                currentTab = tab
                                scope.launch { state.refreshFromCore() }
                            },
                            icon = { Icon(tab.icon, contentDescription = label, modifier = Modifier.size(20.dp)) },
                            label = { Text(label, fontSize = 10.sp, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal) },
                            colors = NavigationBarItemDefaults.colors(
                                selectedIconColor = RelayPurpleLight,
                                selectedTextColor = RelayPurpleLight,
                                unselectedIconColor = TextSecondary,
                                unselectedTextColor = TextSecondary,
                                indicatorColor = RelayPurpleBg
                            )
                        )
                    }
                }
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 14.dp, vertical = 10.dp)
        ) {
            when (currentTab) {
                RelayTab.Send -> SendScreen(
                    selectedPeer = state.selectedPeer,
                    queue = state.queue,
                    outgoingActive = state.outgoingActive,
                    outgoingPaused = state.outgoingPaused,
                    outgoingProgress = state.outgoingProgress,
                    outgoingSpeed = state.outgoingSpeedText,
                    onChangePeer = { state.showPeerSheet = true },
                    onOpenClean = { state.showCleanSheet = true },
                    onPickFiles = { filesPicker.launch(arrayOf("*/*")) },
                    onPickFolder = { folderPicker.launch(null) },
                    onRemoveItem = { state.queue.remove(it) },
                    onSendNow = { state.sendNow() },
                    onTogglePause = { state.togglePause() },
                    onCancelOutgoing = { state.cancelOutgoing() }
                )

                RelayTab.Receive -> ReceiveScreen(
                    downloadPath = state.downloadDir,
                    activePort = state.activePort,
                    incomingActive = state.incomingActive,
                    incomingProgress = state.incomingProgress,
                    incomingSpeed = state.incomingSpeedText,
                    incomingFiles = state.incomingFiles,
                    onCancelIncoming = { state.cancelIncoming() }
                )

                RelayTab.Devices -> DevicesScreen(
                    trustedPeers = state.trustedPeers.filter { !it.isBlocked },
                    discoveredPeers = state.discoveredPeers,
                    onOpenConnect = { state.showPeerSheet = true },
                    onPairPeer = { peer ->
                        scope.launch {
                            if (state.client.initiatePairing(peer)) {
                                state.verifyPinPeer = peer
                                state.verifyPinInput = ""
                            }
                        }
                    },
                    onEditPeer = { peer -> state.editingPeer = peer },
                    onBlockPeer = { peer ->
                        scope.launch {
                            state.client.setPeerBlocked(peer.id, true)
                            state.refreshFromCore()
                        }
                    },
                    onSetPolicy = { peer, nextPolicy ->
                        scope.launch {
                            state.client.updateTrustedPeer(peer.id, peer.name, nextPolicy, peer.dedicatedFolder)
                            state.refreshFromCore()
                        }
                    }
                )

                RelayTab.History -> HistoryScreen(history = state.history)

                RelayTab.Settings -> SettingsScreen(
                    hasAllFilesAccess = state.hasAllFilesAccess,
                    hasNotificationPermission = state.hasNotificationPermission,
                    deviceName = state.deviceName,
                    onDeviceNameChange = { state.deviceName = it },
                    downloadDir = state.downloadDir,
                    onDownloadDirChange = { state.downloadDir = it },
                    speedLimit = state.speedLimit,
                    visibility = state.visibility,
                    onVisibilityChange = { state.visibility = it },
                    portMode = state.portMode,
                    onPortModeChange = { state.portMode = it },
                    duplicatePolicy = state.duplicatePolicy,
                    onDuplicatePolicyChange = { state.duplicatePolicy = it },
                    blockedPeers = state.trustedPeers.filter { it.isBlocked },
                    statusText = state.settingsStatus,
                    onSpeedSelected = { state.persistSettings(it) },
                    onSaveAll = { state.persistSettings() },
                    onUnblockPeer = { peer ->
                        scope.launch {
                            state.client.setPeerBlocked(peer.id, false)
                            state.refreshFromCore()
                        }
                    },
                    onManageStorage = onManageAllFilesAccess,
                    onManageNotifications = onManageNotifications,
                    onOpenDiagnostics = { state.openDiagnostics() }
                )
            }
        }
    }

    RelayModalSheets(
        state = state,
        scope = scope,
        onAcceptedIncoming = { currentTab = RelayTab.Receive }
    )
}
