# JAHC Manager

A .NET MAUI Android app for managing a HEMA club (sessions, dues, expenses, statistics).
Backed by Google Sheets (instructors) or an in-memory demo (Guest mode).

## Quick start
1. Install the .NET MAUI workload.
2. Open `JustAnotherHemaClub.sln` in Visual Studio 2022.
3. Pick an Android emulator and press F5.
4. Tap **Use as Guest** to explore without configuring Google Sheets.

## Configuring real Google Sheets backend
- Drop your service account JSON at `Resources/Raw/service-account.json` (gitignored).
- Set `SpreadsheetId` in `MauiProgram.cs`.
- Share the sheet with the service account email.
- Sheet tabs expected: `Fencers`, `Sessions`, `Payments`, `Expenses`, `Instructors`.

## Pricing tiers (dues)
- 1–3 sessions: 3 500 each
- 4–7 sessions: 9 000 flat
- 8+ sessions: 12 000 flat