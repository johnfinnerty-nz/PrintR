package com.printr.android

import com.printr.android.data.PairingPayloadParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PairingPayloadTest {
    @Test
    fun parses_qr_pairing_payload() {
        val raw = """{"app":"PrintR Agent","version":"0.1.0","instanceId":"abc","computerName":"DESKTOP","ipAddress":"192.168.1.10","port":8787,"token":"0123456789abcdef0123456789abcdef"}"""

        val pairing = PairingPayloadParser.parse(raw)

        assertEquals("DESKTOP", pairing.name)
        assertEquals("192.168.1.10", pairing.host)
        assertEquals("8787", pairing.port)
        assertEquals("abc", pairing.instanceId)
    }

    @Test
    fun validates_manual_pairing() {
        assertEquals("Enter the Windows computer IP address.", PairingPayloadParser.validateManual("", "8787", "0123456789abcdef"))
        assertEquals("Enter a valid port.", PairingPayloadParser.validateManual("192.168.1.10", "99999", "0123456789abcdef"))
        assertEquals("Pairing token looks too short.", PairingPayloadParser.validateManual("192.168.1.10", "8787", "short"))
        assertNull(PairingPayloadParser.validateManual("192.168.1.10", "8787", "0123456789abcdef"))
    }
}
