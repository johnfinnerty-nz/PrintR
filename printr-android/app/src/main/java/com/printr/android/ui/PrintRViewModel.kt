package com.printr.android.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.printr.android.data.PairingDetails
import com.printr.android.data.ConnectionState
import com.printr.android.data.DiscoveryClient
import com.printr.android.data.DiscoveryComputer
import com.printr.android.data.PairingStore
import com.printr.android.data.PairingPayloadParser
import com.printr.android.data.PrintOptions
import com.printr.android.data.PrintRClient
import com.printr.android.data.PrintStatus
import com.printr.android.data.PrinterInfo
import com.printr.android.data.SelectedFile
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.Job
import kotlinx.coroutines.CancellationException

data class PrintRUiState(
    val pairing: PairingDetails = PairingDetails(),
    val pairedComputers: List<PairingDetails> = emptyList(),
    val discoveredComputers: List<DiscoveryComputer> = emptyList(),
    val connectionState: ConnectionState = ConnectionState.NotPaired,
    val recentJobs: List<String> = emptyList(),
    val selectedFiles: List<SelectedFile> = emptyList(),
    val printers: List<PrinterInfo> = emptyList(),
    val options: PrintOptions = PrintOptions(),
    val status: PrintStatus = PrintStatus.Idle,
    val statusMessage: String = "",
    val warnings: List<String> = emptyList()
)

class PrintRViewModel(app: Application) : AndroidViewModel(app) {
    private val store = PairingStore(app)
    private val client = PrintRClient()
    private val discovery = DiscoveryClient(app)
    private val savedPairing = store.load()
    private val _state = MutableStateFlow(PrintRUiState(pairing = savedPairing, pairedComputers = store.loadAll(), options = store.loadOptions(), connectionState = if (savedPairing.isComplete) ConnectionState.PairedOffline else ConnectionState.NotPaired))
    val state: StateFlow<PrintRUiState> = _state
    private var uploadJob: Job? = null

    fun editPairing(details: PairingDetails) {
        _state.update { it.copy(pairing = details, connectionState = if (details.isComplete) ConnectionState.PairedOffline else ConnectionState.NotPaired) }
    }

    fun updatePairing(details: PairingDetails) {
        store.save(details)
        _state.update { it.copy(pairing = store.load(), pairedComputers = store.loadAll(), connectionState = ConnectionState.PairedOffline) }
    }

    fun pairFromQr(raw: String) {
        runCatching { PairingPayloadParser.parse(raw) }
            .onSuccess {
                updatePairing(it)
                refreshPrinters(store.load())
            }
            .onFailure { e -> fail(e) }
    }

    fun removePairing(id: String) {
        store.remove(id)
        val selected = store.load()
        _state.update { it.copy(pairing = selected, pairedComputers = store.loadAll(), printers = emptyList(), connectionState = if (selected.isComplete) ConnectionState.PairedOffline else ConnectionState.NotPaired, status = PrintStatus.Idle, statusMessage = "") }
    }

    fun chooseDefault(details: PairingDetails) {
        updatePairing(details)
        refreshPrinters(details)
    }

    fun renamePairing(id: String, name: String) {
        val current = store.loadAll().firstOrNull { it.id == id } ?: return
        updatePairing(current.copy(name = name))
    }

    fun discoverComputers() {
        viewModelScope.launch {
            _state.update { it.copy(status = PrintStatus.Connecting, statusMessage = "Searching local network") }
            runCatching { discovery.discover() }
                .onSuccess { found ->
                    _state.update { it.copy(discoveredComputers = found, status = PrintStatus.Idle, statusMessage = if (found.isEmpty()) "No PrintR Agents found. Discovery may be blocked by the network." else "Found ${found.size} computer(s).") }
                }
                .onFailure { _ ->
                    _state.update { it.copy(status = PrintStatus.Idle, statusMessage = "Discovery is unavailable on this network. Open Computers to pair manually or scan a QR code.") }
                }
        }
    }

    fun useDiscoveredComputer(computer: DiscoveryComputer) {
        editPairing(
            _state.value.pairing.copy(
                name = computer.name,
                host = computer.host,
                port = computer.port,
                instanceId = computer.instanceId,
                scheme = computer.scheme,
                tlsFingerprint = computer.tlsFingerprint
            )
        )
        _state.update { it.copy(status = PrintStatus.Idle, statusMessage = "Enter the pairing token, then test the connection.") }
    }

    fun setFiles(files: List<SelectedFile>) {
        _state.update { it.copy(selectedFiles = files, status = PrintStatus.Idle, statusMessage = "") }
    }

    fun updateOptions(options: PrintOptions) {
        store.saveOptions(options)
        _state.update { it.copy(options = options) }
    }

    fun clearPairings() {
        store.clear()
        _state.update { it.copy(pairing = PairingDetails(), pairedComputers = emptyList(), printers = emptyList(), connectionState = ConnectionState.NotPaired, status = PrintStatus.Idle, statusMessage = "") }
    }

    fun testConnection() {
        val pairing = _state.value.pairing
        viewModelScope.launch {
            _state.update { it.copy(status = PrintStatus.Connecting, statusMessage = "Connecting") }
            runCatching { client.pairTest(pairing) }
                .onSuccess { result ->
                    val updated = pairing.copy(instanceId = result.instanceId, name = result.computerName)
                    store.save(updated)
                    _state.update {
                        it.copy(
                            pairing = store.load(),
                            pairedComputers = store.loadAll(),
                            connectionState = ConnectionState.PairedReachable,
                            status = PrintStatus.Completed,
                            statusMessage = result.message
                        )
                    }
                    refreshPrinters(store.load())
                }
                .onFailure { e -> fail(e) }
        }
    }

