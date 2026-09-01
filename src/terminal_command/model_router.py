from __future__ import annotations

import json
import urllib.request
from typing import Any, Callable, Protocol

from .capabilities import CapabilityRegistry
from .contracts import Action, InputKind, RouteResult


class ModelRouter(Protocol):
    def route(self, text: str) -> RouteResult | None: ...


Transport = Callable[[str, dict[str, Any], float], dict[str, Any]]


class OllamaRouter:
    def __init__(
        self,
        model: str,
        *,
        registry: CapabilityRegistry | None = None,
        base_url: str = "http://127.0.0.1:11434",
        timeout_s: float = 8.0,
        transport: Transport | None = None,
    ):
        self.model = model
        self.registry = registry
        self.base_url = base_url.rstrip("/")
        self.timeout_s = timeout_s
        self.transport = transport or self._http_transport

    def route(self, text: str) -> RouteResult | None:
        capability_rows = self.registry.describe() if self.registry is not None else []
        payload = {
            "model": self.model,
            "stream": False,
            "format": "json",
            "messages": [
                {
                    "role": "system",
                    "content": (
                        "You are an intent router, not an executor. Prefer a registered capability when one fits. "
                        "Return JSON only. Preferred shape: {capability: string, arguments: object, confidence: 0..1, explanation: string}. "
                        "Compatibility fallback shape: {intent: string, command: array of argv strings, backend: native|wsl, confidence: 0..1, explanation: string}. "
                        "If uncertain, return an empty command and no capability. Registered capabilities: "
                        + json.dumps(capability_rows, separators=(",", ":"))
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
        confidence = parsed.get("confidence")
        if not isinstance(confidence, (int, float)) or isinstance(confidence, bool):
            return None
        confidence = float(confidence)
        if not 0.0 <= confidence <= 1.0:
            return None
        explanation = parsed.get("explanation")
        if explanation is not None and not isinstance(explanation, str):
            explanation = str(explanation)

        capability_id = parsed.get("capability")
        if capability_id is not None:
            if not isinstance(capability_id, str) or not capability_id.strip() or self.registry is None:
                return None
            resolved = self.registry.resolve_id(capability_id.strip())
            if resolved is None:
                return None
            arguments = parsed.get("arguments", {})
            if not isinstance(arguments, dict):
                return None
            try:
                action = self.registry.invoke(resolved, arguments)
            except ValueError:
                return None
            action.metadata["model_proposed"] = True
            action.metadata["capability_first"] = True
            return RouteResult(
                input_kind=InputKind.NATURAL_LANGUAGE,
                source="model",
                action=action,
                confidence=confidence,
                rule_id=f"capability:{resolved}",
                model_id=self.model,
                explanation=explanation,
            )

        command = parsed.get("command")
        backend = parsed.get("backend", "native")
        if not isinstance(command, list) or not command or not all(isinstance(part, str) and part for part in command):
            return None
        if backend not in {"native", "wsl"}:
            return None
        intent = parsed.get("intent")
        if not isinstance(intent, str) or not intent.strip():
            intent = "model_command"
        return RouteResult(
            input_kind=InputKind.NATURAL_LANGUAGE,
            source="model",
            action=Action(
                name=intent.strip(),
                command=command,
                backend=backend,
                metadata={"model_proposed": True, "compatibility_fallback": True},
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
