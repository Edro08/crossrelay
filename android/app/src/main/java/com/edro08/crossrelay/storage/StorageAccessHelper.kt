package com.edro08.crossrelay.storage

import android.content.Context
import android.content.Intent
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Environment
import android.provider.Settings
import java.io.File

object StorageAccessHelper {
    fun hasAllFilesAccess(): Boolean = Environment.isExternalStorageManager()

    fun buildAllFilesAccessIntent(context: Context): Intent {
        return Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
            data = Uri.parse("package:${context.packageName}")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
    }

    fun defaultDownloadDirectory(): String {
        val downloads = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val target = File(downloads, "CrossRelay")
        if (hasAllFilesAccess() && !target.exists()) {
            target.mkdirs()
        }
        return target.absolutePath
    }

    fun scanReceivedFiles(context: Context, paths: List<String>) {
        if (paths.isEmpty()) return
        MediaScannerConnection.scanFile(context, paths.toTypedArray(), null, null)
    }

    fun resolveRealPath(context: Context, uri: Uri): String? {
        if ("file".equals(uri.scheme, ignoreCase = true)) {
            return uri.path
        }
        val authority = uri.authority ?: ""
        if (authority == "com.android.externalstorage.documents") {
            val docId = android.provider.DocumentsContract.getDocumentId(uri)
            val parts = docId.split(":", limit = 2)
            if (parts.size == 2) {
                val type = parts[0]
                val rel = parts[1]
                if ("primary".equals(type, ignoreCase = true)) {
                    return "${Environment.getExternalStorageDirectory().absolutePath}/$rel"
                }
            }
        }
        // Fallback: query MediaStore _data column when available
        runCatching {
            context.contentResolver.query(uri, arrayOf("_data"), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val idx = cursor.getColumnIndex("_data")
                    if (idx >= 0) {
                        val p = cursor.getString(idx)
                        if (!p.isNullOrBlank() && File(p).exists()) return p
                    }
                }
            }
        }
        return uri.path?.takeIf { File(it).exists() }
    }

    fun resolveTreePath(uri: Uri): String? {
        if (uri.authority == "com.android.externalstorage.documents") {
            val docId = android.provider.DocumentsContract.getTreeDocumentId(uri)
            val parts = docId.split(":", limit = 2)
            if (parts.size == 2 && "primary".equals(parts[0], ignoreCase = true)) {
                return "${Environment.getExternalStorageDirectory().absolutePath}/${parts[1]}".trimEnd('/')
            }
        }
        return uri.path
    }
}
