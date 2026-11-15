package com.edro08.crossrelay.ui.component

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.edro08.crossrelay.R
import com.edro08.crossrelay.core.CoreBridge
import com.edro08.crossrelay.ui.state.CrossRelayStateHolder
import com.edro08.crossrelay.ui.theme.*
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RelayModalSheets(
    state: CrossRelayStateHolder,
    scope: CoroutineScope,
    onAcceptedIncoming: () -> Unit
) {
    if (state.showPeerSheet) {
        val allPeers = (state.trustedPeers.filter { !it.isBlocked } + state.discoveredPeers)
            .distinctBy { it.id.ifBlank { it.ip } }
        ModalBottomSheet(onDismissRequest = { state.showPeerSheet = false }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 12.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(stringResource(R.string.send_sheet_select_peer), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                if (allPeers.isEmpty()) {
                    Text(stringResource(R.string.send_sheet_no_peers), color = TextMuted, fontSize = 12.sp)
                } else {
                    allPeers.forEach { peer ->
                        PeerOptionCard(
                            name = peer.name,
                            sub = "${peer.os.uppercase()} · ${peer.ip}:${peer.port}",
                            selected = state.selectedPeer?.id == peer.id
                        ) {
                            state.selectedPeer = peer
                            state.showPeerSheet = false
                        }
                    }
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedTextField(
                        value = state.probeInput,
                        onValueChange = { state.probeInput = it },
                        placeholder = { Text(stringResource(R.string.send_sheet_ip_placeholder), fontSize = 11.sp) },
                        singleLine = true,
                        modifier = Modifier.weight(1f)
                    )
                    Button(
                        onClick = { state.probeManualPeer() },
                        colors = ButtonDefaults.buttonColors(containerColor = RelayPurple)
                    ) {
                        Text(stringResource(R.string.action_probe), fontSize = 11.sp)
                    }
                }
                if (state.probeFeedback.isNotBlank()) {
                    Text(
                        text = state.probeFeedback,
                        color = if (state.probeFeedback.startsWith("✓")) EmeraldOnline else RoseDangerText,
                        fontSize = 11.sp
                    )
                }
                Spacer(modifier = Modifier.height(10.dp))
            }
        }
    }

    if (state.showCleanSheet) {
        ModalBottomSheet(onDismissRequest = { state.showCleanSheet = false }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 12.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(stringResource(R.string.send_sheet_clean_title), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                CleanOptionButton(stringResource(R.string.send_clean_done), EmeraldOnline) {
                    state.queue.removeAll { it.status == "Listo" }
                    state.showCleanSheet = false
                }
                CleanOptionButton(stringResource(R.string.send_clean_pending), TextSecondary) {
                    state.queue.removeAll { it.status == "En cola" }
                    state.showCleanSheet = false
                }
                CleanOptionButton(stringResource(R.string.send_clean_all), RoseDangerText) {
                    state.queue.clear()
                    state.showCleanSheet = false
                }
                Spacer(modifier = Modifier.height(10.dp))
            }
        }
    }

    if (state.pinDialogPin != null) {
        ModalBottomSheet(onDismissRequest = { state.pinDialogPin = null }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(stringResource(R.string.devices_pairing_title), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Text(stringResource(R.string.devices_pairing_subtitle), color = TextSecondary, fontSize = 12.sp)
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(16.dp))
                        .background(ObsidianInput)
                        .border(1.dp, Color(0xFF332763), RoundedCornerShape(16.dp))
                        .padding(vertical = 14.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = state.pinDialogPin.orEmpty(),
                        color = RelayPurpleLight,
                        fontSize = 28.sp,
                        fontWeight = FontWeight.ExtraBold,
                        fontFamily = FontFamily.Monospace,
                        letterSpacing = 6.sp
                    )
                }
                Text(stringResource(R.string.devices_pairing_expiry), color = EmeraldOnline, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
            }
        }
    }

    state.verifyPinPeer?.let { peer ->
        ModalBottomSheet(onDismissRequest = { state.verifyPinPeer = null }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(stringResource(R.string.devices_pair_with, peer.name), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                OutlinedTextField(
                    value = state.verifyPinInput,
                    onValueChange = { if (it.length <= 6) state.verifyPinInput = it },
                    label = { Text(stringResource(R.string.devices_pin_label)) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Button(
                    onClick = {
                        scope.launch {
                            if (state.client.verifyPairing(peer, state.verifyPinInput)) {
                                state.verifyPinPeer = null
                                state.refreshFromCore()
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = RelayPurple)
                ) {
                    Text(stringResource(R.string.devices_verify_and_pair), color = Color.White, fontWeight = FontWeight.Bold)
                }
            }
        }
    }

    state.incomingPromptSessionId?.let { sid ->
        ModalBottomSheet(onDismissRequest = { state.incomingPromptSessionId = null }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(stringResource(R.string.receive_incoming_request_title), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                Text(state.incomingPromptText, color = TextSecondary, fontSize = 13.sp)
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedButton(
                        onClick = {
                            scope.launch {
                                state.client.respondTransfer(sid, false)
                                state.incomingPromptSessionId = null
                            }
                        },
                        modifier = Modifier.weight(1f)
                    ) { Text(stringResource(R.string.action_reject), color = RoseDangerText) }
                    Button(
                        onClick = {
                            scope.launch {
                                state.client.respondTransfer(sid, true)
                                state.incomingPromptSessionId = null
                                onAcceptedIncoming()
                            }
                        },
                        modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(containerColor = RelayPurple)
                    ) { Text(stringResource(R.string.action_accept), color = Color.White) }
                }
            }
        }
    }

    if (state.showDiagnosticsSheet) {
        ModalBottomSheet(onDismissRequest = { state.showDiagnosticsSheet = false }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(stringResource(R.string.settings_diag_sheet_title), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                val entries = state.diagResult?.ports.orEmpty()
                if (entries.isEmpty()) {
                    Text(stringResource(R.string.settings_diag_querying), color = TextSecondary, fontSize = 12.sp)
                } else {
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        entries.take(3).forEach { e ->
                            val (label, col) = when (e.state) {
                                "active_self" -> stringResource(R.string.settings_diag_active) to EmeraldOnline
                                "free" -> stringResource(R.string.settings_diag_free) to EmeraldOnline
                                else -> stringResource(R.string.settings_diag_occupied) to RoseDangerText
                            }
                            PortDiagChip(":${e.port}", label, col, Modifier.weight(1f))
                        }
                    }
                }
                Text(
                    text = if (CoreBridge.isNativeLoaded) {
                        stringResource(R.string.settings_diag_core_running, CoreBridge.controlPort)
                    } else {
                        stringResource(R.string.settings_diag_core_standby)
                    },
                    color = if (CoreBridge.isNativeLoaded) EmeraldOnline else RelayPurpleLight,
                    fontSize = 11.sp,
                    fontFamily = FontFamily.Monospace
                )
                Spacer(modifier = Modifier.height(8.dp))
            }
        }
    }

    state.editingPeer?.let { peer ->
        var alias by remember(peer.id) { mutableStateOf(peer.name) }
        var folder by remember(peer.id) { mutableStateOf(peer.dedicatedFolder) }
        ModalBottomSheet(onDismissRequest = { state.editingPeer = null }, containerColor = ObsidianSurface) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 12.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text("Editar vínculo", color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                OutlinedTextField(
                    value = alias,
                    onValueChange = { alias = it },
                    label = { Text("Alias del equipo") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(RelaySpacing.controlRadius)
                )
                OutlinedTextField(
                    value = folder,
                    onValueChange = { folder = it },
                    label = { Text("Carpeta dedicada (vacío = global)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(RelaySpacing.controlRadius)
                )
                Button(
                    onClick = {
                        scope.launch {
                            state.client.updateTrustedPeer(peer.id, alias.trim().ifBlank { peer.name }, peer.policy, folder.trim())
                            state.editingPeer = null
                            state.refreshFromCore()
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = RelayPurple),
                    shape = RoundedCornerShape(RelaySpacing.controlRadius)
                ) {
                    Text(stringResource(R.string.action_save_settings), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                }
                Spacer(modifier = Modifier.height(8.dp))
            }
        }
    }
}
