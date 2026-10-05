# 13 – Fila UI, dark theme

Every page today is the default ASP.NET MVC template: plain Bootstrap, top navbar, light background. The user opens the app (Web and the Identity login) and sees the Fila admin layout in dark mode: sidebar, header, cards, tables, badges and forms taken from `fila_samples/`.

## What Fila is (from `fila_samples/documentation/index.html`)

- Bootstrap 5 admin template, no jQuery dependency. `assets/css/style.css` already contains Bootstrap, so it **replaces** `lib/bootstrap/dist/css/bootstrap.min.css`; JS still uses `assets/js/bootstrap.bundle.min.js`.
- CSS loaded in `<head>`: `sidebar-menu.css`, `simplebar.css`, `remixicon.css`, `style.css` (the rest — `prism`, `quill`, `swiper`, `jsvectormap` — only if a page uses them).
- JS before `</body>`: `bootstrap.bundle.min.js`, `sidebar-menu.js`, `simplebar.min.js`, `custom/custom.js` (plus `apexcharts.min.js` / `data-table.js` only where needed).
- Icons: RemixIcon (`ri-*`, local font in `assets/fonts`) and Material Symbols (`<span class="material-symbols-outlined">name</span>`, loaded from `fonts.gstatic.com`). Font: Outfit from Google Fonts.
- Dark mode: `<body data-theme="dark">`. `style.css` has all the `[data-theme=dark]` rules. `custom.js` toggles it with the `#switch-toggle` button and saves it in `localStorage["fila_theme"]`. Note: the sidebar-only and header-only toggles write to the **same** key, so don't include those two toggles.
- Colors come from `assets/scss/_variables.scss` (`$primary: #0f79f3`, …). Changing them needs `sass ./assets/scss/style.scss ./assets/css/style.css`; we use the compiled `style.css` as is.

### Reference pages

| Need | Fila page to copy from |
|---|---|
| Layout: `.sidebar-area` + `aside.layout-menu`, `.container-fluid > .main-content`, `header.header-area`, `.main-content-container`, `footer.footer-area` | `starter.html`, `blank-page.html` |
| Login / register / logged out | `sign-in.html`, `sign-up.html`, `logout.html` |
| Tables (items catalog, exports, export rows) | `basic-table.html`, `data-table.html`, `products-list.html` |
| Paging | `paginations.html` |
| Status badges (`Pending` / `Processing` / `Done` / `Failed`) | `basic-elements.html`, `orders.html` |
| Cards / stat cards (counts on the export header) | `cards.html`, `widgets.html` |
| Forms, buttons, checkboxes, validation | `basic-elements.html`, `buttons.html`, `validation.html` |
| Alerts / empty states / errors | `alerts.html`, `404-error-page.html`, `internal-error.html` |
| Profile | `my-profile.html`, `account-settings.html` |

## Questions to resolve first

1. Where do the Fila assets live? Copy only the files we use into `src/SteamItems.Web/wwwroot/fila/` and `src/SteamItems.Identity/wwwroot/fila/` (duplicated), or one shared place (e.g. a Razor Class Library / linked folder) both apps serve from?
   → Answer: one shared Razor Class Library (`src/SteamItems.UI`) holding the Fila assets under `wwwroot/fila/`; Web and Identity reference it and serve them from `/_content/SteamItems.UI/fila/...`.
2. Dark only, or dark by default with the light/dark toggle in the header kept (`custom.js` + `localStorage`)? If dark only: hardcode `data-theme="dark"` on `<body>` and drop the toggle.
   → Answer: dark only. `data-theme="dark"` hardcoded on `<body>`, no toggle, `custom.js` theme code not relied on.
3. Is `fila_samples/` committed to the repo long-term, or removed after the assets are copied? (Its own `.gitignore` and `package.json` suggest it was meant as a separate project.)
   → Answer: keep `fila_samples/` in the repo as the reference for later frontend tasks.
4. jQuery: Fila does not need it, but `jquery-validation-unobtrusive` (client-side form validation) does. Keep jQuery only on form pages, or drop client-side validation and rely on server-side?
   → Answer: keep jQuery + unobtrusive validation, loaded only on form pages (`_ValidationScriptsPartial`).
