package com.edro08.crossrelay.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable

private val CrossRelayDarkScheme = darkColorScheme(
    primary = RelayPurple,
    onPrimary = TextPrimary,
    secondary = RelayPurpleLight,
    background = PitchBlack,
    onBackground = TextPrimary,
    surface = ObsidianSurface,
    onSurface = TextPrimary,
    surfaceVariant = ObsidianInput,
    onSurfaceVariant = TextSecondary,
    outline = BorderSubtle,
    error = RoseDanger
)

@Composable
fun CrossRelayTheme(
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = CrossRelayDarkScheme,
        typography = Typography,
        content = content
    )
}
