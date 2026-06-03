# JAHC Manager

**JAHC Manager** is the official mobile companion app for **Just Another HEMA Club** — a Historical European Martial Arts club based in Budapest. It's a lightweight club-management tool that helps instructors run training sessions and helps fencers stay on top of their attendance and dues.

> Built with .NET MAUI for Android.

---

## ✨ Features

### 🏠 Home
- Club introduction and weekly training schedule (days, times, locations).
- Quick links to the club's **Instagram**, **Facebook** and **Telegram**.

### ⚔️ Trainings
- Browse **past trainings grouped by month** in expandable cards.
- **Instructors** can:
  - Create new training sessions.
  - Edit the topic/description of a training.
  - Tick attendees from the full fencer roster.
  - Attach an optional note to each month.
- **Regular fencers** can:
  - Mark themselves as attending a session with one tap.
  - See a clear "✓ Attended" confirmation.

### 🧑‍🤝‍🧑 Fencers
- Manage the club's roster (active/inactive, student status, instructor flag, contact info).

### 💰 Finance
- Track monthly **payments** per fencer.
- Log and review **expenses** by date range and category.

### 📊 Statistics
- Visual summary of attendance and finance data for instructors.

### 👤 Profile
- View and edit your personal data.
- Accept the **GDPR** and **Liability** statements (required at registration).

### 🔐 Authentication & Security
- Username/password login with hashed credentials.
- Optional **biometric login** (fingerprint) via `Plugin.Fingerprint`.
- Role-based UI: instructors see admin controls; regular fencers see only what they need.

---

## 📲 How to use

1. **Install the APK** on an Android device (Android 7.0 / API 24 or higher).
2. **Register** an account from the login screen:
   - Provide username, name, email and password.
   - Accept the **GDPR** and **Liability** statements.
3. **Log in** — instructors are flagged in the backend and unlock additional menus automatically.
4. Use the **flyout menu** (☰) to move between Home, Trainings, Fencers, Finance, Statistics and Profile.
5. On the **Trainings** page:
   - Tap the **↻ Refresh** chip to pull the latest data.
   - Tap a month to expand it and see all sessions.
   - Tap **Attend Training** on an upcoming session to register your attendance.
6. Log out at any time from the bottom of the flyout menu.

---

## 🛠 Technical overview

| Area | Detail |
|---|---|
| Framework | **.NET MAUI** (`net8.0-android`) |
| Architecture | MVVM via **CommunityToolkit.Mvvm** |
| Navigation | `Shell` with a custom flyout template |
| Backend | **Google Sheets** as the data store, accessed through `Google.Apis.Sheets.v4` with a service account |
| Auth | Local credential check (hashed) + **Plugin.Fingerprint** for biometrics |
| Logging | `Microsoft.Extensions.Logging.Debug` |
| Min Android | API 24 (Android 7.0) |

### Data model (Google Sheets tabs)
- `Fencers` — Id, Username, PasswordHash, Name, Email, Active, IsStudent, GdprAccepted, LiabilityAccepted, IsInstructor
- `Trainings` — Id, Date, Topic, AttendeeFencerIds (CSV)
- `Payments` — FencerId, Year, Month, Amount, PaidOn
- `Expenses` — Id, Date, Category, Description, Amount
- `MonthNotes` — Year, Month, Note (append-only, latest wins)

All sheet I/O is encapsulated in `Services/GoogleSheetsService.cs`.

---

## 🚀 Building from source

### Prerequisites
- Visual Studio 2022 (17.8+) with the **.NET MAUI** workload
- **.NET 8 SDK**
- Android SDK + an emulator or physical device (API 24+)
- A Google Cloud **service account** with access to the club spreadsheet

### Steps
1. Clone the repo: