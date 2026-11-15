package com.edro08.crossrelay.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import com.edro08.crossrelay.R
import com.edro08.crossrelay.core.CoreBridge
import com.edro08.crossrelay.core.LocalCoreClient
import com.edro08.crossrelay.storage.StorageAccessHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File

class CrossRelayService : Service() {
    companion object {
        const val CHANNEL_ID = "crossrelay_transfer_channel"
        const val NOTIF_ID = 4242
        private const val ACTION_PAUSE_RESUME = "com.edro08.crossrelay.ACTION_PAUSE_RESUME"
        private const val ACTION_CANCEL = "com.edro08.crossrelay.ACTION_CANCEL"

        fun start(context: Context) {
            val intent = Intent(context, CrossRelayService::class.java)
            context.startForegroundService(intent)
        }
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = LocalCoreClient { CoreBridge.controlPort }
    private var multicastLock: WifiManager.MulticastLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var wakeLock: PowerManager.WakeLock? = null
    @Volatile private var transferPaused = false
    @Volatile private var activeDirection = "outgoing"

    override fun onCreate() {
        super.onCreate()
        ensureNotificationChannel()
        startForeground(NOTIF_ID, buildNotification("Iniciando nodo TLS 1.3..."))
        acquireNetworkLocks()

        serviceScope.launch {
            val dataDir = File(filesDir, "crossrelay").apply { mkdirs() }.absolutePath
            val deviceName = Build.MODEL ?: "Android-Node"
            val downloadDir = StorageAccessHelper.defaultDownloadDirectory()
            val ipcPort = CoreBridge.startNode(dataDir, deviceName, downloadDir)
            notifyIdle(ipcPort)
            if (ipcPort > 0) {
                observeTransfers(ipcPort)
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_PAUSE_RESUME -> {
                serviceScope.launch {
                    transferPaused = !transferPaused
                    client.setTransferPaused(transferPaused)
                }
            }
            ACTION_CANCEL -> {
                serviceScope.launch {
                    if (activeDirection == "incoming") {
                        client.cancelIncoming()
                    } else {
                        client.cancelOutgoing()
                    }
                    transferPaused = false
                    notifyIdle(CoreBridge.controlPort)
                }
            }
        }
        return START_STICKY
    }

    override fun onDestroy() {
        CoreBridge.stopNode()
        releaseNetworkLocks()
        serviceScope.cancel()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private suspend fun observeTransfers(ipcPort: Int) {
        while (serviceScope.isActive) {
            try {
                client.streamEvents { event, data ->
                    when (event) {
                        "transfer_progress" -> {
                            activeDirection = data.optString("direction", "outgoing")
                            val rec = data.optLong("bytesTransferred", 0L)
                            val tot = data.optLong("totalBytes", 1L).coerceAtLeast(1L)
                            val bps = data.optLong("speedBps", 0L)
                            val pct = ((rec * 100L) / tot).toInt().coerceIn(0, 100)
                            val mbps = String.format("%.1f MB/s", bps / (1024.0 * 1024.0))
                            val prefix = if (activeDirection == "incoming") "Recibiendo" else "Enviando"
                            val fileName = data.optString("currentFile", data.optString("fileName", ""))
                            val text = if (fileName.isNotBlank()) "$prefix $pct% · $mbps · $fileName" else "$prefix $pct% · $mbps"
                            val nm = getSystemService(NotificationManager::class.java)
                            nm?.notify(NOTIF_ID, buildTransferNotification(text, pct))
                        }
                        "transfer_completed" -> {
                            transferPaused = false
                            notifyIdle(ipcPort)
                        }
                    }
                }
            } catch (_: Throwable) {
            }
            delay(2500)
        }
    }

    private fun notifyIdle(ipcPort: Int) {
        val statusText = if (ipcPort > 0) {
            "En línea · TLS 1.3 :${CoreBridge.activeP2PPort} · UDP :4243"
        } else {
            "Servicio activo (esperando crossrelay.aar)"
        }
        val nm = getSystemService(NotificationManager::class.java)
        nm?.notify(NOTIF_ID, buildNotification(statusText))
    }

    private fun acquireNetworkLocks() {
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
        if (wifi != null) {
            multicastLock = wifi.createMulticastLock("CrossRelayRadar").apply {
                setReferenceCounted(false)
                acquire()
            }
            wifiLock = wifi.createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "CrossRelayWifi").apply {
                setReferenceCounted(false)
                acquire()
            }
        }
        val pm = getSystemService(Context.POWER_SERVICE) as? PowerManager
        wakeLock = pm?.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "CrossRelay::TransferLock")?.apply {
            setReferenceCounted(false)
            acquire(60 * 60 * 1000L)
        }
    }

    private fun releaseNetworkLocks() {
        try {
            if (multicastLock?.isHeld == true) multicastLock?.release()
            if (wifiLock?.isHeld == true) wifiLock?.release()
            if (wakeLock?.isHeld == true) wakeLock?.release()
        } catch (_: Throwable) {
        }
    }

    private fun ensureNotificationChannel() {
        val mgr = getSystemService(NotificationManager::class.java) ?: return
        val channel = NotificationChannel(
            CHANNEL_ID,
            "CrossRelay LAN Node",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "Mantiene activo el nodo TLS 1.3 y el radar UDP :4243"
        }
        mgr.createNotificationChannel(channel)
    }

    private fun buildNotification(content: String): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("CrossRelay")
            .setContentText(content)
            .setOngoing(true)
            .build()
    }

    private fun buildTransferNotification(content: String, progressPct: Int): Notification {
        val pauseIntent = PendingIntent.getService(
            this,
            1,
            Intent(this, CrossRelayService::class.java).setAction(ACTION_PAUSE_RESUME),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val cancelIntent = PendingIntent.getService(
            this,
            2,
            Intent(this, CrossRelayService::class.java).setAction(ACTION_CANCEL),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val pauseLabel = if (transferPaused) "Reanudar" else "Pausar"
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle("CrossRelay · Transferencia")
            .setContentText(content)
            .setProgress(100, progressPct, false)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .addAction(0, pauseLabel, pauseIntent)
            .addAction(0, "Cancelar", cancelIntent)
            .build()
    }
}
