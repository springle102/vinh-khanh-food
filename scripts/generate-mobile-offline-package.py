#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import sys
import urllib.parse
import urllib.request


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate the MAUI bundled offline package from the real backend bootstrap projection."
    )
    parser.add_argument("--api-base-url", default=os.environ.get("VK_MOBILE_SEED_API_BASE_URL", "http://127.0.0.1:5080"))
    parser.add_argument("--language", default=os.environ.get("VK_MOBILE_SEED_LANGUAGE", "vi"))
    parser.add_argument("--bootstrap-file", default=os.environ.get("VK_MOBILE_SEED_BOOTSTRAP_FILE"))
    parser.add_argument(
        "--backend-wwwroot",
        default=os.environ.get("VK_BACKEND_WWWROOT", "apps/backend-api/wwwroot"),
    )
    parser.add_argument(
        "--output",
        default=os.environ.get("VK_MOBILE_SEED_OUTPUT", "apps/mobile-app/Resources/Raw/seed/offline-package"),
    )
    return parser.parse_args()


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def read_bootstrap(args: argparse.Namespace) -> dict:
    if args.bootstrap_file:
        with open(args.bootstrap_file, "r", encoding="utf-8") as handle:
            return json.load(handle)

    endpoint = urllib.parse.urljoin(args.api_base_url.rstrip("/") + "/", "api/v1/bootstrap")
    url = f"{endpoint}?languageCode={urllib.parse.quote(args.language)}"
    print(f"[mobile-seed] Fetching bootstrap: {url}")
    with urllib.request.urlopen(url, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))


def safe_reset_output(output: Path) -> None:
    output = output.resolve()
    expected_suffix = Path("apps/mobile-app/Resources/Raw/seed/offline-package")
    if expected_suffix not in output.parents and output.name != "offline-package":
        raise RuntimeError(f"Refusing to reset unexpected output directory: {output}")

    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)
    (output / "assets" / "image").mkdir(parents=True, exist_ok=True)
    (output / "assets" / "audio").mkdir(parents=True, exist_ok=True)


def get_data(envelope: dict) -> dict:
    data = envelope.get("data")
    if not isinstance(data, dict):
        raise RuntimeError("Bootstrap envelope does not contain a data object.")
    return data


def is_playable_audio(audio: dict) -> bool:
    audio_url = str(audio.get("audioUrl") or "").strip()
    if not audio_url:
        return False

    if bool(audio.get("isOutdated")):
        return False

    public_status = str(audio.get("status") or "").strip().lower()
    generation_status = str(audio.get("generationStatus") or "").strip().lower()
    if public_status and public_status != "ready":
        return False

    if generation_status in {"failed", "outdated", "pending"}:
        return False

    return not generation_status or generation_status == "success"


def extension_for(remote_url: str, kind: str) -> str:
    path = urllib.parse.urlparse(remote_url).path or remote_url
    suffix = Path(path).suffix.lower()
    if suffix and 2 <= len(suffix) <= 8:
        return suffix
    return ".mp3" if kind == "audio" else ".jpg"


def resolve_local_storage_file(remote_url: str, backend_wwwroot: Path) -> Path | None:
    parsed = urllib.parse.urlparse(remote_url)
    candidate_path = urllib.parse.unquote(parsed.path if parsed.scheme else remote_url)
    candidate_path = candidate_path.replace("\\", "/").lstrip("/")
    if not candidate_path.startswith("storage/"):
        return None

    local_path = backend_wwwroot / Path(candidate_path)
    return local_path if local_path.exists() else None


def read_asset_bytes(remote_url: str, args: argparse.Namespace) -> bytes | None:
    backend_wwwroot = Path(args.backend_wwwroot).resolve()
    local_path = resolve_local_storage_file(remote_url, backend_wwwroot)
    if local_path is not None:
        return local_path.read_bytes()

    parsed = urllib.parse.urlparse(remote_url)
    if parsed.scheme in {"http", "https"}:
        url = remote_url
    else:
        url = urllib.parse.urljoin(args.api_base_url.rstrip("/") + "/", remote_url.lstrip("/"))

    try:
        with urllib.request.urlopen(url, timeout=45) as response:
            return response.read()
    except Exception as exc:  # noqa: BLE001 - build tool should warn and continue.
        print(f"[mobile-seed] WARN: asset not copied: {remote_url} ({exc})")
        return None


