package com.printr.android.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.width
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Outline
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class PrintRNavigationTest {
    @get:Rule val compose = createComposeRule()

    @Test fun labelsStayOnOneLineAndEveryTabRemainsReachable() {
        val width = mutableStateOf(360)
        val fontScale = mutableStateOf(1f)
        val selected = mutableStateOf("Print")
        compose.setContent {
            CompositionLocalProvider(LocalDensity provides Density(1f, fontScale.value)) {
                MaterialTheme(shapes = PrintRShapes) {
                    Box(Modifier.width((width.value - 32).dp)) {
                        PrintRNavigation(selected.value, onSelect = { selected.value = it })
                    }
                }
            }
        }
        for (screenWidth in listOf(320, 360, 411)) {
            for (scale in listOf(1f, 1.3f, 2f)) {
                compose.runOnIdle { width.value = screenWidth; fontScale.value = scale }
                for (tab in listOf("Print", "Computers", "Settings")) {
                    val layouts = mutableListOf<TextLayoutResult>()
                    compose.onNodeWithText(tab, useUnmergedTree = true)
                        .performScrollTo().assertIsDisplayed()
                        .performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(layouts) }
                    assertTrue("Missing text layout: $screenWidth/$scale/$tab", layouts.isNotEmpty())
                    layouts.forEach {
                        assertEquals("Wrapped label: $screenWidth/$scale/$tab", 1, it.lineCount)
                        // A cached paragraph can retain the previous constraint width after
                        // resize. Check actual glyph bounds, not that unused paragraph space.
                        assertFalse("Truncated label: $screenWidth/$scale/$tab", it.multiParagraph.didExceedMaxLines)
                        assertFalse("Clipped label height: $screenWidth/$scale/$tab", it.didOverflowHeight)
                        assertTrue("Clipped label start: $screenWidth/$scale/$tab", it.getLineLeft(0) >= -1f)
                        assertTrue("Clipped label end: $screenWidth/$scale/$tab right=${it.getLineRight(0)} width=${it.size.width}", it.getLineRight(0) <= it.size.width + 1f)
                    }
                    compose.onNodeWithText(tab).performClick().assertIsSelected()
                    if (screenWidth >= 360 && scale <= 1.3f) {
                        val bounds = compose.onNodeWithText(tab).fetchSemanticsNode().boundsInRoot
                        assertTrue("Tab outside regular viewport: $screenWidth/$scale/$tab", bounds.left >= -1f && bounds.right <= screenWidth - 31f)
                    }
                }
            }
        }
    }

    @Test fun cornersRemainSubtleAtDifferentControlSizes() {
        for (height in listOf(48f, 72f)) {
            val outline = PrintRShapes.small.createOutline(Size(200f, height), LayoutDirection.Ltr, Density(1f)) as Outline.Rounded
            assertEquals(6f, outline.roundRect.topLeftCornerRadius.x, 0f)
        }
    }
}
