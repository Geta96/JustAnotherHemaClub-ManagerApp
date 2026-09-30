using System.Security.Claims;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Web.Components;
using JustAnotherHemaClub.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering. The Google service-account
// JSON stays server-side (see ICredentialProvider): all Sheets I/O runs here,
// never in the browser.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The spreadsheet id and the service-account JSON path come from server config
// (appsettings.json / user-secrets / environment) instead of a bundled asset.
var spreadsheetId = builder.Configuration["GoogleSheets:SpreadsheetId"]
    ?? throw new InvalidOperationException("Missing config: GoogleSheets:SpreadsheetId");

// --- Core backends (same shape as MauiProgram) ---
builder.Services.AddSingleton<ICredentialProvider, WebCredentialProvider>();
builder.Services.AddSingleton(sp =>
    new GoogleSheetsService(spreadsheetId, sp.GetRequiredService<ICredentialProvider>()));
builder.Services.AddSingleton<CachedGoogleSheetsService>(sp =>
    new CachedGoogleSheetsService(sp.GetRequiredService<GoogleSheetsService>()));
builder.Services.AddSingleton<IGoogleSheetsService>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());
builder.Services.AddSingleton<ICacheControl>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());

builder.Services.AddSingleton<TestDataService>();

builder.Services.AddScoped<ICredentialStore, WebCredentialStore>();
builder.Services.AddScoped(sp =>
    new AuthService(sp, sp.GetRequiredService<ICredentialStore>()));

builder.Services.AddScoped<IDialogService, WebDialogService>();

builder.Services.AddScoped<UserSession>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.Name = "jahc.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

