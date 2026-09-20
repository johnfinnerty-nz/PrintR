package com.printr.android.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp

private val destinations = listOf("Print", "Computers", "Settings")

@Composable
fun PrintRNavigation(selectedTab: String, onSelect: (String) -> Unit, modifier: Modifier = Modifier) {
    val textMeasurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val labelStyle = MaterialTheme.typography.labelLarge
    // Measure at the actual font scale. Never shrink, ellipsize or split a tab label.
    val minimumWidths = destinations.map {
        val textWidth = textMeasurer.measure(it, labelStyle, softWrap = false).size.width
        maxOf(64.dp, with(density) { textWidth.toDp() } + 24.dp)
    }
    BoxWithConstraints(modifier.fillMaxWidth()) {
        val equalWidth = (maxWidth - 12.dp) / destinations.size
        val extraWidth = maxOf(0.dp, (maxWidth - 12.dp - minimumWidths.reduce { a, b -> a + b }) / destinations.size)
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).selectableGroup(),
            horizontalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            destinations.forEachIndexed { index, tab ->
                val tabWidth = if (equalWidth >= minimumWidths.max()) equalWidth else minimumWidths[index] + extraWidth
                val tabModifier = Modifier.width(tabWidth).heightIn(min = 48.dp).semantics {
                    selected = selectedTab == tab
                    role = Role.Tab
                }
                val padding = PaddingValues(horizontal = 10.dp, vertical = 10.dp)
                if (selectedTab == tab) {
                    PrintRButton(onClick = { onSelect(tab) }, modifier = tabModifier, contentPadding = padding) {
                        Text(tab, style = labelStyle, maxLines = 1, softWrap = false)
                    }
                } else {
                    PrintROutlinedButton(onClick = { onSelect(tab) }, modifier = tabModifier, contentPadding = padding) {
                        Text(tab, style = labelStyle, maxLines = 1, softWrap = false)
                    }
                }
            }
        }
    }
}
