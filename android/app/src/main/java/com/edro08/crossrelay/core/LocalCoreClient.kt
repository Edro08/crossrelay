package com.edro08.crossrelay.core

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL

data class PeerItem(
    val id: String,
    val name: String,
    val os: String,
    val ip: String,
    val port: Int,
    val fingerprint: String,
    val isOnline: Boolean,
    val isPaired: Boolean,
    val isBlocked: Boolean,
    val policy: String,
    val dedicatedFolder: String
)

data class PortDiagEntry(
    val port: Int,
    val state: String
)

data class PortDiagnosticsResponse(
    val os: String,
    val activePort: Int,
    val firewallStatus: String,
    val ports: List<PortDiagEntry>
)

class LocalCoreClient(private val portProvider: () -> Int) {
    private fun baseUrl(): String? {
        val port = portProvider()
        return if (port > 0) "http://127.0.0.1:$port" else null
    }

    suspend fun getStatus(): JSONObject? = requestJson("GET", "/local/v1/status")

    suspend fun getSettings(): JSONObject? = requestJson("GET", "/local/v1/settings")

    suspend fun saveSettings(payload: JSONObject): Boolean =
        requestJson("POST", "/local/v1/settings", payload) != null

    suspend fun getTrustedPeers(): List<PeerItem> {
        val arr = requestArray("GET", "/local/v1/trusted") ?: return emptyList()
        return buildList {
            for (i in 0 until arr.length()) {
                val p = arr.getJSONObject(i)
                add(
                    PeerItem(
                        id = p.optString("id"),
                        name = p.optString("customAlias").ifBlank { p.optString("name", "Peer") },
                        os = p.optString("os", "unknown"),
                        ip = p.optString("ip", ""),
                        port = p.optInt("port", 4242),
                        fingerprint = p.optString("fingerprint", ""),
                        isOnline = p.optBoolean("isOnline", false),
                        isPaired = true,
                        isBlocked = p.optBoolean("isBlocked", false),
                        policy = p.optString("policy", "AskConfirmation"),
                        dedicatedFolder = p.optString("dedicatedDownloadFolder", "")
                    )
                )
            }
        }
    }

    suspend fun setPeerBlocked(id: String, blocked: Boolean): Boolean {
        val body = JSONObject().put("id", id).put("blocked", blocked)
        return requestJson("POST", "/local/v1/trusted/block", body) != null
    }

    suspend fun updateTrustedPeer(id: String, alias: String, policy: String, folder: String): Boolean {
        val body = JSONObject()
            .put("id", id)
            .put("customAlias", alias)
            .put("policy", policy)
            .put("dedicatedDownloadFolder", folder)
        return requestJson("POST", "/local/v1/trusted/update", body) != null
    }

    suspend fun probePeer(ip: String, port: Int): PeerItem? {
        val body = JSONObject().put("ip", ip).put("port", port)
        val p = requestJson("POST", "/local/v1/peers/probe", body) ?: return null
        return PeerItem(
            id = p.optString("id"),
            name = p.optString("name", ip),
            os = p.optString("os", "unknown"),
            ip = p.optString("ip", ip),
            port = p.optInt("port", if (port > 0) port else 4242),
            fingerprint = p.optString("fingerprint", ""),
            isOnline = true,
            isPaired = p.optBoolean("isPaired", false),
            isBlocked = false,
            policy = "AskConfirmation",
            dedicatedFolder = ""
        )
    }

    suspend fun initiatePairing(peer: PeerItem): Boolean {
        val body = JSONObject().put(
            "peer",
            JSONObject()
                .put("id", peer.id)
                .put("name", peer.name)
                .put("os", peer.os)
                .put("ip", peer.ip)
                .put("port", peer.port)
                .put("fingerprint", peer.fingerprint)
        )
        return requestJson("POST", "/local/v1/pair/init", body) != null
    }

    suspend fun verifyPairing(peer: PeerItem, pin: String): Boolean {
        val body = JSONObject()
            .put(
                "peer",
                JSONObject()
                    .put("id", peer.id)
                    .put("name", peer.name)
                    .put("os", peer.os)
                    .put("ip", peer.ip)
                    .put("port", peer.port)
                    .put("fingerprint", peer.fingerprint)
            )
            .put("pin", pin)
        return requestJson("POST", "/local/v1/pair/verify", body) != null
    }