// --- Auth endpoints (non-interactive; the Login form POSTs here) ---
app.MapPost("/auth/login", async (
    HttpContext http,
    AuthService auth,
    TestDataService testData,
    [Microsoft.AspNetCore.Mvc.FromForm] string username,
    [Microsoft.AspNetCore.Mvc.FromForm] string password,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
{
    if (!await auth.LoginAsync(username, password) || auth.CurrentFencer is null)
        return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

    if (auth.IsTestMode)
        ServiceSwap.Activate(testData);

    var f = auth.CurrentFencer;
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, f.Id ?? ""),
        new(ClaimTypes.Name, f.Username ?? ""),
        new("displayName", f.Name ?? ""),
        new("email", f.Email ?? ""),
        new("isStudent", f.IsStudent ? "1" : "0"),
        new("isTestMode", auth.IsTestMode ? "1" : "0"),
    };
    if (f.IsInstructor)
        claims.Add(new Claim(ClaimTypes.Role, "Instructor"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http, AuthService auth) =>
{
    auth.Logout();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// --- Attend / un-attend the "Next lesson" shown on the Home card ---
// Mirrors HomeViewModel.ToggleAttendNextLessonAsync: toggles the signed-in
// fencer's id on the target training, materializing the recurring occurrence
// first when only a rule id was supplied.
app.MapPost("/home/attend", async (
    HttpContext http,
    IGoogleSheetsService sheets,
    ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? trainingId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? ruleId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? date,
    [Microsoft.AspNetCore.Mvc.FromForm] string? attending,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
{
    var fencerId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(fencerId))
        return Results.Redirect("/login");

    // Only allow local redirects back into the app.
    var target = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/')
        ? returnUrl
        : "/";

    var trainings = await sheets.GetTrainingsAsync();
    TrainingSession? session = null;

    if (!string.IsNullOrWhiteSpace(trainingId))
        session = trainings.FirstOrDefault(t => t.Id == trainingId);

    // Fall back to (or create) the recurring occurrence when we only have a rule.
    if (session is null && !string.IsNullOrWhiteSpace(ruleId) &&
        DateTime.TryParse(date, out var day))
    {
        var rules = await sheets.GetRecurringTrainingsAsync();
        var rule = rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is not null)
        {
            var id = $"rec_{rule.Id}_{day:yyyyMMdd}";
            var topicKey = (rule.Topic ?? "").Trim().ToLowerInvariant();

            session = trainings.FirstOrDefault(t => t.Id == id)
                ?? trainings.FirstOrDefault(t =>
                       t.Date.Date == day.Date &&
                       t.Date.TimeOfDay == rule.TimeOfDay &&
                       (t.Topic ?? "").Trim().ToLowerInvariant() == topicKey);

            if (session is null)
            {
                session = new TrainingSession
                {
                    Id = id,
                    Date = day.Date + rule.TimeOfDay,
                    EndDate = day.Date + rule.EndTimeOfDay,
                    Topic = rule.Topic ?? "",
                };
                await sheets.UpsertTrainingAsync(session);
            }
        }
    }

    if (session is null)
        return Results.Redirect(target);

    // "attending" reflects the state the button was showing; toggle away from it.
    var wasAttending = attending == "1";
    if (wasAttending)
        session.AttendeeFencerIds.Remove(fencerId);
    else if (!session.AttendeeFencerIds.Contains(fencerId))
        session.AttendeeFencerIds.Add(fencerId);

    await sheets.UpsertTrainingAsync(session);
    cache.InvalidateTrainings();

    return Results.Redirect(target);
}).RequireAuthorization().DisableAntiforgery();

// --- Instructor: save the full attendee list for a training ---
// The edit form submits a checkbox per fencer (name="attendee"); only the checked
// ids arrive, so we replace the attendee list with exactly that set.
app.MapPost("/trainings/attendees", async (
    HttpContext http,
    IGoogleSheetsService sheets,
    ICacheControl cache,
    AuthService auth) =>
{
    if (!http.User.IsInRole("Instructor"))
        return Results.Redirect("/trainings?tab=trainings");

    var form = await http.Request.ReadFormAsync();
    var trainingId = form["trainingId"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    var target = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/')
        ? returnUrl
        : "/trainings?tab=trainings";

    if (string.IsNullOrWhiteSpace(trainingId))
        return Results.Redirect(target);

    var trainings = await sheets.GetTrainingsAsync();
    var session = trainings.FirstOrDefault(t => t.Id == trainingId);
    if (session is null)
        return Results.Redirect(target);

    session.AttendeeFencerIds = form["attendee"]
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .Distinct()
        .ToList();

    await sheets.UpsertTrainingAsync(session);
    cache.InvalidateTrainings();

    return Results.Redirect(target);
}).RequireAuthorization().DisableAntiforgery();

// ================= WEEKLY (recurring) TRAINING RULES =================
// Instructor-only create / edit / delete. Regular fencers view them read-only.
app.MapPost("/weekly/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var rules = await sheets.GetRecurringTrainingsAsync();
    var rule = rules.FirstOrDefault(r => r.Id == form["ruleId"].ToString());
    if (rule is null) return Results.Redirect(back);

    rule.Topic = form["topic"].ToString();
    if (ParseTime(form["startTime"], out var st)) rule.TimeOfDay = st;
    if (ParseTime(form["endTime"], out var et)) rule.EndTimeOfDay = et;
    if (DateTime.TryParse(form["startDate"], out var sd)) rule.StartDate = sd;
    rule.EndDate = form["hasEndDate"] == "on" && DateTime.TryParse(form["endDate"], out var ed)
        ? ed : null;

    await sheets.UpsertRecurringTrainingAsync(rule);
    cache.InvalidateRecurringTrainings();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/weekly/add", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    if (!Enum.TryParse<DayOfWeek>(form["dayOfWeek"], out var dow)) dow = DayOfWeek.Tuesday;
    ParseTime(form["startTime"], out var st);
    ParseTime(form["endTime"], out var et);
    DateTime.TryParse(form["startDate"], out var sd);
    if (sd == default) sd = DateTime.Today;

    var rule = new RecurringTrainingRule
    {
        Id = Guid.NewGuid().ToString("N"),
        DayOfWeek = dow,
        TimeOfDay = st == default ? new TimeSpan(18, 0, 0) : st,
        EndTimeOfDay = et == default ? new TimeSpan(20, 0, 0) : et,
        Topic = form["topic"].ToString(),
        StartDate = sd,
        EndDate = form["hasEndDate"] == "on" && DateTime.TryParse(form["endDate"], out var ed) ? ed : null,
        CreatedByFencerId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
    };

    await sheets.UpsertRecurringTrainingAsync(rule);
    cache.InvalidateRecurringTrainings();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/weekly/delete", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=weekly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var ruleId = form["ruleId"].ToString();
    if (!string.IsNullOrWhiteSpace(ruleId))
    {
        await sheets.DeleteRecurringTrainingAsync(ruleId);
        cache.InvalidateRecurringTrainings();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= 1-ON-1 (INDIVIDUAL) LESSONS =================
// Students request from instructor(s). Instructors add directly, request from
// other instructors, accept / reject requests, edit notes and delete.
app.MapPost("/lessons/create", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");

    var form = await http.Request.ReadFormAsync();
    DateTime.TryParse(form["date"], out var d);
    ParseTime(form["time"], out var tt);
    var when = (d == default ? DateTime.Today : d.Date) + tt;
    var topic = form["topic"].ToString();
    var mode = form["mode"].ToString(); // "direct" | "request"

    IndividualLesson lesson;
    if (http.User.IsInRole("Instructor") && mode == "direct")
    {
        var studentId = form["studentId"].ToString();
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Redirect(back);
        lesson = new IndividualLesson
        {
            Date = when,
            StudentId = studentId,
            InstructorId = string.IsNullOrWhiteSpace(form["instructorId"]) ? meId : form["instructorId"].ToString(),
            Topic = topic,
            Notes = form["notes"].ToString(),
            NextIdea = form["nextIdea"].ToString(),
            Status = IndividualLessonStatus.Accepted,
        };
    }
    else
    {
        // Request flow (students always; instructors in request mode).
        var targets = form["instructor"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        if (targets.Count == 0) return Results.Redirect(back);
        lesson = new IndividualLesson
        {
            Date = when,
            StudentId = meId,
            InstructorId = "",
            Topic = topic,
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = targets,
        };
    }

    await sheets.UpsertIndividualLessonAsync(lesson);
    cache.InvalidateIndividualLessons();
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/accept", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.InstructorId = meId;
        l.Status = IndividualLessonStatus.Accepted;
        l.RequestedInstructorIds = new();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/reject", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.RequestedInstructorIds = l.RequestedInstructorIds.Where(id => id != meId).ToList();
        if (l.RequestedInstructorIds.Count == 0)
            l.Status = IndividualLessonStatus.Rejected;
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        l.Topic = form["topic"].ToString();
        l.Notes = form["notes"].ToString();
        l.NextIdea = form["nextIdea"].ToString();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/lessons/delete", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth) =>
{
    const string back = "/trainings?tab=lessons";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var lessons = await sheets.GetIndividualLessonsAsync();
    var l = lessons.FirstOrDefault(x => x.Id == form["lessonId"].ToString());
    if (l is not null)
    {
        // Reuse Rejected as a soft-delete, matching the MAUI app.
        l.Status = IndividualLessonStatus.Rejected;
        l.RequestedInstructorIds = new();
        await sheets.UpsertIndividualLessonAsync(l);
        cache.InvalidateIndividualLessons();
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// --- Profile: the signed-in fencer edits their own details ---
// Mirrors ProfileViewModel.SaveAsync: Name/Email/IsStudent/GDPR/Liability are
// editable; Username, IsInstructor and Active are not touched here.
app.MapPost("/profile/save", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache,
    [Microsoft.AspNetCore.Mvc.FromForm] string? name,
    [Microsoft.AspNetCore.Mvc.FromForm] string? email,
    [Microsoft.AspNetCore.Mvc.FromForm] string? isStudent,
    [Microsoft.AspNetCore.Mvc.FromForm] string? gdpr,
    [Microsoft.AspNetCore.Mvc.FromForm] string? liability) =>
{
    var meId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(meId)) return Results.Redirect("/login");

    try
    {
        var fencers = await sheets.GetFencersAsync();
        var me = fencers.FirstOrDefault(f => f.Id == meId);
        if (me is null) return Results.Redirect("/profile");

        me.Name = (name ?? "").Trim();
        me.Email = (email ?? "").Trim();
        me.IsStudent = isStudent == "on";
        me.GdprAccepted = gdpr == "on";
        me.LiabilityAccepted = liability == "on";

        await sheets.UpsertFencerAsync(me);
        cache.InvalidateFencers();
        return Results.Redirect("/profile?saved=1");
    }
    catch (Exception ex)
    {
        return Results.Redirect($"/profile?error={Uri.EscapeDataString(ex.Message)}");
    }
}).RequireAuthorization().DisableAntiforgery();

// --- Finance: promote a member to instructor (instructor-only) ---
app.MapPost("/fencers/promote", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId) =>
{
    var back = $"/fencers?fencer={Uri.EscapeDataString(fencerId ?? "")}";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (string.IsNullOrWhiteSpace(fencerId)) return Results.Redirect("/fencers");

    var fencers = await sheets.GetFencersAsync();
    var target = fencers.FirstOrDefault(f => f.Id == fencerId);
    if (target is not null && !target.IsInstructor)
    {
        target.IsInstructor = true;
        await sheets.UpsertFencerAsync(target);
    }
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

// ================= FINANCE =================
// Instructor-only money actions. Members never reach these (buttons are hidden),
// but the endpoints re-check the role server-side.
app.MapPost("/finance/markpaid", async (
    HttpContext http, IGoogleSheetsService sheets, ICacheControl cache, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string fencerId,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (string.IsNullOrWhiteSpace(fencerId)) return Results.Redirect(back);

    if (!decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value) || value == 0m)
        return Results.Redirect(back);

    await sheets.MarkPaidAsync(new Payment
    {
        FencerId = fencerId,
        Year = year,
        Month = month,
        Amount = value,
        PaidOn = DateTime.Now,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/expense/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string? category,
    [Microsoft.AspNetCore.Mvc.FromForm] string? description,
    [Microsoft.AspNetCore.Mvc.FromForm] string? amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value);
    if (string.IsNullOrWhiteSpace(description) && value <= 0m) return Results.Redirect(back);

    var day = Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(year, month));
    await sheets.AddExpenseAsync(new Expense
    {
        Date = new DateTime(year, month, day),
        Category = category ?? "",
        Description = description ?? "",
        Amount = value,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/income/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] int year,
    [Microsoft.AspNetCore.Mvc.FromForm] int month,
    [Microsoft.AspNetCore.Mvc.FromForm] string? category,
    [Microsoft.AspNetCore.Mvc.FromForm] string? description,
    [Microsoft.AspNetCore.Mvc.FromForm] string? amount) =>
{
    const string back = "/finance?tab=monthly";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value);
    if (string.IsNullOrWhiteSpace(description) && value <= 0m) return Results.Redirect(back);

    var day = Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(year, month));
    await sheets.AddIncomeAsync(new Income
    {
        Date = new DateTime(year, month, day),
        Category = category ?? "",
        Description = description ?? "",
        Amount = value,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/add", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    decimal.TryParse(form["fullPrice"], System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var full);
    if (full <= 0m) return Results.Redirect(back);
    decimal.TryParse(form["studentPrice"], System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var student);

    var (sessions, months, isCustom) = form["tier"].ToString() switch
    {
        "0" => (1, 1, false),
        "1" => (4, 1, false),
        "2" => (0, 1, false),
        _ => (0, 1, true),
    };
    var hasEnd = form["hasEndDate"] == "on";
    if (isCustom && !hasEnd) return Results.Redirect(back);

    DateTime.TryParse(form["startDate"], out var start);
    if (start == default) start = DateTime.Today;
    DateTime.TryParse(form["endDate"], out var end);

    await sheets.UpsertPriceRuleAsync(new PriceRule
    {
        SessionCount = sessions,
        MonthCount = months,
        IsCustomPeriod = isCustom,
        FullPrice = full,
        StudentPrice = student > 0 ? student : DuesCalculator.SuggestStudentPrice(full),
        StartDate = start,
        EndDate = hasEnd ? end : null,
    });
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/save", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);

    var form = await http.Request.ReadFormAsync();
    var rules = await sheets.GetPriceRulesAsync();
    var rule = rules.FirstOrDefault(r => r.Id == form["ruleId"].ToString());
    if (rule is null) return Results.Redirect(back);

    if (decimal.TryParse(form["fullPrice"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var full)) rule.FullPrice = full;
    if (decimal.TryParse(form["studentPrice"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var student)) rule.StudentPrice = student;
    if (DateTime.TryParse(form["startDate"], out var start)) rule.StartDate = start;
    var hasEnd = form["hasEndDate"] == "on";
    rule.EndDate = hasEnd && DateTime.TryParse(form["endDate"], out var end) ? end : null;
    rule.IsCustomPeriod = form["isCustomPeriod"] == "on";
    if (rule.IsCustomPeriod && rule.EndDate is null) return Results.Redirect(back);

    await sheets.UpsertPriceRuleAsync(rule);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/finance/price/delete", async (
    HttpContext http, IGoogleSheetsService sheets, AuthService auth,
    [Microsoft.AspNetCore.Mvc.FromForm] string ruleId) =>
{
    const string back = "/finance?tab=prices";
    if (!http.User.IsInRole("Instructor")) return Results.Redirect(back);
    if (!string.IsNullOrWhiteSpace(ruleId))
        await sheets.DeletePriceRuleAsync(ruleId);
    return Results.Redirect(back);
}).RequireAuthorization().DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool ParseTime(string? s, out TimeSpan t) => TimeSpan.TryParse(s, out t);