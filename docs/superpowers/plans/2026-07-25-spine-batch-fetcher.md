# Spine Batch Fetcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Python CLI that batches PRTS melee-enemy Spine downloads and valid 158×158 portraits into a project-external staging directory.

**Architecture:** The CLI obtains the 1,657-entry PRTS enemy dataset from its JSON endpoint, excludes every entry whose `attackType` contains `远程`, then reads only candidate detail pages. A standard-library HTTP client resolves model metadata and MediaWiki avatar URLs; a transactional writer creates a unit directory only after all three Spine files and the portrait pass validation.

**Tech Stack:** Python 3 standard library (`argparse`, `dataclasses`, `hashlib`, `html`, `json`, `pathlib`, `re`, `urllib`, `unittest`, `zlib`); no third-party packages.

## Global Constraints

- Create the tool only under `G:\ARKnoNIGHTS_tools\spine-fetcher\`; it must never write into `G:\ARKnoNIGHTS_beta` or `Assets/`.
- The PRTS index endpoint is `https://prts.wiki/index.php?title=敌人一览/数据&action=raw&ctype=application/json`.
- Exclude every candidate with `远程` anywhere in its `attackType`, including `近战 远程`.
- Download `.skel` as `.skel.bytes` and `.atlas` as `.atlas.txt`; do not convert binary skeleton data to JSON.
- A portrait is accepted only when it is structurally valid PNG data with original dimensions exactly `158 × 158`.
- All network input is untrusted. Do not execute downloaded content, upload data, use credentials, or start a background service.
- A batch with zero completed unit directories exits nonzero; a network or candidate failure is always represented in `report.json`.

---

## File Structure

- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\pyproject.toml` — Python package metadata and `spine-fetcher` console entry point.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\README.md` — install-free command examples, source endpoints, output contract, and rollback by deleting the external directory.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\models.py` — immutable candidate, model, result, and report data structures.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\http_client.py` — injected, timeout-bound HTTP GET interface and `urllib` implementation.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\prts.py` — PRTS dataset, page metadata, avatar API, and model-base parsing.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\png.py` — dependency-free PNG structural and dimension validator.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\pipeline.py` — candidate filtering, temporary downloads, hashes, atomic promotion, and reports.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\cli.py` and `__main__.py` — command-line parsing and exit codes.
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\__init__.py`, `test_prts.py`, `test_png.py`, `test_pipeline.py`, and `test_cli.py` — offline `unittest` coverage using scripted HTTP responses and generated PNG bytes.

## Task 1: Establish the package contract and offline fixtures

**Files:**
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\pyproject.toml`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\models.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\test_prts.py`

**Interfaces:**
- Produces `EnemyCandidate(chinese_name: str, detail_title: str, attack_type: str)` and `CanonicalModel(model_base: str, numeric_id: str, english_name: str)`.
- `parse_model_base(model_base: str) -> CanonicalModel` raises `ValueError` for an ambiguous base name.

- [ ] **Step 1: Initialize the isolated external tool repository**

Run:

```powershell
New-Item -ItemType Directory -Force G:\ARKnoNIGHTS_tools\spine-fetcher | Out-Null
git -C G:\ARKnoNIGHTS_tools\spine-fetcher init
New-Item -ItemType Directory -Force G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher, G:\ARKnoNIGHTS_tools\spine-fetcher\tests | Out-Null
New-Item -ItemType File -Force G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\__init__.py, G:\ARKnoNIGHTS_tools\spine-fetcher\tests\__init__.py | Out-Null
```

Expected: a new Git repository exists only at `G:\ARKnoNIGHTS_tools\spine-fetcher`; no path under `G:\ARKnoNIGHTS_beta` changes.

- [ ] **Step 2: Write failing canonical-name tests**

```python
from spine_fetcher.prts import parse_model_base

def test_parse_model_base_omits_numeric_variant():
    parsed = parse_model_base("enemy_1000_gopro_3")
    self.assertEqual((parsed.numeric_id, parsed.english_name, parsed.unit_key),
                     ("1000", "gopro", "1000_gopro"))

def test_parse_model_base_rejects_non_enemy_name():
    with self.assertRaises(ValueError):
        parse_model_base("gopro")
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `py -3 -m unittest tests.test_prts -v` from `G:\ARKnoNIGHTS_tools\spine-fetcher`.

Expected: FAIL because `spine_fetcher.prts` does not exist.

- [ ] **Step 4: Add package metadata and immutable models**

```toml
[project]
name = "spine-fetcher"
version = "0.1.0"
requires-python = ">=3.11"

[project.scripts]
spine-fetcher = "spine_fetcher.cli:main"
```

```python
@dataclass(frozen=True)
class CanonicalModel:
    model_base: str
    numeric_id: str
    english_name: str
    @property
    def unit_key(self) -> str:
        return f"{self.numeric_id}_{self.english_name}"
```

