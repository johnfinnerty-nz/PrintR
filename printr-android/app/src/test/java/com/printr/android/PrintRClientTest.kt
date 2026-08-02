package com.printr.android

import com.printr.android.data.PairingDetails
import com.printr.android.data.PrintRClient
import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PrintRClientTest {
    @Test
    fun health_returns_true_for_ok_agent() = runBlocking {
        val server = MockWebServer()
        server.enqueue(MockResponse().setResponseCode(200).setBody("""{"status":"ok"}"""))
        server.start()
        try {
            val pairing = PairingDetails(host = server.hostName, port = server.port.toString(), token = "token")
            assertTrue(PrintRClient().health(pairing))
        } finally {
            server.shutdown()
        }
    }

    @Test
    fun parses_printer_list_response() {
        val json = """
            {
              "defaultPrinter": "HP OfficeJet",
              "printers": [
                { "name": "HP OfficeJet", "isDefault": true, "status": "Ready" },
                { "name": "PDF Writer", "isDefault": false, "status": "Ready" }
              ]
            }
        """.trimIndent()

        val printers = PrintRClient().parsePrinters(json)

        assertEquals(2, printers.size)
        assertEquals("HP OfficeJet", printers[0].name)
        assertTrue(printers[0].isDefault)
        assertEquals("PDF Writer", printers[1].name)
    }
}