    suspend fun sendFiles(peer: PeerItem, filePaths: List<String>): Boolean {
        val body = JSONObject()
            .put(
                "peer",
                JSONObject()
                    .put("id", peer.id)
                    .put("name", peer.name)
                    .put("os", peer.os)
                    .put("ip", peer.ip)
                    .put("port", peer.port)
                    .put("fingerprint", peer.fingerprint)
            )
            .put("filePaths", JSONArray(filePaths))
        return requestJson("POST", "/local/v1/send", body) != null
    }

    suspend fun respondTransfer(sessionId: String, accepted: Boolean): Boolean {
        val body = JSONObject().put("sessionId", sessionId).put("accepted", accepted)
        return requestJson("POST", "/local/v1/transfer/respond", body) != null
    }

    suspend fun diagnosePorts(ports: List<Int>): PortDiagnosticsResponse? {
        val arr = JSONArray(ports)
        val obj = requestJson("POST", "/local/v1/diagnostics/ports", JSONObject().put("ports", arr))
            ?: return null
        val list = mutableListOf<PortDiagEntry>()
        val items = obj.optJSONArray("ports") ?: JSONArray()
        for (i in 0 until items.length()) {
            val e = items.getJSONObject(i)
            list.add(PortDiagEntry(e.optInt("port"), e.optString("state")))
        }
        return PortDiagnosticsResponse(
            os = obj.optString("os", "android"),
            activePort = obj.optInt("activePort", 0),
            firewallStatus = obj.optString("firewallStatus", "check_manual"),
            ports = list
        )
    }

    suspend fun setTransferPaused(paused: Boolean): Boolean =
        requestJson("POST", "/local/v1/transfer/pause", JSONObject().put("paused", paused)) != null

    suspend fun cancelOutgoing(): Boolean =
        requestJson("POST", "/local/v1/transfer/cancel", JSONObject()) != null

    suspend fun cancelIncoming(): Boolean =
        requestJson("POST", "/local/v1/receive/cancel", JSONObject()) != null

    suspend fun streamEvents(onEvent: (eventType: String, data: JSONObject) -> Unit) =
        withContext(Dispatchers.IO) {
            val base = baseUrl() ?: return@withContext
            var conn: HttpURLConnection? = null
            try {
                conn = (URL("$base/local/v1/events").openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = 3000
                    readTimeout = 0
                    setRequestProperty("Accept", "text/event-stream")
                }
                if (conn.responseCode !in 200..299) return@withContext
                val reader = BufferedReader(InputStreamReader(conn.inputStream))
                var currentEvent = "message"
                while (currentCoroutineContext().isActive) {
                    val line = reader.readLine() ?: break
                    when {
                        line.startsWith("event:") -> currentEvent = line.removePrefix("event:").trim()
                        line.startsWith("data:") -> {
                            val raw = line.removePrefix("data:").trim()
                            val json = runCatching { JSONObject(raw) }.getOrNull() ?: JSONObject()
                            onEvent(currentEvent, json)
                        }
                    }
                }
            } catch (_: Throwable) {
            } finally {
                conn?.disconnect()
            }
        }

    private suspend fun requestJson(
        method: String,
        path: String,
        body: JSONObject? = null
    ): JSONObject? {
        val text = requestRaw(method, path, body) ?: return null
        return runCatching { if (text.isBlank()) JSONObject() else JSONObject(text) }.getOrNull()
    }

    private suspend fun requestArray(
        method: String,
        path: String
    ): JSONArray? {
        val text = requestRaw(method, path, null) ?: return null
        return runCatching { if (text.isBlank()) JSONArray() else JSONArray(text) }.getOrNull()
    }

    private suspend fun requestRaw(
        method: String,
        path: String,
        body: JSONObject?
    ): String? = withContext(Dispatchers.IO) {
        val base = baseUrl() ?: return@withContext null
        var conn: HttpURLConnection? = null
        try {
            conn = (URL("$base$path").openConnection() as HttpURLConnection).apply {
                requestMethod = method
                connectTimeout = 2500
                readTimeout = 5000
                setRequestProperty("Accept", "application/json")
                if (body != null) {
                    doOutput = true
                    setRequestProperty("Content-Type", "application/json")
                    outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
                }
            }
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else return@withContext null
            stream.bufferedReader().use { it.readText() }
        } catch (_: Throwable) {
            null
        } finally {
            conn?.disconnect()
        }
    }
}
