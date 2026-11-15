package com.edro08.crossrelay.ui.screen.devices

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
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
import com.edro08.crossrelay.ui.component.RelayBadge
import com.edro08.crossrelay.ui.component.RelayCard
import com.edro08.crossrelay.ui.component.RelayDropdown
import com.edro08.crossrelay.ui.component.RelayIconTile
import com.edro08.crossrelay.ui.theme.*

@Composable
fun DevicesScreen(
    trustedPeers: List<PeerItem>,
    discoveredPeers: List<PeerItem>,
    onOpenConnect: () -> Unit,
    onPairPeer: (PeerItem) -> Unit,
    onEditPeer: (PeerItem) -> Unit,
    onBlockPeer: (PeerItem) -> Unit,
    onSetPolicy: (PeerItem, String) -> Unit
) {
    val unpairedPeers = discoveredPeers.filter { dp -> trustedPeers.none { it.id == dp.id } }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(RelaySpacing.md)
    ) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(RelaySpacing.md)) {
            Button(
                onClick = {
                    val first = unpairedPeers.firstOrNull()
                    if (first != null) onPairPeer(first) else onOpenConnect()
                },
                modifier = Modifier
                    .weight(1f)
                    .height(44.dp),
                colors = ButtonDefaults.buttonColors(containerColor = RelayPurple),
                shape = RoundedCornerShape(16.dp),
                contentPadding = PaddingValues(horizontal = 10.dp)
            ) {
                Text(stringResource(R.string.devices_pair_pin), color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold, maxLines = 1)
            }

            Box(
                modifier = Modifier
                    .weight(1f)
                    .height(44.dp)
                    .clip(RoundedCornerShape(16.dp))
                    .background(SurfaceContainer)
                    .border(1.dp, BorderInput, RoundedCornerShape(16.dp))
                    .clickable(onClick = onOpenConnect),
                contentAlignment = Alignment.Center
            ) {
                Text(stringResource(R.string.devices_connect_manual), color = TextPrimary, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
            }
        }

        if (unpairedPeers.isNotEmpty()) {
            Text(
                text = stringResource(R.string.devices_lan_section),
                color = TextMuted,
                fontSize = 10.sp,
                fontFamily = FontFamily.Monospace,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(horizontal = 4.dp)
            )
            unpairedPeers.forEach { peer ->
                RelayCard(padding = RelaySpacing.lg) {
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
                            RelayIconTile(icon = Icons.Default.Computer, size = 40.dp, tint = TextSecondary)
                            Column(modifier = Modifier.weight(1f)) {
                                Text(peer.name, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text("${peer.os.uppercase()} · ${peer.ip}:${peer.port}", color = TextSecondary, fontSize = 10.sp, fontFamily = FontFamily.Monospace)
                            }
                        }
                        Spacer(modifier = Modifier.width(8.dp))
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(12.dp))
                                .background(RelayPurpleBg)
                                .border(1.dp, IconTileBorder, RoundedCornerShape(12.dp))
                                .clickable { onPairPeer(peer) }
                                .padding(horizontal = 10.dp, vertical = 7.dp)
                        ) {
                            Text(stringResource(R.string.devices_request_connection), color = RelayPurpleLight, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                        }
                    }
                }
            }
        }

        Text(
            text = stringResource(R.string.devices_trusted_section, trustedPeers.size),
            color = TextMuted,
            fontSize = 10.sp,
            fontFamily = FontFamily.Monospace,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.padding(horizontal = 4.dp)
        )

        if (trustedPeers.isEmpty()) {
            RelayCard { Text(stringResource(R.string.devices_trusted_empty), color = TextMuted, fontSize = 12.sp) }
        } else {
            trustedPeers.forEach { peer ->
                RelayCard(padding = RelaySpacing.lg) {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
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
                                    Text(peer.name, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                    Text("● En línea · ${peer.ip}:${peer.port}", color = EmeraldOnline, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
                                }
                            }
                            RelayBadge(
                                text = peer.os.uppercase(),
                                textColor = RelayPurpleLight,
                                bgColor = IconTileBg,
                                borderColor = IconTileBorder
                            )
                        }

                        HorizontalDivider(color = BorderSubtle, thickness = 1.dp)

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            RelayDropdown(
                                selectedValue = peer.policy,
                                options = listOf(
                                    "AutoAccept" to stringResource(R.string.devices_policy_auto),
                                    "AskConfirmation" to stringResource(R.string.devices_policy_ask)
                                ),
                                onSelect = { nextPolicy -> onSetPolicy(peer, nextPolicy) },
                                compact = true,
                                modifier = Modifier.width(140.dp)
                            )
                            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                Box(
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(12.dp))
                                        .background(ObsidianInput)
                                        .border(1.dp, ButtonSecondaryBorder, RoundedCornerShape(12.dp))
                                        .clickable { onEditPeer(peer) }
                                        .padding(horizontal = 10.dp, vertical = 7.dp)
                                ) {
                                    Text("Editar", color = TextPrimary, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                                }
                                Box(
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(12.dp))
                                        .background(RoseDangerBg)
                                        .border(1.dp, RoseDangerBorder, RoundedCornerShape(12.dp))
                                        .clickable { onBlockPeer(peer) }
                                        .padding(horizontal = 10.dp, vertical = 7.dp)
                                ) {
                                    Text(stringResource(R.string.action_block), color = RoseDangerText, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
