package com.edro08.crossrelay.ui.screen.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.edro08.crossrelay.R
import com.edro08.crossrelay.core.PeerItem
import com.edro08.crossrelay.ui.component.RelayBadge
import com.edro08.crossrelay.ui.component.RelayCard
import com.edro08.crossrelay.ui.component.RelayDropdown
import com.edro08.crossrelay.ui.component.RelayIconTile
import com.edro08.crossrelay.ui.theme.*

@Composable
fun SettingsScreen(
    hasAllFilesAccess: Boolean,
    hasNotificationPermission: Boolean,
    deviceName: String,
    onDeviceNameChange: (String) -> Unit,
    downloadDir: String,
    onDownloadDirChange: (String) -> Unit,
    speedLimit: Int,
    visibility: String,
    onVisibilityChange: (String) -> Unit,
    portMode: String,
    onPortModeChange: (String) -> Unit,
    duplicatePolicy: String,
    onDuplicatePolicyChange: (String) -> Unit,
    blockedPeers: List<PeerItem>,
    statusText: String,
    onSpeedSelected: (Int) -> Unit,
    onSaveAll: () -> Unit,
    onUnblockPeer: (PeerItem) -> Unit,
    onManageStorage: () -> Unit,
    onManageNotifications: () -> Unit,
    onOpenDiagnostics: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)
    ) {
        // Permisos del sistema (MANAGE_EXTERNAL_STORAGE + POST_NOTIFICATIONS)
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    RelayIconTile(icon = Icons.Default.FolderOpen, size = 34.dp)
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(R.string.settings_perms_title),
                            color = TextPrimary,
                            fontWeight = FontWeight.Bold,
                            fontSize = 13.sp
                        )
                        Text(
                            text = stringResource(R.string.settings_perms_subtitle),
                            color = TextSecondary,
                            fontSize = 10.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }

                PermissionRow(
                    title = stringResource(R.string.settings_perm_storage_title),
                    subtitle = stringResource(R.string.settings_perm_storage_desc),
                    granted = hasAllFilesAccess,
                    onManage = onManageStorage
                )

                PermissionRow(
                    title = stringResource(R.string.settings_perm_notif_title),
                    subtitle = stringResource(R.string.settings_perm_notif_desc),
                    granted = hasNotificationPermission,
                    onManage = onManageNotifications
                )
            }
        }

        // Límite de Velocidad (Dropdown)
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.settings_throttling_title), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                RelayDropdown(
                    selectedValue = speedLimit,
                    options = listOf(
                        0 to stringResource(R.string.settings_speed_unlimited_full),
                        100 to stringResource(R.string.settings_speed_100),
                        50 to stringResource(R.string.settings_speed_50),
                        10 to stringResource(R.string.settings_speed_10)
                    ),
                    onSelect = onSpeedSelected
                )
            }
        }

        // Identidad y Red (con Diagnóstico de Puertos dentro)
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.settings_identity_network_title), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 12.sp)

                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.settings_device_name_label), color = TextSecondary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                    OutlinedTextField(
                        value = deviceName,
                        onValueChange = onDeviceNameChange,
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(RelaySpacing.controlRadius)
                    )
                }

                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("Modo de Descubrimiento mDNS", color = TextSecondary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                    RelayDropdown(
                        selectedValue = visibility,
                        options = listOf(
                            "everyone" to stringResource(R.string.settings_vis_all),
                            "paired" to stringResource(R.string.settings_vis_paired),
                            "hidden" to stringResource(R.string.settings_vis_hidden)
                        ),
                        onSelect = onVisibilityChange
                    )
                }

                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("Política de Puertos", color = TextSecondary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                    RelayDropdown(
                        selectedValue = portMode,
                        options = listOf(
                            "auto" to stringResource(R.string.settings_port_auto),
                            "fixed" to stringResource(R.string.settings_port_fixed),
                            "range" to stringResource(R.string.settings_port_range)
                        ),
                        onSelect = onPortModeChange
                    )
                }

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(38.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(IconTileBg)
                        .border(1.dp, IconTileBorder, RoundedCornerShape(12.dp))
                        .clickable(onClick = onOpenDiagnostics),
                    contentAlignment = Alignment.Center
                ) {
                    Text(stringResource(R.string.settings_diagnose_ports), color = RelayPurpleLight, fontWeight = FontWeight.SemiBold, fontSize = 11.sp)
                }
            }
        }

        // Almacenamiento y Archivos
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.settings_files_title), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.settings_global_folder_label), color = TextSecondary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                    OutlinedTextField(
                        value = downloadDir,
                        onValueChange = onDownloadDirChange,
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(RelaySpacing.controlRadius)
                    )
                }
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("Archivos Duplicados al Recibir", color = TextSecondary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                    RelayDropdown(
                        selectedValue = duplicatePolicy,
                        options = listOf(
                            "rename" to stringResource(R.string.settings_dup_rename),
                            "overwrite" to stringResource(R.string.settings_dup_overwrite),
                            "skip" to stringResource(R.string.settings_dup_skip)
                        ),
                        onSelect = onDuplicatePolicyChange
                    )
                }
                Button(
                    onClick = onSaveAll,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(42.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = RelayPurple)
                ) {
                    Text(stringResource(R.string.action_save_settings), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                }
                if (statusText.isNotBlank()) {
                    Text(statusText, color = EmeraldOnline, fontSize = 11.sp)
                }
            }
        }

        // Nodos Bloqueados (card independiente)
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(stringResource(R.string.settings_blocked_title, blockedPeers.size), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                }
                if (blockedPeers.isEmpty()) {
                    Text(stringResource(R.string.settings_blocked_empty), color = TextMuted, fontSize = 11.sp)
                } else {
                    blockedPeers.forEach { peer ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(12.dp))
                                .background(ObsidianInput)
                                .border(1.dp, RoseDangerBorder, RoundedCornerShape(12.dp))
                                .padding(horizontal = 12.dp, vertical = 8.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(peer.name, color = RoseDangerText, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(SurfaceContainer)
                                    .border(1.dp, ButtonSecondaryBorder, RoundedCornerShape(10.dp))
                                    .clickable { onUnblockPeer(peer) }
                                    .padding(horizontal = 10.dp, vertical = 5.dp)
                            ) {
                                Text(stringResource(R.string.action_unblock), color = TextPrimary, fontSize = 10.sp, fontWeight = FontWeight.SemiBold)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun PermissionRow(
    title: String,
    subtitle: String,
    granted: Boolean,
    onManage: () -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Row(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = title,
                        color = TextPrimary,
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    RelayBadge(
                        text = stringResource(if (granted) R.string.settings_perm_active else R.string.settings_perm_inactive),
                        textColor = if (granted) EmeraldOnline else WarningAmber,
                        bgColor = if (granted) EmeraldOnline.copy(alpha = 0.12f) else WarningAmber.copy(alpha = 0.12f)
                    )
                }
                Text(
                    text = subtitle,
                    color = if (granted) TextMuted else WarningAmber,
                    fontSize = 10.sp,
                    fontFamily = FontFamily.Monospace,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
            }
            Spacer(modifier = Modifier.width(8.dp))
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(10.dp))
                    .background(ObsidianInput)
                    .border(1.dp, ButtonSecondaryBorder, RoundedCornerShape(10.dp))
                    .clickable(onClick = onManage)
                    .padding(horizontal = 10.dp, vertical = 6.dp)
            ) {
                Text(stringResource(R.string.settings_perm_manage), color = RelayPurpleLight, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
            }
        }
    }
}
