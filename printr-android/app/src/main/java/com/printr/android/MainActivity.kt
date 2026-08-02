package com.printr.android

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.print.PrintAttributes
import android.print.PrintManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.lightColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.core.content.IntentCompat
import com.printr.android.data.DuplexMode
import com.printr.android.data.PairingDetails
import com.printr.android.data.PageOrientation
import com.printr.android.data.PaperSize
import com.printr.android.data.PrintOptions
import com.printr.android.data.PrintStatus
import com.printr.android.data.PrinterInfo
import com.printr.android.data.SelectedFile
import com.printr.android.data.SupportedDocumentTypes
import com.printr.android.data.selectedFileFromUri
import com.printr.android.print.PdfPrintAdapter
import com.printr.android.ui.PrintRViewModel
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions

private enum class ThemeMode(val storageValue: String, val label: String, val shortLabel: String) {
    System("system", "System default", "System"),
    Light("light", "Light", "Light"),
    Dark("dark", "Dark", "Dark");

    companion object {
        fun fromStorage(value: String?): ThemeMode = entries.firstOrNull { it.storageValue == value } ?: System
    }
}

class MainActivity : ComponentActivity() {
    private val viewModel: PrintRViewModel by viewModels()
    private val uiPreferences by lazy { getSharedPreferences("printr_ui", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handleShareIntent(intent)
        setContent {
            var themeMode by remember { mutableStateOf(ThemeMode.fromStorage(uiPreferences.getString("theme_mode", null))) }
            PrintRTheme(themeMode) {
                Surface(modifier = Modifier.fillMaxSize()) {
                    PrintRApp(
                        viewModel = viewModel,
                        onDirectPrint = { file -> directPrint(file) },
                        themeMode = themeMode,
                        onThemeChange = {
                            themeMode = it
                            uiPreferences.edit().putString("theme_mode", it.storageValue).apply()
                        }
                    )
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleShareIntent(intent)
    }

    private fun handleShareIntent(intent: Intent?) {
        if (intent == null) return
        val uris = when (intent.action) {
            Intent.ACTION_SEND -> listOfNotNull(IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java))
            Intent.ACTION_SEND_MULTIPLE -> IntentCompat.getParcelableArrayListExtra(intent, Intent.EXTRA_STREAM, Uri::class.java).orEmpty()
            else -> emptyList()
        }
        if (uris.isNotEmpty()) {
            viewModel.setFiles(uris.map { selectedFileFromUri(it) })
        }
    }

    private fun directPrint(file: SelectedFile) {
        val type = SupportedDocumentTypes.displayType(file)
        if (type != "PDF") {
            if (type == "DOCX document") viewModel.showDirectDocxUnsupported()
            return
        }
        val options = viewModel.state.value.options
        val manager = getSystemService(PrintManager::class.java)
        val adapter = PdfPrintAdapter(contentResolver, file.uri, file.displayName)
        val baseMediaSize = if (options.paperSize == PaperSize.Letter.wireValue) {
            PrintAttributes.MediaSize.NA_LETTER
        } else {
            PrintAttributes.MediaSize.ISO_A4
        }
        val mediaSize = if (options.orientation == PageOrientation.Landscape.wireValue) {
            baseMediaSize.asLandscape()
        } else {
            baseMediaSize.asPortrait()
        }
        val duplexMode = when (options.duplexMode) {
            DuplexMode.ShortEdge.wireValue -> PrintAttributes.DUPLEX_MODE_SHORT_EDGE
            DuplexMode.LongEdge.wireValue -> PrintAttributes.DUPLEX_MODE_LONG_EDGE
            else -> if (options.duplex) PrintAttributes.DUPLEX_MODE_LONG_EDGE else PrintAttributes.DUPLEX_MODE_NONE
        }
        manager.print(
            "PrintR ${file.displayName}",
            adapter,
            PrintAttributes.Builder()
                .setMediaSize(mediaSize)
                .setColorMode(if (options.colorMode == "bw") PrintAttributes.COLOR_MODE_MONOCHROME else PrintAttributes.COLOR_MODE_COLOR)
                .setDuplexMode(duplexMode)
                .build()
        )
    }
}

@Composable
private fun PrintRTheme(themeMode: ThemeMode, content: @Composable () -> Unit) {
    val systemDark = isSystemInDarkTheme()
    val useDark = when (themeMode) {
        ThemeMode.System -> systemDark
        ThemeMode.Light -> false
        ThemeMode.Dark -> true
    }
    MaterialTheme(colorScheme = if (useDark) darkColorScheme() else lightColorScheme(), content = content)
}

@Composable
private fun PrintRApp(
    viewModel: PrintRViewModel,
    onDirectPrint: (SelectedFile) -> Unit,
    themeMode: ThemeMode,
    onThemeChange: (ThemeMode) -> Unit
) {
    val state by viewModel.state.collectAsState()
    val context = LocalContext.current
    var renameTarget by remember { mutableStateOf<PairingDetails?>(null) }
    var renameText by remember { mutableStateOf("") }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNotEmpty()) {
            viewModel.setFiles(uris.map { uri -> context.selectedFileFromUri(uri) })
        }
    }
    val qrScanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        result.contents?.let(viewModel::pairFromQr)
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .navigationBarsPadding()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(start = 16.dp, top = 35.dp, end = 16.dp, bottom = 20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            androidx.compose.foundation.Image(
                painter = painterResource(id = com.printr.android.R.drawable.ic_printr),
                contentDescription = "PrintR logo",
                modifier = Modifier.size(48.dp)
            )
            androidx.compose.foundation.layout.Spacer(Modifier.width(10.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text("PrintR", style = MaterialTheme.typography.headlineLarge)
                Text("Send to a Windows printer", style = MaterialTheme.typography.bodySmall)
            }
            ThemeSelector(themeMode, onThemeChange)
        }
        Text("Send a document to a printer on your Windows computer.", style = MaterialTheme.typography.bodyLarge)
        ConnectionBanner(state.connectionState, state.statusMessage)

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                PairingSection(state.pairing, viewModel::editPairing, viewModel::testConnection)
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(modifier = Modifier.weight(1f), onClick = {
                qrScanner.launch(ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setPrompt("Scan PrintR Agent QR code"))
                    }) { Text("Scan QR") }
                    OutlinedButton(modifier = Modifier.weight(1f), onClick = viewModel::discoverComputers) {
                        Text("Discover")
                    }
                }
            }
        }

        ComputerLists(
            paired = state.pairedComputers,
            discovered = state.discoveredComputers,
            onChoose = viewModel::chooseDefault,
            onRemove = { viewModel.removePairing(it.id) },
            onRename = {
                renameTarget = it
                renameText = it.displayName
            },
            onUseDiscovered = viewModel::useDiscoveredComputer
        )

        OutlinedButton(
            modifier = Modifier.fillMaxWidth(),
            onClick = { picker.launch(SupportedDocumentTypes.PickerMimeTypes) }
        ) { Text("Choose files") }
        FileSection(state.selectedFiles)
        PrinterSection(
            printers = state.printers,
            selectedPrinter = state.options.printerName,
            onSelect = { viewModel.updateOptions(state.options.copy(printerName = it)) },
            onRefresh = viewModel::refreshPrinters,
            canRefresh = state.pairing.isComplete
        )
        OptionsSection(state.options, viewModel::updateOptions)

        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                modifier = Modifier.fillMaxWidth(),
                enabled = state.pairing.isComplete && state.selectedFiles.isNotEmpty(),
                onClick = viewModel::uploadSelectedFiles
            ) { Text("Send to Windows computer") }
            Button(
                modifier = Modifier.fillMaxWidth(),
                enabled = state.selectedFiles.firstOrNull() != null,
                onClick = { state.selectedFiles.firstOrNull()?.let(onDirectPrint) }
            ) { Text("Print directly from Android") }
        }
        StatusSection(state.status, state.statusMessage, state.warnings)
        if (state.status == PrintStatus.Failed) {
            Button(modifier = Modifier.fillMaxWidth(), onClick = viewModel::retryUpload, enabled = state.selectedFiles.isNotEmpty() && state.pairing.isComplete) { Text("Retry") }
        }
        if (state.recentJobs.isNotEmpty()) {
            Text("Recent jobs", style = MaterialTheme.typography.titleMedium)
            state.recentJobs.forEach { Text(it, style = MaterialTheme.typography.bodySmall) }
        }
        if (state.pairedComputers.isNotEmpty()) {
            TextButton(modifier = Modifier.fillMaxWidth(), onClick = viewModel::clearPairings) { Text("Clear saved pairings") }
        }
    }

    renameTarget?.let { target ->
        AlertDialog(
            onDismissRequest = { renameTarget = null },
            title = { Text("Rename computer") },
            text = {
                OutlinedTextField(
                    value = renameText,
                    onValueChange = { renameText = it },
                    label = { Text("Computer name") },
                    singleLine = true
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    viewModel.renamePairing(target.id, renameText.trim())
                    renameTarget = null
                }) { Text("Save") }
            },
            dismissButton = { TextButton(onClick = { renameTarget = null }) { Text("Cancel") } }
        )
    }
}

