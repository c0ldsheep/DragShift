# Security

DragShift is a desktop app for Mac and Windows plus a one-page website. The apps work on files you choose, on your own
computer: no accounts, no servers and no network code. The website is static (GitHub Pages): no cookies, no
trackers and no forms, and after it loads it makes no requests at all.

## Reporting a problem

Please use GitHub's private vulnerability reporting on this repository (the **Security** tab, then **Report a
vulnerability**), not a public issue.

## Audit, 2026-11-09

The audit covered the whole repository: the website (`site/`), the Mac app (`Sources/`), the Windows app
(`windows/src`), the build and publish workflows (`.github/`) and the local test server (`scripts/serve-site.py`).

**Method:**
- a static scan of the code
- a read-only probe of the local server, and of an existing GitHub Pages site to see what production sends
- reading the code for each check
- live tests of the risky paths: booby-trapped zip files, PDF locking, missing files, hidden and slow visits

**Grades:** PASS, FIXED (fixed in this audit, with a regression test), N/A (no such feature), ASK (needs a decision
or money).

| # | Check | Grade | Evidence · fix |
|---|---|---|---|
| 1 | Secrets in the frontend | PASS | No backend and no keys. The scan found no key formats in the site. |
| 2 | Secrets in git | PASS | The scan and `git log -p` found none. `.gitignore` keeps out builds and private notes (`HANDOFF*.md`, old previews). The probe found no public secret files. |
| 3 | Missing authentication | N/A | No accounts and no API. |
| 4 | Missing authorization | N/A | No roles. |
| 5 | Open database rules | N/A | No database. |
| 6 | SQL / NoSQL injection | N/A | No database. |
| 7 | Command and code injection | PASS | **Mac:** tools run with an argument list and never a shell (`Naming.swift` `Shell.run`); tools come only from `/opt/homebrew/bin` and `/usr/local/bin` (`FileKind.swift` `Tools.find`); paths are absolute. **Windows:** it only starts Explorer with its own output path (`Util.cs:88`). **Site:** no `eval` or `new Function` (`page.test.js`). |
| 8 | Mass assignment, unvalidated input | N/A | No API. For the Windows hand-off, see #34. |
| 9 | Password storage | N/A | No accounts. PDF passwords are typed into a secure field, used once in memory, and never saved or logged (`SmallEditors.swift` `PasswordSheet`). Lock PDF uses AES-128, tested (PDF `/V 4 /R 4 /AESV2`), the strongest PDFKit writes. |
| 10 | Brute force | N/A | Nothing to log in to. |
| 11 | Security headers | FIXED · ASK | **FIXED:** the local server now sends a CSP with `frame-ancestors 'none'`, `nosniff`, `X-Frame-Options`, `Referrer-Policy` and `Permissions-Policy` (`serve.test.js`). The page itself carries `default-src 'none'`, `no-referrer` and a frame-buster (`page.test.js`). **ASK:** GitHub Pages can't send custom headers, so `nosniff` and `frame-ancestors` would need a custom domain behind a proxy such as Cloudflare. |
| 12 | Insecure cookies | N/A | No cookies. The page never reads or writes `document.cookie` (`page.test.js`). |
| 13 | Verbose errors | FIXED | The local server no longer shows its Python version or folder listings (probe 11/11, `serve.test.js`). GitHub Pages hides both. |
| 14 | Vulnerable or fake dependencies | FIXED | The site and the Mac app have **no** third-party packages. Windows has one Microsoft package, now locked by content hash (`packages.lock.json`, `RestoreLockedMode` in CI). Every GitHub Action is pinned to a commit, not a tag that can move (`page.test.js`). |
| 15 | Open redirects | PASS | No redirects built from input. |
| 16 | Exposed admin panels | N/A | None. |
| 17 | HTTPS everywhere | PASS | GitHub Pages sends HSTS and redirects http to https (probe of an existing Pages site). The local test server is http on the Wi-Fi only. |
| 18 | Backups | ASK | Until it's pushed, the only copy of the code is on one Mac. |
| 19 | XSS | PASS | HTML is written from strings only from fixed icons and numbers (`main.js` meter icon, `ring.js`). Nothing from the address bar, the referrer, the window name or other windows reaches the page; the address bar gives only clamped numbers and one fixed word. The CSP allows only this site's scripts. Guarded by `page.test.js` "nothing a visitor or another site controls…". |
| 20 | CSRF | N/A | No state-changing requests. `form-action 'none'`. |
| 21 | Insecure file uploads | N/A | The site never uploads. The apps open files you choose. |
| 22 | Path traversal | PASS · FIXED | **Unzip:** Windows checks every entry stays in the folder (`Engine.cs` `Unzip`). Mac `ditto` was tested live with `../`, absolute-path and symlink entries, and all stayed inside. **Rename:** refuses `/`, `:` and leading dots (`Rename.swift`). **FIXED:** the local server serves only `index.html` and `assets/` (including `../` and `%2e%2e` tries, `serve.test.js`). |
| 23 | SSRF | N/A | No server; neither app has network code. |
| 24 | Password reset | N/A | No accounts. |
| 25 | Session management | N/A | No sessions. |
| 26 | JWT | N/A | None. |
| 27 | Permissive CORS | PASS | The local server sends no CORS. GitHub Pages sends `*` on public static files, which is fine: nothing private. |
| 28 | Rate limits | N/A | No endpoints and nothing that costs money. |
| 29 | Exposed test environments | FIXED | The Wi-Fi test server (`--lan`) used to list folders and serve tests, tools and notes to anyone on the same Wi-Fi. Now it serves only the public files. Stop it after testing. |
| 30 | Default credentials | N/A | None. |
| 31 | Webhooks | N/A | None. |
| 32 | Payment checks | N/A | Free. |
| 33 | IDOR / BOLA | N/A | No records. |
| 34 | Trusting user-controlled input | FIXED | The Windows hand-off between two copies of the app (`WM_COPYDATA`) now needs DragShift's marker, a size under 4 MB, and at most 1,000 paths that exist (`AppMain.cs` `Receiver`). The site's test switches work only with `?test` / `?rec` and are clamped. |
| 35 | Sensitive data in logs | PASS | The Mac log records action names, counts and failures, never passwords or clipboard contents (`App.swift:124` logs only item count and types). The Windows keyboard hook reads only Shift and Escape and logs nothing typed (`DragTracker.cs` `OnKey`). |
| 36 | Exposed source maps | PASS | None (probe). |
| 37 | Prompt injection | N/A | No AI features. |
| 38 | Unsafe AI output | N/A | No AI features. |
| 39 | AI cost abuse | N/A | No AI features. |
| 40 | Data sent to AI providers | N/A | Text reading and translation run on the device; nothing is sent. |

