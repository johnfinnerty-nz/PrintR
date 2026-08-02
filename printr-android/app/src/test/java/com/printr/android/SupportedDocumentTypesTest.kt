package com.printr.android

import com.printr.android.data.PrintRClient
import com.printr.android.data.PrintOptions
import com.printr.android.data.SupportedDocumentTypes
import okio.Buffer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class SupportedDocumentTypesTest {
    @Test
    fun docx_mime_is_supported_for_picker() {
        assertTrue(SupportedDocumentTypes.PickerMimeTypes.contains(SupportedDocumentTypes.Docx))
    }

    @Test
    fun docx_display_type_uses_mime_or_extension() {
        assertEquals("DOCX document", SupportedDocumentTypes.displayType("proposal", SupportedDocumentTypes.Docx))
        assertEquals("DOCX document", SupportedDocumentTypes.displayType("proposal.docx", null))
    }

    @Test
    fun docx_upload_mime_is_preserved_when_provider_omits_type() {
        assertEquals(SupportedDocumentTypes.Docx, SupportedDocumentTypes.uploadMimeType("proposal.docx", null))
    }

    @Test
    fun upload_filename_is_sanitized_but_extension_is_preserved() {
        val safe = PrintRClient().sanitizeUploadFileName("Quarterly Plan #1.docx")

        assertEquals("Quarterly_Plan__1.docx", safe)
    }

    @Test
    fun upload_request_includes_docx_filename_and_mime_type() {
        val temp = File.createTempFile("proposal", ".docx")
        temp.writeText("docx bytes")
        try {
            val body = PrintRClient().buildMultipartBody(
                temp,
                "proposal.docx",
                null,
                PrintOptions(duplex = true, duplexMode = "shortEdge", orientation = "landscape", paperSize = "letter")
            )
            val buffer = Buffer()

            body.writeTo(buffer)
            val multipart = buffer.readUtf8()

            assertTrue(multipart.contains("filename=\"proposal.docx\""))
            assertTrue(multipart.contains(SupportedDocumentTypes.Docx))
            assertTrue(multipart.contains("name=\"duplexMode\""))
            assertTrue(multipart.contains("shortEdge"))
            assertTrue(multipart.contains("name=\"orientation\""))
            assertTrue(multipart.contains("landscape"))
            assertTrue(multipart.contains("name=\"paperSize\""))
            assertTrue(multipart.contains("letter"))
        } finally {
            temp.delete()
        }
    }

    @Test
    fun parses_printer_list_response() {
        val json = """{"defaultPrinter":"Office Printer","printers":[{"name":"Office Printer","isDefault":true,"status":"Ready"},{"name":"PDF Writer","isDefault":false,"status":"Ready"}]}"""

        val printers = PrintRClient().parsePrinters(json)

        assertEquals(2, printers.size)
        assertEquals("Office Printer", printers[0].name)
        assertTrue(printers[0].isDefault)
        assertEquals("PDF Writer", printers[1].name)
    }
}