@Composable
private fun ThemeSelector(themeMode: ThemeMode, onThemeChange: (ThemeMode) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { expanded = true }) {
            Text("Theme: ${themeMode.shortLabel}")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            ThemeMode.entries.forEach { mode ->
                DropdownMenuItem(
                    text = { Text(mode.label) },
                    onClick = {
                        onThemeChange(mode)
                        expanded = false
                    }
                )
            }
        }
    }
}

@Composable
private fun ConnectionBanner(connection: com.printr.android.data.ConnectionState, message: String) {
    val color = when (connection) {
        com.printr.android.data.ConnectionState.PairedReachable -> MaterialTheme.colorScheme.primaryContainer
        com.printr.android.data.ConnectionState.Printing -> MaterialTheme.colorScheme.tertiaryContainer
        com.printr.android.data.ConnectionState.PairedOffline -> MaterialTheme.colorScheme.errorContainer
        com.printr.android.data.ConnectionState.NotPaired -> MaterialTheme.colorScheme.surfaceVariant
    }
    Surface(modifier = Modifier.fillMaxWidth(), color = color, shape = MaterialTheme.shapes.medium) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text("${connectionLabel(connection)}", style = MaterialTheme.typography.titleSmall)
            if (message.isNotBlank()) Text(message, style = MaterialTheme.typography.bodySmall)
        }
    }
}

