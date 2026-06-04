# JAHC Manager

**JAHC Manager** is the official mobile companion app for **Just Another HEMA Club** — a Historical European Martial Arts club based in Budapest. It's a lightweight club-management tool that helps instructors run training sessions, manage 1-on-1 lessons and finances, and helps fencers stay on top of their attendance and dues.

> Built with **.NET MAUI** for Android.

---

## ✨ Features

### 🏠 Home
- Club introduction and weekly training schedule (days, times, locations).
- Quick links to the club's **Instagram**, **Facebook** and **Telegram**.

### ⚔️ Trainings
- Browse **past trainings grouped by month** in expandable cards.
- Each training card is **collapsed by default** — tap to expand and edit.
- **Instructors** can:
  - Create new training sessions (one-off or weekly recurring).
  - Edit topic, attendees, and delete trainings (Delete/Save side by side).
  - Attach an optional note to each month.
- **Regular fencers** can:
  - Tap **Attend** to register attendance in one tap from the collapsed header.

### 🔁 Weekly
- A dedicated tab listing every active **recurring weekly training rule**.
- Instructors can edit each rule's topic, time-of-day, start date, optional end date, or delete the rule.
- **Add Weekly Training** button opens the *New Training* form with the "Repeat weekly on this day" checkbox pre-selected.
- Recurring sessions are materialized automatically by `RecurringTrainingMaterializer` (idempotent; deterministic ids of the form `rec_{ruleId}_{yyyyMMdd}`).

### 🤺 1 on 1 Lessons
- Tab next to Trainings and Weekly under the Trainings flyout group.
- Anyone active can be the student of a lesson — **instructors included**.
- **Instructors** can:
  - **Add Lesson as Instructor** (direct entry with notes & next-session idea — visible only to instructors).
  - **Request a 1 on 1 Lesson** from one or more other instructors (the requester is the student of the request).
  - Filter lessons by student or instructor; **My lessons** quick filter; **Clear** resets both.
  - Edit notes/next idea after acceptance, delete a lesson (Reject is the soft-delete equivalent).
- **Regular fencers** can:
  - Request a 1 on 1 lesson from one or more instructors; see only their own lessons.
- Targeted instructors see **Accept / Reject** on pending requests.
- Cards are collapsed by default with a one-line header (date · student · instructor — topic) and expand to the full editor on tap.

### 🧑‍🤝‍🧑 Fencers
- Manage the club's roster (active/inactive, student status, instructor flag, contact info).

### 💰 Finance
- Track monthly **payments** per fencer.
- Per-month cards are collapsed by default; expand to see dues and expenses for that month.
- Instructors:
  - **Mark Paid** dues with one tap.
  - Add expenses through a single **Add Expense** button that expands to a category/description/amount form, with **Cancel** / **Save** side by side.
- Members see a **My payments** card summarising "All payed up" or the total due.

### 📊 Statistics
- Visual summary of attendance and finance data for instructors.

### 👤 Profile
- View and edit your personal data.
- Accept the **GDPR** and **Liability** statements (required at registration).

### 🔐 Authentication & Security
- Username/password login with hashed credentials.
- **Email validation** at registration (`@` required + RFC-ish shape check); usernames and emails must be unique.
- "Keep me logged in" and optional **biometric login** (fingerprint/face) via `Plugin.Fingerprint`; the biometric checkbox lives next to "Keep me logged in" to keep card height stable.
- **Animated login**: crest slides up, form slides in with a fade; switches to a centred "Logging in…" splash during sign-in and back to the form on error.
- Role-based UI: instructors see admin controls; regular fencers see only what they need.
- "Forgot my password" flow uses the username + email on file, then sets a new ≥6-char password.

### 🌗 Theming
- Brand palette: Wine (`#5D1312`), Cream (`#EEE7D5`), Slate blue (`#476FB5`), and a dedicated `DangerRed`.
- App pins the **Light theme** and disables Android **force-dark** so text on cream cards stays readable on every device.

---

## 📲 How to use

1. **Install the APK** on an Android device (Android 7.0 / API 24 or higher).
2. **Register** an account from the login screen:
   - Provide name, valid email (with `@`), unique username and password.
   - Accept the **GDPR** and **Liability** statements.
3. **Log in** — instructors are flagged in the backend and unlock additional menus automatically.
4. Use the **flyout menu** (☰) to move between Home, Trainings (Trainings · Weekly · 1 on 1 Lessons), Fencers, Finance, Statistics and Profile.
5. On the **Trainings** tab:
   - Tap a month to expand it and see all sessions.
   - Tap a training card to expand its details; instructors edit and use **Delete** / **Save**; members tap **Attend**.
   - Tap the **↻ Refresh** chip to pull the latest data.
6. On the **Weekly** tab:
   - Instructors edit/delete existing weekly rules or tap **Add Weekly Training** to create a new one.
7. On the **1 on 1 Lessons** tab:
   - Instructors filter, add, or request; members request from instructors.
8. On the **Finance** tab:
   - Expand a month to see dues; instructors can **Mark Paid** and **Add Expense**.
9. Log out at any time from the bottom of the flyout menu.

---

## 🛠 Technical overview

| Area | Detail |
|---|---|
| Framework | **.NET MAUI** (`net8.0-android`, C# 12) |
| Architecture | MVVM via **CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`) |
| Navigation | `Shell` with a custom flyout template + bottom `Tab` for Trainings group |
| Backend | **Google Sheets** as the data store, accessed through `Google.Apis.Sheets.v4` with a service account |
| Caching | In-memory decorator (`CachedGoogleSheetsService` + `ICacheControl`) — reads are cached, writes patch the cache, `WarmAsync()` runs on login |
| Auth | Local credential check (SHA-256 hashed) + **Plugin.Fingerprint** for biometrics, with persisted credentials in `SecureStorage` |
| Logging | `Microsoft.Extensions.Logging.Debug` |
| Min Android | API 24 (Android 7.0); target SDK 34 |

### Data model (Google Sheets tabs)
- `Fencers` — Id, Username, PasswordHash, Name, Email, Active, IsStudent, GdprAccepted, LiabilityAccepted, IsInstructor
- `Trainings` — Id, Date, Topic, AttendeeFencerIds (CSV) — recurring sessions use deterministic ids of the form `rec_{ruleId}_{yyyyMMdd}`
- `RecurringTrainings` — Id, DayOfWeek, TimeOfDay, Topic, StartDate, EndDate, CreatedByFencerId
- `IndividualLessons` — Id, Date, StudentId, InstructorId, Topic, Notes, NextIdea, Status, RequestedInstructorIds (CSV)
- `Payments` — FencerId, Year, Month, Amount, PaidOn
- `Expenses` — Id, Date, Category, Description, Amount
- `MonthNotes` — Year, Month, Note (append-only, latest wins)

> All sheet I/O is encapsulated in `Services/GoogleSheetsService.cs`; the cache decorator lives in `Services/CachedGoogleSheetsService.cs`.

### Project layout (high level)