def collect_assets(data: dict, args: argparse.Namespace, output: Path) -> list[dict]:
    entries_by_key: dict[str, dict] = {}

    def add(remote_url: str | None, kind: str, entity_type: str, entity_id: str, language_code: str = "") -> None:
        key = (remote_url or "").strip()
        if not key or key in entries_by_key:
            return

        asset_bytes = read_asset_bytes(key, args)
        if not asset_bytes:
            return

        digest = hashlib.sha256(key.encode("utf-8")).hexdigest()
        relative_path = f"assets/{kind}/{digest}{extension_for(key, kind)}"
        absolute_path = output / relative_path
        absolute_path.parent.mkdir(parents=True, exist_ok=True)
        absolute_path.write_bytes(asset_bytes)

        entries_by_key[key] = {
            "key": key,
            "kind": kind,
            "relativePath": relative_path,
            "entityType": entity_type or "",
            "entityId": entity_id or "",
            "languageCode": language_code or "",
            "sizeBytes": len(asset_bytes),
        }

    for asset in data.get("mediaAssets") or []:
        media_type = str(asset.get("type") or "").lower()
        if "image" in media_type:
            add(asset.get("url"), "image", asset.get("entityType") or "", asset.get("entityId") or "")

    for food in data.get("foodItems") or []:
        add(food.get("imageUrl"), "image", "food_item", food.get("id") or "")

    for route in data.get("routes") or []:
        add(route.get("coverImageUrl"), "image", "route", route.get("id") or "")

    for audio in data.get("audioGuides") or []:
        if is_playable_audio(audio):
            add(
                audio.get("audioUrl"),
                "audio",
                audio.get("entityType") or "",
                audio.get("entityId") or "",
                audio.get("languageCode") or "",
            )

    return sorted(entries_by_key.values(), key=lambda item: (item["kind"], item["relativePath"]))


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def get_version(data: dict) -> str:
    sync_state = data.get("syncState") or {}
    version = str(sync_state.get("version") or "").strip()
    if version:
        return version
    return f"mobile-seed-{dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d%H%M%S')}"