private fun connectionLabel(connection: com.printr.android.data.ConnectionState): String = when (connection) {
    com.printr.android.data.ConnectionState.NotPaired -> "Not paired"
    com.printr.android.data.ConnectionState.PairedOffline -> "Paired, computer offline"
    com.printr.android.data.ConnectionState.PairedReachable -> "Paired and reachable"
    com.printr.android.data.ConnectionState.Printing -> "Printing"
}

@Composable
private fun PairingSection(pairing: PairingDetails, onChange: (PairingDetails) -> Unit, onTest: () -> Unit) {
    val validPort = pairing.port.toIntOrNull()?.let { it in 1..65535 } == true
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Windows computer", style = MaterialTheme.typography.titleMedium)
        Text("Scan the agent QR code, or enter the connection details manually.", style = MaterialTheme.typography.bodySmall)
        OutlinedTextField(
            value = pairing.host,
            onValueChange = { onChange(pairing.copy(host = it.trim())) },
            label = { Text("IP address or hostname") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        OutlinedTextField(
            value = pairing.port,
            onValueChange = { onChange(pairing.copy(port = it.filter(Char::isDigit))) },
            label = { Text("Port") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        OutlinedTextField(
            value = pairing.token,
            onValueChange = { onChange(pairing.copy(token = it.trim())) },
            label = { Text("Pairing token") },
            visualTransformation = PasswordVisualTransformation(),
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        Button(
            modifier = Modifier.fillMaxWidth(),
            enabled = pairing.host.isNotBlank() && validPort && pairing.token.isNotBlank(),
            onClick = onTest
        ) { Text("Test connection") }
    }
}

@Composable
private fun ComputerLists(
    paired: List<PairingDetails>,
    discovered: List<com.printr.android.data.DiscoveryComputer>,
    onChoose: (PairingDetails) -> Unit,
    onRemove: (PairingDetails) -> Unit,
    onRename: (PairingDetails) -> Unit,
    onUseDiscovered: (com.printr.android.data.DiscoveryComputer) -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Paired computers", style = MaterialTheme.typography.titleMedium)
        if (paired.isEmpty()) {
            Text("No saved computers yet.", style = MaterialTheme.typography.bodySmall)
        } else {
            paired.forEach { computer ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Row(modifier = Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(computer.displayName, style = MaterialTheme.typography.titleSmall)
                            Text("${computer.host}:${computer.port}", style = MaterialTheme.typography.bodySmall)
                        }
                        TextButton(onClick = { onChoose(computer) }) { Text("Use") }
                        TextButton(onClick = { onRename(computer) }) { Text("Rename") }
                        TextButton(onClick = { onRemove(computer) }) { Text("Remove") }
                    }
                }
            }
        }
        if (discovered.isNotEmpty()) {
            HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))
            Text("Discovered computers", style = MaterialTheme.typography.titleMedium)
            discovered.forEach { computer ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Row(modifier = Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(computer.name, style = MaterialTheme.typography.titleSmall)
                            Text("${computer.host}:${computer.port} - ${computer.status}", style = MaterialTheme.typography.bodySmall)
                        }
                        OutlinedButton(onClick = { onUseDiscovered(computer) }) { Text("Use") }
                    }
                }
            }
        }
    }
}

