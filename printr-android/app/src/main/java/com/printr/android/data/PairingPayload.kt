package com.printr.android.data

object PairingPayloadParser {
    fun parse(raw: String): PairingDetails {
        require(readString(raw, "app") == "PrintR Agent") { "QR code is not a PrintR pairing code." }
        val host = readString(raw, "ipAddress").ifBlank { readString(raw, "host") }
        val port = readNumber(raw, "port") ?: "8787"
        val token = readString(raw, "token")
        val scheme = readString(raw, "scheme").lowercase().ifBlank { "http" }
        val fingerprint = normalizeTlsFingerprint(readString(raw, "tlsFingerprint"))
        require(host.isNotBlank() && token.isNotBlank()) { "Pairing code is missing host or token." }
        require(scheme == "http" || scheme == "https") { "Pairing code has an unsupported connection type." }
        require(scheme != "https" || fingerprint.length == 64) { "Pairing code is missing a valid TLS fingerprint." }
        return PairingDetails(
            name = readString(raw, "computerName"),
            host = host,
            port = port,
            token = token,
            instanceId = readString(raw, "instanceId"),
            tlsFingerprint = fingerprint.ifBlank { null },
            scheme = scheme
        )
    }

    fun validateManual(host: String, port: String, token: String, tlsFingerprint: String? = null, scheme: String = "https"): String? = when {
        host.isBlank() -> "Enter the computer IP address."
        (port.toIntOrNull() ?: -1) !in 1..65535 -> "Enter a valid port."
        token.length < 16 -> "Pairing token looks too short."
        scheme.lowercase() !in setOf("http", "https") -> "Enter a valid connection type."
        scheme.equals("https", ignoreCase = true) && normalizeTlsFingerprint(tlsFingerprint.orEmpty()).length != 64 -> "Enter the TLS fingerprint shown by PrintR Agent."
        else -> null
    }

    private fun readString(raw: String, key: String): String {
        val match = Regex("\"$key\"\\s*:\\s*\"([^\"]*)\"").find(raw)
        return match?.groupValues?.get(1).orEmpty()
    }

    private fun readNumber(raw: String, key: String): String? {
        val match = Regex("\"$key\"\\s*:\\s*(\\d+)").find(raw)
        return match?.groupValues?.get(1)
    }
}
