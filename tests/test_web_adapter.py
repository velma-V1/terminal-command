from __future__ import annotations

import io

import pytest

from terminal_command.web_adapter import WebResponse, build_fetch_action, fetch_url, validate_url
from terminal_command.policy import PolicyEngine


class FakeResponse:
    def __init__(self, body: bytes, *, url: str = "https://example.com/final", status: int = 200):
        self._body = io.BytesIO(body)
        self._url = url
        self.status = status
        self.headers = {"Content-Type": "text/plain"}

    def read(self, size: int = -1) -> bytes:
        return self._body.read(size)

    def geturl(self) -> str:
        return self._url

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False


class FakeOpener:
    def __init__(self, response: FakeResponse):
        self.response = response
        self.requests = []

    def open(self, request, timeout=None):
        self.requests.append((request, timeout))
        return self.response


def test_validate_url_allows_only_http_https_without_embedded_credentials():
    assert validate_url("https://example.com/path") == "https://example.com/path"
    for value in ("file:///etc/passwd", "ftp://example.com/x", "https://user:pass@example.com/x", "javascript:alert(1)"):
        with pytest.raises(ValueError):
            validate_url(value)


def test_fetch_url_is_size_bounded_and_does_not_need_live_network():
    opener = FakeOpener(FakeResponse(b"hello"))
    response = fetch_url("https://example.com", max_bytes=10, timeout=3, opener=opener)
    assert isinstance(response, WebResponse)
    assert response.body == b"hello"
    assert response.status == 200
    assert response.url == "https://example.com/final"
    assert opener.requests[0][1] == 3


def test_fetch_url_rejects_response_larger_than_limit():
    opener = FakeOpener(FakeResponse(b"01234567890"))
    with pytest.raises(ValueError, match="exceeds"):
        fetch_url("https://example.com", max_bytes=10, opener=opener)


def test_fetch_url_revalidates_redirect_destination():
    opener = FakeOpener(FakeResponse(b"ok", url="file:///etc/passwd"))
    with pytest.raises(ValueError):
        fetch_url("https://example.com", opener=opener)


def test_web_fetch_action_is_network_scoped_and_approval_gated():
    action = build_fetch_action("https://example.com/data", max_bytes=4096, timeout=5)
    assert action.metadata["network"] is True
    assert action.metadata["remote"] is True
    assert "4096" in action.command
    assert PolicyEngine().evaluate(action).decision.value == "require_approval"