@Composable
private fun FileSection(files: List<SelectedFile>) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text("Selected files", style = MaterialTheme.typography.titleMedium)
        if (files.isEmpty()) {
            Text("No file selected", style = MaterialTheme.typography.bodySmall)
        } else {
            files.forEach { file ->
                Text("${file.displayName} (${SupportedDocumentTypes.displayType(file)})", style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

@Composable
private fun OptionsSection(options: PrintOptions, onChange: (PrintOptions) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Print options", style = MaterialTheme.typography.titleMedium)
        OutlinedTextField(
            value = options.printerName,
            onValueChange = { onChange(options.copy(printerName = it)) },
            label = { Text("Printer name") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        OutlinedTextField(
            value = options.copies,
            onValueChange = { onChange(options.copy(copies = it.filter(Char::isDigit).take(2))) },
            label = { Text("Copies") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        ChoiceField(
            label = "Color",
            selected = if (options.colorMode == "bw") "bw" else "color",
            choices = listOf("color" to "Color", "bw" to "Black and white"),
            onSelected = { onChange(options.copy(colorMode = it)) }
        )
        ChoiceField(
            label = "Duplex",
            selected = options.duplexMode,
            choices = DuplexMode.entries.map { it.wireValue to it.label },
            onSelected = { onChange(options.copy(duplexMode = it, duplex = it != DuplexMode.None.wireValue)) }
        )
        ChoiceField(
            label = "Orientation",
            selected = options.orientation,
            choices = PageOrientation.entries.map { it.wireValue to it.label },
            onSelected = { onChange(options.copy(orientation = it)) }
        )
        ChoiceField(
            label = "Paper size",
            selected = options.paperSize,
            choices = PaperSize.entries.map { it.wireValue to it.label },
            onSelected = { onChange(options.copy(paperSize = it)) }
        )
        OutlinedTextField(
            value = options.pageRange,
            onValueChange = { onChange(options.copy(pageRange = it)) },
            label = { Text("Page range") },
            placeholder = { Text("1-3, 7") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
    }
}

@Composable
private fun ChoiceField(label: String, selected: String, choices: List<Pair<String, String>>, onSelected: (String) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    val selectedLabel = choices.firstOrNull { it.first == selected }?.second ?: choices.firstOrNull()?.second.orEmpty()
    Box {
        OutlinedButton(modifier = Modifier.fillMaxWidth(), onClick = { expanded = true }) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(label, modifier = Modifier.weight(1f))
                Text(selectedLabel)
            }
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            choices.forEach { (value, text) ->
                DropdownMenuItem(
                    text = { Text(text) },
                    onClick = {
                        onSelected(value)
                        expanded = false
                    }
                )
            }
        }
    }
}

@Composable
private fun PrinterSection(
    printers: List<PrinterInfo>,
    selectedPrinter: String,
    onSelect: (String) -> Unit,
    onRefresh: () -> Unit,
    canRefresh: Boolean
) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Printers", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            OutlinedButton(enabled = canRefresh, onClick = onRefresh) { Text("Refresh") }
        }
        if (!canRefresh) {
            Text("Pair with a Windows computer to load its printers.", style = MaterialTheme.typography.bodySmall)
        } else if (printers.isEmpty()) {
            Text("No printers returned by the Windows Agent.", style = MaterialTheme.typography.bodySmall)
        } else {
            printers.forEach { printer ->
                Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    androidx.compose.material3.RadioButton(
                        selected = printer.name == selectedPrinter,
                        onClick = { onSelect(printer.name) }
                    )
                    Column(modifier = Modifier.weight(1f)) {
                        Text(printer.name)
                        Text("${if (printer.isDefault) "Default - " else ""}${printer.status}", style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusSection(status: PrintStatus, message: String, warnings: List<String>) {
    val color = when (status) {
        PrintStatus.Failed -> MaterialTheme.colorScheme.errorContainer
        PrintStatus.Completed -> MaterialTheme.colorScheme.primaryContainer
        else -> MaterialTheme.colorScheme.secondaryContainer
    }
    Surface(modifier = Modifier.fillMaxWidth(), color = color, shape = MaterialTheme.shapes.medium) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("Status: ${status.name.lowercase()}", style = MaterialTheme.typography.titleMedium)
            if (message.isNotBlank()) Text(message)
            warnings.forEach { Text("Warning: $it", style = MaterialTheme.typography.bodySmall) }
        }
    }
}
