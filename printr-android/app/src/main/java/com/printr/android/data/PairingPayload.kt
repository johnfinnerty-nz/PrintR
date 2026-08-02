package com.printr.android.data

object PairingPayloadParser {
    fun parse(raw: String): PairingDetails {
        require(readString(raw, "app") == "PrintR Agent") { "QR code is not a PrintR pairing code." }
        val host = readString(raw, "ipAddress").ifBlank { readString(raw, "host") }
        val port = readNumber(raw, "port") ?: "8787"
        val token = readString(raw, "token")
        require(host.isNotBlank() && token.isNotBlank()) { "Pairing code is missing host or token." }
        return PairingDetails(
            name = readString(raw, "computerName"),
            host = host,
            port = port,
            token = token,
            instanceId = readString(raw, "instanceId"),
            tlsFingerprint = readString(raw, "tlsFingerprint").ifBlank { null }
        )
    }

    fun validateManual(host: String, port: String, token: String): String? = when {
        host.isBlank() -> "Enter the Windows computer IP address."
        (port.toIntOrNull() ?: -1) !in 1..65535 -> "Enter a valid port."
        token.length < 16 -> "Pairing token looks too short."
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