Implement `parse_model_base` with the exact pattern `^enemy_(?P<id>[0-9]+)_(?P<name>[a-z0-9]+(?:_[a-z0-9]+)*?)(?:_(?P<variant>[0-9]+))?$`, rejecting any nonmatch.

- [ ] **Step 5: Run the canonical-name tests**

Run: `py -3 -m unittest tests.test_prts -v`

Expected: PASS; `enemy_1000_gopro_3` produces `1000_gopro` and does not include `_3` in the unit key.

- [ ] **Step 6: Commit the package contract**

```powershell
git -C G:\ARKnoNIGHTS_tools\spine-fetcher add pyproject.toml spine_fetcher\models.py spine_fetcher\prts.py tests\test_prts.py
git -C G:\ARKnoNIGHTS_tools\spine-fetcher commit -m "feat: add spine fetcher model naming"
```

## Task 2: Implement PRTS candidate, model, and portrait resolution

**Files:**
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\http_client.py`
- Modify: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\prts.py`
- Modify: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\test_prts.py`

**Interfaces:**
- Consumes `HttpClient.get(url: str) -> bytes`.
- Produces `PrtsClient.list_melee_candidates() -> list[EnemyCandidate]`, `PrtsClient.resolve_model(candidate) -> ModelSource`, and `PrtsClient.resolve_portrait(candidate) -> str`.
- `ModelSource` contains `prefix_url`, `model_base`, and the detail URL.

- [ ] **Step 1: Write failing data-source and filtering tests**

```python
client = PrtsClient(FakeHttp({INDEX_URL: b'[{"name":"近战","enemyLink":"近战","attackType":"近战"},{"name":"混合","enemyLink":"混合","attackType":"近战 远程"}]'}))
self.assertEqual([item.chinese_name for item in client.list_melee_candidates()], ["近战"])
```

Add a detail-page fixture containing the `敌人模型` JSON object with `prefix` and `skin.默认.战斗.file`, and assert that `enemy_1000_gopro_3` is resolved. Add a MediaWiki API fixture for `文件:头像_敌人_近战.png` and assert that its `imageinfo[0].url` is returned.

- [ ] **Step 2: Run the resolver tests to verify they fail**

Run: `py -3 -m unittest tests.test_prts -v`

Expected: FAIL because `PrtsClient` and `FakeHttp` behaviour are absent.

- [ ] **Step 3: Implement bounded HTTP and PRTS parsing**

Implement `UrllibHttpClient.get` with `urllib.request.Request`, a descriptive `User-Agent`, a 30-second timeout, and `raise_for_status` equivalent for non-2xx status codes. Fetch candidates only from `INDEX_URL`; build detail pages as `https://prts.wiki/w/` plus `urllib.parse.quote(detail_title, safe="")`. Fetch raw wikitext with `?action=raw`, locate the first JSON object after `敌人模型`, decode it with `json.JSONDecoder().raw_decode`, and extract `prefix` plus `skin["默认"]["战斗"]["file"]`. Resolve portraits through:

```text
https://prts.wiki/api.php?action=query&titles=File%3A头像_敌人_<中文名>.png&prop=imageinfo&iiprop=url&format=json
```

Require exactly one image URL. Return structured errors when the data object, default battle skin, or image URL is absent.

- [ ] **Step 4: Run the resolver tests**

Run: `py -3 -m unittest tests.test_prts -v`

Expected: PASS; remote and mixed candidates are absent, while melee candidates resolve model and portrait sources without a browser.

- [ ] **Step 5: Commit the PRTS client**

```powershell
git -C G:\ARKnoNIGHTS_tools\spine-fetcher add spine_fetcher\http_client.py spine_fetcher\prts.py tests\test_prts.py
git -C G:\ARKnoNIGHTS_tools\spine-fetcher commit -m "feat: resolve PRTS enemy assets"
```

## Task 3: Validate PNGs and atomically materialize complete units

**Files:**
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\png.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\pipeline.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\test_png.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\test_pipeline.py`

**Interfaces:**
- Produces `read_png_dimensions(data: bytes) -> tuple[int, int]` and `validate_avatar_png(data: bytes) -> None`.
- Produces `BatchPipeline.run(output_root: Path) -> BatchReport` and creates only `<output_root>/staging/<unit_key>/` for complete units.

- [ ] **Step 1: Write failing validation and transaction tests**

```python
self.assertEqual(read_png_dimensions(make_png(158, 158)), (158, 158))
with self.assertRaises(ValueError):
    validate_avatar_png(make_png(157, 158))

