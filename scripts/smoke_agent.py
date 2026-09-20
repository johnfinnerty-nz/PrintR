"""Run local HTTPS integration checks against a self-contained agent binary.

Uses an isolated temporary profile and mock printing. Never sends paper jobs.
"""
import argparse
import io
import json
import os
from pathlib import Path
import socket
import ssl
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("binary")
    args = parser.parse_args()
    binary = str(Path(args.binary).resolve())
    with tempfile.TemporaryDirectory(prefix="printr-smoke-") as temp:
        root = Path(temp)
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", 0))
            port = probe.getsockname()[1]
        token = "local-smoke-test-token"
        (root / "settings.json").write_text(json.dumps({"Token": token, "Port": port, "DebugKeepSpoolFiles": False, "MockPrintMode": True, "FriendlyName": "PrintR QA", "MaxUploadMegabytes": 40}), encoding="utf-8")
        environment = dict(os.environ, PRINTR_DATA_DIR=temp)
        # This local test trusts only the isolated loopback endpoint it launches.
        context = ssl._create_unverified_context()
        base = f"https://127.0.0.1:{port}"
        def request(path, body=None, mime=None, authenticated=True):
            headers = {"X-PrintR-Token": token} if authenticated else {}
            if mime:
                headers["Content-Type"] = mime
            req = urllib.request.Request(base + path, body, headers)
            try:
                with urllib.request.urlopen(req, context=context, timeout=20) as response:
                    return response.status, json.load(response)
            except urllib.error.HTTPError as error:
                return error.code, error.read().decode()

        def upload(name, data, page_range=""):
            boundary = "printr-smoke-boundary"
            body = (f'--{boundary}\r\nContent-Disposition: form-data; name="pageRange"\r\n\r\n{page_range}\r\n'
                    f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode() + data + f"\r\n--{boundary}--\r\n".encode()
            return request("/print", body, f"multipart/form-data; boundary={boundary}")

        with (root / "service.log").open("w") as log:
            process = subprocess.Popen([binary, "--headless", "--loopback"], env=environment, stdout=log, stderr=log)
            try:
                for _ in range(100):
                    if process.poll() is not None:
                        raise RuntimeError((root / "service.log").read_text())
                    try:
                        if request("/health", authenticated=False)[0] == 200:
                            break
                    except (OSError, urllib.error.URLError):
                        time.sleep(0.2)
                else:
                    raise RuntimeError("Agent did not start")
                assert request("/printers", authenticated=False)[0] == 401
                assert request("/jobs", authenticated=False)[0] == 401
                assert request("/pair/test", b'{"token":"wrong"}', "application/json", False)[0] == 401
                assert request("/pair/test", json.dumps({"token": token}).encode(), "application/json", False)[0] == 200
                assert upload("spoof.pdf", b"not a PDF")[0] == 400
                assert upload("macro.docm", b"binary")[0] == 400
                assert upload("sample.txt", b"Hello PrintR", "4-1")[0] == 400
                assert upload("sample.txt", b"Hello PrintR", "1,duplex")[0] == 400
                job_ids = []
                for name, data in [("sample.txt", b"Hello PrintR"), ("table.csv", b"name,total\nPrintR,42\n"), ("letter.rtf", b"{\\rtf1 Hello PrintR}"), ("large.txt", b"A" * (31 * 1024 * 1024))]:
                    status, response = upload(name, data)
                    assert status == 202, (status, response)
                    job_ids.append(response["jobId"])
                for extension, entry in [("docx", "word/document.xml"), ("xlsx", "xl/workbook.xml"), ("pptx", "ppt/presentation.xml"), ("odt", "content.xml"), ("ods", "content.xml"), ("odp", "content.xml")]:
                    buffer = io.BytesIO()
                    with zipfile.ZipFile(buffer, "w") as archive:
                        archive.writestr(entry, "<document />")
                        if extension.startswith("od"):
                            kind = {"odt": "text", "ods": "spreadsheet", "odp": "presentation"}[extension]
                            archive.writestr("mimetype", "application/vnd.oasis.opendocument." + kind)
                    status, response = upload("sample." + extension, buffer.getvalue())
                    assert status == 202, (status, response)
                    job_ids.append(response["jobId"])
                for job_id in job_ids:
                    for _ in range(100):
                        _, result = request("/jobs/" + job_id)
                        if result["status"] == "completed":
                            assert "Mock print mode" in " ".join(result["warnings"])
                            break
                        assert result["status"] != "failed", result
                        time.sleep(0.1)
                    else:
                        raise AssertionError("Job did not complete")
                for _ in range(30):
                    if not list((root / "Spool").iterdir()):
                        break
                    time.sleep(0.1)
                assert not list((root / "Spool").iterdir()), "Spool data not cleaned"
                print(json.dumps({"result": "PASS", "platform": request("/health", authenticated=False)[1]["platform"], "mock_jobs": len(job_ids), "checks": ["HTTPS startup", "authentication", "pairing", "content validation", "page ranges", "31 MB upload", "expanded formats", "job completion", "spool cleanup"]}, indent=2))
            finally:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == "__main__":
    main()
