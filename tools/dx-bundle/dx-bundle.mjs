#!/usr/bin/env node
// dx-bundle — zip a location bundle folder and push it to a DXSuite instance.
//
//   node dx-bundle.mjs push --dir ./my-bundle --url https://dxsuite.example --key <admin-api-key> [--watch]
//
// Validation mirrors the server-side importer so most rejections surface locally
// before any upload happens. --watch re-pushes on file changes, auto-bumping a
// -dev.N prerelease suffix each time because uploaded versions are immutable.

import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import archiver from 'archiver';
import chokidar from 'chokidar';

const ID_PATTERN = /^[a-z0-9][a-z0-9\-_]{1,63}$/;
const VERSION_PATTERN = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$/;

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--watch') args.watch = true;
    else if (a.startsWith('--')) args[a.slice(2)] = argv[++i];
    else args._.push(a);
  }
  return args;
}

function loadManifest(dir) {
  const manifestPath = path.join(dir, 'manifest.json');
  if (!fs.existsSync(manifestPath)) fail(`no manifest.json in ${dir}`);

  let manifest;
  try {
    manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  } catch (ex) {
    fail(`manifest.json is not valid JSON: ${ex.message}`);
  }

  if (!ID_PATTERN.test(manifest.id ?? '')) fail('manifest "id" must match ' + ID_PATTERN);
  if (!manifest.name) fail('manifest "name" is required');
  if (!VERSION_PATTERN.test(manifest.version ?? '')) fail('manifest "version" must be semver (e.g. 1.2.0)');
  if (!manifest.entry) fail('manifest "entry" is required (e.g. index.html)');
  if (!fs.existsSync(path.join(dir, manifest.entry))) fail(`entry file "${manifest.entry}" not found in ${dir}`);

  return manifest;
}

async function zipBundle(dir, manifestOverride) {
  const zipPath = path.join(os.tmpdir(), `dx-bundle-${process.pid}-${Math.random().toString(36).slice(2)}.zip`);
  const output = fs.createWriteStream(zipPath);
  const archive = archiver('zip', { zlib: { level: 9 } });

  const done = new Promise((resolve, reject) => {
    output.on('close', resolve);
    archive.on('error', reject);
  });

  archive.pipe(output);
  archive.glob('**/*', {
    cwd: dir,
    ignore: ['manifest.json', 'node_modules/**', '.git/**'],
    dot: false
  });
  archive.append(JSON.stringify(manifestOverride, null, 2), { name: 'manifest.json' });
  await archive.finalize();
  await done;

  return zipPath;
}

async function pushOnce(dir, url, key, manifest) {
  const zipPath = await zipBundle(dir, manifest);
  try {
    const form = new FormData();
    form.append('bundle', new Blob([fs.readFileSync(zipPath)], { type: 'application/zip' }),
      `${manifest.id}-${manifest.version}.zip`);

    const response = await fetch(new URL('/api/location-bundles/import', url), {
      method: 'POST',
      headers: { 'X-API-KEY': key },
      body: form
    });

    const text = await response.text();
    if (!response.ok) {
      console.error(`push failed (${response.status}): ${text}`);
      return false;
    }

    const result = JSON.parse(text);
    console.log(`pushed ${result.bundleKey} v${result.version} (${result.action})`);
    for (const warning of result.warnings ?? []) console.warn(`  warning: ${warning}`);
    return true;
  } finally {
    fs.rmSync(zipPath, { force: true });
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const command = args._[0];

  if (command !== 'push') {
    console.log('usage: dx-bundle push --dir <bundle-folder> --url <instance-url> --key <admin-api-key> [--watch]');
    process.exit(command ? 1 : 0);
  }

  const dir = path.resolve(args.dir ?? '.');
  const url = args.url ?? fail('--url is required');
  const key = args.key ?? process.env.DX_API_KEY ?? fail('--key (or DX_API_KEY) is required');

  if (!args.watch) {
    const manifest = loadManifest(dir);
    const ok = await pushOnce(dir, url, key, manifest);
    process.exit(ok ? 0 : 1);
  }

  // Watch mode: each push must be a new immutable version, so a -dev.N prerelease
  // suffix is bumped on every change.
  let devCounter = 0;
  let pushing = false;
  let pending = false;

  const pushDev = async () => {
    if (pushing) { pending = true; return; }
    pushing = true;
    try {
      const manifest = loadManifest(dir);
      devCounter++;
      const base = manifest.version.split('-')[0];
      manifest.version = `${base}-dev.${devCounter}`;
      await pushOnce(dir, url, key, manifest);
    } catch (ex) {
      console.error(`watch push failed: ${ex.message}`);
    } finally {
      pushing = false;
      if (pending) { pending = false; void pushDev(); }
    }
  };

  console.log(`watching ${dir} — pushing on change (Ctrl+C to stop)`);
  let timer = null;
  chokidar.watch(dir, { ignored: /node_modules|\.git/, ignoreInitial: true }).on('all', () => {
    clearTimeout(timer);
    timer = setTimeout(() => void pushDev(), 1500);
  });

  await pushDev();
}

main().catch((ex) => fail(ex.message));
