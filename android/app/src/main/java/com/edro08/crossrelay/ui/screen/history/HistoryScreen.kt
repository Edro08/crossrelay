package com.edro08.crossrelay.ui.screen.history

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
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
import com.edro08.crossrelay.ui.component.RelayBadge
import com.edro08.crossrelay.ui.component.RelayCard
import com.edro08.crossrelay.ui.state.HistoryEntry
import com.edro08.crossrelay.ui.theme.*

private enum class HistoryFilter { All, Sent, Received }

@Composable
fun HistoryScreen(history: List<HistoryEntry>) {
    var filter by remember { mutableStateOf(HistoryFilter.All) }
    val filtered = remember(history.toList(), filter) {
        when (filter) {
            HistoryFilter.All -> history
            HistoryFilter.Sent -> history.filter { it.direction.contains("Enviado", ignoreCase = true) || it.direction.contains("↑") }
            HistoryFilter.Received -> history.filter { it.direction.contains("Recibido", ignoreCase = true) || it.direction.contains("↓") }
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)
    ) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(RelaySpacing.sm)) {
            FilterChipButton(stringResource(R.string.history_filter_all), filter == HistoryFilter.All, Modifier.weight(1f)) {
                filter = HistoryFilter.All
            }
            FilterChipButton(stringResource(R.string.history_filter_sent), filter == HistoryFilter.Sent, Modifier.weight(1f)) {
                filter = HistoryFilter.Sent
            }
            FilterChipButton(stringResource(R.string.history_filter_received), filter == HistoryFilter.Received, Modifier.weight(1f)) {
                filter = HistoryFilter.Received
            }
        }

        if (filtered.isEmpty()) {
            RelayCard { Text(stringResource(R.string.history_empty), color = TextMuted, fontSize = 12.sp) }
        } else {
            filtered.forEach { item ->
                val isIncoming = item.direction.contains("Recibido", ignoreCase = true) || item.direction.contains("↓")
                RelayCard(padding = RelaySpacing.lg) {
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                            Text(item.title, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.sp, modifier = Modifier.weight(1f))
                            RelayBadge(
                                text = item.direction,
                                textColor = if (isIncoming) RelayPurpleLight else EmeraldOnline,
                                bgColor = if (isIncoming) RelayPurpleBg else EmeraldOnline.copy(alpha = 0.12f),
                                borderColor = if (isIncoming) IconTileBorder else EmeraldOnline.copy(alpha = 0.25f)
                            )
                        }
                        Text(item.subtitle, color = TextSecondary, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                    }
                }
            }
        }
    }
}

@Composable
private fun FilterChipButton(
    label: String,
    selected: Boolean,
    modifier: Modifier,
    onClick: () -> Unit
) {
    val shape = RoundedCornerShape(12.dp)
    Box(
        modifier = modifier
            .height(36.dp)
            .clip(shape)
            .background(if (selected) RelayPurple else SurfaceContainer)
            .border(1.dp, if (selected) RelayPurple else BorderInput, shape)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = label,
            color = if (selected) Color.White else TextSecondary,
            fontSize = 12.sp,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold
        )
    }
}
