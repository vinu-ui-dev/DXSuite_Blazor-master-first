# DXSuite Help documentation source

This folder holds the **markdown source** for the in-application help site. It is compiled by
[MkDocs](https://www.mkdocs.org/) with the [Material](https://squidfunk.github.io/mkdocs-material/)
theme into static HTML at `../src/DXSuite.Host/wwwroot/docs`, which the Host serves at `/docs`.

## Prerequisites

- Python 3.10+ (`py --version`)
- The pinned MkDocs packages (installed automatically by `build-docs.ps1`, or manually):
  ```powershell
  py -m pip install -r requirements.txt
  ```

## Authoring workflow

1. Edit or add markdown under `docs-src/`. Register new pages in the `nav:` block of the
   repo-root `mkdocs.yml`.
2. Preview with live reload while writing:
   ```powershell
   ./build-docs.ps1 -Serve
   ```
   (opens http://127.0.0.1:8000)
3. Produce the served output:
   ```powershell
   ./build-docs.ps1
   ```
   This writes straight into `src/DXSuite.Host/wwwroot/docs` (via `site_dir` in `mkdocs.yml`).
4. Commit **both** your markdown changes and the regenerated `wwwroot/docs` HTML. The compiled
   output is committed intentionally so the site ships without a build step in CI.

## Updating docs on a deployed (customer) server — no rebuild required

The Host serves `wwwroot/docs` as **loose static files from disk at runtime** (via
`UseStaticFiles`, ahead of authentication). So documentation can be updated in place without
recompiling or restarting the application:

1. Build locally: `./build-docs.ps1`
2. Copy the entire `src/DXSuite.Host/wwwroot/docs` folder to the deployed server's
   `wwwroot/docs` (replacing its contents).
3. The new pages are live on the next page load. No `dotnet` build, no app restart, no IIS recycle.

## Conventions

- `use_directory_urls: false` produces flat `page.html` files so links resolve correctly under
  the `/docs` sub-path. Do not change this.
- Brand colors live in `docs-src/assets/extra.css` (mirrors `--dx-blue` from the app design system).
- The theme override `docs-src/overrides/main.html` adds the "Back to DXSuite" banner link.

## Linking help from the app

The `<HelpLink>` component (`DXSuite.Shared/Components/HelpLink.razor`) opens a page here. Pass the
page path without extension, relative to the docs root — e.g. `<HelpLink Page="director/overrides" />`
opens `/docs/director/overrides.html`. The base URL is configurable via `Help:BaseUrl` in
`appsettings.json` (defaults to `/docs`).