report = pipeline.run(tmp_path)
self.assertEqual(report.completed, 0)
self.assertEqual((tmp_path / "invalid-portraits.zh-Hans.txt").read_text("utf-8"), "源石虫\n")
self.assertFalse((tmp_path / "staging" / "1007_slime").exists())
```

Add a successful case that asserts all four renamed files, `manifest.json`, SHA-256 fields, and no temporary directory remain. Add a duplicate-key case that asserts no existing directory is overwritten.

- [ ] **Step 2: Run the validation and transaction tests to verify they fail**

Run: `py -3 -m unittest tests.test_png tests.test_pipeline -v`

Expected: FAIL because PNG validation and `BatchPipeline` do not exist.

- [ ] **Step 3: Implement binary validation and staging promotion**

Validate PNG signature, chunk lengths, CRCs, one IHDR chunk, IEND, zlib-decompressible concatenated IDAT data, and width/height before accepting the portrait. For each non-remote candidate, download `<prefix><model_base>.skel`, `.atlas`, and `.png`, then the portrait. Write into `<output_root>/.tmp/<uuid>/`; save model files as `<model_base>.skel.bytes`, `<model_base>.atlas.txt`, and `<model_base>.png`, save the portrait as `UIImage_<unit_key>.png`, write a deterministic UTF-8 `manifest.json`, then use `Path.replace` to promote the directory only if `<output_root>/staging/<unit_key>` does not exist. Hash every final file with SHA-256.

Write deduplicated, sorted Chinese names to `invalid-portraits.zh-Hans.txt`; write every candidate outcome to pretty UTF-8 `report.json`. Remove each temporary directory in `finally`.

- [ ] **Step 4: Run the validation and transaction tests**

Run: `py -3 -m unittest tests.test_png tests.test_pipeline -v`

Expected: PASS; no failed avatar produces a partial staged unit, and accepted files use `.skel.bytes`, `.atlas.txt`, and `UIImage_<unit_key>.png` names.

- [ ] **Step 5: Commit the pipeline**

```powershell
git -C G:\ARKnoNIGHTS_tools\spine-fetcher add spine_fetcher\png.py spine_fetcher\pipeline.py tests\test_png.py tests\test_pipeline.py
git -C G:\ARKnoNIGHTS_tools\spine-fetcher commit -m "feat: stage validated spine units"
```

## Task 4: Expose the CLI, document it, and smoke-test one network candidate

**Files:**
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\cli.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\spine_fetcher\__main__.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\tests\test_cli.py`
- Create: `G:\ARKnoNIGHTS_tools\spine-fetcher\README.md`

**Interfaces:**
- Produces `main(argv: Sequence[str] | None = None) -> int`.
- Command: `py -3 -m spine_fetcher --output <external-output> [--limit <positive-int>]`.

- [ ] **Step 1: Write failing CLI tests**

```python
self.assertEqual(main(["--output", str(tmp_path), "--limit", "1"]), 0)
self.assertEqual(main(["--output", str(tmp_path), "--limit", "0"]), 2)
self.assertEqual(main(["--output", str(tmp_path)]), 1)  # fake pipeline completes zero units
```

Inject a factory into `main` so the test never performs network I/O.

- [ ] **Step 2: Run the CLI tests to verify they fail**

Run: `py -3 -m unittest tests.test_cli -v`

Expected: FAIL because the CLI entry point does not exist.

- [ ] **Step 3: Implement argument handling and operational documentation**

Accept a required `--output` path and optional positive `--limit`; reject zero, negatives, and non-integers with `argparse` exit code 2. Exit 0 only if at least one candidate completes, otherwise exit 1 after writing reports. Document these exact commands:

```powershell
py -3 -m unittest discover -s tests -v
py -3 -m spine_fetcher --output G:\ARKnoNIGHTS_tools\spine-fetcher-output --limit 1
```

Document the PRTS JSON endpoint, raw detail-page and MediaWiki avatar API use, the no-`Assets` boundary, the four-file staging contract, the invalid-portrait list, rate/timeout behaviour, and deletion of the external output directory as rollback.

- [ ] **Step 4: Run all offline tests and one bounded smoke test**

Run: `py -3 -m unittest discover -s tests -v`

Expected: every test passes.

Run: `py -3 -m spine_fetcher --output G:\ARKnoNIGHTS_tools\spine-fetcher-output --limit 1`

Expected: exit 0, parseable `report.json`, and either one complete staged unit or a documented nonzero source/asset failure; do not represent a zero-completion run as success.

- [ ] **Step 5: Commit the runnable CLI**

```powershell
git -C G:\ARKnoNIGHTS_tools\spine-fetcher add README.md spine_fetcher\cli.py spine_fetcher\__main__.py tests\test_cli.py
git -C G:\ARKnoNIGHTS_tools\spine-fetcher commit -m "feat: add spine fetcher cli"
```
