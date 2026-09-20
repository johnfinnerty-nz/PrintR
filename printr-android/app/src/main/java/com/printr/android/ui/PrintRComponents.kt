package com.printr.android.ui

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Shapes
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

val PrintRShapes = Shapes(
    extraSmall = RoundedCornerShape(4.dp),
    small = RoundedCornerShape(6.dp),
    medium = RoundedCornerShape(8.dp),
    large = RoundedCornerShape(10.dp),
    extraLarge = RoundedCornerShape(12.dp)
)

// Material buttons use a full pill by default, independently of theme shapes.
@Composable
fun PrintRButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    contentPadding: PaddingValues = ButtonDefaults.ContentPadding,
    content: @Composable RowScope.() -> Unit
) = Button(
    onClick = onClick, modifier = modifier, enabled = enabled,
    shape = MaterialTheme.shapes.small, contentPadding = contentPadding, content = content
)

@Composable
fun PrintROutlinedButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    contentPadding: PaddingValues = ButtonDefaults.ContentPadding,
    content: @Composable RowScope.() -> Unit
) = OutlinedButton(
    onClick = onClick, modifier = modifier, enabled = enabled,
    shape = MaterialTheme.shapes.small, contentPadding = contentPadding, content = content
)

@Composable
fun PrintRTextButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    content: @Composable RowScope.() -> Unit
) = TextButton(
    onClick = onClick, modifier = modifier, enabled = enabled,
    shape = MaterialTheme.shapes.small, content = content
)
