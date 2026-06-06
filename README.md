# JAHC Manager

**JAHC Manager** is the official mobile companion app for **Just Another HEMA Club** — a Historical European Martial Arts club based in Budapest. It's a lightweight club-management tool that helps instructors run training sessions, manage 1-on-1 lessons and finances, and helps fencers stay on top of their attendance and dues.

> Built with **.NET MAUI** for Android.

---

## ✨ Features

### 🏠 Home
- Club introduction and weekly training schedule (days, times, locations).
- Quick links to the club's **Instagram**, **Facebook** and **Telegram**.

### ⚔️ Trainings hub
A single flyout entry (**Trainings**) hosts three swipeable tabs inside a `CarouselView`:

#### Trainings tab
- Past trainings grouped by month in expandable cards.
- Each training card is **collapsed by default** — tap to expand and edit.
- **Instructors** can:
  - Create new training sessions (one-off or weekly recurring) from the *New Training* page.
  - Edit topic and attendees; **Save** collapses the card on success (visual confirmation that the write went through). **Delete** prompts for confirmation.
  - Attach an optional note to each month.
- **Regular fencers** can tap **Attend** in one tap from the collapsed header.

#### Weekly tab
- Lists every active **recurring weekly training rule**.
- Instructors can edit each rule's topic, start time, end time, start date, optional end date, or delete it. **Save changes** collapses the rule's card on success.
- **Add Weekly Training** opens the *New Training* form with the "Repeat weekly on this day" checkbox pre-selected.
- Recurring sessions are materialised automatically by `RecurringTrainingMaterializer` (idempotent; deterministic ids of the form `rec_{ruleId}_{yyyyMMdd}`).

#### 1 on 1 tab
- Anyone active can be the student of a lesson — **instructors included**.
- **Instructors** can:
  - **Add as Instructor** (direct entry with notes & next-session idea — visible only to instructors).
  - **Request 1 on 1 Lesson** from one or more other instructors (the requester is the student of the request).
  - Filter lessons by student or instructor; **Clear** resets both.
  - Edit notes/next idea after acceptance; **Save** collapses the card on success. **Delete** is a soft-delete (status = `Rejected`).
- **Regular fencers** can request a 1 on 1 lesson from one or more instructors; they see only their own lessons.
- Targeted instructors see **Accept / Reject** on pending requests.
- Cards are collapsed by default with a one-line header (date · student · instructor — topic) and expand to the full editor on tap.

### 🧑‍🤝‍🧑 Fencers
- Pick a fencer from a dropdown to open a **hero profile card** with avatar, role chips and contact info.
- Instructor view also shows GDPR/Liability/Payment status with a tidy right‑aligned stat layout.
- **Activity panel** with compact stats: `1.5 avg · 2 mo`, `2 in Jun 2026`, 1‑on‑1 received/given counters, active months and the four most recent trainings attended.
- Instructors can **Promote to instructor** with one confirmation prompt.

### 💰 Finance
- Three swipeable tabs (`Monthly`, `Yearly`, `All Time`) inside a `CarouselView` driven by `FinanceTabTemplateSelector`. Each carousel item is a lightweight `FinanceTab` wrapper around the shared `FinanceViewModel`, so every tab can reach the same view-model state via `Vm.*` bindings without `BindingContext` switching.
- Per-month cards are collapsed by default; expand to see dues and expenses for that month.
- Instructors:
  - **Mark Paid** dues with one tap.
  - **Add Expense** expands a single button into a category/description/amount form, with **Cancel** / **Save** side by side.
- Members see a **My payments** card summarising "All payed up" or the total due.
- Yearly and All‑Time tabs surface income / expenses / balance / sessions / average attendance aggregates computed once per load.

### 📊 Statistics
- Visual summary of attendance and finance data for instructors.

### 👤 Profile
- View and edit your personal data.
- Accept the **GDPR** and **Liability** statements (required at registration).

### 🔐 Authentication & Security
- Username/password login with SHA-256 hashed credentials.
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
4. Use the **flyout menu** (☰) to move between Home, Trainings, Fencers, Finance, Statistics and Profile.
5. Inside **Trainings** swipe (or tap the tab buttons) between **Trainings · Weekly · 1 on 1**:
   - **Trainings**: tap a month, then a session card; instructors edit and **Save** (the card collapses on success). Members tap **Attend**.
   - **Weekly**: instructors edit/delete existing rules or tap **Add Weekly Training** to create one.
   - **1 on 1**: instructors filter, add or request; members request from instructors. Targeted instructors get **Accept / Reject**.
6. On the **Fencers** tab pick a fencer to open their profile and activity stats.
7. On the **Finance** tab:
   - Expand a month to see dues; instructors can **Mark Paid** and **Add Expense**.
   - Instructors can swipe over to **Yearly** and **All Time** for aggregates.
8. Tap the **↻ Refresh** chip on any page to invalidate the cache and re-pull data.
9. Log out at any time from the bottom of the flyout menu.

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
- `Fencers` — Id, Username, PasswordHash, Name, Email, Active, IsStudent, GdprAccepted, LiabilityAccepted, IsInstructor
- `Trainings` — Id, Date, Topic, AttendeeFencerIds (CSV), EndDate — recurring sessions use deterministic ids of the form `rec_{ruleId}_{yyyyMMdd}`
- `RecurringTrainings` — Id, DayOfWeek, TimeOfDay, Topic, StartDate, EndDate, CreatedByFencerId, EndTimeOfDay
- `IndividualLessons` — Id, Date, StudentId, InstructorId, Topic, Notes, NextIdea, Status, RequestedInstructorIds (CSV) — `Rejected` doubles as soft-delete
- `Payments` — FencerId, Year, Month, Amount, PaidOn
- `Expenses` — Id, Date, Category, Description, Amount
- `MonthNotes` — Year, Month, Note (append-only, latest wins)

> All sheet I/O is encapsulated in `Services/GoogleSheetsService.cs`; the cache decorator lives in `Services/CachedGoogleSheetsService.cs`.

### Tab pattern (Trainings hub & Finance)
Both pages share the same pattern:
- A top-level `ViewModel` (`TrainingsHubViewModel`, `FinanceViewModel`) exposes a `Tabs` collection and a `SelectedTabIndex`.
- A `CarouselView` binds `ItemsSource="{Binding Tabs}"` and `Position="{Binding SelectedTabIndex, Mode=TwoWay}"`.
- A `DataTemplateSelector` (`TrainingsHubTabTemplateSelector`, `FinanceTabTemplateSelector`) picks the right `DataTemplate` per item.
- For Finance, items are wrapped in a `FinanceTab { Key, Vm }` record so every child binding can reach the shared `FinanceViewModel` via `Vm.*` paths without needing to swap `BindingContext` mid-template.

### Project layout (high level)