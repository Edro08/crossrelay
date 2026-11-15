package com.edro08.crossrelay.ui.screen.receive

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.edro08.crossrelay.R
import com.edro08.crossrelay.ui.component.RelayBadge
import com.edro08.crossrelay.ui.component.RelayCard
import com.edro08.crossrelay.ui.state.IncomingFileEntry
import com.edro08.crossrelay.ui.theme.*

@Composable
fun ReceiveScreen(
    downloadPath: String,
    activePort: Int,
    incomingActive: Boolean,
    incomingProgress: Float,
    incomingSpeed: String,
    incomingFiles: List<IncomingFileEntry>,
    onCancelIncoming: () -> Unit
) {
    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)) {
        RelayCard(padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(stringResource(R.string.receive_station_title), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp)
                    Text(stringResource(R.string.receive_station_active, activePort), color = EmeraldOnline, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                }
                Text(downloadPath, color = TextSecondary, fontSize = 11.sp, fontFamily = FontFamily.Monospace, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(stringResource(R.string.receive_zero_trust), color = RelayPurpleLight, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                    Text(stringResource(R.string.receive_mediastore_sync), color = EmeraldOnline, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                }
            }
        }
        RelayCard(modifier = Modifier.weight(1f), padding = RelaySpacing.lg) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(stringResource(R.string.receive_live_title), color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                    Text(stringResource(R.string.receive_sha256), color = EmeraldOnline, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                }
                if (incomingActive) {
                    Text(
                        text = stringResource(R.string.receive_progress_summary, (incomingProgress * 100).toInt(), incomingSpeed),
                        color = RelayPurpleLight,
                        fontSize = 12.sp,
                        fontFamily = FontFamily.Monospace
                    )
                    LinearProgressIndicator(
                        progress = { incomingProgress },
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(5.dp)
                            .clip(RoundedCornerShape(99.dp)),
                        color = RelayPurple,
                        trackColor = BorderSubtle
                    )
                    Button(onClick = onCancelIncoming, colors = ButtonDefaults.buttonColors(containerColor = RoseDangerBg)) {
                        Text(stringResource(R.string.receive_cancel), color = RoseDangerText, fontSize = 11.sp)
                    }
                } else if (incomingFiles.isEmpty()) {
                    Text(stringResource(R.string.receive_waiting), color = TextMuted, fontSize = 12.sp)
                }

                if (incomingFiles.isNotEmpty()) {
                    HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
                    LazyColumn(modifier = Modifier.weight(1f, fill = false)) {
                        items(incomingFiles, key = { it.name }) { file ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 8.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = file.name,
                                    color = TextPrimary,
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                    modifier = Modifier.weight(1f)
                                )
                                Spacer(modifier = Modifier.width(8.dp))
                                RelayBadge(
                                    text = if (file.done) "Verificado ✓" else "Recibiendo",
                                    textColor = if (file.done) EmeraldOnline else RelayPurpleLight,
                                    bgColor = if (file.done) EmeraldOnline.copy(alpha = 0.12f) else RelayPurpleBg,
                                    borderColor = if (file.done) EmeraldOnline.copy(alpha = 0.25f) else IconTileBorder
                                )
                            }
                            HorizontalDivider(color = BorderSubtle, thickness = 1.dp)
                        }
                    }
                }
            }
        }
    }
}
