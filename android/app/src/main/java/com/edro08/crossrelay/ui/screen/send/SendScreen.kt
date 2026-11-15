package com.edro08.crossrelay.ui.screen.send

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.CreateNewFolder
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.FilterList
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
import com.edro08.crossrelay.ui.component.ActionPickButton
import com.edro08.crossrelay.ui.component.DonutProgress
import com.edro08.crossrelay.ui.component.RelayCard
import com.edro08.crossrelay.ui.component.RelayIconTile
import com.edro08.crossrelay.ui.state.QueueEntry
import com.edro08.crossrelay.ui.theme.*

@Composable
fun SendScreen(
    selectedPeer: PeerItem?,
    queue: List<QueueEntry>,
    outgoingActive: Boolean,
    outgoingPaused: Boolean,
    outgoingProgress: Float,
    outgoingSpeed: String,
    onChangePeer: () -> Unit,
    onOpenClean: () -> Unit,
    onPickFiles: () -> Unit,
    onPickFolder: () -> Unit,
    onRemoveItem: (QueueEntry) -> Unit,
    onSendNow: () -> Unit,
    onTogglePause: () -> Unit,
    onCancelOutgoing: () -> Unit
) {
    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)) {
        RelayCard {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    modifier = Modifier.weight(1f),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    RelayIconTile(icon = Icons.Default.Computer, size = 42.dp)
                    Column(modifier = Modifier.weight(1f)) {
                        Text(selectedPeer?.name ?: stringResource(R.string.send_no_peer_selected), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(
                            text = if (selectedPeer != null) "${selectedPeer.os.uppercase()} · ${selectedPeer.ip}:${selectedPeer.port}" else stringResource(R.string.send_select_peer_hint),
                            color = if (selectedPeer != null) EmeraldOnline else TextSecondary,
                            fontSize = 11.sp,
                            fontFamily = FontFamily.Monospace,
                            maxLines = 1
                        )
                    }
                }
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(RelaySpacing.controlRadius))
                        .background(ObsidianInput)
                        .border(1.dp, ButtonSecondaryBorder, RoundedCornerShape(RelaySpacing.controlRadius))
                        .clickable(onClick = onChangePeer)
                        .padding(horizontal = 12.dp, vertical = 8.dp)
                ) {
                    Text(stringResource(R.string.action_change), color = TextPrimary, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(RelaySpacing.md)) {
            ActionPickButton(stringResource(R.string.send_pick_files), Icons.Default.Description, Modifier.weight(1f), onPickFiles)
            ActionPickButton(stringResource(R.string.send_pick_folder), Icons.Default.CreateNewFolder, Modifier.weight(1f), onPickFolder)
        }

        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .clip(RoundedCornerShape(RelaySpacing.cardRadius))
                .background(ObsidianSurface)
                .border(1.dp, BorderSubtle, RoundedCornerShape(RelaySpacing.cardRadius))
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 14.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text(stringResource(R.string.send_queue_title, queue.size), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                    Text(stringResource(R.string.send_queue_posix_subtitle), color = TextSecondary, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    val cleanText = stringResource(R.string.action_clean)
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(RelaySpacing.controlRadius))
                            .background(Color(0xFF18191E))
                            .border(1.dp, BorderInput, RoundedCornerShape(RelaySpacing.controlRadius))
                            .clickable(onClick = onOpenClean)
                            .padding(horizontal = 10.dp, vertical = 7.dp)
                    ) {
                        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Default.FilterList, contentDescription = cleanText, tint = TextSecondary, modifier = Modifier.size(14.dp))
                            Text(cleanText, color = TextPrimary, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                        }
                    }
                    Button(
                        onClick = onSendNow,
                        enabled = selectedPeer != null && queue.isNotEmpty(),
                        colors = ButtonDefaults.buttonColors(containerColor = RelayPurple),
                        shape = RoundedCornerShape(RelaySpacing.controlRadius),
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp)
                    ) {
                        Text(stringResource(R.string.send_now), color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
            HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
            if (queue.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(stringResource(R.string.send_queue_empty), color = TextMuted, fontSize = 12.sp)
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    items(queue, key = { it.path }) { item ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 14.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(item.name, color = Color.White, fontWeight = FontWeight.SemiBold, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text("${item.path} · ${item.sizeLabel}", color = TextMuted, fontSize = 10.sp, fontFamily = FontFamily.Monospace, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            }
                            Icon(
                                imageVector = Icons.Default.Close,
                                contentDescription = stringResource(R.string.action_remove),
                                tint = TextMuted,
                                modifier = Modifier
                                    .size(16.dp)
                                    .clickable { onRemoveItem(item) }
                            )
                        }
                        HorizontalDivider(color = Color(0xFF14161D), thickness = 1.dp)
                    }
                }
            }
        }

        RelayCard {
            val pctInt = (outgoingProgress * 100).toInt()
            Column(verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    DonutProgress(progress = outgoingProgress, label = "$pctInt%")
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = if (outgoingActive) stringResource(R.string.send_transfer_in_progress) else stringResource(R.string.send_transfer_idle),
                            color = Color.White,
                            fontWeight = FontWeight.Bold,
                            fontSize = 13.sp
                        )
                        Text(outgoingSpeed, color = RelayPurpleLight, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                    }
                }
                LinearProgressIndicator(
                    progress = { outgoingProgress },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(5.dp)
                        .clip(RoundedCornerShape(99.dp)),
                    color = RelayPurple,
                    trackColor = BorderSubtle
                )
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(RelaySpacing.sm)) {
                    OutlinedButton(onClick = onTogglePause, enabled = outgoingActive, modifier = Modifier.weight(1f), shape = RoundedCornerShape(RelaySpacing.controlRadius)) {
                        Text(if (outgoingPaused) stringResource(R.string.action_resume) else stringResource(R.string.action_pause), color = TextPrimary, fontSize = 11.sp)
                    }
                    Box(
                        modifier = Modifier
                            .weight(1f)
                            .clip(RoundedCornerShape(RelaySpacing.controlRadius))
                            .background(RoseDangerBg)
                            .border(1.dp, RoseDangerBorder, RoundedCornerShape(RelaySpacing.controlRadius))
                            .clickable(enabled = outgoingActive, onClick = onCancelOutgoing)
                            .padding(vertical = 9.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(stringResource(R.string.action_cancel), color = RoseDangerText, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                    }
                }
            }
        }
    }
}
