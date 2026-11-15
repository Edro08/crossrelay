package com.edro08.crossrelay.ui.component

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.edro08.crossrelay.ui.theme.*

@Composable
fun RelayCard(
    modifier: Modifier = Modifier,
    padding: Dp = RelaySpacing.lg,
    content: @Composable () -> Unit
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(RelaySpacing.cardRadius))
            .background(ObsidianSurface)
            .border(1.dp, BorderSubtle, RoundedCornerShape(RelaySpacing.cardRadius))
            .padding(padding)
    ) {
        content()
    }
}

@Composable
fun RelayIconTile(
    icon: ImageVector,
    contentDescription: String? = null,
    size: Dp = 44.dp,
    tint: Color = RelayPurpleLight,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .size(size)
            .clip(RoundedCornerShape(RelaySpacing.controlRadius))
            .background(IconTileBg)
            .border(1.dp, IconTileBorder, RoundedCornerShape(RelaySpacing.controlRadius)),
        contentAlignment = Alignment.Center
    ) {
        Icon(icon, contentDescription = contentDescription, tint = tint, modifier = Modifier.size(size * 0.45f))
    }
}

@Composable
fun RelayBadge(
    text: String,
    textColor: Color,
    bgColor: Color,
    borderColor: Color = Color.Transparent,
    modifier: Modifier = Modifier
) {
    val shape = RoundedCornerShape(99.dp)
    Box(
        modifier = modifier
            .clip(shape)
            .background(bgColor)
            .then(if (borderColor != Color.Transparent) Modifier.border(1.dp, borderColor, shape) else Modifier)
            .padding(horizontal = 8.dp, vertical = 3.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = text,
            color = textColor,
            fontSize = 10.sp,
            fontFamily = FontFamily.Monospace,
            fontWeight = FontWeight.SemiBold,
            maxLines = 1
        )
    }
}

@Composable
fun <T> RelayDropdown(
    selectedValue: T,
    options: List<Pair<T, String>>,
    onSelect: (T) -> Unit,
    modifier: Modifier = Modifier,
    compact: Boolean = false
) {
    var expanded by remember { mutableStateOf(false) }
    val selectedLabel = options.firstOrNull { it.first == selectedValue }?.second ?: ""
    val shape = RoundedCornerShape(if (compact) 12.dp else RelaySpacing.controlRadius)

    Box(modifier = modifier) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .clip(shape)
                .background(ObsidianInput)
                .border(1.dp, if (expanded) RelayPurple else BorderInput, shape)
                .clickable { expanded = true }
                .padding(horizontal = 12.dp, vertical = if (compact) 8.dp else 10.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = selectedLabel,
                color = TextPrimary,
                fontSize = if (compact) 11.sp else 12.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f)
            )
            Spacer(modifier = Modifier.width(6.dp))
            Icon(
                imageVector = Icons.Default.KeyboardArrowDown,
                contentDescription = null,
                tint = TextSecondary,
                modifier = Modifier
                    .size(16.dp)
                    .rotate(if (expanded) 180f else 0f)
            )
        }

        DropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false },
            modifier = Modifier
                .background(SurfaceContainer)
                .border(1.dp, BorderInput, RoundedCornerShape(14.dp))
        ) {
            options.forEach { (value, label) ->
                val isSelected = value == selectedValue
                DropdownMenuItem(
                    text = {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = label,
                                color = if (isSelected) TextPrimary else TextSecondary,
                                fontSize = 12.sp,
                                fontWeight = if (isSelected) FontWeight.SemiBold else FontWeight.Normal
                            )
                            if (isSelected) {
                                Spacer(modifier = Modifier.width(12.dp))
                                Icon(
                                    imageVector = Icons.Default.Check,
                                    contentDescription = null,
                                    tint = RelayPurpleLight,
                                    modifier = Modifier.size(15.dp)
                                )
                            }
                        }
                    },
                    onClick = {
                        expanded = false
                        onSelect(value)
                    },
                    modifier = Modifier
                        .padding(horizontal = 6.dp, vertical = 2.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(if (isSelected) RelayPurpleBg else Color.Transparent)
                )
            }
        }
    }
}

@Composable
fun ActionPickButton(
    label: String,
    icon: ImageVector,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    val shape = RoundedCornerShape(RelaySpacing.tileRadius)
    Row(
        modifier = modifier
            .height(48.dp)
            .clip(shape)
            .background(SurfaceContainer)
            .border(1.dp, BorderInput, shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(icon, contentDescription = label, tint = RelayPurpleLight, modifier = Modifier.size(17.dp))
        Spacer(modifier = Modifier.width(8.dp))
        Text(label, color = TextPrimary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
fun DonutProgress(progress: Float, label: String) {
    Box(modifier = Modifier.size(52.dp), contentAlignment = Alignment.Center) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            drawArc(BorderSubtle, 0f, 360f, false, style = Stroke(width = 6.dp.toPx(), cap = StrokeCap.Round))
            drawArc(RelayPurple, -90f, 360f * progress, false, style = Stroke(width = 6.dp.toPx(), cap = StrokeCap.Round))
        }
        Text(label, color = TextPrimary, fontSize = 12.sp, fontWeight = FontWeight.ExtraBold, fontFamily = FontFamily.Monospace)
    }
}

@Composable
fun PeerOptionCard(name: String, sub: String, selected: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(RelaySpacing.tileRadius)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(if (selected) Color(0xFF171329) else SurfaceContainer)
            .border(1.dp, if (selected) RelayPurple else BorderInput, shape)
            .clickable(onClick = onClick)
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(2.dp)
    ) {
        Text(name, color = TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.sp)
        Text(sub, color = EmeraldOnline, fontSize = 11.sp, fontFamily = FontFamily.Monospace)
    }
}

@Composable
fun CleanOptionButton(label: String, badgeColor: Color, onClick: () -> Unit) {
    val shape = RoundedCornerShape(RelaySpacing.tileRadius)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(SurfaceContainer)
            .border(1.dp, BorderInput, shape)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, color = badgeColor, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
    }
}

@Composable
fun PortDiagChip(port: String, state: String, color: Color, modifier: Modifier) {
    Column(
        modifier = modifier
            .clip(RoundedCornerShape(RelaySpacing.controlRadius))
            .background(SurfaceContainer)
            .border(1.dp, BorderInput, RoundedCornerShape(RelaySpacing.controlRadius))
            .padding(vertical = 10.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(port, color = TextPrimary, fontSize = 11.sp, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold)
        Text(state, color = color, fontSize = 9.sp, fontFamily = FontFamily.Monospace)
    }
}