## App-specific notes

- **Mac:** hardened runtime, with no entitlements, no URL scheme and no network code.
- **Unlock (Windows):** the password typed into the Unlock window is used once, in memory, to open the PDF, and is never saved or logged. It is never put in a file name or a message either, and the box is cleared when the window closes (`PasswordForm.cs`, `PdfRewrite.cs`; the self-test checks that a wrong password is never repeated back).
- **Zip bombs (fixed 2026-11-10):** a small zip that unpacks to fill the disk.
  - Mac: refused before unpacking when its listed size won't fit (keeping 1 GB free) or passes 20 GB, or it holds
    over 100,000 files.
  - Windows: counts the bytes as it writes, because listed sizes can lie, and stops and deletes the half-made folder.
  - Tests: `BombTests` on the Mac. The Windows self-test runs an oversized zip and a zip whose names try to leave
    the folder (`../`).
- **Picture bombs (fixed 2026-11-10):** a valid 48 KB PNG can be 20,000 × 20,000 pixels, which needs 1.6 GB once
  decoded. Pictures over 250 megapixels are refused with a plain message, before any pixels are decoded: on the Mac
  (`ImageOps.maxPixels`, tested with that very file) and on Windows (`Images.MaxPixels`, in the self-test). macOS
  itself already rejects files whose header lies about the size (tested).
- **PDF pages with broken or giant sizes (fixed 2026-11-12):** a PDF can claim any page size in a few bytes. A page
  whose size isn't a real number is skipped, and one that would need more than 250 megapixels at the chosen
  sharpness is refused with a plain message before any memory is asked for (Mac: `PDFOps.pixels`, tested with a
  200 × 200 inch page; Windows: `Images.RequireSafeSize`).
- **Nothing half-made (2026-11-12):** unzip builds its folder under a hidden name and gives it the real name in one
  move, and Cancel stops the encoder or tool and removes what it was writing (tested on both systems).
- **Text reading on any Mac:** when macOS's accurate text recognizer isn't available (seen running as Intel under
  Rosetta), the fast one is used instead of showing a raw system error.
- **Watch folders (Mac):** these convert new files automatically using Apple's own image and PDF readers, the same
  ones Finder previews use.
- **Downloads aren't signed yet.** Mac builds are ad-hoc signed until the Developer ID account is ready; the Windows
  .exe is unsigned. See "Still open" below.

## Still open

1. Sign and notarize the Mac app (Developer ID), and sign the Windows .exe.
2. Publish a SHA-256 checksum for each download, plus GitHub build attestations, so anyone can check that a download
   is the real one.
3. Push the code to GitHub (backup, #18).
4. Optional: a custom domain behind Cloudflare for real response headers (#11).