    fun refreshPrinters(pairing: PairingDetails = _state.value.pairing) {
        if (!pairing.isComplete) return
        viewModelScope.launch {
            _state.update { it.copy(statusMessage = "Loading printers") }
            runCatching { client.printers(pairing) }
                .onSuccess { printers ->
                    val selected = _state.value.options.printerName
                    val defaultPrinter = printers.firstOrNull { it.isDefault }?.name
                    val nextPrinter = when {
                        selected.isNotBlank() && printers.any { it.name == selected } -> selected
                        defaultPrinter != null -> defaultPrinter
                        printers.isNotEmpty() -> printers.first().name
                        else -> selected
                    }
                    val nextOptions = _state.value.options.copy(printerName = nextPrinter)
                    store.saveOptions(nextOptions)
                    _state.update {
                        it.copy(
                            printers = printers,
                            connectionState = ConnectionState.PairedReachable,
                            options = nextOptions,
                            status = if (it.status == PrintStatus.Failed) PrintStatus.Idle else it.status,
                            statusMessage = if (printers.isEmpty()) "No printers returned by PrintR Agent" else "Loaded ${printers.size} printer(s)"
                        )
                    }
                }
                .onFailure { e -> fail(e) }
        }
    }

    fun uploadSelectedFiles() {
        if (uploadJob?.isActive == true) return
        val snapshot = _state.value
        if (snapshot.status in listOf(PrintStatus.Uploading, PrintStatus.Queued, PrintStatus.Converting, PrintStatus.Converted, PrintStatus.Printing)) return
        if (snapshot.selectedFiles.isEmpty()) return
        uploadJob = viewModelScope.launch {
            snapshot.selectedFiles.forEachIndexed { index, file ->
                _state.update {
                    it.copy(
                        status = PrintStatus.Uploading,
                        statusMessage = "Uploading ${index + 1} of ${snapshot.selectedFiles.size}: ${file.displayName}",
                        connectionState = ConnectionState.Printing
                    )
                }
                try {
                    val response = client.upload(getApplication(), snapshot.pairing, file, snapshot.options)
                    _state.update {
                        it.copy(
                            status = PrintStatus.Queued,
                            statusMessage = "${response.status}: ${response.message}",
                            recentJobs = listOf(response.jobId) + it.recentJobs.take(9),
                            warnings = response.warnings
                        )
                    }
                    pollJob(snapshot.pairing, response.jobId)
                    if (_state.value.status == PrintStatus.Failed) return@launch
                } catch (e: Throwable) {
                    if (e is CancellationException) throw e
                    fail(e)
                    return@launch
                }
            }
            _state.update { it.copy(connectionState = ConnectionState.PairedReachable, statusMessage = "All selected files completed.") }
        }
    }

    fun uploadFirstFile() = uploadSelectedFiles()

    fun retryUpload() = uploadSelectedFiles()

    fun showDirectDocxUnsupported() {
        _state.update {
            it.copy(
                status = PrintStatus.Failed,
                statusMessage = "DOCX direct printing is not supported. Send it to your paired computer instead."
            )
        }
    }

    private suspend fun pollJob(pairing: PairingDetails, jobId: String) {
        if (jobId.isBlank()) return
        repeat(660) {
            delay(1000)
            val job = client.job(pairing, jobId)
            val mapped = when (job.status.lowercase()) {
                "queued" -> PrintStatus.Queued
                "receiving" -> PrintStatus.Uploading
                "validating" -> PrintStatus.Uploading
                "converting" -> PrintStatus.Converting
                "converted" -> PrintStatus.Converted
                "printing" -> PrintStatus.Printing
                "completed" -> PrintStatus.Completed
                "failed" -> PrintStatus.Failed
                "cancelled" -> PrintStatus.Failed
                else -> PrintStatus.Failed
            }
            _state.update {
                it.copy(
                    status = mapped,
                    connectionState = if (mapped == PrintStatus.Completed) ConnectionState.PairedReachable else it.connectionState,
                    statusMessage = "${job.status}: ${job.message}",
                    warnings = job.warnings
                )
            }
            if (mapped == PrintStatus.Completed || mapped == PrintStatus.Failed) return
        }
        _state.update {
            it.copy(
                status = PrintStatus.Failed,
                connectionState = ConnectionState.PairedOffline,
                statusMessage = "Timed out while waiting for PrintR Agent. Check the computer's print queue before retrying."
            )
        }
    }

    private fun fail(e: Throwable) {
        val message = when {
            e.message?.contains("Wrong", ignoreCase = true) == true -> e.message!!
            e.message?.contains("timeout", ignoreCase = true) == true -> "Computer offline or firewall blocked the connection."
            e.message?.contains("failed to connect", ignoreCase = true) == true -> "Computer offline or Windows Firewall blocked PrintR Agent."
            e.message?.contains("certificate", ignoreCase = true) == true || e.message?.contains("TLS", ignoreCase = true) == true -> "The PrintR Agent security certificate changed or is missing. Check its pairing details and pair again."
            e.message?.contains("Unsupported", ignoreCase = true) == true -> "Unsupported file. See Settings for supported formats."
            e.message?.contains("DOCX", ignoreCase = true) == true -> e.message!!
            else -> e.message ?: "Print failed."
        }
        _state.update { it.copy(status = PrintStatus.Failed, statusMessage = message, connectionState = if (it.pairing.isComplete) ConnectionState.PairedOffline else ConnectionState.NotPaired) }
    }
}
