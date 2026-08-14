---
name: design-conventions
description: Design guidance for this project — color tokens, typography, grid and layout, imagery, voice and writing conventions, and the LESS/Grunt styles workflow. Use when working on UI, styling, layout, widgets or components with a visual surface, writing user-facing copy, or answering design questions.
---

# Design conventions

## Design direction

Warm Artisan Craft meets Organic Simplicity — earthy browns, clean sans-serif type, botanical line-art. Feels like a specialty-coffee menu on quality stock: restrained, warm, confident. Flat chrome; full-bleed photography carries each page. Tone: human, approachable, never corporate.

## Colors

Source of truth: `wwwroot/Content/Styles/variables.less`. Use `@color-<name>` variables — never hard-code values. Overlays use `rgba()`; no CSS custom properties or `color-mix()`.

| Variable | Role |
| --- | --- |
| `@color-black` | Body ink, deep espresso surfaces |
| `@color-darkbrown` | Header bg, primary buttons, dark overlays |
| `@color-brown` | Headings (`h1`–`h3`), footer text |
| `@color-lightbrown` | Links, store category bar, Our Story bg |
| `@color-lightestbrown` / `@color-lightgold` | Cappuccino section bg / headings |
| `@color-brownbg` / `@color-darkbrownbg` | Parchment backgrounds |
| `@color-gold` | Search-box background |
| `@color-red` | Discount badge, sale price, `.product-tile--red` |
| `@color-error` / `@color-error-soft` | Form validation errors / required-field marker, error labels |
| `@color-sale` | On-sale banner, checkout header, cart sale accents |
| `@color-focus` | Input focus outline, active product-filter state |
| `@color-border` | Default input/box borders |
| `@color-green-100` | Form-success background **only** |
| `@input-bg` / `@input-bg-disabled` | Form input backgrounds |

- **Pricing is red**: original price struck through, active price/badge in `@color-red`. Never green.
- A few one-off decorative hexes remain hard-coded in place (landing-page hero copy in `Landing-page.less`, product-tile grays in `Products.less`, category card in `Site.less`) — reuse in place or promote to a `@color-<purpose>` variable.
- Known inconsistencies, kept as-is deliberately: three error/sale reds besides `@color-red` (`@color-error`, `@color-error-soft`, `@color-sale`) and the off-palette teal `@color-focus`. Don't spread them to new contexts; pick the variable matching the purpose.
- Gray/blue ramps and social colors in `variables.less` are inherited admin/icon defaults; prefer the nearest `@color-gray-*` over new grays. Missing a color? Add a descriptive `@color-<purpose>` variable.

## Voice & writing

- Short, sharp, human: contractions, plain language, address the reader as "you", lead with concrete value — no buzzwords (robust, synergy, cutting-edge, end-to-end; "use" not "leverage").
- Marketing headlines Title Case; body and subheadings sentence case; product names capitalized (Xperience by Kentico — never "XbyK", Content Hub, Page Builder).
- Oxford comma; dates as "October 6, 2025"; em dash for breaks, en dash for ranges, hyphen for compound adjectives.
- One clear CTA per message, strong direct verbs ("Book a demo"); standalone CTAs may be ALL CAPS; bold for emphasis only — no underlining or ALL CAPS in sentences.
- Prefer: hybrid headless, plugin, microsite, third-party, nonprofit (not pure headless, plug-in, mini-site, 3rd party, non-profit).

## Typography

- Self-hosted families only (`wwwroot/Content/Fonts/`, declared in `Fonts.less`) — **never link font CDNs**: GT-Wallsheim (site body & headings), Source Sans Pro (landing-page body, lead paragraphs), PT Serif (landing `h1`), Core-icons (icon glyphs).
- Type scale lives in `Grid.less` on `@base-unit`, in `rem` — read it before changing type; don't invent new heading levels. `h1`–`h3` use `@color-brown`; body `@color-black`.
- Links: `@color-lightbrown`, underlined; no underline on hover/focus (in headings: underline only on hover).
- The `@font-family-*` variables in `variables.less` are unused admin defaults.

## Layout

- Mobile-first 12-column float grid (`Grid.less`): `.col-{xs,sm,md,lg,xl}-*`, fixed-width `.container` per breakpoint, negative-margin `.row` gutters. Breakpoints are literal `@media` queries in `Grid.less` — reuse those values.
- No spacing/radius tokens — author spacing in `rem` as multiples of `@base-unit`.
- Page structure: `body` (display: table) → `.page-wrap` → dark header (`@color-darkbrown`) + `.page-container-inner` (textured bg) → `.footer-wrapper`. `body.inverted` = clean canvas variant; `.inverted.no-bg` removes decoration.

## Imagery

- Decorative assets in `wwwroot/Content/Images/`, referenced relatively from `.less` (`../Images/`).
- Photography: authentic, candid people in real settings; natural light, soft warm tones. Avoid forced smiles, white studio backgrounds, corporate poses, clipart, heavy filters.

## Accessibility

No project-specific rules documented yet — keep sufficient contrast, semantic HTML, and keyboard operability (menus, forms, checkout).

## Styles workflow

- LESS compiled by Grunt (`npx grunt less`) into served `Site.css` / `Landing-page.css` — edit `.less` only, recompile after every change.
- Entry points: `Site.less` (main site) and `Landing-page.less` (landing pages); new files must be `@import`ed from an entry point. Cascade order = `@import` order (no Tailwind, no `@layer`).
- Layouts link the compiled CSS: `_DancingGoatLayout.cshtml` → `Site.css`; `_LandingPageLayout.cshtml` → `Landing-page.css`.
