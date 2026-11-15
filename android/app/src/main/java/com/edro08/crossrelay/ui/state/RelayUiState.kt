package com.edro08.crossrelay.ui.state

import androidx.compose.runtime.*
import com.edro08.crossrelay.core.CoreBridge
import com.edro08.crossrelay.core.LocalCoreClient
import com.edro08.crossrelay.core.PeerItem
import com.edro08.crossrelay.core.PortDiagnosticsResponse
import com.edro08.crossrelay.storage.StorageAccessHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.File

data class QueueEntry(
    val path: String,
    val name: String,
    val sizeLabel: String,
    val status: String
)

data class HistoryEntry(
    val title: String,
    val subtitle: String,
    val direction: String
)

data class IncomingFileEntry(
    val name: String,
    val done: Boolean
)

class CrossRelayStateHolder(
    private val scope: CoroutineScope,
    val client: LocalCoreClient
) {
    var hasAllFilesAccess by mutableStateOf(StorageAccessHelper.hasAllFilesAccess())
    var hasNotificationPermission by mutableStateOf(true)
    var activePort by mutableIntStateOf(4242)
    var controlPort by mutableIntStateOf(CoreBridge.controlPort)

    // Settings
    var deviceName by mutableStateOf("Android-Node")
    var downloadDir by mutableStateOf(StorageAccessHelper.defaultDownloadDirectory())
    var speedLimit by mutableIntStateOf(0)
    var visibility by mutableStateOf("everyone")
    var portMode by mutableStateOf("auto")
    var portRangeStart by mutableIntStateOf(4242)
    var portRangeEnd by mutableIntStateOf(4248)
    var duplicatePolicy by mutableStateOf("rename")
    var settingsStatus by mutableStateOf("")

    // Peers
    val discoveredPeers = mutableStateListOf<PeerItem>()
    val trustedPeers = mutableStateListOf<PeerItem>()
    var selectedPeer by mutableStateOf<PeerItem?>(null)

    // Queue & Telemetry
    val queue = mutableStateListOf<QueueEntry>()
    val history = mutableStateListOf<HistoryEntry>()
    var outgoingActive by mutableStateOf(false)
    var outgoingPaused by mutableStateOf(false)
    var outgoingProgress by mutableFloatStateOf(0f)
    var outgoingSpeedText by mutableStateOf("0 MB/s")
    var incomingActive by mutableStateOf(false)
    var incomingProgress by mutableFloatStateOf(0f)
    var incomingSpeedText by mutableStateOf("0 MB/s")
    val incomingFiles = mutableStateListOf<IncomingFileEntry>()

    // Sheets / Modals
    var showPeerSheet by mutableStateOf(false)
    var showCleanSheet by mutableStateOf(false)
    var showDiagnosticsSheet by mutableStateOf(false)
    var editingPeer by mutableStateOf<PeerItem?>(null)
    var diagResult by mutableStateOf<PortDiagnosticsResponse?>(null)
    var probeInput by mutableStateOf("")
    var probeFeedback by mutableStateOf("")

    var pinDialogPin by mutableStateOf<String?>(null)
    var verifyPinPeer by mutableStateOf<PeerItem?>(null)
    var verifyPinInput by mutableStateOf("")
    var incomingPromptSessionId by mutableStateOf<String?>(null)
    var incomingPromptText by mutableStateOf("")

    val speedLabel: String
        get() = when (speedLimit) {
            100 -> "100 MB/s"
            50 -> "50 MB/s"
            10 -> "10 MB/s"
            else -> "Sin límite"
        }

    fun addSharedPaths(paths: List<String>) {
        paths.forEach { path ->
            val f = File(path)
            if (f.exists() && queue.none { it.path == path }) {
                queue.add(QueueEntry(path, f.name, "Compartido", "En cola"))
            }
        }
    }

    fun addFilePath(path: String) {
        val file = File(path)
        if (queue.none { it.path == path }) {
            val mb = file.length() / (1024.0 * 1024.0)
            queue.add(QueueEntry(path, file.name, String.format("%.1f MB", mb), "En cola"))
        }
    }

    fun addFolderPath(path: String) {
        val dir = File(path)
        if (queue.none { it.path == path }) {
            queue.add(QueueEntry(path, "${dir.name}/", "Carpeta", "En cola"))
        }
    }

    suspend fun refreshFromCore() {
        hasAllFilesAccess = StorageAccessHelper.hasAllFilesAccess()
        controlPort = CoreBridge.controlPort
        client.getStatus()?.let { status ->
            val ap = status.optInt("activePort", 0)
            if (ap > 0) activePort = ap
            status.optJSONObject("settings")?.let { s ->
                deviceName = s.optString("deviceName", deviceName)
                downloadDir = s.optString("downloadDirectory", downloadDir).ifBlank { downloadDir }
                speedLimit = s.optInt("speedLimit", 0)
                visibility = s.optString("visibility", "everyone")
                portMode = s.optString("portMode", "auto")
                portRangeStart = s.optInt("port", 4242)
                portRangeEnd = s.optInt("portRangeEnd", 4248)
                duplicatePolicy = s.optString("duplicatePolicy", "rename")
            }
        }
        val trusted = client.getTrustedPeers()
        trustedPeers.clear()
        trustedPeers.addAll(trusted)
        if (selectedPeer == null) {
            selectedPeer = trusted.firstOrNull { !it.isBlocked && it.isOnline }
                ?: discoveredPeers.firstOrNull { it.isOnline }
        }
    }

    suspend fun startEventLoop() {
        while (CoreBridge.controlPort <= 0) {
            delay(300)
        }
        refreshFromCore()
        client.streamEvents { eventType, data ->
            scope.launch { handleSseEvent(eventType, data) }
        }
    }

    private suspend fun handleSseEvent(eventType: String, data: JSONObject) {
        when (eventType) {
            "peer_discovered" -> {
                val p = PeerItem(
                    id = data.optString("id"),
                    name = data.optString("name", data.optString("ip")),
                    os = data.optString("os", "unknown"),
                    ip = data.optString("ip"),
                    port = data.optInt("port", 4242),
                    fingerprint = data.optString("fingerprint"),
                    isOnline = true,
                    isPaired = data.optBoolean("isPaired", false),
                    isBlocked = false,
                    policy = "AskConfirmation",
                    dedicatedFolder = ""
                )
                val idx = discoveredPeers.indexOfFirst { it.id == p.id }
                if (idx >= 0) discoveredPeers[idx] = p else discoveredPeers.add(p)
                if (selectedPeer == null) selectedPeer = p
            }
            "peer_offline" -> {
                val id = data.optString("id")
                discoveredPeers.removeAll { it.id == id }
            }
            "peer_paired" -> {
                pinDialogPin = null
                verifyPinPeer = null
                refreshFromCore()
            }
            "pin_challenge" -> pinDialogPin = data.optString("pin")
            "incoming_transfer" -> {
                incomingPromptSessionId = data.optString("sessionId")
                val sender = data.optString("senderName", "Equipo remoto")
                val count = data.optInt("fileCount", 1)
                incomingPromptText = "$sender quiere enviarte $count archivo(s)."
            }
            "transfer_progress" -> {
                val dir = data.optString("direction", "outgoing")
                val rec = data.optLong("bytesTransferred", 0L)
                val tot = data.optLong("totalBytes", 1L).coerceAtLeast(1L)
                val bps = data.optLong("speedBps", 0L)
                val mbps = String.format("%.1f MB/s", bps / (1024.0 * 1024.0))
                val pct = (rec.toFloat() / tot.toFloat()).coerceIn(0f, 1f)
                if (dir == "incoming") {
                    incomingActive = true
                    incomingProgress = pct
                    incomingSpeedText = mbps
                    val currentFile = data.optString("currentFile", data.optString("fileName", ""))
                    if (currentFile.isNotBlank() && incomingFiles.none { it.name == currentFile }) {
                        for (i in incomingFiles.indices) {
                            if (!incomingFiles[i].done) incomingFiles[i] = incomingFiles[i].copy(done = true)
                        }
                        if (incomingFiles.size >= 200) {
                            incomingFiles.removeAt(0)
                        }
                        incomingFiles.add(IncomingFileEntry(currentFile, false))
                    }
                } else {
                    outgoingActive = true
                    outgoingProgress = pct
                    outgoingSpeedText = mbps
                }
            }
            "transfer_completed" -> {
                val dir = data.optString("direction", "outgoing")
                val ok = data.optBoolean("success", false)
                if (dir == "incoming") {
                    incomingActive = false
                    for (i in incomingFiles.indices) {
                        incomingFiles[i] = incomingFiles[i].copy(done = ok)
                    }
                    history.add(0, HistoryEntry("Recepción LAN", if (ok) "Completado ✓" else "Cancelado/Error", "↓ Recibido"))
                } else {
                    outgoingActive = false
                    outgoingPaused = false
                    history.add(0, HistoryEntry("Envío LAN (${queue.size})", if (ok) "Completado ✓" else "Cancelado/Error", "↑ Enviado"))
                }
            }
        }
    }

    fun persistSettings(newSpeed: Int = speedLimit) {
        scope.launch {
            val payload = JSONObject()
                .put("deviceName", deviceName)
                .put("downloadDirectory", downloadDir)
                .put("visibility", visibility)
                .put("speedLimit", newSpeed)
                .put("port", portRangeStart)
                .put("portMode", portMode)
                .put("portRangeEnd", portRangeEnd)
                .put("duplicatePolicy", duplicatePolicy)
            if (client.saveSettings(payload)) {
                speedLimit = newSpeed
                settingsStatus = "✓ Ajustes guardados en Go Core"
            }
        }
    }

    fun sendNow() {
        val target = selectedPeer ?: return
        if (queue.isEmpty()) return
        scope.launch {
            outgoingActive = true
            outgoingProgress = 0f
            client.sendFiles(target, queue.map { it.path })
        }
    }

    fun togglePause() {
        scope.launch {
            val next = !outgoingPaused
            if (client.setTransferPaused(next)) outgoingPaused = next
        }
    }

    fun cancelOutgoing() {
        scope.launch {
            client.cancelOutgoing()
            outgoingActive = false
            outgoingPaused = false
        }
    }

    fun cancelIncoming() {
        scope.launch {
            client.cancelIncoming()
            incomingActive = false
        }
    }

    fun openDiagnostics() {
        showDiagnosticsSheet = true
        scope.launch {
            diagResult = client.diagnosePorts(listOf(4242, 4244, 4245, 4246, 4247, 4248))
        }
    }

    fun probeManualPeer() {
        scope.launch {
            val parts = probeInput.trim().split(":")
            val ip = parts.getOrNull(0) ?: ""
            val port = parts.getOrNull(1)?.toIntOrNull() ?: 0
            val found = client.probePeer(ip, port)
            if (found != null) {
                selectedPeer = found
                if (discoveredPeers.none { it.id == found.id }) discoveredPeers.add(found)
                probeFeedback = "✓ Conectado a ${found.name}"
                showPeerSheet = false
            } else {
                probeFeedback = "✕ No se pudo contactar a $probeInput"
            }
        }
    }
}
