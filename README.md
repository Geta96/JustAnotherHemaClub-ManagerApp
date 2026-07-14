# JAHC Manager

**JAHC Manager** is the official mobile companion app for **Just Another HEMA Club**, a Historical European Martial Arts club in Budapest. It helps instructors run trainings, 1-on-1 lessons, prices, finances and in-club tournaments, and helps fencers track attendance and dues.

> Built with **.NET MAUI** for Android (API 24+).

---

## ✨ Features

- **🏠 Home** — Club intro, weekly schedule, and links to Instagram, Facebook and Telegram.
- **⚔️ Trainings** — A `CarouselView` with four tabs:
  - **Trainings** — Instructors create/edit sessions (one-off or recurring); fencers tap **Attend**.
  - **Weekly** — Instructors manage recurring rules (topic, times, dates); fencers view them.
  - **1-on-1** — Fencers request lessons from instructors; instructors create and accept/reject them.
  - **Prices** — Instructors set single/pack/unlimited prices, plus per-student overrides.
- **🏆 Tournaments** — Create tournaments, add fencers to pools, run pool matches, generate the elimination bracket and view final results.
- **🧑‍🤝‍🧑 Fencers** — Hero profile cards with avatar, role chips, contact info, GDPR/liability/payment status (instructor view), an activity panel, and **Promote to instructor**.
- **💰 Finance** — Instructor tabs (`Monthly`, `Yearly`, `All Time`, `Prices`); members see **Monthly** only. Track dues, expenses and one-off incomes; **Mark Paid** / **Add Expense** / **Add Income**.
- **📊 Statistics** — Visual summary of attendance and other stats.
- **👤 Profile** — View/edit personal data; accept GDPR and Liability statements.
- **🔐 Auth & Security** — SHA-256 login, email validation, unique usernames/emails, "Keep me logged in", and optional biometric login via `Plugin.Fingerprint`.

---

## 📲 How to use

1. **Install the APK** (Android 7.0 / API 24+).
2. **Register** with name, valid email, unique username, password, and accept the GDPR and Liability statements.
3. **Log in** — instructors unlock additional menus automatically.

---

## 🛠 Technical overview

| Area | Detail |
|---|---|
| Framework | **.NET MAUI** (`net8.0-android`, C# 12) |
| Architecture | MVVM via **CommunityToolkit.Mvvm** |
| Navigation | `Shell` flyout; multi-tab pages use `CarouselView` + `DataTemplateSelector` |
| Backend | **Google Sheets** via `Google.Apis.Sheets.v4` (service account) |
| Caching | In-memory decorator (`CachedGoogleSheetsService` + `ICacheControl`), warmed on login |
| Auth | SHA-256 credentials + **Plugin.Fingerprint**, persisted in `SecureStorage` |
| Min Android | API 24 (Android 7.0); target SDK 34 |

### Data model (Google Sheets tabs)

- **Core** — `Fencers`, `Trainings` (recurring ids `rec_{ruleId}_{yyyyMMdd}`), `RecurringTrainings`, `IndividualLessons` (`Rejected` = soft-delete).
- **Finance** — `Payments`, `Expenses`, `Incomes` (one-off; missing tab tolerated), `MonthNotes` (append-only), `Prices` (`SessionCount` 0=unlimited, 1=single, N=pack).
- **Tournaments** (versioned for optimistic concurrency) — `Tournaments`, `TournamentFencers`, `Pools`, `Matches` (with 2-minute soft-lock via `LockedBy*` columns), `FinalStandings`.

> Sheet I/O is split across partials (`GoogleSheetsService.*.cs`), mirrored by the cache decorator (`CachedGoogleSheetsService.*.cs`).

