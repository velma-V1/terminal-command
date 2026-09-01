from __future__ import annotations

import json
import urllib.request
from typing import Any, Callable, Protocol

from .contracts import Action, InputKind, RouteResult


class ModelRouter(Protocol):
    def route(self, text: str) -> RouteResult | None: ...


Transport = Callable[[str, dict[str, Any], float], dict[str, Any]]


class OllamaRouter:
    def __init__(
        self,
        model: str,
        *,
        base_url: str = "http://127.0.0.1:11434",
        timeout_s: float = 8.0,
        transport: Transport | None = None,
    ):
        self.model = model
        self.base_url = base_url.rstrip("/")
        self.timeout_s = timeout_s
        self.transport = transport or self._http_transport

    def route(self, text: str) -> RouteResult | None:
        payload = {
            "model": self.model,
            "stream": False,
            "format": "json",
            "messages": [
                {
                    "role": "system",
                    "content": (
                        "You are an intent router, not an executor. Convert the user's request into one local command. "
                        "Return JSON only with keys intent, command (array of argv strings), backend (native or wsl), "
                        "confidence (0..1), explanation. If a safe concrete command cannot be inferred, return an empty command array."
                    ),
                },
                {"role": "user", "content": text},
            ],
        }
        try:
            response = self.transport(f"{self.base_url}/api/chat", payload, self.timeout_s)
            content = response["message"]["content"]
            parsed = json.loads(content)
            return self._parse(parsed)
        except (KeyError, TypeError, ValueError, OSError, TimeoutError, json.JSONDecodeError):
            return None
        except Exception:
            return None

    def _parse(self, parsed: dict[str, Any]) -> RouteResult | None:
        if not isinstance(parsed, dict):
            return None
        command = parsed.get("command")
        backend = parsed.get("backend", "native")
        confidence = parsed.get("confidence")
        if not isinstance(command, list) or not command or not all(isinstance(part, str) and part for part in command):
            return None
        if backend not in {"native", "wsl"}:
            return None
        if not isinstance(confidence, (int, float)) or isinstance(confidence, bool):
            return None
        confidence = float(confidence)
        if not 0.0 <= confidence <= 1.0:
            return None
        intent = parsed.get("intent")
        if not isinstance(intent, str) or not intent.strip():
            intent = "model_command"
        explanation = parsed.get("explanation")
        if explanation is not None and not isinstance(explanation, str):
            explanation = str(explanation)
        return RouteResult(
            input_kind=InputKind.NATURAL_LANGUAGE,
            source="model",
            action=Action(
                name=intent.strip(),
                command=command,
                backend=backend,
                metadata={"model_proposed": True},
            ),
            confidence=confidence,
            model_id=self.model,
            explanation=explanation,
        )

    @staticmethod
    def _http_transport(url: str, payload: dict[str, Any], timeout: float) -> dict[str, Any]:
        request = urllib.request.Request(
            url,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))