5. Sidebar menu: which entries (Home, Items, Exports, Profile, Swagger link?), and does the logged-in user + Log out go in the header dropdown (Fila's profile dropdown) or the sidebar?
   → Answer: sidebar = Home, Items, Exports, Profile, Swagger (API). User name, Profile and Log out in Fila's header profile dropdown.
6. Preloader (the animated "FILA" letters): remove, or replace the letters with the app name?
   → Answer: keep the preloader, letters replaced with the app name.
7. Do we pull in `data-table.js` for the tables, or keep the current server-side paging/filter from tasks 05 and 12 and only restyle?
   → Answer: no `data-table.js`; keep server-side paging/filter, restyle with Fila table + pagination markup.
8. Delete the old `site.css`, `_Layout.cshtml.css` and `wwwroot/lib/bootstrap` once nothing references them?
   → Answer: delete them once unreferenced (keep `lib/jquery*` for validation).

## Scope

- Fila assets (CSS, JS, fonts, the few images used, e.g. favicon/logo) added to the app(s) per question 1. Don't copy the 195 demo pages or unused vendor libs.
- Web `_Layout.cshtml` rebuilt on the Fila structure: sidebar with the app's menu, header with user dropdown and Log out, content container, footer. Active menu item highlighted.
- Identity `_Layout.cshtml` + `Login`, `Register`, `LoggedOut`, `Error` on Fila's auth pages (`sign-in.html` / `sign-up.html` / `logout.html`).
- Web pages refactored to Fila components, behaviour unchanged:
  - `Home/Index`, `Home/Privacy`, `Home/Profile`
  - `Items/Index` (catalog + selection + export button)
  - `Exports/Index` (status table, live SignalR updates from task 11 still work)
  - `Exports/Details` (header cards with counts, rows table, filter, paging from task 12)
  - `Shared/Error`
- Status values shown as Fila badges, the same colour per status everywhere.
- Dark theme applied on every page, Web and Identity, per question 2.
- Remove the old styling the new layout no longer uses (per question 8).

## Acceptance criteria

- [ ] Every page in Web and Identity renders with the Fila layout in dark mode on first visit (no light flash, no `localStorage` needed).
- [ ] No page still loads `lib/bootstrap/dist/css/bootstrap.min.css` or `site.css` styles that fight Fila's.
- [ ] Login → Items → select → export → Exports → Details works end to end exactly as before (task 12 acceptance still passes).
- [ ] `/Exports` status updates still arrive live (SignalR), and the badges change colour with the status.
- [ ] Form validation errors (login, register) show in Fila's form style.
- [ ] Sidebar collapses to the burger menu on a phone width (≤ 767px) and every page is usable there.
- [ ] Icons render (RemixIcon and Material Symbols), no 404s for CSS/JS/fonts in the browser console.
- [ ] Existing tests still pass (`dotnet test`).

## Implementation notes

- Assets: `src/SteamItems.UI/wwwroot/fila/` (served at `/_content/SteamItems.UI/fila/...`). Shared partials in `SteamItems.UI/Views/Shared`: `_FilaHead` (CSS + favicon), `_FilaPreloader`, `_FilaScripts`. Only the woff2/woff/ttf RemixIcon fonts are copied.
- `custom/custom.js` is **not** loaded: it also wires the demo widgets and throws when Swiper/Quill/etc. are missing. `fila/js/app.js` keeps the parts we use (preloader, sticky header, sidebar menu, burger buttons, tooltips, password show/hide).
- `fila/css/app.css` holds the few overrides on top of the untouched `style.css`: dark preloader (Fila's is white in both themes), dark alerts (Fila has no `[data-theme=dark]` alert rules), ASP.NET validation classes in Fila's invalid style, the longer app name in the logo/preloader.
- Fila bug worked around in `app.js`: `Menu.manageScroll` (sidebar-menu.js) throws on every window resize below the breakpoint (it expects PerfectScrollbar, which Fila doesn't ship).
- Status badges: Fila's soft badge (`default-badge text-X bg-X bg-opacity-10`), colours in `ExportStatusExtensions.BadgeClass` / `FileItemStatusExtensions.BadgeClass` (Pending secondary, Processing primary, Completed / Processed success, Completed with errors warning, Failed danger). The class string is also what SignalR sends to `/Exports`.

## Rule for later tasks

From this task on, any task with frontend work builds its pages from Fila components (see the reference pages table above) in the dark theme. No plain Bootstrap / default template markup.
