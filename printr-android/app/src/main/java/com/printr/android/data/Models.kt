package com.printr.android.data

import android.net.Uri

data class PairingDetails(
    val id: String = "",
    val name: String = "",
    val host: String = "",
    val port: String = "8787",
    val token: String = "",
    val instanceId: String = "",
    val tlsFingerprint: String? = null,
    val scheme: String = "https"
) {
    val baseUrl: String get() = "${scheme.lowercase()}://$host:$port"
    val isComplete: Boolean get() = host.isNotBlank() && port.isNotBlank() && token.isNotBlank()
    val displayName: String get() = name.ifBlank { host.ifBlank { "Unpaired computer" } }
}

data class SelectedFile(
    val uri: Uri,
    val displayName: String,
    val mimeType: String?
)

data class PrintOptions(
    val printerName: String = "",
    val copies: String = "1",
    val colorMode: String = "color",
    val duplex: Boolean = false,
    val pageRange: String = "",
    val duplexMode: String = DuplexMode.None.wireValue,
    val orientation: String = PageOrientation.Portrait.wireValue,
    val paperSize: String = PaperSize.A4.wireValue
)

enum class DuplexMode(val wireValue: String, val label: String) {
    None("none", "Off"),
    LongEdge("longEdge", "Long edge"),
    ShortEdge("shortEdge", "Short edge");

    companion object {
        fun fromWire(value: String): DuplexMode = entries.firstOrNull { it.wireValue == value } ?: None
    }
}

enum class PageOrientation(val wireValue: String, val label: String) {
    Portrait("portrait", "Portrait"),
    Landscape("landscape", "Landscape")
}

enum class PaperSize(val wireValue: String, val label: String) {
    A4("a4", "A4"),
    Letter("letter", "US Letter")
}

data class PrinterInfo(
    val name: String,
    val isDefault: Boolean,
    val status: String
)

enum class PrintStatus {
    Idle,
    Connecting,
    Uploading,
    Queued,
    Converting,
    Converted,
    Printing,
    Completed,
    Failed
}

enum class ConnectionState {
    NotPaired,
    PairedOffline,
    PairedReachable,
    Printing
}

data class PrintJobResponse(
    val jobId: String,
    val status: String,
    val message: String,
    val warnings: List<String> = emptyList()
)

data class DiscoveryComputer(
    val name: String,
    val host: String,
    val port: String,
    val instanceId: String,
    val status: String,
    val scheme: String = "https",
    val tlsFingerprint: String? = null
)

data class PairTestResult(
    val success: Boolean,
    val instanceId: String,
    val computerName: String,
    val message: String
)
