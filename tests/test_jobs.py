from __future__ import annotations

from datetime import datetime, timedelta, timezone

from terminal_command.jobs import JobStore


def test_job_store_persists_due_jobs_and_run_state(tmp_path):
    store = JobStore(tmp_path / "jobs.json")
    now = datetime(2026, 9, 1, 12, 0, tzinfo=timezone.utc)
    job = store.add("health", ["terminal-command", "--doctor"], interval_seconds=300, now=now)
    assert job.enabled is True
    assert store.due(now=now) == [job]

    updated = store.mark_run(job.id, now=now, status="success")
    assert updated.last_status == "success"
    assert updated.next_run_at == (now + timedelta(seconds=300)).isoformat()
    assert store.due(now=now) == []

    reloaded = JobStore(tmp_path / "jobs.json")
    assert reloaded.get(job.id).last_status == "success"


def test_jobs_are_explicitly_enabled_and_disabled(tmp_path):
    store = JobStore(tmp_path / "jobs.json")
    job = store.add("watch", ["echo", "ok"], interval_seconds=60)
    assert store.disable(job.id).enabled is False
    assert all(item.id != job.id for item in store.due())
    assert store.enable(job.id).enabled is True


def test_job_store_validates_interval_and_command(tmp_path):
    store = JobStore(tmp_path / "jobs.json")
    for command, interval in (([], 60), (["echo"], 0)):
        try:
            store.add("bad", command, interval_seconds=interval)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid job should fail")
