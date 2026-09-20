package com.printr.android

import com.printr.android.data.PairingPayloadParser
import com.printr.android.data.DiscoveryClient
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PairingPayloadTest {
    @Test
    fun parses_qr_pairing_payload() {
        val raw = """{"app":"PrintR Agent","version":"1.0.0","instanceId":"abc","computerName":"DESKTOP","ipAddress":"192.168.1.10","port":8787,"token":"0123456789abcdef0123456789abcdef","scheme":"https","tlsFingerprint":"AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99"}"""

        val pairing = PairingPayloadParser.parse(raw)

        assertEquals("DESKTOP", pairing.name)
        assertEquals("192.168.1.10", pairing.host)
        assertEquals("8787", pairing.port)
        assertEquals("abc", pairing.instanceId)
        assertEquals("https", pairing.scheme)
        assertEquals("AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899", pairing.tlsFingerprint)
    }

    @Test
    fun validates_manual_pairing() {
        assertEquals("Enter the computer IP address.", PairingPayloadParser.validateManual("", "8787", "0123456789abcdef"))
        assertEquals("Enter a valid port.", PairingPayloadParser.validateManual("192.168.1.10", "99999", "0123456789abcdef"))
        assertEquals("Pairing token looks too short.", PairingPayloadParser.validateManual("192.168.1.10", "8787", "short"))
        assertEquals("Enter the TLS fingerprint shown by PrintR Agent.", PairingPayloadParser.validateManual("192.168.1.10", "8787", "0123456789abcdef"))
        assertNull(PairingPayloadParser.validateManual("192.168.1.10", "8787", "0123456789abcdef", "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899"))
    }

    @Test
    fun keeps_legacy_qr_payloads_on_http() {
        val raw = """{"app":"PrintR Agent","version":"0.1.0","instanceId":"abc","computerName":"DESKTOP","ipAddress":"192.168.1.10","port":8787,"token":"0123456789abcdef0123456789abcdef"}"""

        val pairing = PairingPayloadParser.parse(raw)

        assertEquals("http", pairing.scheme)
    }

    @Test
    fun parses_secure_discovery_response() {
        val response = """{"app":"PrintR Agent","computerName":"OFFICE-PC","host":"192.168.1.20","port":8787,"instanceId":"agent-1","scheme":"https","tlsFingerprint":"AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899"}"""

        val computer = DiscoveryClient.parseResponse(response, "192.168.1.20")

        requireNotNull(computer)
        assertEquals("OFFICE-PC", computer.name)
        assertEquals("https", computer.scheme)
        assertEquals("secure", computer.status)
    }

    @Test
    fun rejects_secure_discovery_response_without_fingerprint() {
        val response = """{"app":"PrintR Agent","computerName":"OFFICE-PC","host":"192.168.1.20","port":8787,"scheme":"https"}"""

        assertNull(DiscoveryClient.parseResponse(response, "192.168.1.20"))
    }
}
