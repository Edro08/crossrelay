package com.edro08.crossrelay

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.mutableStateListOf
import androidx.core.content.ContextCompat
import com.edro08.crossrelay.service.CrossRelayService
import com.edro08.crossrelay.storage.StorageAccessHelper
import com.edro08.crossrelay.ui.navigation.AppNavigation
import com.edro08.crossrelay.ui.theme.CrossRelayTheme

class MainActivity : ComponentActivity() {
    private val sharedPaths = mutableStateListOf<String>()

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) {
        CrossRelayService.start(this)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        handleShareIntent(intent)

        if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
        ) {
            CrossRelayService.start(this)
        } else {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }

        setContent {
            CrossRelayTheme {
                AppNavigation(
                    sharedPaths = sharedPaths.toList(),
                    onManageAllFilesAccess = {
                        startActivity(StorageAccessHelper.buildAllFilesAccessIntent(this))
                    },
                    onManageNotifications = {
                        val intent = Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
                            putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
                        }
                        startActivity(intent)
                    }
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleShareIntent(intent)
    }

    private fun handleShareIntent(intent: Intent?) {
        if (intent == null) return
        when (intent.action) {
            Intent.ACTION_SEND -> {
                val uri = intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
                uri?.path?.let { sharedPaths.add(it) }
            }
            Intent.ACTION_SEND_MULTIPLE -> {
                val uris = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java)
                uris?.mapNotNull { it.path }?.let { sharedPaths.addAll(it) }
            }
        }
    }
}
