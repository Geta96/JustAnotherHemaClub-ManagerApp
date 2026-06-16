# JAHC Manager

**JAHC Manager** is the official mobile companion app for **Just Another HEMA Club** — a Historical European Martial Arts club based in Budapest. It's a lightweight club-management tool that helps instructors run training sessions, manage 1-on-1 lessons, prices and finances, run in-club tournaments, and helps fencers stay on top of their attendance and dues.

> Built with **.NET MAUI** for Android.

---

## ✨ Features

### 🏠 Home
- Club introduction and weekly training schedule (days, times, locations).
- Quick links to the club's **Instagram**, **Facebook** and **Telegram**.

### ⚔️ Trainings hub
A single flyout entry (**Trainings**) hosts four swipeable tabs inside a `CarouselView`:

#### Trainings tab
- **Instructors** can:
  - Create new training sessions (one-off or weekly recurring)
  - Edit topic and attendees.
- **Regular fencers** can tap **Attend** in one tap from the collapsed header.

#### Weekly tab
- **Instructors** can
  - Adding Weekly training will create a new training in the specificed days.
  - Instructors can edit each rule's topic, start time, end time, start date, optional end date, or delete it.
- **Regular fencers** can see weekly trainings.

#### 1 on 1 tab
- Anyone active can be the student of a lesson — **instructors included**.
- **Regular fencers** can request a 1 on 1 lesson from one or more instructors; they see only their own lessons.
- **Instructors** can create 1 on 1 lessons, and accpet/reject requested lessons.

#### Prices
- Instructors can set the monthly price for students
  - Set Single Session, 4-Session Pack and Unlimited Monthly Pack prices.
  - Set custom student prices that override the defaults.

### Tournaments tab
- Create and manage a tournament
- add fencers and put them into pools
- manage the pool matches and generate the elimination bracket
- See the final resultof the tournament

### 🧑‍🤝‍🧑 Fencers
- Pick a fencer from a dropdown to open a **hero profile card** with avatar, role chips and contact info.
- Instructor view also shows GDPR/Liability/Payment status with a tidy right‑aligned stat layout.
- **Activity panel** with compact stats: `1.5 avg · 2 mo`, `2 in Jun 2026`, 1‑on‑1 received/given counters, active months and the four most recent trainings attended.
- Instructors can **Promote to instructor** with one confirmation prompt.

### 💰 Finance
- Four swipeable tabs for instructors (`Monthly`, `Yearly`, `All Time`, `Prices`); members see only the **Monthly** view.
- **Monthly tab** — per-month card showing dues, expenses and **one-off incomes** (donations, gear sales, fencers paying for extras). Instructors get three actions:
  - **Mark Paid** - **Add Expense** — **Add Income**
- Members see a **My payments** card summarising "All payed up" or the total due.
- Yearly and All‑Time tabs surface **income (dues + one-off incomes)** / expenses / balance / sessions / average attendance aggregates computed once per load.

### 📊 Statistics
- Visual summary of attendance and other statistics.

### 👤 Profile
- View and edit your personal data.
- Accept the **GDPR** and **Liability** statements (required at registration).

### 🔐 Authentication & Security
- Username/password login with SHA-256 hashed credentials.
- **Email validation** at registration (`@` required + RFC-ish shape check); usernames and emails must be unique.
- "Keep me logged in" and optional **biometric login** (fingerprint/face) via `Plugin.Fingerprint`; the biometric checkbox lives next to "Keep me logged in" to keep card height stable.

---

## 📲 How to use

1. **Install the APK** on an Android device (Android 7.0 / API 24 or higher).
2. **Register** an account from the login screen:
   - Provide name, valid email (with `@`), unique username and password.
   - Accept the **GDPR** and **Liability** statements.
3. **Log in** — instructors are flagged in the backend and unlock additional menus automatically.

---

## 🛠 Technical overview

| Area | Detail |
|---|---|
| Framework | **.NET MAUI** (`net8.0-android`, C# 12) |
| Architecture | MVVM via **CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`) |
| Navigation | `Shell` with a custom flyout template; multi-tab pages use in-page `CarouselView` + `DataTemplateSelector` |
| Backend | **Google Sheets** as the data store, accessed through `Google.Apis.Sheets.v4` with a service account |
| Caching | In-memory decorator (`CachedGoogleSheetsService` + `ICacheControl`) — reads are cached, writes patch the cache, `WarmAsync()` runs on login |
| Auth | Local credential check (SHA-256 hashed) + **Plugin.Fingerprint** for biometrics, with persisted credentials in `SecureStorage` |
| Logging | `Microsoft.Extensions.Logging.Debug` |
| Min Android | API 24 (Android 7.0); target SDK 34 |

### Data model (Google Sheets tabs)

**Core tabs**
- `Fencers` — Id, Username, PasswordHash, Name, Email, Active, IsStudent, GdprAccepted, LiabilityAccepted, IsInstructor
- `Trainings` — Id, Date, Topic, AttendeeFencerIds (CSV), EndDate — recurring sessions use deterministic ids of the form `rec_{ruleId}_{yyyyMMdd}`
- `RecurringTrainings` — Id, DayOfWeek, TimeOfDay, Topic, StartDate, EndDate, CreatedByFencerId, EndTimeOfDay
- `IndividualLessons` — Id, Date, StudentId, InstructorId, Topic, Notes, NextIdea, Status, RequestedInstructorIds (CSV) — `Rejected` doubles as soft-delete

**Finance tabs**
- `Payments` — FencerId, Year, Month, Amount, PaidOn
- `Expenses` — Id, Date, Category, Description, Amount
- `Incomes` — Id, Date, Category, Description, Amount — one-off, non-dues income (donations, gear sales, fencers paying for extras); missing tab is tolerated (treated as "no incomes recorded")
- `MonthNotes` — Year, Month, Note (append-only, latest wins)
- `Prices` — Id, SessionCount, FullPrice, StudentPrice, StartDate, EndDate — `SessionCount` is `0` for the unlimited monthly pass, `1` for a single-session ticket, `N` for an N-session pack; missing tab falls back to `DuesCalculator` defaults

**Tournament tabs** (normalised + versioned for optimistic concurrency)
- `Tournaments` — Id, Name, PasswordPlain, CreatedAt, State, Version
- `TournamentFencers` — TournamentId, FencerId, Name, IsWithdrawn, OrderIndex
- `Pools` — TournamentId, PoolId, Index, FencerIds (CSV), IsClosed, Version
- `Matches` — TournamentId, MatchId, PoolId, BracketRound, BracketSlot, BracketTag (`Final`/`Bronze`), OrderInPool, LeftFencerId, RightFencerId, LeftScore, RightScore, LeftYellowCards, LeftRedCards, RightYellowCards, RightRedCards, RemainingTimeSeconds, Status, WinnerFencerId, StartedAtUtc, FinishedAtUtc, Version, UpdatedAtUtc, UpdatedByUserId, LockedByUserId, LockedAtUtc — `LockedBy*` columns implement a 2‑minute soft‑lock so two judges can't edit the same fight
- `FinalStandings` — TournamentId, Position, FencerId

> Sheet I/O is split across partials: `Services/GoogleSheetsService.cs` (fencers, trainings, lessons, payments, expenses, month notes), `Services/GoogleSheetsService.Prices.cs` (price rules), `Services/GoogleSheetsService.Incomes.cs` (one-off incomes) and `Services/GoogleSheetsService.Tournaments.cs` (tournament aggregate). The cache decorator follows the same split: `Services/CachedGoogleSheetsService.cs` + `Services/CachedGoogleSheetsService.Tournaments.cs`.