def create_seed_db(output: Path, envelope: dict, manifest: dict, metadata: dict, language: str) -> None:
    db_path = output / "seed.db"
    if db_path.exists():
        db_path.unlink()

    data = get_data(envelope)
    asset_map = {item["key"]: item["relativePath"] for item in manifest["files"]}

    connection = sqlite3.connect(db_path)
    try:
        cursor = connection.cursor()
        cursor.executescript(
            """
            PRAGMA journal_mode=DELETE;
            CREATE TABLE mobile_metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
            CREATE TABLE bootstrap_cache (
                id TEXT PRIMARY KEY NOT NULL,
                language_code TEXT NOT NULL,
                envelope_json TEXT NOT NULL,
                dataset_version TEXT NOT NULL,
                installation_source TEXT NOT NULL,
                saved_at_utc TEXT NOT NULL
            );
            CREATE TABLE settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            CREATE TABLE categories (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL
            );
            CREATE TABLE pois (
                id TEXT PRIMARY KEY NOT NULL,
                slug TEXT NOT NULL,
                address TEXT NOT NULL,
                lat REAL NOT NULL,
                lng REAL NOT NULL,
                category_id TEXT NOT NULL,
                status TEXT NOT NULL,
                featured INTEGER NOT NULL,
                price_range TEXT NOT NULL,
                trigger_radius REAL NOT NULL,
                priority INTEGER NOT NULL,
                place_tier INTEGER NOT NULL DEFAULT 0,
                average_visit_duration INTEGER NOT NULL,
                popularity_score INTEGER NOT NULL,
                tags_json TEXT NOT NULL
            );
            CREATE TABLE poi_translations (
                poi_id TEXT NOT NULL,
                language_code TEXT NOT NULL,
                title TEXT NOT NULL,
                short_text TEXT NOT NULL,
                full_text TEXT NOT NULL,
                PRIMARY KEY (poi_id, language_code)
            );
            CREATE TABLE tours (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                theme TEXT NOT NULL,
                description TEXT NOT NULL,
                duration_minutes INTEGER NOT NULL,
                cover_image_url TEXT NOT NULL,
                is_active INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE tour_pois (
                tour_id TEXT NOT NULL,
                poi_id TEXT NOT NULL,
                stop_order INTEGER NOT NULL,
                PRIMARY KEY (tour_id, poi_id)
            );
            CREATE TABLE audio_assets (
                id TEXT PRIMARY KEY NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                language_code TEXT NOT NULL,
                audio_url TEXT NOT NULL,
                local_path TEXT NOT NULL,
                content_version TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE media_assets (
                id TEXT PRIMARY KEY NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                media_type TEXT NOT NULL,
                remote_url TEXT NOT NULL,
                local_path TEXT NOT NULL,
                alt_text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            CREATE TABLE sync_logs_queue (
                idempotency_key TEXT PRIMARY KEY NOT NULL,
                event_type TEXT NOT NULL,
                poi_id TEXT NULL,
                language_code TEXT NOT NULL,
                platform TEXT NOT NULL,
                session_id TEXT NOT NULL,
                source TEXT NOT NULL,
                metadata TEXT NULL,
                duration_in_seconds INTEGER NULL,
                occurred_at_utc TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                last_attempt_at_utc TEXT NULL
            );
            CREATE INDEX ix_sync_logs_queue_status ON sync_logs_queue (status, occurred_at_utc);
            """
        )

        version = metadata["version"]
        generated_at = metadata["generatedAtUtc"]
        cursor.executemany(
            "INSERT INTO mobile_metadata (key, value) VALUES (?, ?)",
            [
                ("dataset_version", version),
                ("installation_source", "bundled_seed"),
                ("generated_at_utc", generated_at),
                ("poi_count", str(metadata["poiCount"])),
                ("tour_count", str(metadata["tourCount"])),
                ("audio_count", str(metadata["audioCount"])),
                ("image_count", str(metadata["imageCount"])),
            ],
        )
        cursor.execute(
            """
            INSERT INTO bootstrap_cache (id, language_code, envelope_json, dataset_version, installation_source, saved_at_utc)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            ("current", language, json.dumps(envelope, ensure_ascii=False), version, "bundled_seed", generated_at),
        )

        settings = data.get("settings") or {}
        for key, value in settings.items():
            cursor.execute("INSERT INTO settings (key, value) VALUES (?, ?)", (key, json.dumps(value, ensure_ascii=False)))

        for category in data.get("categories") or []:
            cursor.execute(
                "INSERT OR REPLACE INTO categories (id, name) VALUES (?, ?)",
                (category.get("id") or "", category.get("name") or ""),
            )

        for poi in data.get("pois") or []:
            cursor.execute(
                """
                INSERT OR REPLACE INTO pois (
                    id, slug, address, lat, lng, category_id, status, featured, price_range,
                    trigger_radius, priority, place_tier, average_visit_duration, popularity_score, tags_json
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    poi.get("id") or "",
                    poi.get("slug") or "",
                    poi.get("address") or "",
                    float(poi.get("lat") or 0),
                    float(poi.get("lng") or 0),
                    poi.get("categoryId") or "",
                    poi.get("status") or "",
                    1 if poi.get("featured") else 0,
                    poi.get("priceRange") or "",
                    float(poi.get("triggerRadius") or 0),
                    int(poi.get("priority") or 0),
                    int(poi.get("placeTier") or 0),
                    int(poi.get("averageVisitDuration") or 0),
                    int(poi.get("popularityScore") or 0),
                    json.dumps(poi.get("tags") or [], ensure_ascii=False),
                ),
            )

        for translation in data.get("translations") or []:
            if str(translation.get("entityType") or "").lower() != "poi":
                continue
            cursor.execute(
                """
                INSERT OR REPLACE INTO poi_translations (poi_id, language_code, title, short_text, full_text)
                VALUES (?, ?, ?, ?, ?)
                """,
                (
                    translation.get("entityId") or "",
                    translation.get("languageCode") or "",
                    translation.get("title") or "",
                    translation.get("shortText") or "",
                    translation.get("fullText") or "",
                ),
            )

        for route in data.get("routes") or []:
            route_id = route.get("id") or ""
            cursor.execute(
                """
                INSERT OR REPLACE INTO tours (
                    id, name, theme, description, duration_minutes, cover_image_url, is_active, updated_at_utc
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    route_id,
                    route.get("name") or "",
                    route.get("theme") or "",
                    route.get("description") or "",
                    int(route.get("durationMinutes") or 0),
                    asset_map.get(route.get("coverImageUrl") or "", route.get("coverImageUrl") or ""),
                    1 if route.get("isActive") else 0,
                    route.get("updatedAt") or generated_at,
                ),
            )
            for index, poi_id in enumerate(route.get("stopPoiIds") or [], start=1):
                cursor.execute(
                    "INSERT OR REPLACE INTO tour_pois (tour_id, poi_id, stop_order) VALUES (?, ?, ?)",
                    (route_id, poi_id, index),
                )

        for audio in data.get("audioGuides") or []:
            if not is_playable_audio(audio):
                continue
            remote_url = audio.get("audioUrl") or ""
            cursor.execute(
                """
                INSERT OR REPLACE INTO audio_assets (
                    id, entity_type, entity_id, language_code, audio_url, local_path, content_version, updated_at_utc
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    audio.get("id") or "",
                    audio.get("entityType") or "",
                    audio.get("entityId") or "",
                    audio.get("languageCode") or "",
                    remote_url,
                    asset_map.get(remote_url, ""),
                    audio.get("contentVersion") or "",
                    audio.get("updatedAt") or generated_at,
                ),
            )

        for index, asset in enumerate(data.get("mediaAssets") or []):
            remote_url = asset.get("url") or ""
            cursor.execute(
                """
                INSERT OR REPLACE INTO media_assets (
                    id, entity_type, entity_id, media_type, remote_url, local_path, alt_text, created_at_utc
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    f"media-{index + 1}",
                    asset.get("entityType") or "",
                    asset.get("entityId") or "",
                    asset.get("type") or "",
                    remote_url,
                    asset_map.get(remote_url, ""),
                    asset.get("altText") or "",
                    asset.get("createdAt") or generated_at,
                ),
            )

        connection.commit()
    finally:
        connection.close()


def main() -> int:
    args = parse_args()
    output = Path(args.output)
    safe_reset_output(output)

    envelope = read_bootstrap(args)
    data = get_data(envelope)
    version = get_version(data)
    generated_at = data.get("syncState", {}).get("generatedAt") or utc_now()
    server_changed_at = data.get("syncState", {}).get("lastChangedAt")

    files = collect_assets(data, args, output)
    metadata = {
        "version": version,
        "generatedAtUtc": generated_at,
        "lastUpdatedAtUtc": utc_now(),
        "installationSource": "bundled_seed",
        "serverLastChangedAtUtc": server_changed_at,
        "packageSizeBytes": sum(int(item["sizeBytes"]) for item in files),
        "poiCount": len(data.get("pois") or []),
        "audioCount": len([item for item in files if item["kind"] == "audio"]),
        "imageCount": len([item for item in files if item["kind"] == "image"]),
        "tourCount": len(data.get("routes") or []),
        "languageCount": len((data.get("settings") or {}).get("supportedLanguages") or []),
        "fileCount": len(files),
    }
    manifest = {
        "version": version,
        "generatedAtUtc": generated_at,
        "files": files,
    }

    write_json(output / "bootstrap-envelope.json", envelope)
    write_json(output / "manifest.json", manifest)
    write_json(output / "metadata.json", metadata)
    create_seed_db(output, envelope, manifest, metadata, args.language)

    package_size = metadata["packageSizeBytes"]
    package_size += (output / "bootstrap-envelope.json").stat().st_size
    package_size += (output / "manifest.json").stat().st_size
    package_size += (output / "metadata.json").stat().st_size
    package_size += (output / "seed.db").stat().st_size
    metadata["packageSizeBytes"] = package_size
    write_json(output / "metadata.json", metadata)

    print(
        "[mobile-seed] Generated offline package: "
        f"pois={metadata['poiCount']}, tours={metadata['tourCount']}, "
        f"images={metadata['imageCount']}, audio={metadata['audioCount']}, "
        f"files={metadata['fileCount']}, version={metadata['version']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
